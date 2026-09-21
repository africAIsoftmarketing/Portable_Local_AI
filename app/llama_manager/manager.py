"""
Rôle    : gestion du cycle de vie de llama-server (spawn subprocess, health check,
          arrêt propre). Bind strictement en loopback (jamais exposé au LAN).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import asyncio
import logging
import os
import shutil
import signal
import sys
from pathlib import Path
from typing import Optional

import httpx

from app.config.models import Settings

logger = logging.getLogger("studio.llama")

# Timeouts (secondes)
_STARTUP_HEALTH_TIMEOUT = 60
_HEALTH_POLL_INTERVAL = 0.5
_STOP_GRACE = 10


class LlamaManager:
    """Gère un unique processus llama-server par instance de studio."""

    def __init__(self, settings: Settings, platform: dict, backend: dict,
                 studio_root: Path):
        self.settings = settings
        self.platform = platform
        self.backend = backend
        self.studio_root = studio_root
        self.host = settings.server.llama_host
        self.port = settings.server.llama_port
        self.proc: Optional[asyncio.subprocess.Process] = None
        self.model_path: Optional[Path] = None
        self.pid_file = studio_root / "data" / "pids" / "llama-server.pid"
        self._client: Optional[httpx.AsyncClient] = None

    # ── API publique ─────────────────────────────────────────────────────────
    async def start(self) -> None:
        """Résout binaire + modèle, spawn le subprocess, attend /health 200."""
        binary = self._resolve_binary()
        if not binary.exists():
            raise RuntimeError(
                f"Binaire llama-server introuvable : {binary}. "
                f"Lancez scripts/fetch-binaries.sh ou compilez llama.cpp."
            )

        model = self._resolve_model()
        self.model_path = model
        logger.info("Modèle : %s (%.1f Mo)",
                    model.name, model.stat().st_size / (1024 * 1024))

        threads = self.settings.model.threads or max(1, (os.cpu_count() or 2) - 1)
        gpu_layers = (self.settings.model.gpu_layers
                      if self.settings.model.gpu_layers is not None
                      else self.backend.get("gpu_layers", 0))

        cmd = [
            str(binary),
            "-m", str(model),
            "-c", str(self.settings.model.context_size),
            "-t", str(threads),
            "--host", self.host,
            "--port", str(self.port),
            "-ngl", str(gpu_layers),
            "--log-disable",  # évite les traces sensibles dans stdout
        ]

        env = os.environ.copy()
        # Prépend le dossier des libs pour la résolution runtime des .so/.dylib/.dll.
        lib_dir = str(binary.parent)
        if self.platform["os"] == "linux":
            env["LD_LIBRARY_PATH"] = lib_dir + os.pathsep + env.get("LD_LIBRARY_PATH", "")
        elif self.platform["os"] == "darwin":
            env["DYLD_LIBRARY_PATH"] = lib_dir + os.pathsep + env.get("DYLD_LIBRARY_PATH", "")
        elif self.platform["os"] == "windows":
            env["PATH"] = lib_dir + os.pathsep + env.get("PATH", "")

        logger.info("Spawn llama-server : %s", " ".join(cmd[:6]) + " ...")
        self.proc = await asyncio.create_subprocess_exec(
            *cmd,
            env=env,
            stdout=asyncio.subprocess.DEVNULL,
            stderr=asyncio.subprocess.PIPE,
        )
        self._write_pid()

        # Health check bloquant (avec timeout)
        ok = await self._wait_health()
        if not ok:
            stderr = ""
            if self.proc.stderr:
                try:
                    stderr = (await asyncio.wait_for(
                        self.proc.stderr.read(4096), timeout=0.5)).decode(errors="ignore")
                except (asyncio.TimeoutError, Exception):
                    pass
            await self.stop()
            raise RuntimeError(
                f"llama-server n'a pas répondu 200 sur /health en "
                f"{_STARTUP_HEALTH_TIMEOUT}s. stderr={stderr[:400]}"
            )
        self._client = httpx.AsyncClient(
            base_url=f"http://{self.host}:{self.port}",
            timeout=httpx.Timeout(300.0, connect=5.0),
        )

    async def stop(self) -> None:
        """SIGTERM puis SIGKILL après grâce."""
        if self._client:
            try:
                await self._client.aclose()
            except Exception:  # noqa: BLE001
                pass
            self._client = None

        if not self.proc or self.proc.returncode is not None:
            self._clear_pid()
            return

        logger.info("Arrêt llama-server (pid=%d)...", self.proc.pid)
        try:
            self.proc.send_signal(signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            await asyncio.wait_for(self.proc.wait(), timeout=_STOP_GRACE)
        except asyncio.TimeoutError:
            logger.warning("Timeout sur SIGTERM, envoi SIGKILL.")
            try:
                self.proc.kill()
            except ProcessLookupError:
                pass
            await self.proc.wait()
        self._clear_pid()
        logger.info("llama-server arrêté.")

    async def reload_model(self) -> None:
        """Bascule vers un autre modèle : stop propre puis re-start.

        Le nouveau chemin doit être positionné dans self.settings.model.path
        avant d'appeler cette méthode (via ConfigLoader.save_settings).
        """
        logger.info("Rechargement du modèle demandé (path=%s)...",
                    self.settings.model.path)
        await self.stop()
        # Recharge fraîche des settings (par sécurité, en cas d'édition externe).
        from app.config.loader import load_settings
        self.settings = load_settings(force_reload=True)
        await self.start()
        logger.info("Rechargement terminé (modèle=%s).",
                    self.model_path.name if self.model_path else "?")

    async def health(self) -> dict:
        """Statut runtime (retourné par /health de l'orchestrateur)."""
        if not self.proc or self.proc.returncode is not None:
            return {"status": "stopped",
                    "model": self.model_path.name if self.model_path else None}
        if not self._client:
            return {"status": "starting"}
        try:
            r = await self._client.get("/health", timeout=2.0)
            body = r.json() if r.headers.get("content-type", "").startswith("application/json") else {}
            return {
                "status": "ok" if r.status_code == 200 else "unhealthy",
                "http_status": r.status_code,
                "model": self.model_path.name if self.model_path else None,
                "context_size": self.settings.model.context_size,
                "backend": self.backend["backend"],
                "upstream": body,
            }
        except Exception as e:  # noqa: BLE001
            return {"status": "unreachable", "error": str(e)[:200]}

    def client(self) -> Optional[httpx.AsyncClient]:
        """Retourne le client HTTP vers llama-server (None si non prêt)."""
        return self._client

    # ── Interne ──────────────────────────────────────────────────────────────
    def _resolve_binary(self) -> Path:
        plat_key = self.platform["key"]
        backend = self.backend["backend"]
        ext = ".exe" if self.platform["os"] == "windows" else ""
        return self.studio_root / "bin" / plat_key / backend / f"llama-server{ext}"

    def _resolve_model(self) -> Path:
        # Si un chemin explicite est configuré, on l'utilise.
        cfg = self.settings.model.path
        if cfg:
            p = Path(cfg)
            if not p.is_absolute():
                p = self.studio_root / p
            if not p.exists():
                raise RuntimeError(f"Modèle configuré introuvable : {p}")
            return p

        # Sinon on prend le premier .gguf par ordre alphabétique dans models/.
        models_dir = self.studio_root / "models"
        candidates = sorted(models_dir.glob("*.gguf"))
        if not candidates:
            raise RuntimeError(
                f"Aucun modèle .gguf dans {models_dir}. "
                f"Téléchargez-en un depuis huggingface.co."
            )
        return candidates[0]

    async def _wait_health(self) -> bool:
        url = f"http://{self.host}:{self.port}/health"
        deadline = asyncio.get_event_loop().time() + _STARTUP_HEALTH_TIMEOUT
        async with httpx.AsyncClient(timeout=2.0) as c:
            while asyncio.get_event_loop().time() < deadline:
                # Le processus a-t-il déjà crashé ?
                if self.proc and self.proc.returncode is not None:
                    return False
                try:
                    r = await c.get(url)
                    if r.status_code == 200:
                        return True
                except Exception:  # noqa: BLE001
                    pass
                await asyncio.sleep(_HEALTH_POLL_INTERVAL)
        return False

    def _write_pid(self) -> None:
        if not self.proc:
            return
        self.pid_file.parent.mkdir(parents=True, exist_ok=True)
        self.pid_file.write_text(str(self.proc.pid), encoding="utf-8")

    def _clear_pid(self) -> None:
        try:
            self.pid_file.unlink(missing_ok=True)
        except Exception:  # noqa: BLE001
            pass

"""
Rôle    : client MCP stdio (JSON-RPC 2.0 line-delimited). Chaque instance gère
          un subprocess de skill, un handshake `initialize` + `tools/list`, des
          appels `tools/call` timeoutés, la surveillance et le respawn (max N).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
Version : 1.0.6 (2026-09-25) — TRANSPORT PAR THREADS (au lieu des pipes asyncio).
          Sur la clé Windows, les skills restaient muets 120 s alors que le
          même code répond en < 1 s ailleurs : le transport asyncio (pipes
          overlapped de la boucle Proactor) est le seul maillon propre à
          Windows. On utilise désormais `subprocess.Popen` + deux threads
          lecteurs (stdout / stderr) qui remettent les lignes à la boucle via
          `call_soon_threadsafe` : fonctionne à l'identique quelle que soit la
          boucle asyncio (Proactor, Selector, uvloop).
          Autres durcissements :
            - enfant lancé en UTF-8 forcé (`-X utf8`, PYTHONIOENCODING) et sans
              fenêtre console (CREATE_NO_WINDOW) ;
            - la fin du stderr de l'enfant est jointe au message d'erreur et
              journalisée en WARNING : un échec n'est plus jamais muet ;
            - un skill « unavailable » peut être relancé à la demande (budget
              max_restart), pas seulement un skill « crashed ».
          Version 1.0.5 : délai de démarrage distinct du délai par appel.
"""
from __future__ import annotations

import asyncio
import collections
import json
import logging
import os
import subprocess
import sys
import threading
from pathlib import Path
from typing import Any, Optional

logger = logging.getLogger("studio.mcp.client")

_STDERR_TAIL = 30  # lignes de stderr conservées pour le diagnostic


class MCPError(Exception):
    """Erreur JSON-RPC retournée par un skill."""


def _child_env() -> dict:
    env = dict(os.environ)
    env["PYTHONUTF8"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    env["PYTHONUNBUFFERED"] = "1"
    # Évite d'écrire des __pycache__ sur la clé (lent, parfois en lecture seule).
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    return env


class MCPClient:
    """Un client = un skill = un subprocess Python."""

    def __init__(self, name: str, server_path: Path, cwd: Path,
                 timeout_sec: float = 30.0, max_restart: int = 3,
                 startup_timeout_sec: float | None = None):
        self.name = name
        self.server_path = server_path
        self.cwd = cwd
        self.timeout_sec = timeout_sec
        # Démarrage (handshake) : plus tolérant que les appels d'outils.
        self.startup_timeout_sec = startup_timeout_sec or max(timeout_sec, 120.0)
        self.max_restart = max_restart

        self.proc: Optional[subprocess.Popen] = None
        self.tools: list[dict] = []
        self.status: str = "stopped"  # stopped|starting|running|unavailable|crashed
        self.last_error: Optional[str] = None
        self.restart_count: int = 0

        self._id_counter: int = 0
        self._pending: dict[int, asyncio.Future] = {}
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        self._write_lock = threading.Lock()
        self._stderr_tail: collections.deque[str] = collections.deque(maxlen=_STDERR_TAIL)
        self._start_lock = asyncio.Lock()
        self._generation = 0  # invalide les lectures d'un ancien process

    # ── API publique ─────────────────────────────────────────────────────────
    async def start(self) -> None:
        """Spawn le subprocess + handshake initialize + tools/list."""
        async with self._start_lock:
            if self.status == "running":
                return
            self.status = "starting"
            self.last_error = None
            self._loop = asyncio.get_running_loop()
            self._stderr_tail.clear()

            python = sys.executable  # même interpréteur que l'orchestrateur
            cmd = [python, "-X", "utf8", "-u", str(self.server_path)]
            flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
            try:
                # Popen est bloquant (CreateProcess) : hors de la boucle.
                self.proc = await asyncio.to_thread(
                    subprocess.Popen, cmd,
                    cwd=str(self.cwd),
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    bufsize=0,
                    env=_child_env(),
                    creationflags=flags,
                )
            except Exception as e:  # noqa: BLE001
                self.status = "unavailable"
                self.last_error = f"spawn failed: {e}"
                raise

            self._generation += 1
            gen = self._generation
            threading.Thread(target=self._stdout_reader, args=(self.proc, gen),
                             name=f"mcp-{self.name}-out", daemon=True).start()
            threading.Thread(target=self._stderr_reader, args=(self.proc,),
                             name=f"mcp-{self.name}-err", daemon=True).start()

            try:
                await self._request("initialize",
                                    {"protocolVersion": "0.1",
                                     "clientInfo": {"name": "africaisoft-studio",
                                                    "version": "1.0.6"}},
                                    timeout=self.startup_timeout_sec)
                res = await self._request("tools/list", {},
                                          timeout=self.startup_timeout_sec)
                self.tools = res.get("tools", [])
                self.status = "running"
                logger.info("MCP skill '%s' prêt (%d outils).",
                            self.name, len(self.tools))
            except Exception as e:  # noqa: BLE001
                self.status = "unavailable"
                tail = self.stderr_tail()
                self.last_error = f"handshake failed: {e}" + (
                    f" | stderr: {tail}" if tail else "")
                if tail:
                    logger.warning("MCP '%s' stderr (fin) :\n%s", self.name, tail)
                await self._hard_stop()
                raise

    async def call(self, tool_name: str, arguments: dict[str, Any]) -> Any:
        """Appelle un outil. Timeout par appel (self.timeout_sec)."""
        if self.status != "running":
            # Relance à la demande si budget restant (crashed OU unavailable).
            if self.status in ("crashed", "unavailable") \
                    and self.restart_count < self.max_restart:
                await self._try_restart()
            if self.status != "running":
                raise MCPError(
                    {"code": -32603,
                     "message": f"skill '{self.name}' indisponible ({self.status})"})
        return await self._request("tools/call",
                                   {"name": tool_name, "arguments": arguments})

    async def stop(self) -> None:
        """Arrêt propre (terminate puis kill après 5 s)."""
        self.status = "stopped"
        proc = self.proc
        if not proc or proc.poll() is not None:
            return
        try:
            proc.terminate()
        except (ProcessLookupError, OSError):
            return
        try:
            await asyncio.to_thread(proc.wait, 5.0)
        except subprocess.TimeoutExpired:
            try:
                proc.kill()
            except (ProcessLookupError, OSError):
                pass

    def stderr_tail(self, n: int = 12) -> str:
        return "\n".join(list(self._stderr_tail)[-n:])

    def as_openai_tools(self) -> list[dict]:
        """Convertit `tools/list` en format OpenAI (function calling)."""
        out = []
        for t in self.tools:
            out.append({
                "type": "function",
                "function": {
                    "name": f"{self.name}__{t['name']}",  # préfixe pour éviter les collisions
                    "description": t.get("description", ""),
                    "parameters": t.get("inputSchema") or {"type": "object"},
                },
            })
        return out

    def snapshot(self) -> dict:
        """État introspectable pour /api/skills."""
        return {
            "name": self.name,
            "status": self.status,
            "restart_count": self.restart_count,
            "last_error": self.last_error,
            "tools": [{"name": t["name"], "description": t.get("description", "")}
                      for t in self.tools],
        }

    # ── Interne ──────────────────────────────────────────────────────────────
    async def _request(self, method: str, params: dict,
                       timeout: float | None = None) -> Any:
        proc = self.proc
        if proc is None or proc.stdin is None:
            raise MCPError({"code": -32603, "message": "process absent"})
        self._id_counter += 1
        req_id = self._id_counter
        msg = {"jsonrpc": "2.0", "id": req_id,
               "method": method, "params": params}
        fut: asyncio.Future = asyncio.get_running_loop().create_future()
        self._pending[req_id] = fut
        data = (json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8")
        try:
            await asyncio.to_thread(self._write, proc, data)
        except BaseException as e:  # noqa: BLE001 — y compris annulation
            self._pending.pop(req_id, None)
            if isinstance(e, Exception):
                raise MCPError({"code": -32603, "message": f"stdin write: {e}"})
            raise
        limit = timeout or self.timeout_sec
        try:
            resp = await asyncio.wait_for(fut, timeout=limit)
        except asyncio.TimeoutError:
            raise MCPError({"code": -32000,
                            "message": f"timeout {limit}s sur {method}"})
        finally:
            # Toujours retiré (timeout, annulation à l'arrêt…) : aucune future
            # orpheline ne recevra d'exception jamais lue.
            self._pending.pop(req_id, None)
        if "error" in resp:
            raise MCPError(resp["error"])
        return resp.get("result", {})

    def _write(self, proc: subprocess.Popen, data: bytes) -> None:
        with self._write_lock:
            proc.stdin.write(data)
            proc.stdin.flush()

    # Threads lecteurs : ne touchent JAMAIS l'état asyncio directement.
    def _stdout_reader(self, proc: subprocess.Popen, gen: int) -> None:
        try:
            for raw in iter(proc.stdout.readline, b""):
                self._post(self._on_line, raw, gen)
        except Exception as e:  # noqa: BLE001
            logger.debug("MCP '%s' lecture stdout interrompue : %s", self.name, e)
        self._post(self._on_eof, gen)

    def _stderr_reader(self, proc: subprocess.Popen) -> None:
        try:
            for raw in iter(proc.stderr.readline, b""):
                line = raw.decode("utf-8", errors="replace").rstrip()
                if line:
                    self._stderr_tail.append(line)
                    logger.debug("MCP[%s] stderr: %s", self.name, line)
        except Exception:  # noqa: BLE001
            pass

    def _post(self, fn, *args) -> None:
        loop = self._loop
        if loop is None or loop.is_closed():
            return
        try:
            loop.call_soon_threadsafe(fn, *args)
        except RuntimeError:  # boucle fermée pendant l'arrêt
            pass

    def _on_line(self, raw: bytes, gen: int) -> None:
        if gen != self._generation:
            return
        try:
            msg = json.loads(raw.decode("utf-8", errors="replace"))
        except json.JSONDecodeError:
            logger.debug("MCP '%s' non-JSON stdout: %s", self.name, raw[:120])
            return
        if not isinstance(msg, dict):
            return
        fut = self._pending.pop(msg.get("id"), None)
        if fut is not None and not fut.done():
            fut.set_result(msg)

    def _on_eof(self, gen: int) -> None:
        if gen != self._generation:
            return
        for fut in list(self._pending.values()):
            if not fut.done():
                fut.set_exception(MCPError({"code": -32000,
                                            "message": "process ended"}))
        self._pending.clear()
        if self.status == "running":
            self.status = "crashed"
            rc = self.proc.poll() if self.proc else "?"
            tail = self.stderr_tail()
            logger.warning("MCP skill '%s' terminé (rc=%s).%s", self.name, rc,
                           ("\n" + tail) if tail else "")

    async def _try_restart(self) -> None:
        self.restart_count += 1
        logger.info("MCP '%s' respawn (%d/%d)",
                    self.name, self.restart_count, self.max_restart)
        try:
            await self._hard_stop()
            await self.start()
        except Exception as e:  # noqa: BLE001
            self.last_error = f"restart failed: {e}"
            self.status = "unavailable" if self.restart_count >= self.max_restart else "crashed"

    async def _hard_stop(self) -> None:
        proc = self.proc
        self._generation += 1  # ignore toute ligne tardive de l'ancien process
        if proc and proc.poll() is None:
            try:
                proc.kill()
            except (ProcessLookupError, OSError):
                pass
            try:
                await asyncio.to_thread(proc.wait, 2.0)
            except subprocess.TimeoutExpired:
                pass
        if proc:
            for s in (proc.stdin, proc.stdout, proc.stderr):
                try:
                    if s:
                        s.close()
                except Exception:  # noqa: BLE001
                    pass
        self.proc = None

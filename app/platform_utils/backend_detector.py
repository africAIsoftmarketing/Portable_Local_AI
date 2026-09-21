"""
Rôle    : détection du backend d'inférence à utiliser (CUDA > ROCm > Vulkan > CPU,
          Metal sur macOS). Chaque sonde a un timeout individuel de 1 s, budget total < 5 s.
          Retourne le backend et la raison explicite pour traçabilité.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import logging
import shutil
import subprocess
from pathlib import Path

from app.config.models import Settings

logger = logging.getLogger("studio.backend")

_PROBE_TIMEOUT_S = 1.0


def _run(cmd: list[str]) -> tuple[bool, str]:
    """Exécute une commande, timeout 1 s, retourne (succès, stdout tronqué)."""
    if not shutil.which(cmd[0]):
        return False, f"binaire absent: {cmd[0]}"
    try:
        r = subprocess.run(cmd, capture_output=True, text=True,
                           timeout=_PROBE_TIMEOUT_S, check=False)
        return r.returncode == 0, r.stdout[:200]
    except subprocess.TimeoutExpired:
        return False, "timeout"
    except Exception as e:  # noqa: BLE001
        return False, f"exception: {e}"


def _binary_exists(studio_root: Path, plat_key: str, backend: str) -> bool:
    """Vérifie qu'un binaire llama-server est bien présent pour ce couple plat/backend."""
    ext = ".exe" if plat_key.startswith("windows") else ""
    return (studio_root / "bin" / plat_key / backend / f"llama-server{ext}").exists()


def detect_backend(plat: dict, settings: Settings) -> dict:
    """
    Décide du backend selon l'arbre : override > macOS Metal > CUDA > ROCm > Vulkan > CPU.
    Vérifie systématiquement que le binaire correspondant est disponible avant de valider.
    """
    from app.config.loader import STUDIO_ROOT

    forced = settings.platform.backend
    plat_key = plat["key"]

    # ── Override utilisateur ──────────────────────────────────────────────────
    if forced != "auto":
        if _binary_exists(STUDIO_ROOT, plat_key, forced):
            return {"backend": forced,
                    "reason": f"forcé par config (platform.backend={forced})",
                    "gpu_layers": settings.platform.gpu_layers if settings.platform.gpu_layers is not None else 999,
                    "binary_available": True}
        return {"backend": forced,
                "reason": f"forcé mais binaire manquant : bin/{plat_key}/{forced}/",
                "gpu_layers": 0,
                "binary_available": False}

    # ── macOS : Metal natif ───────────────────────────────────────────────────
    if plat["os"] == "darwin":
        if _binary_exists(STUDIO_ROOT, plat_key, "metal"):
            return {"backend": "metal",
                    "reason": "macOS natif",
                    "gpu_layers": 999,
                    "binary_available": True}
        return _cpu_fallback(STUDIO_ROOT, plat_key, "binaire Metal absent")

    # ── Linux / Windows : arbre GPU ───────────────────────────────────────────
    if plat["os"] in ("linux", "windows"):
        # CUDA
        ok, out = _run(["nvidia-smi", "--query-gpu=name", "--format=csv,noheader"])
        if ok and out.strip() and _binary_exists(STUDIO_ROOT, plat_key, "cuda"):
            return {"backend": "cuda",
                    "reason": f"nvidia-smi ok ({out.strip().splitlines()[0][:50]})",
                    "gpu_layers": 999, "binary_available": True}

        # ROCm (Linux uniquement, non bloquant)
        if plat["os"] == "linux":
            ok, out = _run(["rocm-smi", "--showproductname"])
            if ok and _binary_exists(STUDIO_ROOT, plat_key, "rocm"):
                return {"backend": "rocm",
                        "reason": "rocm-smi ok",
                        "gpu_layers": 999, "binary_available": True}

        # Vulkan
        ok, out = _run(["vulkaninfo", "--summary"])
        if ok and ("DISCRETE_GPU" in out or "INTEGRATED_GPU" in out) \
                and _binary_exists(STUDIO_ROOT, plat_key, "vulkan"):
            return {"backend": "vulkan",
                    "reason": "vulkaninfo ok",
                    "gpu_layers": 999, "binary_available": True}

    # ── Fallback CPU ──────────────────────────────────────────────────────────
    return _cpu_fallback(STUDIO_ROOT, plat_key, "aucun GPU utilisable détecté")


def _cpu_fallback(studio_root: Path, plat_key: str, reason: str) -> dict:
    available = _binary_exists(studio_root, plat_key, "cpu")
    return {
        "backend": "cpu",
        "reason": reason,
        "gpu_layers": 0,
        "binary_available": available,
    }

"""
Rôle    : détection du backend d'inférence à utiliser (CUDA > ROCm > Vulkan > CPU,
          Metal sur macOS). Chaque sonde a un timeout individuel de 1 s.
          Retourne un `reason_code` machine (à traduire côté UI) plus des
          paramètres. Le champ `reason` humain est conservé pour compat mais
          contient uniquement le code en anglais neutre.
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
    if not shutil.which(cmd[0]):
        return False, f"binary absent: {cmd[0]}"
    try:
        r = subprocess.run(cmd, capture_output=True, text=True,
                           timeout=_PROBE_TIMEOUT_S, check=False)
        return r.returncode == 0, r.stdout[:200]
    except subprocess.TimeoutExpired:
        return False, "timeout"
    except Exception as e:  # noqa: BLE001
        return False, f"exception: {e}"


def _binary_exists(studio_root: Path, plat_key: str, backend: str) -> bool:
    ext = ".exe" if plat_key.startswith("windows") else ""
    return (studio_root / "bin" / plat_key / backend / f"llama-server{ext}").exists()


def _result(backend: str, code: str, params: dict, gpu_layers: int,
            binary_available: bool) -> dict:
    """Construit un résultat homogène avec code neutre et paramètres."""
    return {
        "backend": backend,
        "reason_code": code,
        "reason_params": params,
        # `reason` conservé pour rétrocompat (clients qui n'ont pas migré vers le code).
        "reason": code,
        "gpu_layers": gpu_layers,
        "binary_available": binary_available,
    }


def detect_backend(plat: dict, settings: Settings) -> dict:
    """
    Décide du backend selon l'arbre : override > macOS Metal > CUDA > ROCm > Vulkan > CPU.
    Retourne un reason_code (à localiser côté UI) et des paramètres pour interpolation.
    """
    from app.config.loader import STUDIO_ROOT

    forced = settings.platform.backend
    plat_key = plat["key"]

    # ── Override utilisateur ──────────────────────────────────────────────────
    if forced != "auto":
        if _binary_exists(STUDIO_ROOT, plat_key, forced):
            gl = settings.platform.gpu_layers if settings.platform.gpu_layers is not None else 999
            return _result(forced, "forced_by_config",
                           {"backend": forced}, gl, True)
        return _result(forced, "forced_binary_missing",
                       {"backend": forced, "path": f"bin/{plat_key}/{forced}/"},
                       0, False)

    # ── macOS : Metal natif ───────────────────────────────────────────────────
    if plat["os"] == "darwin":
        if _binary_exists(STUDIO_ROOT, plat_key, "metal"):
            return _result("metal", "metal_native", {}, 999, True)
        return _result("cpu", "metal_binary_missing", {}, 0,
                       _binary_exists(STUDIO_ROOT, plat_key, "cpu"))

    # ── Linux / Windows : arbre GPU ───────────────────────────────────────────
    if plat["os"] in ("linux", "windows"):
        # CUDA
        ok, out = _run(["nvidia-smi", "--query-gpu=name", "--format=csv,noheader"])
        if ok and out.strip() and _binary_exists(STUDIO_ROOT, plat_key, "cuda"):
            gpu_name = out.strip().splitlines()[0][:60] if out.strip() else ""
            return _result("cuda", "cuda_found", {"gpu": gpu_name}, 999, True)

        # ROCm (Linux uniquement)
        if plat["os"] == "linux":
            ok, _ = _run(["rocm-smi", "--showproductname"])
            if ok and _binary_exists(STUDIO_ROOT, plat_key, "rocm"):
                return _result("rocm", "rocm_found", {}, 999, True)

        # Vulkan
        ok, out = _run(["vulkaninfo", "--summary"])
        if ok and ("DISCRETE_GPU" in out or "INTEGRATED_GPU" in out) \
                and _binary_exists(STUDIO_ROOT, plat_key, "vulkan"):
            return _result("vulkan", "vulkan_found", {}, 999, True)

    # ── Fallback CPU ──────────────────────────────────────────────────────────
    available = _binary_exists(STUDIO_ROOT, plat_key, "cpu")
    return _result("cpu", "no_gpu_detected", {}, 0, available)

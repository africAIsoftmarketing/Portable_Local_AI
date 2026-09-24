"""
Rôle    : détection OS + architecture normalisée pour le nommage bin/<plat>/<backend>/.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
Version : 0.6.1 (2026-09-24) — Windows x64 → clé « windows » (nommage
          verrouillé de la master copy : bin/windows/), avec repli
          rétrocompatible sur l'ancien bin/windows-x86_64/ s'il est seul présent.
"""
from __future__ import annotations

import platform
from pathlib import Path
from typing import Optional


def _windows_x64_key(studio_root: Optional[Path] = None) -> str:
    """
    Clé bin/ de Windows x64. Nommage verrouillé du master copy : « windows ».
    Repli sur l'ancien « windows-x86_64 » UNIQUEMENT s'il est seul présent
    (installations construites avant l'alignement du nommage).
    """
    if studio_root is None:
        try:
            from app.config.loader import STUDIO_ROOT  # import tardif : pas de cycle
            studio_root = STUDIO_ROOT
        except Exception:  # noqa: BLE001 — la détection ne doit jamais échouer
            return "windows"
    bin_dir = studio_root / "bin"
    if not (bin_dir / "windows").is_dir() and (bin_dir / "windows-x86_64").is_dir():
        return "windows-x86_64"
    return "windows"


def detect_platform() -> dict:
    """
    Retourne {os, arch, key} où key correspond au sous-dossier bin/<key>/.
    Ex. Linux x86_64 → "linux-x86_64", macOS ARM → "darwin-arm64".
    """
    system = platform.system().lower()  # linux, darwin, windows
    machine = platform.machine().lower()

    # Normalisation architecture (variantes possibles selon les OS).
    arch_map = {
        "x86_64": "x86_64", "amd64": "x86_64",
        "aarch64": "aarch64", "arm64": "arm64",
        "armv8l": "aarch64",
    }
    arch = arch_map.get(machine, machine)

    # macOS utilise arm64 (pas aarch64) dans le nommage bin/.
    if system == "darwin" and arch == "aarch64":
        arch = "arm64"

    key = f"{system}-{arch}"
    # Windows x64 : dossier sans suffixe d'architecture (bin/windows/).
    if system == "windows" and arch == "x86_64":
        key = _windows_x64_key()
    return {
        "os": system,
        "arch": arch,
        "key": key,
        "python_version": platform.python_version(),
        "platform_string": platform.platform(),
    }

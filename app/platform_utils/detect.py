"""
Rôle    : détection OS + architecture normalisée pour le nommage bin/<plat>/<backend>/.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import platform


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
    return {
        "os": system,
        "arch": arch,
        "key": key,
        "python_version": platform.python_version(),
        "platform_string": platform.platform(),
    }

"""
Rôle    : configuration du logger racine (format à colonnes, rotation optionnelle).
          Aucune donnée utilisateur écrite en clair (métadonnées uniquement).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import logging
import os
import sys
from logging.handlers import RotatingFileHandler
from pathlib import Path

from app.config.loader import STUDIO_ROOT


_FORMAT = "%(asctime)s %(levelname)-5s %(name)-20s %(message)s"
_DATEFMT = "%Y-%m-%dT%H:%M:%S"


def configure_logging() -> None:
    """Configure le logger racine (idempotent)."""
    root = logging.getLogger()
    if getattr(root, "_studio_configured", False):
        return

    level_name = os.environ.get("STUDIO_LOG_LEVEL", "INFO").upper()
    level = getattr(logging, level_name, logging.INFO)
    root.setLevel(level)

    # Nettoyage des handlers existants (uvicorn en installe déjà).
    for h in list(root.handlers):
        root.removeHandler(h)

    fmt = logging.Formatter(_FORMAT, datefmt=_DATEFMT)

    # Handler stdout (toujours).
    sh = logging.StreamHandler(sys.stdout)
    sh.setFormatter(fmt)
    root.addHandler(sh)

    # Handler fichier (rotation 1 Mo × 5).
    logs_dir = STUDIO_ROOT / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)
    fh = RotatingFileHandler(
        logs_dir / "runtime.log",
        maxBytes=1_000_000,
        backupCount=5,
        encoding="utf-8",
    )
    fh.setFormatter(fmt)
    root.addHandler(fh)

    # Handler dédié startup (append-only, séparé).
    sh_startup = logging.FileHandler(logs_dir / "startup.log", encoding="utf-8")
    sh_startup.setLevel(logging.INFO)
    sh_startup.setFormatter(fmt)
    logging.getLogger("studio").addHandler(sh_startup)

    # Réduit le bruit d'uvicorn en INFO.
    logging.getLogger("uvicorn.access").setLevel(logging.WARNING)
    logging.getLogger("httpx").setLevel(logging.WARNING)

    root._studio_configured = True  # type: ignore[attr-defined]

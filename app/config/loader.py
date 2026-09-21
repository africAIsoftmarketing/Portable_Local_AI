"""
Rôle    : chargement + validation stricte de config/settings.json.
          Fournit la constante STUDIO_ROOT (racine du dépôt/clé USB) utilisée partout.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import json
import logging
import os
from pathlib import Path
from threading import Lock

from pydantic import ValidationError

from app.config.models import Settings

logger = logging.getLogger("studio.config")

# Racine du dépôt = parent du package app/.
STUDIO_ROOT: Path = Path(__file__).resolve().parent.parent.parent

# Cache mémoire + verrou pour recharges concurrentes.
_cache: dict[str, Settings] = {}
_cache_lock = Lock()


def _config_file() -> Path:
    return STUDIO_ROOT / "config" / "settings.json"


def load_settings(force_reload: bool = False) -> Settings:
    """Charge et valide settings.json. Cache le résultat sauf force_reload=True."""
    key = str(_config_file())
    with _cache_lock:
        if not force_reload and key in _cache:
            return _cache[key]

        cfg_path = _config_file()
        if not cfg_path.exists():
            logger.warning("config/settings.json absent, valeurs par défaut appliquées.")
            settings = Settings()
            _cache[key] = settings
            return settings

        try:
            raw = json.loads(cfg_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            raise RuntimeError(f"settings.json invalide (JSON) : {e}") from e

        try:
            settings = Settings.model_validate(raw)
        except ValidationError as e:
            raise RuntimeError(f"settings.json invalide (schéma) : {e}") from e

        # Override par variables d'environnement (utile dans le dev container).
        env_bind = os.environ.get("STUDIO_BIND_HOST")
        env_port = os.environ.get("STUDIO_PORT")
        if env_bind:
            settings.server.bind_host = env_bind
        if env_port:
            try:
                settings.server.port = int(env_port)
            except ValueError:
                logger.warning("STUDIO_PORT invalide : %s", env_port)

        _cache[key] = settings
        return settings


def save_settings(settings: Settings) -> None:
    """Écrit atomiquement settings.json (validation Pydantic implicite)."""
    cfg_path = _config_file()
    cfg_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = cfg_path.with_suffix(".json.tmp")
    tmp.write_text(
        json.dumps(settings.model_dump(mode="json"), indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    tmp.replace(cfg_path)
    with _cache_lock:
        _cache[str(cfg_path)] = settings

"""
Rôle    : résolution multi-source du system prompt selon la priorité :
          [1] override par requête (si non locked)
          [2] preset actif (config/system_prompts/<id>.txt)
          [3] fichier custom (config/system_prompt.txt)
          [4] défaut fabricant (config/system_prompt.default.txt)
          Si locked=true dans settings, la valeur n'est jamais exposée par l'API
          et les overrides sont silencieusement ignorés.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

from pathlib import Path
from typing import Optional

from app.config.loader import STUDIO_ROOT
from app.config.models import Settings

CONFIG_DIR = STUDIO_ROOT / "config"
SYSTEM_PROMPT_FILE = CONFIG_DIR / "system_prompt.txt"
DEFAULT_SYSTEM_PROMPT_FILE = CONFIG_DIR / "system_prompt.default.txt"
PRESETS_DIR = CONFIG_DIR / "system_prompts"


def resolve_system_prompt(settings: Settings,
                          request_override: Optional[str] = None) -> Optional[str]:
    """
    Résout le prompt système effectif pour une requête. Retourne None si aucun
    prompt n'est disponible (aucun fichier).
    """
    # [1] Override par requête, uniquement si non locked.
    if request_override and not settings.system_prompt.locked:
        return request_override

    # Si locked, les overrides sont ignorés silencieusement (pas d'erreur remontée).
    # On retombe sur la source configurée.

    # [2] Preset actif
    active = settings.system_prompt.active_preset
    if active:
        preset_path = PRESETS_DIR / f"{active}.txt"
        if preset_path.exists():
            return preset_path.read_text(encoding="utf-8").strip()

    # [3] Fichier custom
    if SYSTEM_PROMPT_FILE.exists():
        return SYSTEM_PROMPT_FILE.read_text(encoding="utf-8").strip()

    # [4] Défaut fabricant
    if DEFAULT_SYSTEM_PROMPT_FILE.exists():
        return DEFAULT_SYSTEM_PROMPT_FILE.read_text(encoding="utf-8").strip()

    return None


def read_current_prompt() -> str:
    """Retourne le contenu actuellement stocké (custom > défaut)."""
    if SYSTEM_PROMPT_FILE.exists():
        return SYSTEM_PROMPT_FILE.read_text(encoding="utf-8")
    if DEFAULT_SYSTEM_PROMPT_FILE.exists():
        return DEFAULT_SYSTEM_PROMPT_FILE.read_text(encoding="utf-8")
    return ""


def write_prompt(content: str) -> None:
    """Écrit atomiquement config/system_prompt.txt."""
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    tmp = SYSTEM_PROMPT_FILE.with_suffix(".txt.tmp")
    tmp.write_text(content, encoding="utf-8")
    tmp.replace(SYSTEM_PROMPT_FILE)


def approximate_token_count(text: str) -> int:
    """
    Compteur de tokens approximatif (~4 caractères/token pour l'anglais,
    ~3,5 pour le français). Ne remplace pas un vrai tokenizer mais suffit
    comme indication côté UI. Le compteur précis se fait en JS côté client.
    """
    if not text:
        return 0
    # Moyenne pondérée simple.
    return max(1, round(len(text) / 3.8))

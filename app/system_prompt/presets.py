"""
Rôle    : gestion de la bibliothèque de presets (config/system_prompts/*.txt).
          Un preset = un fichier texte identifié par son nom (sans extension).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

from typing import Optional

from app.system_prompt.resolver import PRESETS_DIR


def list_presets() -> list[dict]:
    """Liste tous les presets disponibles avec métadonnées basiques."""
    items = []
    if not PRESETS_DIR.exists():
        return items
    for p in sorted(PRESETS_DIR.glob("*.txt")):
        try:
            content = p.read_text(encoding="utf-8")
            first_line = content.strip().splitlines()[0] if content.strip() else ""
            items.append({
                "id": p.stem,
                "preview": first_line[:160],
                "char_count": len(content),
            })
        except Exception:  # noqa: BLE001
            continue
    return items


def load_preset(preset_id: str) -> Optional[str]:
    """Charge le contenu d'un preset ou None si absent."""
    # Anti path-traversal simple : id alphanumérique + tirets/underscores.
    if not preset_id.replace("_", "").replace("-", "").isalnum():
        return None
    p = PRESETS_DIR / f"{preset_id}.txt"
    if not p.exists():
        return None
    return p.read_text(encoding="utf-8").strip()

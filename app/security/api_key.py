"""
Rôle    : génération et lecture de la clé API stockée dans config/api_key.txt.
          Générée au 1er lancement (32 octets base64url). Permissions 600 sur POSIX.
          JAMAIS écrite en logs.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import base64
import os
import secrets
from pathlib import Path


def ensure_api_key(path: Path) -> str:
    """
    Retourne la clé API. Si le fichier n'existe pas, en crée une nouvelle.
    Ne loggue JAMAIS la clé complète (le logger appelant doit tronquer).
    """
    if path.exists():
        content = path.read_text(encoding="utf-8").strip()
        if content:
            return content

    key = _generate_key()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(key, encoding="utf-8")

    # Restreint les permissions sur POSIX (Windows : ACL par défaut suffisant).
    if os.name == "posix":
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass
    return key


def _generate_key() -> str:
    """Génère 32 octets urandom encodés en base64url (~43 caractères)."""
    return base64.urlsafe_b64encode(secrets.token_bytes(32)).rstrip(b"=").decode("ascii")

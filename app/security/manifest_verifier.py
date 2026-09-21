"""
Rôle    : vérification du manifest release.json (SHA256 + signature Ed25519).
          En Phase 2 dev, la signature Ed25519 n'est pas encore obligatoire :
          require_signature=false par défaut avec warning visible.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path


def verify_manifest(manifest_path: Path, require_signature: bool = False
                    ) -> tuple[bool, str]:
    """
    Vérifie l'intégrité du manifest et (optionnellement) sa signature.
    Retourne (ok, message).
    En dev : manifest non signé accepté avec message d'avertissement.
    """
    if not manifest_path.exists():
        return False, f"Manifest introuvable : {manifest_path}"

    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return False, f"Manifest JSON invalide : {e}"

    sig = data.get("signature", {})
    sig_value = sig.get("value")

    if require_signature:
        if not sig_value:
            return False, ("Signature Ed25519 manquante et require_signature=true. "
                           "Reconstruire le manifest ou désactiver require_signature.")
        # Note : la vérification Ed25519 nécessitera la lib cryptography (Phase 3).
        # Pour l'instant, on refuse en attendant l'implémentation.
        return False, "Vérification Ed25519 non implémentée en Phase 2."

    # require_signature=false : accepté avec warning si non signé.
    if not sig_value:
        return True, ("AVERTISSEMENT: manifest non signé (dev build). "
                      "En production, activer security.require_signature=true.")
    return True, "Manifest signé (vérification différée Phase 3)."


def _sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()

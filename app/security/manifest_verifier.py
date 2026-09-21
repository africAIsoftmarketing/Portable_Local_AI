"""
Rôle    : vérification effective du manifest release.json.
          - En Phase 6, la signature Ed25519 est réellement vérifiée si
            security.require_signature=true. La clé publique attendue est
            lue depuis config/public.pem (embarquée dans la clé USB à la
            release). La signature détachée est attendue à release.json.sig
            (produite par scripts/sign-release.py).
          - Si require_signature=false : accepte sans vérification, mais
            renvoie un message d'AVERTISSEMENT visible.
          - Si `cryptography` n'est pas disponible : refus explicite si
            require_signature=true, warning sinon.
Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
"""
from __future__ import annotations

import base64
import json
from pathlib import Path


def verify_manifest(manifest_path: Path, require_signature: bool = False
                    ) -> tuple[bool, str]:
    """
    Vérifie l'intégrité du manifest et (optionnellement) sa signature Ed25519.

    Retourne (ok, message).
    - ok=True + message vide/warning : démarrage autorisé.
    - ok=False + message d'erreur : démarrage refusé.
    """
    if not manifest_path.exists():
        # Manifest absent : toléré en dev, refusé en prod signée.
        if require_signature:
            return False, f"Manifest introuvable et require_signature=true : {manifest_path}"
        return True, f"AVERTISSEMENT: manifest absent ({manifest_path}). Dev build."

    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return False, f"Manifest JSON invalide : {e}"

    sig_file = manifest_path.with_suffix(manifest_path.suffix + ".sig")
    pubkey_file = manifest_path.parent / "config" / "public.pem"

    if not require_signature:
        # Dev/portable non-signé : accepté avec avertissement clair.
        if not sig_file.exists():
            return True, ("AVERTISSEMENT: manifest non signé (dev build). "
                          "En production, activer security.require_signature=true "
                          "et fournir release.json.sig + config/public.pem.")
        # Signature présente mais non exigée : on tente quand même une
        # vérification informative (sans bloquer si elle échoue).
        ok, msg = _verify_ed25519(manifest_path, sig_file, pubkey_file)
        if ok:
            return True, f"Signature Ed25519 vérifiée (mode informatif) : {msg}"
        return True, f"AVERTISSEMENT: signature présente mais non vérifiée : {msg}"

    # require_signature=true : vérification stricte, refus en cas d'échec.
    if not sig_file.exists():
        return False, (f"Signature Ed25519 manquante ({sig_file}) et "
                       "require_signature=true. Générez-la avec "
                       "`python3 scripts/sign-release.py sign release.json`.")
    if not pubkey_file.exists():
        return False, (f"Clé publique Ed25519 attendue à {pubkey_file} et "
                       "require_signature=true.")
    return _verify_ed25519(manifest_path, sig_file, pubkey_file)


def _verify_ed25519(manifest_path: Path, sig_path: Path,
                    pubkey_path: Path) -> tuple[bool, str]:
    """Vérification Ed25519 réelle via `cryptography`. Signature déchiffrée
    en base64 depuis sig_path, comparée à la forme canonique du manifest."""
    try:
        from cryptography.exceptions import InvalidSignature
        from cryptography.hazmat.primitives import serialization
    except ImportError:
        return False, ("Paquet `cryptography` requis pour la vérification "
                       "Ed25519. `pip install cryptography`.")

    try:
        pub = serialization.load_pem_public_key(pubkey_path.read_bytes())
    except Exception as e:  # noqa: BLE001
        return False, f"Clé publique invalide ({pubkey_path}) : {e}"

    try:
        sig = base64.b64decode(sig_path.read_text(encoding="utf-8").strip())
    except Exception as e:  # noqa: BLE001
        return False, f"Signature illisible ({sig_path}) : {e}"

    # Représentation canonique identique à scripts/sign-release.py :
    # JSON UTF-8, clés triées, séparateurs compacts, sans espace.
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
        payload = json.dumps(data, sort_keys=True, separators=(",", ":"),
                             ensure_ascii=False).encode("utf-8")
    except json.JSONDecodeError as e:
        return False, f"Manifest JSON invalide : {e}"

    try:
        pub.verify(sig, payload)
    except InvalidSignature:
        return False, ("Signature Ed25519 INVALIDE : le manifest ou la "
                       "signature ont été modifiés.")
    except Exception as e:  # noqa: BLE001
        return False, f"Erreur de vérification : {e}"

    return True, f"OK (clé {pubkey_path.name}, {len(sig)}o de signature)"

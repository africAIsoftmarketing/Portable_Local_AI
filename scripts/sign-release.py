#!/usr/bin/env python3
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : signature Ed25519 du fichier release.json d'une distribution
#           portable AfricAIsoft. Deux commandes :
#             - generate-keypair : crée keys/private.pem + keys/public.pem
#             - sign <release.json> : produit <release.json>.sig (base64)
#             - verify <release.json> : vérifie la signature avec la clé publique
#           La clé publique fabricant est aussi embarquée dans le studio à
#           l'emplacement config/public.pem. Le démarrage refuse (ou avertit)
#           si la signature est manquante/invalide selon
#           settings.json → security.require_signature.
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
# Deps    : cryptography (dans le venv, déjà présent).
# ─────────────────────────────────────────────────────────────────────────────
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import sys
from pathlib import Path


def _die(msg: str, code: int = 1):
    print(f"[sign-release] ERREUR : {msg}", file=sys.stderr)
    sys.exit(code)


def _canonical(release_json_path: Path) -> bytes:
    """Représentation canonique du release.json pour signature reproductible :
    JSON canonique UTF-8, clés triées, séparateurs compacts, sans espace."""
    data = json.loads(release_json_path.read_text(encoding="utf-8"))
    return json.dumps(data, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=False).encode("utf-8")


def cmd_generate_keypair(out_dir: Path):
    try:
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
        from cryptography.hazmat.primitives import serialization
    except ImportError:
        _die("Le paquet 'cryptography' est requis (pip install cryptography).")

    out_dir.mkdir(parents=True, exist_ok=True)
    priv = Ed25519PrivateKey.generate()
    priv_pem = priv.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    pub_pem = priv.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    (out_dir / "private.pem").write_bytes(priv_pem)
    try:
        os.chmod(out_dir / "private.pem", 0o600)
    except OSError:
        pass  # Windows : NTFS ACL, hors périmètre
    (out_dir / "public.pem").write_bytes(pub_pem)
    print(f"[sign-release] Paire générée dans {out_dir}/")
    print(f"  - private.pem  (mode 0600)")
    print(f"  - public.pem   (à distribuer, à embarquer dans config/public.pem)")
    print(f"  Fingerprint (SHA-256 de la clé publique DER) :")
    der = priv.public_key().public_bytes(
        encoding=serialization.Encoding.DER,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    print(f"    {hashlib.sha256(der).hexdigest()}")


def cmd_sign(release_json: Path, key_path: Path):
    try:
        from cryptography.hazmat.primitives import serialization
    except ImportError:
        _die("Le paquet 'cryptography' est requis.")
    if not release_json.exists():
        _die(f"release.json introuvable : {release_json}")
    if not key_path.exists():
        _die(f"Clé privée introuvable : {key_path}")
    priv = serialization.load_pem_private_key(key_path.read_bytes(), password=None)
    payload = _canonical(release_json)
    sig = priv.sign(payload)
    sig_b64 = base64.b64encode(sig).decode("ascii")
    out = release_json.with_suffix(release_json.suffix + ".sig")
    out.write_text(sig_b64 + "\n", encoding="utf-8")
    print(f"[sign-release] Signature écrite : {out}")
    print(f"  Digest SHA-256 payload canonique : {hashlib.sha256(payload).hexdigest()}")


def cmd_verify(release_json: Path, key_path: Path) -> bool:
    try:
        from cryptography.hazmat.primitives import serialization
        from cryptography.exceptions import InvalidSignature
    except ImportError:
        _die("Le paquet 'cryptography' est requis.")
    sig_path = release_json.with_suffix(release_json.suffix + ".sig")
    if not sig_path.exists():
        print(f"[sign-release] AUCUNE signature : {sig_path}")
        return False
    pub = serialization.load_pem_public_key(key_path.read_bytes())
    sig = base64.b64decode(sig_path.read_text().strip())
    try:
        pub.verify(sig, _canonical(release_json))
    except InvalidSignature:
        print(f"[sign-release] Signature INVALIDE")
        return False
    print(f"[sign-release] Signature OK")
    return True


def main():
    parser = argparse.ArgumentParser(description="Signature Ed25519 d'une release AfricAIsoft.")
    sub = parser.add_subparsers(dest="cmd", required=True)
    s_kg = sub.add_parser("generate-keypair", help="Génère une paire Ed25519.")
    s_kg.add_argument("--out", type=Path, default=Path("keys"))
    s_sg = sub.add_parser("sign", help="Signe un release.json.")
    s_sg.add_argument("release_json", type=Path)
    s_sg.add_argument("--key", type=Path, default=Path("keys/private.pem"))
    s_vf = sub.add_parser("verify", help="Vérifie la signature.")
    s_vf.add_argument("release_json", type=Path)
    s_vf.add_argument("--key", type=Path, default=Path("keys/public.pem"))
    args = parser.parse_args()

    if args.cmd == "generate-keypair":
        cmd_generate_keypair(args.out)
    elif args.cmd == "sign":
        cmd_sign(args.release_json, args.key)
    elif args.cmd == "verify":
        ok = cmd_verify(args.release_json, args.key)
        sys.exit(0 if ok else 2)


if __name__ == "__main__":
    main()

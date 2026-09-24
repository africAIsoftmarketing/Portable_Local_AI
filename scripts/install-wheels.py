"""
Rôle    : prépare le Python « embeddable » de Windows sans pip ni réseau.
          1. Active site-packages dans le fichier python3XX._pth (le Python
             embeddable ignore PYTHONPATH et n'importe `site` que si le ._pth
             le demande). Les sous-processus lancés avec sys.executable
             (serveurs MCP) en bénéficient aussi.
          2. Installe les wheels embarquées (bin/<plat>/python/wheels) en les
             décompressant dans Lib/site-packages — une wheel est une archive
             zip installable par simple extraction (PEP 427). Idempotent :
             une wheel déjà présente (dist-info existant) n'est pas réextraite.
          Bibliothèque standard uniquement (exécutable par l'embeddable nu).
Usage   : python install-wheels.py <dossier_python> <dossier_wheels>
Codes   : 0 = prêt · 1 = erreur (message sur stderr)
Version : 0.6.1 (2026-09-24)
Auteur  : AfricAIsoft — Licence : MIT
"""
from __future__ import annotations

import shutil
import sys
import zipfile
from pathlib import Path

SITE_REL = "Lib\\site-packages"


def ensure_pth(python_dir: Path) -> bool:
    """Ajoute « Lib\\site-packages » et « import site » au ._pth. True si modifié."""
    pths = sorted(python_dir.glob("python3*._pth"))
    if not pths:
        return False  # pas un Python embeddable : rien à faire
    pth = pths[0]
    raw = pth.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    lines = raw.decode("utf-8").splitlines()
    out: list[str] = []
    for line in lines:
        # « #import site » (valeur par défaut) → « import site »
        out.append("import site" if line.strip().lstrip("#").strip() == "import site" else line)
    if SITE_REL not in out:
        pos = out.index("import site") if "import site" in out else len(out)
        out.insert(pos, SITE_REL)
    if "import site" not in out:
        out.append("import site")
    if out == lines:
        return False
    pth.write_bytes((newline.join(out) + newline).encode("utf-8"))
    return True


def _dist_info_name(zf: zipfile.ZipFile) -> str | None:
    for name in zf.namelist():
        top = name.split("/", 1)[0]
        if top.endswith(".dist-info"):
            return top
    return None


def install_wheel(whl: Path, site: Path) -> bool:
    """Extrait une wheel dans site-packages. True si installée, False si déjà présente."""
    with zipfile.ZipFile(whl) as zf:
        dist_info = _dist_info_name(zf)
        if dist_info is None:
            raise ValueError(f"wheel invalide (pas de .dist-info) : {whl.name}")
        if (site / dist_info).is_dir():
            return False
        site_root = site.resolve()
        for info in zf.infolist():
            name = info.filename
            if name.endswith("/"):
                continue
            parts = name.split("/")
            # <nom>.data/purelib|platlib/... → racine de site-packages ;
            # scripts/headers/data ne sont pas nécessaires à l'exécution.
            if parts[0].endswith(".data"):
                if len(parts) < 3 or parts[1] not in ("purelib", "platlib"):
                    continue
                parts = parts[2:]
            target = (site / Path(*parts)).resolve()
            if site_root not in target.parents:
                raise ValueError(f"chemin refusé dans {whl.name} : {name}")
            target.parent.mkdir(parents=True, exist_ok=True)
            with zf.open(info) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst)
    return True


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 1
    python_dir, wheels_dir = Path(argv[1]), Path(argv[2])
    try:
        if ensure_pth(python_dir):
            print("[+] site-packages activé pour le Python embarqué")
        wheels = sorted(wheels_dir.glob("*.whl"))
        if not wheels:
            print(f"[!] Aucune wheel dans {wheels_dir}", file=sys.stderr)
            return 1
        site = python_dir / "Lib" / "site-packages"
        site.mkdir(parents=True, exist_ok=True)
        installed = sum(install_wheel(w, site) for w in wheels)
        print(f"[+] Dépendances Python : {installed} installée(s), "
              f"{len(wheels) - installed} déjà présente(s)")
        return 0
    except (OSError, ValueError, zipfile.BadZipFile) as e:
        print(f"[!] Installation des dépendances impossible : {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))

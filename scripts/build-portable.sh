#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : assemble une distribution portable AfricAIsoft par plateforme.
#           Étapes :
#             1. Télécharge python-build-standalone 3.12 dans bin/<plat>/python/
#             2. Installe les deps (pip download → wheels vendored → install --no-index)
#             3. Télécharge les binaires llama.cpp (via scripts/fetch-binaries.sh)
#             4. Copie app/ config/ ui/ mcp-servers/ scripts/ docs/
#             5. Génère manifest release.json avec SHA-256 de chaque fichier
#             6. Optionnel : signe release.json (voir scripts/sign-release.py)
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
# Usage   : bash scripts/build-portable.sh [--dry-run] [--target linux-x64|linux-arm64|macos-x64|macos-arm64|windows-x64|all]
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail
DRY_RUN=0
TARGET="linux-x64"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run) DRY_RUN=1; shift ;;
        --target)  TARGET="$2"; shift 2 ;;
        *) echo "Argument inconnu: $1" >&2; exit 2 ;;
    esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/$TARGET"

# Table des URLs python-build-standalone 3.12 (mise à jour à la main).
PY_VER="3.12.5"
declare -A PY_URLS=(
  [linux-x64]="https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-unknown-linux-gnu-install_only.tar.gz"
  [linux-arm64]="https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-aarch64-unknown-linux-gnu-install_only.tar.gz"
  [macos-x64]="https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-apple-darwin-install_only.tar.gz"
  [macos-arm64]="https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-aarch64-apple-darwin-install_only.tar.gz"
  [windows-x64]="https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-pc-windows-msvc-install_only.tar.gz"
)

log() { echo -e "\033[1;32m[build-portable]\033[0m $*"; }
warn(){ echo -e "\033[1;33m[build-portable]\033[0m $*" >&2; }
run() { if [ "$DRY_RUN" -eq 1 ]; then echo "  [dry-run] $*"; else eval "$*"; fi; }

if [ "$TARGET" = "all" ]; then
    for t in linux-x64 linux-arm64 macos-x64 macos-arm64 windows-x64; do
        bash "$0" ${DRY_RUN:+--dry-run} --target "$t"
    done
    exit 0
fi

PY_URL="${PY_URLS[$TARGET]:-}"
if [ -z "$PY_URL" ]; then echo "TARGET inconnu: $TARGET" >&2; exit 2; fi

log "Target=$TARGET  Sortie=$OUT  DryRun=$DRY_RUN"

# 1. Runtime Python portable.
log "1/6  Python $PY_VER ← $PY_URL"
run "mkdir -p '$OUT/bin/$TARGET/python'"
run "curl -fSL '$PY_URL' -o '/tmp/python-$TARGET.tar.gz'"
run "tar -C '$OUT/bin/$TARGET/python' --strip-components=1 -xzf '/tmp/python-$TARGET.tar.gz'"

# 2. Deps Python (pip download + install --no-index depuis wheels vendored).
log "2/6  Wheels dependencies (fastapi + uvicorn + httpx + pydantic)"
WHEEL_DIR="$OUT/vendor/wheels"
run "mkdir -p '$WHEEL_DIR'"
run "'$OUT/bin/$TARGET/python/bin/python3' -m pip download -d '$WHEEL_DIR' -r '$ROOT/backend/requirements.txt' 2>/dev/null || pip download -d '$WHEEL_DIR' -r '$ROOT/backend/requirements.txt'"
run "'$OUT/bin/$TARGET/python/bin/python3' -m pip install --no-index --find-links '$WHEEL_DIR' -r '$ROOT/backend/requirements.txt' 2>/dev/null || true"

# 3. Binaires llama.cpp.
log "3/6  Binaires llama.cpp via scripts/fetch-binaries.sh"
run "bash '$ROOT/scripts/fetch-binaries.sh' --target $TARGET --out '$OUT/bin/$TARGET' || true"

# 4. Copie du code studio.
log "4/6  Copie du code studio (app/ config/ ui/ mcp-servers/ scripts/ docs/)"
for d in app config ui mcp-servers scripts docs skills; do
    run "mkdir -p '$OUT/$d'"
    run "cp -a '$ROOT/$d/.' '$OUT/$d/' 2>/dev/null || rsync -a --exclude __pycache__ '$ROOT/$d/' '$OUT/$d/'"
done
for f in README.md LICENSE VERSION CHANGELOG.md install.sh install.bat start.sh start-windows.bat start-macos.sh start-linux.sh; do
    [ -f "$ROOT/$f" ] && run "cp '$ROOT/$f' '$OUT/'"
done

# 5. Manifest release.json (SHA-256 par fichier).
log "5/6  Manifest release.json (SHA-256 par fichier)"
if [ "$DRY_RUN" -eq 0 ]; then
    python3 - <<PYEOF
import hashlib, json, os, sys
root = "$OUT"
files = []
for dp, _, fn in os.walk(root):
    for f in fn:
        p = os.path.join(dp, f)
        rel = os.path.relpath(p, root).replace(os.sep, "/")
        if rel == "release.json" or rel.endswith(".sig"): continue
        h = hashlib.sha256(); size = 0
        with open(p, "rb") as fh:
            while True:
                b = fh.read(1<<20)
                if not b: break
                h.update(b); size += len(b)
        files.append({"path": rel, "size": size, "sha256": h.hexdigest()})
manifest = {
    "product": "AfricAIsoft Portable Studio",
    "version": open(os.path.join(root, "VERSION")).read().strip() if os.path.exists(os.path.join(root,"VERSION")) else "0.0.0",
    "target": "$TARGET",
    "python_version": "$PY_VER",
    "generated_at_utc": __import__("datetime").datetime.utcnow().isoformat() + "Z",
    "files": files,
    "total_files": len(files),
    "total_bytes": sum(f["size"] for f in files),
}
with open(os.path.join(root, "release.json"), "w") as fh:
    json.dump(manifest, fh, indent=2)
print(f"  release.json : {manifest['total_files']} fichiers, {manifest['total_bytes']:,} octets")
PYEOF
else
    echo "  [dry-run] Génération de release.json (calcul SHA-256 de tous les fichiers)"
fi

# 6. Signature Ed25519 optionnelle.
log "6/6  Signature Ed25519 (optionnelle — voir scripts/sign-release.py)"
if [ -f "$ROOT/keys/private.pem" ] && [ "$DRY_RUN" -eq 0 ]; then
    run "python3 '$ROOT/scripts/sign-release.py' sign '$OUT/release.json' --key '$ROOT/keys/private.pem'"
else
    warn "Pas de clé privée trouvée — release non signée. Voir scripts/sign-release.py."
fi

log "Terminé. Livrable prêt dans : $OUT"

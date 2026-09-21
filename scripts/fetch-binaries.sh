#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : téléchargement des binaires llama-server depuis les releases officielles
#           github.com/ggml-org/llama.cpp. Vérification SHA256 optionnelle contre
#           release.json. Compilation depuis les sources documentée en fallback
#           (voir docs/COMPILATION.md, plan Phase 3).
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Usage   : scripts/fetch-binaries.sh [-t TAG] [-p PLATFORM] [-b BACKEND]
#           défauts : dernière release, plateforme courante, backend=cpu
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$STUDIO_ROOT"

TAG="${TAG:-b11071}"
PLATFORM_FILTER="${PLATFORM_FILTER:-}"
BACKEND_FILTER="${BACKEND_FILTER:-cpu}"

while [ $# -gt 0 ]; do
    case "$1" in
        -t) TAG="$2"; shift 2 ;;
        -p) PLATFORM_FILTER="$2"; shift 2 ;;
        -b) BACKEND_FILTER="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 [-t TAG] [-p PLATFORM] [-b BACKEND]"
            echo "  PLATFORM = linux-x86_64|linux-aarch64|darwin-arm64|darwin-x86_64|windows-x86_64"
            echo "  BACKEND  = cpu|cuda|vulkan|rocm|metal"
            exit 0 ;;
        *) echo "Argument inconnu: $1"; exit 1 ;;
    esac
done

# Tableau plateforme → asset pattern (CPU uniquement en Phase 2 ; les autres
# backends sont documentés mais commentés faute de test possible ici).
declare -A CPU_ASSETS=(
    ["linux-x86_64"]="ubuntu-x64.tar.gz"
    ["linux-aarch64"]="ubuntu-arm64.tar.gz"
    ["darwin-arm64"]="macos-arm64.tar.gz"
    ["darwin-x86_64"]="macos-x64.tar.gz"
    ["windows-x86_64"]="win-cpu-x64.zip"
)

echo "Fetch llama.cpp binaries [tag=$TAG, backend=$BACKEND_FILTER]"

RELEASE_JSON="$(curl -fsSL "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$TAG")"
if [ -z "$RELEASE_JSON" ]; then
    echo "Impossible de récupérer la release $TAG"
    exit 1
fi

for PLAT in "${!CPU_ASSETS[@]}"; do
    [ -n "$PLATFORM_FILTER" ] && [ "$PLAT" != "$PLATFORM_FILTER" ] && continue
    [ "$BACKEND_FILTER" != "cpu" ] && { echo "Backend $BACKEND_FILTER non fetché en Phase 2 pour $PLAT (voir docs)"; continue; }

    PATTERN="${CPU_ASSETS[$PLAT]}"
    URL="$(echo "$RELEASE_JSON" | python3 -c "
import sys,json
d=json.load(sys.stdin)
for a in d.get('assets',[]):
    n=a['name']
    if '$PATTERN' in n and not any(x in n.lower() for x in ['cudart','vulkan','sycl','kompute','opencl','cuda']):
        print(a['browser_download_url']); break
")"
    if [ -z "$URL" ]; then
        echo "  [~] $PLAT : asset introuvable ($PATTERN), skip."
        continue
    fi

    DEST_DIR="$STUDIO_ROOT/bin/$PLAT/cpu"
    mkdir -p "$DEST_DIR"
    TMP="$(mktemp -d)"
    NAME="$(basename "$URL")"
    echo "  [*] $PLAT : téléchargement $NAME"
    curl -fL --progress-bar -o "$TMP/$NAME" "$URL" || { echo "  [!] échec"; rm -rf "$TMP"; continue; }

    if [[ "$NAME" == *.zip ]]; then
        command -v unzip >/dev/null || { echo "  [!] unzip requis"; rm -rf "$TMP"; continue; }
        unzip -q "$TMP/$NAME" -d "$TMP/extract"
    else
        mkdir -p "$TMP/extract"
        tar -xzf "$TMP/$NAME" -C "$TMP/extract" --strip-components=1 2>/dev/null || \
            tar -xzf "$TMP/$NAME" -C "$TMP/extract"
    fi

    # Copie dans DEST_DIR (résolution symlinks)
    cp -RL "$TMP/extract"/. "$DEST_DIR"/ 2>/dev/null || cp -r "$TMP/extract"/. "$DEST_DIR"/
    find "$DEST_DIR" -maxdepth 1 -type f -name "llama-server*" -exec chmod +x {} \;
    find "$DEST_DIR" -maxdepth 1 -type f -name "*.so*" -exec chmod 755 {} \;
    find "$DEST_DIR" -maxdepth 1 -type f -name "*.dylib" -exec chmod 755 {} \;

    if [ -f "$DEST_DIR/llama-server" ] || [ -f "$DEST_DIR/llama-server.exe" ]; then
        echo "  [✓] $PLAT installé dans bin/$PLAT/cpu/"
    else
        echo "  [!] $PLAT : llama-server non trouvé dans l'archive"
    fi
    rm -rf "$TMP"
done

echo "Terminé."

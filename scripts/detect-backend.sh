#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : détection du backend d'inférence (CUDA > ROCm > Vulkan > CPU).
#           Sortie clé=valeur sur stdout (BACKEND=..., REASON=..., GPU_LAYERS=...).
#           Chaque sonde timeout 1 s. Budget total < 5 s.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH_RAW="$(uname -m)"
case "$ARCH_RAW" in
    x86_64|amd64) ARCH="x86_64" ;;
    aarch64)      ARCH="aarch64" ;;
    arm64)        ARCH="arm64" ;;
    *) ARCH="$ARCH_RAW" ;;
esac
[ "$OS" = "darwin" ] && [ "$ARCH" = "aarch64" ] && ARCH="arm64"
PLAT_KEY="${OS}-${ARCH}"

# Vérif présence binaire pour un backend donné
_binary_available() {
    [ -f "$STUDIO_ROOT/bin/$PLAT_KEY/$1/llama-server" ] || \
    [ -f "$STUDIO_ROOT/bin/$PLAT_KEY/$1/llama-server.exe" ]
}

# Timeout portable (Linux et macOS)
_probe() {
    if command -v timeout >/dev/null 2>&1; then
        timeout 1 "$@" 2>/dev/null
    else
        # macOS BSD sans coreutils
        perl -e 'alarm(1); exec @ARGV' "$@" 2>/dev/null
    fi
}

BACKEND="cpu"
REASON="fallback aucun GPU détecté"
GPU_LAYERS=0

# macOS : Metal natif
if [ "$OS" = "darwin" ]; then
    if _binary_available "metal"; then
        BACKEND="metal"; REASON="macOS natif"; GPU_LAYERS=999
    else
        REASON="binaire Metal absent"
    fi
else
    # Linux/Windows : arbre GPU
    # ── CUDA ─────────────────────────────────────────────────────────────────
    if _probe nvidia-smi --query-gpu=name --format=csv,noheader >/dev/null 2>&1; then
        if _binary_available "cuda"; then
            BACKEND="cuda"; REASON="nvidia-smi ok"; GPU_LAYERS=999
        fi
    fi

    # ── ROCm (Linux) ─────────────────────────────────────────────────────────
    if [ "$BACKEND" = "cpu" ] && [ "$OS" = "linux" ]; then
        if _probe rocm-smi --showproductname >/dev/null 2>&1; then
            if _binary_available "rocm"; then
                BACKEND="rocm"; REASON="rocm-smi ok"; GPU_LAYERS=999
            fi
        fi
    fi

    # ── Vulkan ───────────────────────────────────────────────────────────────
    if [ "$BACKEND" = "cpu" ]; then
        VK_OUT="$(_probe vulkaninfo --summary 2>/dev/null || true)"
        if echo "$VK_OUT" | grep -qE "DISCRETE_GPU|INTEGRATED_GPU"; then
            if _binary_available "vulkan"; then
                BACKEND="vulkan"; REASON="vulkaninfo ok"; GPU_LAYERS=999
            fi
        fi
    fi
fi

# Affichage clé=valeur (parsable)
echo "PLATFORM=$PLAT_KEY"
echo "BACKEND=$BACKEND"
echo "REASON=$REASON"
echo "GPU_LAYERS=$GPU_LAYERS"

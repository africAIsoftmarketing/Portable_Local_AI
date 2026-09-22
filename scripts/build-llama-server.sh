#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : compilation locale de llama-server (llama.cpp) pour tout backend
#           supporté (cpu | cuda | metal | vulkan | rocm). Utilisé (1) en
#           reproduction de la CI GitHub Actions et (2) comme échappatoire
#           locale pour les backends non couverts par la matrice CI (ROCm).
#           Aucun paquet n'est installé automatiquement : le script détecte
#           l'absence des toolkits requis et affiche des messages clairs.
#
# Usage   : ./scripts/build-llama-server.sh [cpu|cuda|metal|vulkan|rocm] [tag]
#           Défaut : cpu, tag stable llama.cpp récent (résolu via API GitHub).
#
# Sortie  : bin/<os>-<arch>/<backend>/llama-server[.exe] + libs associées.
#           Le nommage `<os>-<arch>` suit la convention des launchers
#           existants (start-linux.sh, start-mac.command, start-windows.bat),
#           soit `linux-x86_64`, `linux-aarch64`, `darwin-arm64`,
#           `darwin-x86_64`, `windows-x86_64` (via WSL/MSYS). Cf. rapport
#           bloquant : nommage brief client `linux-arm64` / `macos-arm64`
#           divergent — non corrigé ici pour ne pas casser l'existant.
#
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-09-21
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BACKEND="${1:-cpu}"
LLAMA_TAG="${2:-}"
STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLAMA_REPO="https://github.com/ggml-org/llama.cpp.git"

# ── Couleurs ─────────────────────────────────────────────────────────────────
if [ -t 1 ]; then
    G='\033[0;32m'; Y='\033[1;33m'; R='\033[0;31m'; B='\033[1m'; N='\033[0m'
else
    G=''; Y=''; R=''; B=''; N=''
fi
say()  { printf "${B}[build-llama]${N} %s\n" "$*"; }
warn() { printf "${Y}[build-llama][WARN]${N} %s\n" "$*" >&2; }
die()  { printf "${R}[build-llama][ERREUR]${N} %s\n" "$*" >&2; exit 1; }

# ── Validation argument ──────────────────────────────────────────────────────
case "${BACKEND}" in
    cpu|cuda|metal|vulkan|rocm) ;;
    *) die "Backend inconnu : ${BACKEND}. Utilisez cpu|cuda|metal|vulkan|rocm." ;;
esac

# ── Détection plateforme (aligné sur scripts/core-startup.sh) ────────────────
OS_LC="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH_RAW="$(uname -m)"
case "${ARCH_RAW}" in
    x86_64|amd64) ARCH="x86_64" ;;
    aarch64)      ARCH="aarch64" ;;
    arm64)        ARCH="arm64" ;;
    *) die "Architecture non supportée : ${ARCH_RAW}" ;;
esac
if [ "${OS_LC}" = "darwin" ] && [ "${ARCH}" = "aarch64" ]; then ARCH="arm64"; fi
PLAT_KEY="${OS_LC}-${ARCH}"
OUT_DIR="${STUDIO_ROOT}/bin/${PLAT_KEY}/${BACKEND}"

say "Cible : ${PLAT_KEY} / ${BACKEND}"
say "Sortie : ${OUT_DIR}"

# ── Vérif prérequis génériques ───────────────────────────────────────────────
need_cmd() {
    command -v "$1" >/dev/null 2>&1 || die "Prérequis manquant : ${1}. Installez-le et relancez."
}
need_cmd cmake
need_cmd git
if [ "${OS_LC}" = "linux" ] || [ "${OS_LC}" = "darwin" ]; then
    need_cmd cc || need_cmd gcc || need_cmd clang
fi

# ── Vérif prérequis backend ──────────────────────────────────────────────────
case "${BACKEND}" in
    cuda)
        command -v nvcc >/dev/null 2>&1 || die \
            "Toolkit CUDA absent (nvcc introuvable). Installez CUDA Toolkit ≥ 12.0 depuis developer.nvidia.com/cuda-downloads."
        ;;
    metal)
        [ "${OS_LC}" = "darwin" ] || die "Metal est disponible uniquement sous macOS."
        ;;
    vulkan)
        command -v glslc >/dev/null 2>&1 || warn \
            "glslc absent : sur Linux 'sudo apt install libvulkan-dev vulkan-tools glslc' ; sur Windows/macOS installer le Vulkan SDK LunarG."
        ;;
    rocm)
        [ "${OS_LC}" = "linux" ] || die "ROCm est supporté uniquement sous Linux."
        command -v hipcc >/dev/null 2>&1 || die \
            "Toolkit ROCm absent (hipcc introuvable). Installez ROCm ≥ 6.0 depuis rocm.docs.amd.com."
        ;;
    cpu)
        : # rien de spécifique
        ;;
esac

# ── Résolution du tag llama.cpp ──────────────────────────────────────────────
resolve_tag() {
    # Tag donné en argument prime.
    if [ -n "${LLAMA_TAG}" ]; then echo "${LLAMA_TAG}"; return; fi
    # Sinon, dernière release bXXXX non prerelease (API GitHub, sans token).
    if command -v curl >/dev/null 2>&1; then
        local t
        t=$(curl -fsSL "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=30" \
            | grep -oE '"tag_name": *"b[0-9]{4,}"' \
            | grep -oE 'b[0-9]{4,}' \
            | awk 'BEGIN{FS="b"} { if ($2+0 >= 5000) print $0 }' \
            | head -1 || true)
        if [ -n "${t}" ]; then echo "${t}"; return; fi
    fi
    warn "Résolution automatique impossible — utilisation du tag master."
    echo "master"
}
TAG="$(resolve_tag)"
say "Tag llama.cpp : ${TAG}"

# ── Checkout llama.cpp dans un cache local ──────────────────────────────────
CACHE_DIR="${STUDIO_ROOT}/.cache/llama.cpp"
mkdir -p "${CACHE_DIR}"
if [ ! -d "${CACHE_DIR}/.git" ]; then
    say "Clone llama.cpp (peut prendre 1-2 min)…"
    git clone --depth 1 --branch "${TAG}" "${LLAMA_REPO}" "${CACHE_DIR}" 2>&1 | tail -5 || \
        git clone "${LLAMA_REPO}" "${CACHE_DIR}"
fi
say "Checkout ${TAG}"
( cd "${CACHE_DIR}" && git fetch --depth 1 origin "${TAG}" 2>/dev/null || true )
( cd "${CACHE_DIR}" && git checkout "${TAG}" 2>/dev/null || git checkout master )

# ── Flags CMake alignés sur .github/workflows/build-master-copy.yml ──────────
BUILD_DIR="${CACHE_DIR}/build-${BACKEND}"
rm -rf "${BUILD_DIR}"
CMAKE_ARGS=(
    -B "${BUILD_DIR}" -S "${CACHE_DIR}"
    -DCMAKE_BUILD_TYPE=Release
    -DLLAMA_CURL=OFF
    -DBUILD_SHARED_LIBS=ON
)

case "${BACKEND}" in
    cpu)
        if [ "${ARCH}" = "x86_64" ]; then
            CMAKE_ARGS+=( -DGGML_NATIVE=OFF -DGGML_AVX2=ON -DGGML_AVX=ON )
        else
            CMAKE_ARGS+=( -DGGML_NATIVE=OFF )
        fi
        # Sur macOS ARM64, désactiver Metal + Accelerate pour un vrai binaire CPU.
        if [ "${OS_LC}" = "darwin" ]; then
            CMAKE_ARGS+=( -DGGML_METAL=OFF -DGGML_ACCELERATE=OFF )
        fi
        ;;
    cuda)
        CMAKE_ARGS+=( -DGGML_CUDA=ON )
        ;;
    metal)
        CMAKE_ARGS+=( -DGGML_METAL=ON -DGGML_METAL_EMBED_LIBRARY=ON )
        ;;
    vulkan)
        CMAKE_ARGS+=( -DGGML_VULKAN=ON )
        ;;
    rocm)
        CMAKE_ARGS+=( -DGGML_HIPBLAS=ON -DAMDGPU_TARGETS="${AMDGPU_TARGETS:-gfx1030;gfx1100;gfx1101;gfx1102}" )
        ;;
esac

say "Configuration cmake…"
cmake "${CMAKE_ARGS[@]}"
say "Compilation (cible : llama-server)…"
cmake --build "${BUILD_DIR}" --config Release --target llama-server --parallel

# ── Installation dans bin/<plat>/<backend>/ ─────────────────────────────────
mkdir -p "${OUT_DIR}"
BIN_NAME="llama-server"
[ "${OS_LC}" = "mingw"* ] || [ "${OS_LC}" = "msys"* ] && BIN_NAME="llama-server.exe"

# Copie du binaire (peut être dans build/bin/, build/bin/Release/ selon plate-forme).
BIN_SRC=""
for cand in "${BUILD_DIR}/bin/${BIN_NAME}" "${BUILD_DIR}/bin/Release/${BIN_NAME}"; do
    [ -f "${cand}" ] && BIN_SRC="${cand}" && break
done
[ -n "${BIN_SRC}" ] || die "Binaire ${BIN_NAME} introuvable après compilation."
cp "${BIN_SRC}" "${OUT_DIR}/"
chmod +x "${OUT_DIR}/${BIN_NAME}"

# Copie des libs partagées (Linux .so, macOS .dylib, Windows .dll).
for pat in "*.so*" "*.dylib" "*.dll"; do
    for dir in "${BUILD_DIR}/bin" "${BUILD_DIR}/bin/Release" "${BUILD_DIR}/src" "${BUILD_DIR}/ggml/src"; do
        find "${dir}" -maxdepth 1 -name "${pat}" -exec cp -a {} "${OUT_DIR}/" \; 2>/dev/null || true
    done
done

say "Vérification version du binaire produit :"
"${OUT_DIR}/${BIN_NAME}" --version 2>&1 | head -3 || warn "Le binaire n'accepte pas --version, tenter --help."

printf "${G}[build-llama] OK${N} → ${OUT_DIR}\n"

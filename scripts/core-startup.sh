#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : logique commune de démarrage Linux/macOS. Appelée par start-linux.sh
#           et start-mac.command. Ne pas exécuter directement.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$STUDIO_ROOT"

# ── Couleurs ─────────────────────────────────────────────────────────────────
if [ -t 1 ]; then
    GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'
    RED='\033[0;31m'; BOLD='\033[1m'; DIM='\033[2m'; NC='\033[0m'
else
    GREEN=''; YELLOW=''; CYAN=''; RED=''; BOLD=''; DIM=''; NC=''
fi

log() { printf "%s\n" "$*" | tee -a "$STUDIO_ROOT/logs/startup.log" >&2; }
info()  { log "$(date -u +%Y-%m-%dT%H:%M:%SZ) [INFO ] $*"; }
warn()  { log "$(date -u +%Y-%m-%dT%H:%M:%SZ) [WARN ] $*"; }
fatal() { log "$(date -u +%Y-%m-%dT%H:%M:%SZ) [FATAL] $*"; exit 1; }

mkdir -p "$STUDIO_ROOT/logs" "$STUDIO_ROOT/data/pids"

echo -e "${CYAN}"
echo " ╔════════════════════════════════════════════════════════╗"
echo " ║   AfricAIsoft Portable Studio - démarrage              ║"
echo " ╚════════════════════════════════════════════════════════╝"
echo -e "${NC}"

# ── [1] Détection OS/arch ────────────────────────────────────────────────────
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH_RAW="$(uname -m)"
case "$ARCH_RAW" in
    x86_64|amd64) ARCH="x86_64" ;;
    aarch64)      ARCH="aarch64" ;;
    arm64)        ARCH="arm64" ;;
    *) fatal "Architecture non supportée : $ARCH_RAW" ;;
esac
if [ "$OS" = "darwin" ] && [ "$ARCH" = "aarch64" ]; then ARCH="arm64"; fi
PLAT_KEY="${OS}-${ARCH}"
info "Plateforme : $PLAT_KEY"

# ── [2] Vérifs dépendances système (sans installer) ──────────────────────────
for cmd in curl python3; do
    command -v "$cmd" >/dev/null 2>&1 || fatal "Dépendance manquante : $cmd"
done

# glibc / libstdc++ (Linux uniquement)
if [ "$OS" = "linux" ]; then
    if ldd --version 2>/dev/null | head -1 | grep -q "GLIBC"; then
        info "glibc détectée"
    else
        warn "Impossible de vérifier glibc"
    fi
fi

# ── [3] Détection backend GPU (rapide, < 5 s) ────────────────────────────────
BACKEND="cpu"
BACKEND_REASON="fallback"
if bash "$STUDIO_ROOT/scripts/detect-backend.sh" > "$STUDIO_ROOT/logs/backend.log" 2>&1; then
    BACKEND="$(grep '^BACKEND=' "$STUDIO_ROOT/logs/backend.log" | tail -1 | cut -d= -f2)"
    BACKEND_REASON="$(grep '^REASON=' "$STUDIO_ROOT/logs/backend.log" | tail -1 | cut -d= -f2-)"
fi
info "Backend détecté : $BACKEND ($BACKEND_REASON)"

# ── [4] Détection filesystem restreint (FAT32/exFAT/noexec) ─────────────────
BIN_DIR="$STUDIO_ROOT/bin/$PLAT_KEY/$BACKEND"
_STAGING_DIR=""
_cleanup_staging() {
    if [ -n "$_STAGING_DIR" ] && [ -d "$_STAGING_DIR" ]; then
        rm -rf "$_STAGING_DIR"
        info "Staging nettoyé : $_STAGING_DIR"
    fi
}
trap _cleanup_staging EXIT INT TERM

_is_restricted_fs() {
    local dir="$1"
    [ -d "$dir" ] || return 1
    if [ -f /proc/mounts ]; then
        local mp; mp=$(stat -c '%m' "$dir" 2>/dev/null || echo "")
        if [ -n "$mp" ] && grep -qE " ${mp} [^ ]+ [^,]*(noexec|nosuid)" /proc/mounts 2>/dev/null; then
            return 0
        fi
    fi
    local fstype=""
    fstype=$(stat -f -c '%T' "$dir" 2>/dev/null) || fstype=$(stat -f '%T' "$dir" 2>/dev/null) || true
    # NB : `${var,,}` (bash 4+) non supporté par le bash 3.2 système de macOS.
    case "$(printf '%s' "$fstype" | tr '[:upper:]' '[:lower:]')" in
        msdos|vfat|exfat|fuseblk|ntfs|ntfs-3g|fuse.ntfs*|fuse.exfat*) return 0 ;;
    esac
    return 1
}

if [ -d "$BIN_DIR" ] && _is_restricted_fs "$BIN_DIR"; then
    warn "Filesystem restreint détecté ($BIN_DIR). Copie vers /tmp..."
    _STAGING_DIR="$(mktemp -d /tmp/portableai.XXXXXXXX)"
    cp -RL "$BIN_DIR"/. "$_STAGING_DIR"/ 2>/dev/null || cp -rL "$BIN_DIR"/. "$_STAGING_DIR"/ 2>/dev/null || cp -r "$BIN_DIR"/. "$_STAGING_DIR"/
    chmod +x "$_STAGING_DIR"/llama-server 2>/dev/null || true
    BIN_DIR="$_STAGING_DIR"
    info "Staging binaire : $BIN_DIR"
fi

# ── [5] Résolution Python ────────────────────────────────────────────────────
if [ -x "$STUDIO_ROOT/bin/$PLAT_KEY/python/bin/python3" ]; then
    PYTHON="$STUDIO_ROOT/bin/$PLAT_KEY/python/bin/python3"
    info "Python portable : $PYTHON"
else
    PYTHON="$(command -v python3)"
    warn "Python portable absent, utilisation Python système : $PYTHON"
fi

# ── [6] Vérif dépendances Python ─────────────────────────────────────────────
"$PYTHON" -c "import fastapi, uvicorn, httpx, pydantic" 2>/dev/null || {
    warn "Certaines dépendances Python manquent. Installation offline..."
    if [ -d "$STUDIO_ROOT/bin/$PLAT_KEY/python/wheels" ]; then
        "$PYTHON" -m pip install --no-index --find-links "$STUDIO_ROOT/bin/$PLAT_KEY/python/wheels" \
            -r "$STUDIO_ROOT/app/requirements.txt" 2>&1 | tail -5
    else
        warn "Wheels absents. Passage en mode dev (pip install online)."
        "$PYTHON" -m pip install --quiet -r "$STUDIO_ROOT/app/requirements.txt" 2>&1 | tail -3
    fi
}

# ── [7] Configuration effective ──────────────────────────────────────────────
BIND_HOST="${STUDIO_BIND_HOST:-$(
    "$PYTHON" -c "import json; print(json.load(open('$STUDIO_ROOT/config/settings.json'))['server']['bind_host'])" 2>/dev/null || echo '127.0.0.1'
)}"
PORT="${STUDIO_PORT:-$(
    "$PYTHON" -c "import json; print(json.load(open('$STUDIO_ROOT/config/settings.json'))['server']['port'])" 2>/dev/null || echo '8080'
)}"
info "API : http://$BIND_HOST:$PORT"

# Env pour l'orchestrateur
export STUDIO_ROOT
export STUDIO_API_PREFIX=""       # portable = pas de préfixe
export STUDIO_BIND_HOST="$BIND_HOST"
export STUDIO_PORT="$PORT"
export STUDIO_BIN_DIR_OVERRIDE="$BIN_DIR"

# Résolution des libs partagées (LD_LIBRARY_PATH / DYLD)
case "$OS" in
    linux)  export LD_LIBRARY_PATH="$BIN_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ;;
    darwin) export DYLD_LIBRARY_PATH="$BIN_DIR${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" ;;
esac

# ── [8] Lancement uvicorn ────────────────────────────────────────────────────
info "Démarrage orchestrateur..."
echo "$$" > "$STUDIO_ROOT/data/pids/orchestrator.pid"

cd "$STUDIO_ROOT"
exec "$PYTHON" -m uvicorn app.main:app \
    --host "$BIND_HOST" \
    --port "$PORT" \
    --log-level "info" \
    --no-access-log

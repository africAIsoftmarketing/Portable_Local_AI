#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : launcher AfricAIsoft Portable Studio pour Linux.
#           Wrapper autour de scripts/core-startup.sh.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(uname -s)" != "Linux" ]; then
    echo "start-linux.sh : cet OS n'est pas Linux. Utilisez start-mac.command ou start-windows.bat."
    exit 1
fi

exec "$STUDIO_ROOT/scripts/core-startup.sh" "$@"

#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : launcher AfricAIsoft Portable Studio pour macOS.
#           Wrapper autour de scripts/core-startup.sh. L'extension .command
#           permet le double-clic depuis le Finder.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "start-mac.command : cet OS n'est pas macOS. Utilisez start-linux.sh ou start-windows.bat."
    exit 1
fi

# Ouvre le navigateur après 3 s (macOS 'open')
(sleep 3 && open "http://127.0.0.1:8080") &

exec "$STUDIO_ROOT/scripts/core-startup.sh" "$@"

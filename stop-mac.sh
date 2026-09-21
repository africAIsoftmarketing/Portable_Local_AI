#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : arrêt propre du studio sur macOS.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
STUDIO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PIDS_DIR="$STUDIO_ROOT/data/pids"

echo "AfricAIsoft Portable Studio - arrêt (macOS)"

kill_pid() {
    local pid="$1" name="$2"
    if [ -z "$pid" ] || ! kill -0 "$pid" 2>/dev/null; then return 0; fi
    echo "  → SIGTERM $name (pid=$pid)"
    kill -TERM "$pid" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        kill -0 "$pid" 2>/dev/null || return 0
        sleep 1
    done
    kill -KILL "$pid" 2>/dev/null || true
}

for f in "$PIDS_DIR"/*.pid; do
    [ -f "$f" ] || continue
    NAME="$(basename "$f" .pid)"
    kill_pid "$(cat "$f" 2>/dev/null)" "$NAME"
    rm -f "$f"
done

pkill -TERM -f "$STUDIO_ROOT/bin/.*/llama-server" 2>/dev/null || true
pkill -TERM -f "uvicorn app.main:app" 2>/dev/null || true

# Nettoyage staging macOS
find "${TMPDIR:-/tmp}" -maxdepth 1 -type d -name "portableai.*" -mmin -1440 -exec rm -rf {} + 2>/dev/null || true

echo "Arrêt terminé."

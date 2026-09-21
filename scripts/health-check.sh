#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : health-check HTTP local. Utilisé par les scripts de démarrage
#           et par les tests. Retourne exit 0 si /health = 200, 1 sinon.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Usage   : scripts/health-check.sh [PORT] [PREFIX]
#           défauts : PORT=8080 PREFIX=""
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
PORT="${1:-8080}"
PREFIX="${2:-}"
URL="http://127.0.0.1:$PORT${PREFIX}/health"

for i in $(seq 1 30); do
    if curl -fsSL --max-time 2 "$URL" >/dev/null 2>&1; then
        echo "OK $URL"
        exit 0
    fi
    sleep 1
done
echo "KO $URL"
exit 1

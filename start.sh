#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : alias de rétrocompatibilité vers le launcher Studio de la plateforme.
#           L'ancien start.sh ne lançait que llama-server. Le workflow Studio
#           complet passe par start-linux.sh (ou start-mac.command) qui délègue
#           à scripts/core-startup.sh (uvicorn + MCP + llama).
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OS="$(uname -s)"

echo ""
echo "  [i] start.sh (legacy PortableAI) — délégation au launcher Studio"
case "$OS" in
    Linux*)  echo "      → start-linux.sh"    ; exec "$HERE/start-linux.sh" "$@"    ;;
    Darwin*) echo "      → start-mac.command" ; exec "$HERE/start-mac.command" "$@" ;;
    *)
        echo "      OS non supporté : $OS. Utilisez start-windows.bat sous Windows."
        exit 1
        ;;
esac

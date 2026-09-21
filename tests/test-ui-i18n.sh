#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : wrapper bash pour le test Playwright de persistance i18n.
#           Prouve le COMPORTEMENT réel du navigateur (pas juste le code source).
# Auteur  : AfricAIsoft
# Licence : MIT
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# Interpréteur Python contenant playwright (partagé avec les autres agents).
PLAYWRIGHT_PY="${PLAYWRIGHT_PY:-/opt/plugins-venv/bin/python}"

if [ ! -x "$PLAYWRIGHT_PY" ]; then
    # Fallback : Python courant (nécessite `pip install playwright` + chromium).
    PLAYWRIGHT_PY="$(command -v python3)"
fi

exec "$PLAYWRIGHT_PY" "$(dirname "$0")/test-ui-i18n.py" "$@"

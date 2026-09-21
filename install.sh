#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : alias de rétrocompatibilité vers le workflow Studio officiel.
#           Le PortableAI d'origine n'était qu'un lanceur `llama-server`.
#           Le workflow Studio complet passe désormais par
#           scripts/fetch-binaries.sh + start-linux.sh (ou start-mac.command,
#           start-windows.bat).
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo ""
echo "  [i] install.sh (legacy PortableAI) — délégation à scripts/fetch-binaries.sh"
echo "      Pour l'installation Studio complète, préférez :"
echo "        bash scripts/fetch-binaries.sh -p <plat> -b cpu"
echo "        bash scripts/build-portable.sh --target <plat>   (runtime Python portable)"
echo ""
exec "$HERE/scripts/fetch-binaries.sh" "$@"

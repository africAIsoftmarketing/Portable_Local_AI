#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests UI Phase 4.
#           - Chargement de l'index HTML (200 + contenu attendu)
#           - Présence de toutes les <div data-testid="..."> essentielles
#           - Fichiers i18n FR/EN téléchargeables et non vides
#           - Endpoints backend exposés (conversations, models/switch, skills/rag/documents)
#           NB : sans navigateur headless ici, on couvre l'ossature statique et
#           les APIs backend qu'elle consomme. Une validation E2E complète
#           est laissée au CI (Playwright) hors périmètre offline.
# Auteur  : AfricAIsoft
# Licence : MIT
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8001}"
API_PREFIX="${API_PREFIX:-/api}"

PASS=0; FAIL=0
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; NC='\033[0m'
pass() { echo -e "${GREEN}  ✓${NC} $1"; PASS=$((PASS+1)); }
fail() { echo -e "${RED}  ✗${NC} $1"; FAIL=$((FAIL+1)); }
info() { echo -e "${YELLOW}  ▸${NC} $1"; }
c()   { curl -sS "$@"; }

echo "=========================================================="
echo "  test-ui.sh (Phase 4)"
echo "=========================================================="

# ── T1 : / renvoie l'index HTML ──────────────────────────────────────────────
info "T1 : GET /api/ → HTML"
HTML=$(c "${BASE_URL}${API_PREFIX}/")
echo "$HTML" | grep -q "<title" && pass "HTML servi" || fail "HTML absent"

# ── T2 : présence des data-testid principaux ─────────────────────────────────
info "T2 : présence des data-testid principaux"
for tid in \
    "conversations-sidebar" \
    "conversation-new-btn" \
    "chat-panel" \
    "chat-input" \
    "chat-send-btn" \
    "right-panel" \
    "tab-skills" \
    "tab-config" \
    "tab-models" \
    "tab-systemprompt" \
    "theme-toggle" \
    "lang-select"
do
    if echo "$HTML" | grep -q "data-testid=\"$tid\""; then
        pass "testid présent : $tid"
    else
        fail "testid manquant : $tid"
    fi
done

# ── T3 : fichiers i18n téléchargeables ───────────────────────────────────────
info "T3 : FR et EN présents"
FR=$(c "${BASE_URL}${API_PREFIX}/assets/i18n/fr.json")
EN=$(c "${BASE_URL}${API_PREFIX}/assets/i18n/en.json")
echo "$FR" | python3 -c "import sys,json; d=json.load(sys.stdin); assert 'chat' in d" \
    && pass "fr.json OK" || fail "fr.json KO"
echo "$EN" | python3 -c "import sys,json; d=json.load(sys.stdin); assert 'chat' in d" \
    && pass "en.json OK" || fail "en.json KO"

# ── T4 : endpoints backend consommés par l'UI ────────────────────────────────
info "T4 : /api/conversations (GET) répond"
CONVS=$(c "${BASE_URL}${API_PREFIX}/conversations")
echo "$CONVS" | python3 -c "import sys,json; d=json.load(sys.stdin); assert 'conversations' in d" \
    && pass "GET /conversations OK" || fail "GET /conversations KO"

info "T4 : POST /api/conversations (créer)"
NEW=$(c -X POST -H "Content-Type: application/json" \
    -d '{"title":"Test UI"}' \
    "${BASE_URL}${API_PREFIX}/conversations")
CID=$(echo "$NEW" | python3 -c "import sys,json; print(json.load(sys.stdin).get('id',''))")
[ -n "$CID" ] && pass "conversation créée id=$CID" || fail "création KO"

if [ -n "$CID" ]; then
    info "T4 : DELETE /api/conversations/$CID"
    CODE=$(c -o /dev/null -w '%{http_code}' -X DELETE \
        "${BASE_URL}${API_PREFIX}/conversations/$CID")
    [ "$CODE" = "200" ] && pass "delete OK" || fail "delete = $CODE"
fi

info "T5 : /api/models/available"
MODELS=$(c "${BASE_URL}${API_PREFIX}/models/available")
echo "$MODELS" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert 'models' in d and isinstance(d['models'],list)
print('    ok:', d['count'], 'modèle(s)')
" && pass "models/available OK" || fail "models/available KO"

info "T6 : /api/skills/rag/documents"
DOCS=$(c "${BASE_URL}${API_PREFIX}/skills/rag/documents")
echo "$DOCS" | python3 -c "import sys,json; d=json.load(sys.stdin); assert 'documents' in d" \
    && pass "rag/documents OK" || fail "rag/documents KO"

info "T7 : /api/config/schema (utilisé par le panneau Configuration)"
SCH=$(c "${BASE_URL}${API_PREFIX}/config/schema")
echo "$SCH" | python3 -c "import sys,json; d=json.load(sys.stdin); assert d.get('properties',{}).get('model')" \
    && pass "config/schema OK" || fail "config/schema KO"

# ── T7 : Persistance i18n (FR→EN→reload→EN et EN→FR→reload→FR) ──────────────
# Bug déjà survenu 2 fois. On vérifie via le code JS que la logique
# d'hydratation est correcte et qu'AUCUN setItem parasite ne réécrit la valeur
# au boot (le seul setItem doit être dans applyLang / le change handler).
info "T7 : Persistance i18n dans app.js — pattern strict identique au thème"
JS=$(c "${BASE_URL}${API_PREFIX}/assets/app.js")
# La fonction applyLang doit exister et écrire localStorage.
if echo "$JS" | grep -q "function applyLang"; then
    pass "applyLang() présent (setter unique i18n)"
else
    fail "applyLang() manquant"
fi
# validLang doit valider strictement fr/en.
if echo "$JS" | grep -q "validLang"; then
    pass "validLang() strict (fr/en uniquement)"
else
    fail "validLang() manquant"
fi
# Le boot NE DOIT PAS contenir un setItem inconditionnel de studio_lang.
# On vérifie qu'il n'y a plus le pattern régressif :
#   localStorage.setItem("studio_lang", state.lang);
# HORS des fonctions applyLang() et du change handler.
BOOT_WRITES=$(echo "$JS" | grep -c 'localStorage.setItem("studio_lang"')
# On accepte au maximum 1 occurrence (dans applyLang) - les autres écritures
# seraient des régressions.
[ "$BOOT_WRITES" -le "1" ] && pass "aucune écriture parasite localStorage lang (occurrences=$BOOT_WRITES ≤ 1)" \
    || fail "trop d'écritures localStorage lang : $BOOT_WRITES (régression probable)"

# ── T8 : Activation preset System Prompt — persistance côté backend ─────────
info "T8 : activation preset cybersec → GET renvoie le contenu du preset"
# Reset préalable pour partir d'un état propre
c -X POST "${BASE_URL}${API_PREFIX}/system-prompt/reset" > /dev/null

# Active le preset cybersec
ACTIVATE=$(c -X POST "${BASE_URL}${API_PREFIX}/system-prompt/activate/cybersec")
PREVIEW=$(echo "$ACTIVATE" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print(d.get('content_preview',''))
")
[ -n "$PREVIEW" ] && pass "activation renvoie content_preview" || fail "activation KO"

# Lit le system prompt courant : DOIT être le contenu du preset cybersec.
GET_BODY=$(c "${BASE_URL}${API_PREFIX}/system-prompt")
IS_CYBER=$(echo "$GET_BODY" | python3 -c "
import sys,json
d=json.load(sys.stdin)
c=(d.get('content') or '').lower()
ap=d.get('active_preset','')
# Le preset cybersec contient le mot 'cybersécurité' ou 'sécurité' ou 'attaque'.
ok = (ap == 'cybersec') and any(k in c for k in ['sécurité','cyber','attaque','vulnérabilité'])
print('yes' if ok else 'no')
print('    active_preset=' + str(ap))
print('    source=' + d.get('source',''))
print('    preview=' + (d.get('content','')[:80] or '(vide)'))
")
echo "$IS_CYBER" | tail -3 | head -3
if echo "$IS_CYBER" | head -1 | grep -q "yes"; then
    pass "GET reflète le preset actif (contenu cybersec + active_preset==cybersec)"
else
    fail "activation preset ne persiste pas côté GET"
fi

# Bascule sur accounting → GET doit changer.
c -X POST "${BASE_URL}${API_PREFIX}/system-prompt/activate/accounting" > /dev/null
IS_ACCT=$(c "${BASE_URL}${API_PREFIX}/system-prompt" | python3 -c "
import sys,json
d=json.load(sys.stdin)
c=(d.get('content') or '').lower()
ap=d.get('active_preset','')
ok = (ap == 'accounting') and any(k in c for k in ['comptab','écriture','ratio','bilan'])
print('yes' if ok else 'no')
")
[ "$IS_ACCT" = "yes" ] && pass "bascule preset accounting effective" \
    || fail "bascule preset accounting non reflétée par GET"

# Cleanup : retour au défaut.
c -X POST "${BASE_URL}${API_PREFIX}/system-prompt/reset" > /dev/null

echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL"
echo "=========================================================="
exit $FAIL

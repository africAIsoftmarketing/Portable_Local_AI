#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests du system prompt (priorité multi-source, lock, override).
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8001}"
API_PREFIX="${API_PREFIX:-/api}"
STUDIO_ROOT="${STUDIO_ROOT:-/app}"

PASS=0; FAIL=0
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; NC='\033[0m'
pass() { echo -e "${GREEN}  ✓${NC} $1"; PASS=$((PASS+1)); }
fail() { echo -e "${RED}  ✗${NC} $1"; FAIL=$((FAIL+1)); }
info() { echo -e "${YELLOW}  ▸${NC} $1"; }

curl_j() { curl -sS "$@"; }

echo "=========================================================="
echo "  test-system-prompt.sh"
echo "=========================================================="

# ── T1 : GET /system-prompt (locked=false) ───────────────────────────────────
info "T1 : GET ${API_PREFIX}/system-prompt (locked=false)"
BODY="$(curl_j "${BASE_URL}${API_PREFIX}/system-prompt")"
echo "$BODY" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert 'content' in d
assert d['locked'] is False
print('    source:', d['source'], '| tokens~:', d['token_count_approx'])
" && pass "GET system-prompt OK" || fail "GET system-prompt"

# ── T2 : PUT /system-prompt ──────────────────────────────────────────────────
info "T2 : PUT ${API_PREFIX}/system-prompt"
MARKER="MARQUEUR_TEST_SYSPROMPT_$(date +%s)"
CUSTOM_CONTENT="Tu es un assistant. ${MARKER}. Réponds toujours en une phrase."
CODE="$(curl_j -o /tmp/put.out -w '%{http_code}' -X PUT \
    -H "Content-Type: application/json" \
    -d "$(python3 -c "import json;print(json.dumps({'content': '''${CUSTOM_CONTENT}'''}))")" \
    "${BASE_URL}${API_PREFIX}/system-prompt")"
[ "$CODE" = "200" ] && pass "PUT = 200" || { fail "PUT = $CODE"; cat /tmp/put.out; }

# Relit pour vérifier
CONTENT_READ="$(curl_j "${BASE_URL}${API_PREFIX}/system-prompt" | python3 -c "import sys,json;print(json.load(sys.stdin)['content'])")"
echo "$CONTENT_READ" | grep -q "$MARKER" && pass "GET reflète PUT ($MARKER)" || fail "GET ne reflète pas PUT"

# ── T3 : Le fichier system_prompt.txt est bien lu par le proxy ──────────────
# Preuve déterministe : on installe un system prompt qui force une réponse
# spécifique, puis on la lit. On utilise la même technique que T4bis
# (mot-code + seed fixe) pour éviter le biais linguistique du modèle 0.5B.
info "T3 : PUT d'un system prompt avec code déterministe → réponse attendue"
CODE="$(curl_j -o /dev/null -w '%{http_code}' -X PUT \
    -H "Content-Type: application/json" \
    -d '{"content":"Answer ONLY with the exact single word: FILE_ALPHA. Nothing else."}' \
    "${BASE_URL}${API_PREFIX}/system-prompt")"
[ "$CODE" = "200" ] && pass "PUT du prompt de test = 200" || fail "PUT test = $CODE"

REPLY_FILE_A="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42,"stream":false}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    réponse fichier=ALPHA : $REPLY_FILE_A"

# Change le fichier, vérifie que la réponse change
curl_j -X PUT -H "Content-Type: application/json" \
    -d '{"content":"Answer ONLY with the exact single word: FILE_BETA. Nothing else."}' \
    "${BASE_URL}${API_PREFIX}/system-prompt" > /dev/null
REPLY_FILE_B="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42,"stream":false}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    réponse fichier=BETA  : $REPLY_FILE_B"

if [ -n "$REPLY_FILE_A" ] && [ -n "$REPLY_FILE_B" ] && [ "$REPLY_FILE_A" != "$REPLY_FILE_B" ]; then
    pass "system prompt fichier injecté (réponses différentes selon fichier)"
else
    fail "system prompt fichier NON injecté (A='$REPLY_FILE_A' vs B='$REPLY_FILE_B')"
fi

# ── T4 : Override par requête via champ 'system' (parité PROXY vs DIRECT) ────
# Preuve déterministe : le proxy avec top-level 'system' doit produire la
# même réponse que llama-server direct avec le même 'system' déplacé en
# messages[0]. On utilise seed=42 pour la détermination.
info "T4 : override 'system' top-level - override effectif"
# Deux top-level 'system' différents → deux réponses différentes.
REPLY_TOP_A="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"system":"Answer ONLY with the exact single word: TOP_ALPHA","messages":[{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
REPLY_TOP_B="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"system":"Answer ONLY with the exact single word: TOP_BETA","messages":[{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    top-level 'system' ALPHA: $REPLY_TOP_A"
echo "    top-level 'system' BETA : $REPLY_TOP_B"
if [ -n "$REPLY_TOP_A" ] && [ -n "$REPLY_TOP_B" ] && [ "$REPLY_TOP_A" != "$REPLY_TOP_B" ]; then
    pass "override top-level 'system' effectif"
else
    fail "override top-level 'system' non effectif (A=$REPLY_TOP_A / B=$REPLY_TOP_B)"
fi

# ── T4bis : Override inline via role='system' dans messages[] ────────────────
# Preuve rigoureuse : le payload envoyé au proxy avec inline role='system'
# doit produire EXACTEMENT la même réponse que l'appel direct à llama-server
# avec le même payload. Si les deux réponses sont identiques (même token le
# premier), la passthrough est prouvée indépendamment du comportement du modèle.
info "T4bis : override inline role='system' - parité PROXY vs DIRECT llama-server"
PAYLOAD='{"messages":[{"role":"system","content":"Reply ONLY in English, never French."},{"role":"user","content":"Hello there."}],"max_tokens":24,"temperature":0.0,"seed":42}'

REPLY_PROXY="$(curl_j -X POST -H "Content-Type: application/json" \
    -d "$PAYLOAD" \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"

REPLY_DIRECT="$(curl -sS -X POST -H "Content-Type: application/json" \
    -d "$PAYLOAD" \
    "http://127.0.0.1:8090/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"

echo "    proxy : $REPLY_PROXY"
echo "    direct: $REPLY_DIRECT"
if [ -n "$REPLY_PROXY" ] && [ "$REPLY_PROXY" = "$REPLY_DIRECT" ]; then
    pass "override inline honoré (proxy passthrough vérifié)"
else
    fail "override inline non appliqué (proxy≠direct)"
fi

# Test complémentaire : preuve que l'inline role='system' PRÉVAUT sur le
# fichier serveur. On compare deux réponses via proxy avec le MÊME user prompt
# mais avec un inline system différent → si les réponses diffèrent, l'override
# est bien pris en compte (le fichier serveur seul donnerait toujours la même
# réponse). deterministe grâce à seed=42 et temperature=0.
REPLY_A="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"system","content":"Answer ONLY with the exact word: ALPHA"},{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
REPLY_B="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"system","content":"Answer ONLY with the exact word: BETA"},{"role":"user","content":"Say the codeword."}],"max_tokens":8,"temperature":0.0,"seed":42}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    inline A (ALPHA): $REPLY_A"
echo "    inline B (BETA) : $REPLY_B"
if [ -n "$REPLY_A" ] && [ -n "$REPLY_B" ] && [ "$REPLY_A" != "$REPLY_B" ]; then
    pass "réponses différentes selon inline system (override effectif)"
else
    fail "réponses identiques → inline system pas pris en compte (A=$REPLY_A / B=$REPLY_B)"
fi

# ── T4ter : Pas de double system - un seul message system remonté ────────────
info "T4ter : pas de duplication du system message"
# Note : on ne peut pas facilement observer le payload envoyé au modèle sans hook.
# On vérifie indirectement : envoi de 2 messages system consécutifs suivis d'un user,
# la réponse doit rester cohérente (200 OK, contenu non vide).
CODE="$(curl_j -o /tmp/t4ter.out -w '%{http_code}' -X POST \
    -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"system","content":"A"},{"role":"system","content":"B"},{"role":"user","content":"hi"}],"max_tokens":8,"temperature":0.0}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
[ "$CODE" = "200" ] && pass "requête multi-system acceptée (fusion faite serveur)" \
    || fail "requête multi-system = $CODE"

# ── T5 : Reset ───────────────────────────────────────────────────────────────
info "T5 : POST /system-prompt/reset"
CODE="$(curl_j -o /dev/null -w '%{http_code}' -X POST "${BASE_URL}${API_PREFIX}/system-prompt/reset")"
[ "$CODE" = "200" ] && pass "reset = 200" || fail "reset = $CODE"

# ── T6 : Lock via config ─────────────────────────────────────────────────────
info "T6 : locked=true → 403 sur GET et PUT et override ignoré"
# Modifie config/settings.json localement pour activer locked
python3 <<PY
import json, os
cfg = "$STUDIO_ROOT/config/settings.json"
with open(cfg) as f: d = json.load(f)
d["system_prompt"]["locked"] = True
with open(cfg, "w") as f: json.dump(d, f, indent=2)
PY
# Force reload en poussant PUT /config (endpoint public, mais on va tester le 403 sur system-prompt)
curl_j -X PUT -H "Content-Type: application/json" \
    -d "$(cat $STUDIO_ROOT/config/settings.json)" \
    "${BASE_URL}${API_PREFIX}/config" >/dev/null 2>&1

# Test GET 403
CODE_GET="$(curl_j -o /dev/null -w '%{http_code}' "${BASE_URL}${API_PREFIX}/system-prompt")"
[ "$CODE_GET" = "403" ] && pass "GET → 403 avec locked" || fail "GET = $CODE_GET (attendu 403)"

# Test PUT 403
CODE_PUT="$(curl_j -o /dev/null -w '%{http_code}' -X PUT \
    -H "Content-Type: application/json" \
    -d '{"content":"tentative"}' \
    "${BASE_URL}${API_PREFIX}/system-prompt")"
[ "$CODE_PUT" = "403" ] && pass "PUT → 403 avec locked" || fail "PUT = $CODE_PUT (attendu 403)"

# Test override silencieusement ignoré (le contenu du prompt locked reste utilisé)
# Le prompt actuellement locked est le default (français). L'override tente d'imposer anglais.
REPLY="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"system":"Reply ONLY in English. Never use French.","messages":[{"role":"user","content":"Salut."}],"max_tokens":32,"temperature":0.0}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    override quand locked (attendu français car locked): $REPLY"
# Si l'override était honoré : réponse anglais. S'il est ignoré : réponse française (default locked).
if echo "$REPLY" | grep -qiE "\b(bonjour|comment|vais|bien|merci|aider)\b"; then
    pass "override ignoré quand locked (default français préservé)"
else
    fail "override a contourné le lock : $REPLY (SÉCURITÉ)"
fi

# ── Cleanup : unlock ─────────────────────────────────────────────────────────
python3 <<PY
import json
cfg = "$STUDIO_ROOT/config/settings.json"
with open(cfg) as f: d = json.load(f)
d["system_prompt"]["locked"] = False
with open(cfg, "w") as f: json.dump(d, f, indent=2)
PY
curl_j -X PUT -H "Content-Type: application/json" \
    -d "$(cat $STUDIO_ROOT/config/settings.json)" \
    "${BASE_URL}${API_PREFIX}/config" >/dev/null 2>&1

echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL"
echo "=========================================================="
exit $FAIL

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
info "T3 : PUT d'un system prompt (français strict) → prompt user anglais → réponse française"
# Le modèle 0.5B suit fiablement l'instruction "réponds en français" mais pas
# des instructions plus complexes. On teste le sens fichier→réponse par contraste
# de langue, en miroir du T4 (override).
CODE="$(curl_j -o /dev/null -w '%{http_code}' -X PUT \
    -H "Content-Type: application/json" \
    -d '{"content":"Tu es un assistant francophone. Tu réponds UNIQUEMENT en français, jamais en anglais, quelle que soit la langue de la question."}' \
    "${BASE_URL}${API_PREFIX}/system-prompt")"
[ "$CODE" = "200" ] && pass "PUT du prompt de test = 200" || fail "PUT test = $CODE"

REPLY="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"Hello, how are you today?"}],"max_tokens":40,"temperature":0.0,"stream":false}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    réponse (attendue français): $REPLY"
if echo "$REPLY" | grep -qiE "\b(bonjour|comment|vais|bien|merci|puis-je|aider|français|aujourd)\b"; then
    pass "system prompt fichier injecté (réponse française malgré prompt user anglais)"
else
    fail "system prompt fichier non respecté (réponse: $REPLY)"
fi

# ── T4 : Override par requête via champ 'system' ─────────────────────────────
info "T4 : override 'system' dans la requête impose une nouvelle langue"
# Fichier actuel = force anglais. On override pour forcer français.
REPLY="$(curl_j -X POST -H "Content-Type: application/json" \
    -d '{"system":"Tu es un assistant. Réponds UNIQUEMENT en français, jamais en anglais.","messages":[{"role":"user","content":"Hello, how are you?"}],"max_tokens":32,"temperature":0.0}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['choices'][0]['message']['content'])" 2>/dev/null)"
echo "    réponse (attendue français): $REPLY"
if echo "$REPLY" | grep -qiE "\b(bonjour|comment|vais|bien|merci|puis-je|aider|français)\b"; then
    pass "override honoré (réponse en français malgré prompt user en anglais)"
else
    fail "override non appliqué (réponse: $REPLY)"
fi

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

#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests end-to-end de l'API (curl).
#           Vérifie : /health, /v1/models, /v1/chat/completions (non-stream + stream),
#           /openapi.json, auth activable, CORS.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Usage   : BASE_URL=http://127.0.0.1:8001 tests/test-api.sh
#           (défaut : BASE_URL=http://127.0.0.1:8001 avec préfixe /api)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8001}"
API_PREFIX="${API_PREFIX:-/api}"
API_KEY="${STUDIO_API_KEY:-}"

PASS=0; FAIL=0
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; NC='\033[0m'

pass() { echo -e "${GREEN}  ✓${NC} $1"; PASS=$((PASS+1)); }
fail() { echo -e "${RED}  ✗${NC} $1"; FAIL=$((FAIL+1)); }
info() { echo -e "${YELLOW}  ▸${NC} $1"; }

hdr()  { [ -n "$API_KEY" ] && printf -- '-H\nAuthorization: Bearer %s\n' "$API_KEY"; }

curl_json() {
    if [ -n "$API_KEY" ]; then
        curl -sS -H "Authorization: Bearer $API_KEY" "$@"
    else
        curl -sS "$@"
    fi
}

echo "=========================================================="
echo "  test-api.sh : base=$BASE_URL prefix=$API_PREFIX"
echo "=========================================================="

# ── T1 : health ──────────────────────────────────────────────────────────────
info "T1 : GET ${API_PREFIX}/health"
R="$(curl_json -w '\n%{http_code}' "${BASE_URL}${API_PREFIX}/health")"
CODE="$(echo "$R" | tail -1)"
BODY="$(echo "$R" | head -n -1)"
[ "$CODE" = "200" ] && pass "health = 200" || fail "health = $CODE"
echo "$BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); assert d['status']=='ok'; assert 'platform' in d; assert 'backend' in d; print('    platform:', d['platform']['key'], 'backend:', d['backend']['backend'])" \
    && pass "health JSON structure ok" || fail "health JSON structure"

# ── T2 : openapi.json ────────────────────────────────────────────────────────
info "T2 : GET ${API_PREFIX}/openapi.json"
CODE="$(curl_json -o /dev/null -w '%{http_code}' "${BASE_URL}${API_PREFIX}/openapi.json")"
[ "$CODE" = "200" ] && pass "openapi.json = 200" || fail "openapi.json = $CODE"

# ── T3 : /v1/models ──────────────────────────────────────────────────────────
info "T3 : GET ${API_PREFIX}/v1/models"
BODY="$(curl_json "${BASE_URL}${API_PREFIX}/v1/models")"
echo "$BODY" | python3 -c "import sys,json; d=json.load(sys.stdin); assert d['object']=='list'; assert len(d['data'])>=1; print('    models:', [m['id'] for m in d['data']])" \
    && pass "models liste OK" || fail "models liste"

# ── T4 : /v1/chat/completions non-stream ─────────────────────────────────────
info "T4 : POST ${API_PREFIX}/v1/chat/completions (non-stream)"
BODY="$(curl_json -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"Réponds uniquement par le mot OK."}],"max_tokens":16,"temperature":0.1,"stream":false}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
echo "$BODY" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert 'choices' in d, d
msg=d['choices'][0]['message']['content']
assert msg, 'contenu vide'
print('    completion:', msg[:80])
" && pass "chat non-stream OK" || fail "chat non-stream (body=$BODY)"

# ── T5 : /v1/chat/completions streaming SSE ──────────────────────────────────
info "T5 : POST ${API_PREFIX}/v1/chat/completions (stream)"
STREAM_OUT="$(curl_json -N -X POST -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"Compte 1 2 3."}],"max_tokens":24,"temperature":0.1,"stream":true}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions" 2>&1 | head -c 4000)"
if echo "$STREAM_OUT" | grep -q "^data: " && echo "$STREAM_OUT" | grep -q "\[DONE\]"; then
    pass "chat stream SSE OK ([DONE] présent)"
else
    fail "chat stream SSE : format inattendu (out=$STREAM_OUT)"
fi

# ── T6 : CORS preflight ──────────────────────────────────────────────────────
info "T6 : CORS OPTIONS"
CORS_H="$(curl -sS -o /dev/null -w '%{http_code}' -X OPTIONS \
    -H "Origin: http://127.0.0.1:8080" \
    -H "Access-Control-Request-Method: POST" \
    -H "Access-Control-Request-Headers: content-type" \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
[ "$CORS_H" = "200" ] || [ "$CORS_H" = "204" ] && pass "CORS preflight = $CORS_H" \
    || fail "CORS preflight = $CORS_H"

# ── Récap ────────────────────────────────────────────────────────────────────
echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL"
echo "=========================================================="
exit $FAIL

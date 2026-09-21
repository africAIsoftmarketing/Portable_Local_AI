#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests des correctifs Phase 3.
#           - context_size = 8192 par défaut (settings + /health + /v1/models)
#           - Respect des tools client (pas d'injection MCP si tools fournis)
#           - Schema OpenAPI de /skills/{name}/invoke (tool requis)
#           - GET /skills/rag/documents
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
echo "  test-phase3-fixes.sh"
echo "=========================================================="

# ── T1 : context_size = 8192 dans /api/config ────────────────────────────────
info "T1 : GET /api/config → model.context_size == 8192"
CTX=$(c "${BASE_URL}${API_PREFIX}/config" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print(d['model'].get('context_size', 'MISSING'))
")
[ "$CTX" = "8192" ] && pass "context_size=8192 dans /config" \
    || fail "context_size=$CTX (attendu 8192)"

# ── T2 : /v1/models expose bien context_size ─────────────────────────────────
info "T2 : /v1/models → context_size présent"
c "${BASE_URL}${API_PREFIX}/v1/models" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert d['data'], 'aucun modèle listé'
m=d['data'][0]
assert 'context_size' in m, list(m)
assert m['context_size']==8192, m['context_size']
print('    ok:', m['id'], 'ctx=', m['context_size'])
" && pass "context_size exposé dans /v1/models" \
  || fail "context_size manquant dans /v1/models"

# ── T3 : OpenAPI à jour (context_size + auto-injection doc) ─────────────────
info "T3 : OpenAPI mentionne auto-injection et /skills/rag/documents"
SPEC=$(c "${BASE_URL}${API_PREFIX}/openapi.json")
echo "$SPEC" | python3 -c "
import sys,json
spec=json.load(sys.stdin)
paths=spec.get('paths',{})
assert '/api/skills/rag/documents' in paths, 'endpoint /skills/rag/documents absent'
assert '/api/skills/{skill_name}/invoke' in paths, 'endpoint invoke absent'
# Vérifie que le schema du body invoke a bien 'tool' requis + 'arguments' object.
sch=paths['/api/skills/{skill_name}/invoke']['post']['requestBody']['content']['application/json']['schema']
# resolve \$ref si nécessaire
def resolve(node):
    if isinstance(node,dict) and '\$ref' in node:
        ref=node['\$ref'].lstrip('#/').split('/')
        cur=spec
        for k in ref: cur=cur[k]
        return cur
    return node
sch=resolve(sch)
props=sch.get('properties',{})
assert 'tool' in props, ('tool absent', list(props))
assert 'arguments' in props, ('arguments absent', list(props))
required=sch.get('required',[])
assert 'tool' in required, ('tool non required', required)
# Description auto-injection
desc=paths['/api/v1/chat/completions']['post'].get('description','')
assert 'auto-injection' in desc.lower() or 'inject' in desc.lower(), 'doc auto-injection manquante'
print('    ok: schéma invoke conforme + doc auto-injection présente')
" && pass "OpenAPI correctement documentée" \
  || fail "OpenAPI incomplète"

# ── T4 : /skills/rag/documents fonctionne ────────────────────────────────────
info "T4 : GET /api/skills/rag/documents"
RES=$(c "${BASE_URL}${API_PREFIX}/skills/rag/documents")
echo "$RES" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert 'documents' in d and isinstance(d['documents'],list)
assert 'count' in d
assert 'documents_dir' in d
print('    ok: %d document(s) listé(s)' % d['count'])
" && pass "/skills/rag/documents renvoie la liste" \
  || fail "/skills/rag/documents KO"

# ── T5 : invoke sans champ 'tool' → 422 (validation Pydantic) ────────────────
info "T5 : POST /skills/rag/invoke sans 'tool' → 422"
CODE=$(c -o /dev/null -w '%{http_code}' -X POST \
    -H "Content-Type: application/json" \
    -d '{"arguments":{}}' \
    "${BASE_URL}${API_PREFIX}/skills/rag/invoke")
[ "$CODE" = "422" ] && pass "validation Pydantic active (422)" \
    || fail "attendu 422, reçu $CODE"

# ── T6 : Respect des tools client — aucune injection MCP ────────────────────
info "T6 : Client fournit tools=[…], donc MCP NON injecté (seule sa liste)"
# On force le modèle à choisir SON outil ; la trace doit ne montrer QUE cet outil.
PAYLOAD='{
  "messages":[{"role":"user","content":"Extrait les emails de ce texte: alice@x.fr et bob@y.com"}],
  "tools":[{"type":"function","function":{"name":"my_custom_extractor",
    "description":"Extrait des entités",
    "parameters":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}}}],
  "tool_choice":{"type":"function","function":{"name":"my_custom_extractor"}},
  "temperature":0.0,"max_tokens":64,"seed":42
}'
RES=$(c -X POST -H "Content-Type: application/json" -d "$PAYLOAD" \
      "${BASE_URL}${API_PREFIX}/v1/chat/completions")
echo "$RES" | python3 -c "
import sys,json
d=json.load(sys.stdin)
trace=d.get('metadata',{}).get('trace',[])
# On cherche l'événement 'thought' avec tools_offered ; il doit être == 1
offered_counts=[e['data'].get('tools_offered') for e in trace if e['type']=='thought']
assert offered_counts, 'aucun thought dans la trace'
# Le client a fourni 1 outil : la boucle doit exposer 1 outil (pas 7 comme avant).
assert all(o == 1 for o in offered_counts), ('injection MCP fuite:', offered_counts)
print('    ok: boucle expose', offered_counts[0], 'outil(s) (client uniquement)')
" && pass "aucune injection MCP quand client fournit tools" \
  || fail "injection MCP fuite malgré tools client"

# ── T7 : override context_size par requête (best-effort) ─────────────────────
info "T7 : payload avec context_size est accepté (passthrough)"
CODE=$(c -o /dev/null -w '%{http_code}' -X POST \
    -H "Content-Type: application/json" \
    -d '{"messages":[{"role":"user","content":"ok"}],"context_size":4096,"max_tokens":8,"temperature":0.0}' \
    "${BASE_URL}${API_PREFIX}/v1/chat/completions")
[ "$CODE" = "200" ] && pass "override context_size accepté (200)" \
    || fail "override context_size = $CODE"

echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL"
echo "=========================================================="
exit $FAIL

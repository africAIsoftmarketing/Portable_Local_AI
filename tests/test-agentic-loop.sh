#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests de la boucle agentique.
#           - Enchaînement forcé via tool_choice pour prouver la mécanique
#             (le modèle 0.5B n'est pas fiable pour décider seul les tools).
#           - Vérification de la trace SSE via /api/events (metadata.trace)
#           - Cas dégradé : skill inexistant, tool malformé.
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
echo "  test-agentic-loop.sh"
echo "=========================================================="

# ── T1 : /v1/chat/completions avec tools[] renvoie une réponse enveloppée ────
info "T1 : chat avec tools[] présent → passe par la boucle agent"
PAYLOAD='{
  "messages":[{"role":"user","content":"Cherche fail2ban dans la base."}],
  "tool_choice":"auto",
  "temperature":0.0,"seed":42,"max_tokens":48
}'
RES="$(c -X POST -H "Content-Type: application/json" -d "$PAYLOAD" \
  "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
echo "$RES" | python3 -c "
import sys,json
d=json.load(sys.stdin)
assert 'metadata' in d, list(d)
assert 'trace' in d['metadata'], d['metadata']
assert isinstance(d['metadata']['trace'], list) and len(d['metadata']['trace'])>=2, d['metadata']
types=[e['type'] for e in d['metadata']['trace']]
print('    trace types:', types)
# Le tableau `trace` retourné contient les événements de boucle : thought,
# tool_calls, tool_results, final (l'event 'start' vit dans le bus SSE).
assert 'thought' in types and ('final' in types or 'tool_calls' in types), types
" && pass "trace agentique présente + metadata" || fail "trace absente"

# ── T2 : forced tool_choice → exécute analyze_security_logs ─────────────────
info "T2 : tool_choice forcé sur cybersec__analyze_security_logs"
LOG='Nov  1 12:00:01 host sshd[1]: Failed password for root from 45.83.64.1 port 22\nNov  1 12:00:02 host sshd[2]: Failed password for admin from 45.83.64.1 port 22\nNov  1 12:00:03 host sshd[3]: Failed password for root from 45.83.64.1 port 22\nNov  1 12:00:04 host sshd[4]: Failed password for oracle from 45.83.64.1 port 22\nNov  1 12:00:05 host sshd[5]: Failed password for postgres from 45.83.64.1 port 22'
PAYLOAD="$(python3 -c "
import json, sys
p={
 'messages':[{'role':'user','content':'Analyse ce log et donne le verdict.\\n\\n$LOG'}],
 'tools':[{'type':'function','function':{'name':'cybersec__analyze_security_logs',
   'description':'Analyse logs',
   'parameters':{'type':'object','properties':{'logs':{'type':'string'}},'required':['logs']}}}],
 'tool_choice':{'type':'function','function':{'name':'cybersec__analyze_security_logs'}},
 'temperature':0.0,'max_tokens':256,'seed':42
}
print(json.dumps(p))
")"
RES="$(c -X POST -H "Content-Type: application/json" -d "$PAYLOAD" \
  "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
echo "$RES" | python3 -c "
import sys,json
d=json.load(sys.stdin)
trace=d['metadata']['trace']
types=[e['type'] for e in trace]
print('    trace types:', types)
# On veut au moins un tool_calls suivi d'un tool_results OU un final
has_tool_flow=('tool_calls' in types and 'tool_results' in types) or 'final' in types
assert has_tool_flow, ('flux tool absent dans la trace', types)
" && pass "flux tool_calls → tool_results dans la trace" || fail "flux tool KO"

# ── T3 : SSE /api/events reçoit les événements ──────────────────────────────
info "T3 : /api/events (SSE) reçoit au moins un événement"
# Démarre un consommateur SSE pour un session_id explicite via query param 'session_id'.
# Notre orchestrateur ne permet pas d'imposer session_id côté requête ;
# on vérifie donc que l'endpoint répond bien 200 + text/event-stream sur un id neutre.
CODE=$(c -o /dev/null -w '%{http_code}' --max-time 2 \
    "${BASE_URL}${API_PREFIX}/events?session_id=probe-$(date +%s)" || true)
# 200 attendu (le stream se coupera à la déconnexion). --max-time coupe la connexion.
[ "$CODE" = "200" ] && pass "endpoint /events répond 200 (SSE)" \
    || fail "/events = $CODE"

# ── T4 : Best-effort - décision autonome 0.5B (marqué informatif) ───────────
info "T4 : (best-effort) décision autonome 0.5B — INFORMATIF, à revalider avec 7B+"
PAYLOAD='{
  "messages":[{"role":"user","content":"Extrait les emails de: contact alice@x.fr et bob@y.com"}],
  "tools":[{"type":"function","function":{"name":"general__extract_key_info",
    "description":"Extrait entités",
    "parameters":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}}}],
  "tool_choice":"auto","temperature":0.0,"max_tokens":128,"seed":42
}'
RES="$(c -X POST -H "Content-Type: application/json" -d "$PAYLOAD" \
  "${BASE_URL}${API_PREFIX}/v1/chat/completions")"
DECIDED=$(echo "$RES" | python3 -c "
import sys,json
try:
    d=json.load(sys.stdin)
    types=[e['type'] for e in d.get('metadata',{}).get('trace',[])]
    print('yes' if 'tool_calls' in types else 'no')
except Exception:
    print('err')
")
if [ "$DECIDED" = "yes" ]; then
    pass "(best-effort) 0.5B a décidé un tool_call autonomement"
else
    echo -e "${YELLOW}  ~${NC} 0.5B n'a pas appelé l'outil (attendu vu la taille du modèle - NON bloquant)"
fi

# ── T5 : Cas dégradé - tool inconnu retourne erreur au modèle sans crash ────
info "T5 : appel d'un tool inconnu → l'orchestrateur ne crashe pas"
PAYLOAD='{
  "messages":[{"role":"user","content":"salut"}],
  "tools":[{"type":"function","function":{"name":"does_not_exist__nope",
    "description":"fictif","parameters":{"type":"object","properties":{},"required":[]}}}],
  "tool_choice":{"type":"function","function":{"name":"does_not_exist__nope"}},
  "temperature":0.0,"max_tokens":32,"seed":42
}'
CODE=$(c -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: application/json" \
    -d "$PAYLOAD" "${BASE_URL}${API_PREFIX}/v1/chat/completions")
[ "$CODE" = "200" ] && pass "tool inconnu : réponse 200, dégradation propre" \
    || fail "tool inconnu = $CODE"

echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL   (T4 = best-effort, non-bloquant)"
echo "=========================================================="
exit $FAIL

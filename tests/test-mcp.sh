#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : tests des skills MCP.
#           - GET /api/skills (registre + status)
#           - POST /api/skills/<name>/invoke pour chaque outil des 4 skills
#           - Timeout / respawn (best-effort : difficile à reproduire côté API)
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
echo "  test-mcp.sh"
echo "=========================================================="

# ── T1 : registre chargé, 4 skills présents ─────────────────────────────────
info "T1 : GET ${API_PREFIX}/skills"
BODY="$(c "${BASE_URL}${API_PREFIX}/skills")"
echo "$BODY" | python3 -c "
import sys, json
d = json.load(sys.stdin)
skills = {s['name']: s for s in d['skills']}
required = ['cybersec','accounting','rag','general']
missing = [s for s in required if s not in skills]
running = [s for s, v in skills.items() if v['status']=='running']
print('    skills:', list(skills), '| running:', running)
assert not missing, f'skills manquants: {missing}'
assert set(running) == set(required), f'skills non running: {set(required)-set(running)}'
" && pass "4 skills running" || fail "skills manquants ou KO"

# ── T2 : cybersec.analyze_security_logs ──────────────────────────────────────
info "T2 : cybersec.analyze_security_logs (brute-force SSH)"
LOG=$'Nov  1 12:00:01 host sshd[1]: Failed password for root from 5.188.10.176 port 22\nNov  1 12:00:02 host sshd[2]: Failed password for admin from 5.188.10.176 port 22\nNov  1 12:00:03 host sshd[3]: Failed password for invalid user oracle from 5.188.10.176 port 22\nNov  1 12:00:04 host sshd[4]: Failed password for root from 5.188.10.176 port 22\nNov  1 12:00:05 host sshd[5]: Failed password for postgres from 5.188.10.176 port 22\nNov  1 12:00:06 host sshd[6]: Failed password for root from 5.188.10.176 port 22\nNov  1 12:05:00 host sudo:   alice : TTY=pts/0 ; PWD=/home/alice ; USER=root ; COMMAND=/bin/rm -rf /tmp/foo'
RES="$(c -X POST -H "Content-Type: application/json" \
  -d "$(python3 -c "import json,sys;print(json.dumps({'tool':'analyze_security_logs','arguments':{'logs': sys.argv[1]}}))" "$LOG")" \
  "${BASE_URL}${API_PREFIX}/skills/cybersec/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
assert r['summary']['anomalies_count']>=2, r
types=[a['type'] for a in r['anomalies']]
assert 'ssh_brute_force' in types, types
print('    anomalies:', types)
" && pass "brute-force + sudo suspicieux détectés" || fail "détection incomplète"

# ── T3 : cybersec.check_ip_reputation ────────────────────────────────────────
info "T3 : cybersec.check_ip_reputation (IP dans blocklist)"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"check_ip_reputation","arguments":{"ip":"5.188.10.176"}}' \
  "${BASE_URL}${API_PREFIX}/skills/cybersec/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
assert r['verdict'] in ('malicious','suspicious'), r
print('    verdict:', r['verdict'], '| sources:', r['sources'])
" && pass "IP flagée correctement" || fail "verdict incorrect"

# ── T4 : accounting.verify_accounting_entries ────────────────────────────────
info "T4 : accounting.verify_accounting_entries (déséquilibre volontaire)"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"verify_accounting_entries","arguments":{"entries":[
    {"account":"512","debit":1000,"credit":0,"journal":"BQ"},
    {"account":"411","debit":0,"credit":950,"journal":"BQ"}
  ]}}' \
  "${BASE_URL}${API_PREFIX}/skills/accounting/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
assert r['balanced'] is False, r
assert abs(r['totals']['imbalance']-50) < 0.001, r
print('    imbalance:', r['totals']['imbalance'])
" && pass "déséquilibre détecté (+50)" || fail "déséquilibre non détecté"

# ── T5 : accounting.calculate_financial_ratios ───────────────────────────────
info "T5 : accounting.calculate_financial_ratios"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"calculate_financial_ratios","arguments":{
    "balance_sheet":{"current_assets":300000,"current_liabilities":200000,"inventory":50000,
                     "total_assets":500000,"total_liabilities":250000,"total_equity":250000},
    "income_statement":{"revenue":800000,"net_income":80000,"operating_income":100000}
  }}' \
  "${BASE_URL}${API_PREFIX}/skills/accounting/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
assert float(r['ratios']['current_ratio']['value']) == 1.5, r
print('    current_ratio=1.5, ROE=', r['ratios']['return_on_equity']['value'])
" && pass "ratios cohérents" || fail "ratios KO"

# ── T6 : rag.search_knowledge_base ───────────────────────────────────────────
info "T6 : rag.search_knowledge_base (indexation auto + recherche)"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"search_knowledge_base","arguments":{"query":"brute-force SSH fail2ban","top_k":3}}' \
  "${BASE_URL}${API_PREFIX}/skills/rag/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
assert r['index_size']>0, r
assert len(r['results'])>=1, r
top=r['results'][0]
assert 'fail2ban' in top['content'].lower() or 'brute' in top['content'].lower(), top
print('    top source:', top['source'], '| score:', top['score'])
" && pass "recherche BM25 pertinente" || fail "recherche KO"

# ── T7 : rag.reindex_knowledge_base ──────────────────────────────────────────
info "T7 : rag reindex (endpoint dédié)"
RES="$(c -X POST "${BASE_URL}${API_PREFIX}/skills/rag/reindex")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)
assert r['status']=='ok', r
assert r['result']['passages_indexed']>0, r
print('    passages:', r['result']['passages_indexed'])
" && pass "réindexation OK" || fail "réindexation KO"

# ── T8 : general.generate_structured_report ──────────────────────────────────
info "T8 : general.generate_structured_report"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"generate_structured_report","arguments":{
    "title":"Rapport test","sections":[
      {"heading":"Contexte","content":"Analyse fictive."},
      {"heading":"Chiffres","table":[["m","€"],["Jan",100],["Fév",120]]}
    ]}}' \
  "${BASE_URL}${API_PREFIX}/skills/general/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
md=r['markdown']
assert md.startswith('# Rapport test'), md[:80]
assert '## Contexte' in md
assert '| m | € |' in md, md
print('    lines:', r['line_count'])
" && pass "rapport Markdown structuré" || fail "rapport KO"

# ── T9 : general.extract_key_info ────────────────────────────────────────────
info "T9 : general.extract_key_info"
RES="$(c -X POST -H "Content-Type: application/json" \
  -d '{"tool":"extract_key_info","arguments":{"text":"Contact : alice@ex.com au 06 12 34 56 78. Virement 1500,00 € vers FR7630006000011234567890189 le 2026-08-24. IP suspect: 5.188.10.176. Voir https://ex.com/policy"}}' \
  "${BASE_URL}${API_PREFIX}/skills/general/invoke")"
echo "$RES" | python3 -c "
import sys,json
r=json.load(sys.stdin)['result']
e=r['entities']
assert e['emails']==['alice@ex.com'], e['emails']
assert e['phones_fr'], e['phones_fr']
assert e['ipv4']==['5.188.10.176']
assert e['ibans']==['FR7630006000011234567890189']
assert e['dates_iso']==['2026-08-24']
assert e['urls'], e['urls']
print('    total_entities:', r['total_entities'])
" && pass "extraction complète" || fail "extraction incomplète"

# ── T10 : skill inconnu → 404 ────────────────────────────────────────────────
info "T10 : POST /skills/unknown/invoke → 404"
CODE="$(c -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: application/json" \
  -d '{"tool":"x"}' "${BASE_URL}${API_PREFIX}/skills/unknown/invoke")"
[ "$CODE" = "404" ] && pass "skill inconnu = 404" || fail "skill inconnu = $CODE"

echo "----------------------------------------------------------"
echo "  PASS=$PASS  FAIL=$FAIL"
echo "=========================================================="
exit $FAIL

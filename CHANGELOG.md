# CHANGELOG

## [0.3.0] Phase 3 — Système agentique complet — 2026-08-24

### Ajouté
- **MCP stdio** (JSON-RPC 2.0 line-delimited) : framework `_shared/mcp_server.py` + client asynchrone `app/mcp/client.py` avec spawn, handshake `initialize` + `tools/list`, appels `tools/call` timeoutés, respawn automatique (max 3 tentatives puis skill `unavailable`).
- **Registre MCP** (`app/mcp/registry.py`) : découverte automatique + lecture `skills/registry.json` + `config/mcp.json`.
- **4 skill packs, 7 outils fonctionnels** (aucun stub) :
  - `cybersec` : `analyze_security_logs`, `check_ip_reputation` (blocklist + CIDR).
  - `accounting` : `verify_accounting_entries`, `calculate_financial_ratios`.
  - `rag` : `search_knowledge_base` (BM25 pur Python vendored ≤ 50 Ko, indexation auto), `reindex_knowledge_base`.
  - `general` : `generate_structured_report`, `extract_key_info` (regex FR+EN : emails, URLs, IPs, téléphones, IBAN, SIRET, montants, dates).
- **Template** `mcp-servers/_template/` pour créer un skill en < 5 min.
- **Boucle agentique** (`app/agent/loop.py`) : max_tool_rounds=5, total_timeout=120 s, `asyncio.gather` avec échec partiel toléré, double stratégie tool calling (natif OpenAI + fallback JSON ```json / inline / `<tool_call>`).
- **Trace SSE** (`app/agent/trace.py`) : bus in-memory, endpoint `GET /api/events?session_id=...`, trace aussi retournée dans `metadata.trace` de `/v1/chat/completions`.
- **Endpoints** : `GET /api/skills`, `POST /api/skills/{name}/invoke`, `POST /api/skills/rag/reindex`, `GET /api/events`.
- **Docs** : `docs/COMPILATION.md` (build llama-server incluant cas glibc<2.38), `docs/ADD-SKILL.md`.
- **Tests** : `tests/test-mcp.sh` (10 tests skills), `tests/test-agentic-loop.sh` (5 scénarios agent).

### Corrigé
- **i18n reload** : la langue EN persiste maintenant au rechargement (init idempotent + détection navigateur + cache-buster).
- **Backend `reason_code`** : plus de français en dur dans `/api/health`, tout est machine-code + params (`no_gpu_detected`, `cuda_found`, …) traduit côté UI.

### Modifié
- `settings.json` : `mcp.enabled=true` (par défaut).
- `/api/health` : composant `mcp` reporte l'état réel du registre.

### Limites documentées
- **Décision autonome des tools** : Qwen 0.5B n'est PAS fiable pour décider seul des appels d'outils. Test T4 de `test-agentic-loop.sh` marqué `best-effort` et non-bloquant. Client doit valider avec un modèle 7B+ (Llama 3.1 8B, Qwen 2.5 7B, Mistral-Nemo).
- **Signature Ed25519** : toujours non implémentée (Phase 4).
- **Backends GPU** : binaires non fetchés par défaut (voir `docs/COMPILATION.md`).

---

## [0.2.0] Phase 2 — Cœur portable — 2026-08-24

### Ajouté
- Orchestrateur FastAPI : `/v1/chat/completions` (stream SSE + non-stream), `/v1/models`, `/health`.
- System prompt multi-source (override > preset > custom > default) avec `locked` (403 + override ignoré).
- Détection backend GPU (arbre CUDA > ROCm > Vulkan > CPU + Metal macOS).
- Launchers `start-linux.sh`, `start-mac.command`, `start-windows.bat` + `stop-*`.
- `scripts/fetch-binaries.sh/.ps1` (téléchargement releases officielles).
- UI statique HTML/CSS/JS vanilla avec i18n FR/EN.
- Tests `test-api.sh` (7/7), `test-system-prompt.sh` (13/13).

### Corrigé
- Override `role:"system"` inline dans `messages[]` (priorité top-level > inline > server).
- UI i18n complète et réversible (17 attributs `data-i18n`).
- Racine `/` = 200 (mini-frontend qui redirige vers `/api/`).

---

## [0.1.0] Phases 0 & 1 — Analyse + architecture — 2026-08-24

- `docs/ANALYSIS.md` (Phase 0).
- `docs/ARCHITECTURE.md` (Phase 1) + amendements Phase 2.

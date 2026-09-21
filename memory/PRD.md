# AfricAIsoft Portable Studio — PRD

## Problème à résoudre

Livrer une distribution portable USB 100% offline permettant à un utilisateur non-technique de faire tourner un LLM local (GGUF via `llama.cpp`) sur Windows 10/11, macOS (Intel + Apple Silicon) et Linux (x86_64 + arm64), avec une API OpenAI-compatible, une UI FR/EN, une boucle agentique et des skills MCP métiers, le tout sans traces (zero-trace) et sans installation sur la machine hôte.

## Personas

- **Analyste sécurité** en mission chez un client : besoin d'analyser des logs sans les exporter.
- **Comptable / auditeur** : vérification d'écritures et calculs, données strictement confidentielles.
- **Juriste** : relecture et structuration de documents sensibles, jamais dans le cloud.
- **Formateur / démonstrateur IA** : besoin d'une démo qui marche sans WiFi.

## Cœur des exigences

- **Portable** : boot depuis clé USB (FAT32/exFAT/NTFS), 0 installation, 0 trace.
- **Multi-plateforme** : 5 cibles (linux-x86_64, linux-aarch64, darwin-arm64, darwin-x86_64, windows-x86_64).
- **API OpenAI-compat** : `/v1/chat/completions` streaming + non-streaming, `/v1/models`.
- **MCP** : 7 skills métiers (analyse logs, écritures comptables, RAG BM25, rapports, extraction, IP reputation, ratios financiers).
- **Boucle agentique** : max 5 rounds, tool calling natif + fallback JSON.
- **UI FR/EN** statique, sans build.
- **Zero-trace** : bind loopback, purge `/tmp`, pas de logs sensibles, API key locale.
- **Signature Ed25519** du manifest release.json (obligatoire en production, off + warning en dev).

## Historique de livraison

### Phase 0 (analyse — livrée)

- Rapport `docs/ANALYSIS.md`. Dépôt actuel = wrapper shell autour de `llama.cpp`. Aucune couche applicative. UI supprimée dans un commit précédent.

### Phase 1 (architecture — livrée)

- Document `docs/ARCHITECTURE.md`. 12 sections (composants, séquence démarrage, détection GPU, boucle agentique, MCP, system prompt, sécurité, config, Python portable, tests, arborescence, décisions).

### Phase 2 (cœur portable — livrée, 24/08/2026)

- Restructuration du dépôt : `app/` orchestrateur, `config/`, `ui/`, `scripts/`, `bin/<plat>/<backend>/`, `tests/`, `data/pids/`, `logs/`.
- Orchestrateur FastAPI complet : proxy OpenAI-compat streaming SSE + non-streaming, `/v1/models`, `/health` avec plateforme + backend + composants, `/openapi.json`, `/docs`, auth optionnelle Bearer, CORS configurable, rate-limit stub (off par défaut).
- Gestion system prompt multi-source (override request > preset > custom > default) avec verrouillage (`locked=true` → 403 lecture ET écriture, override silencieusement ignoré).
- Détection backend GPU (`scripts/detect-backend.sh` + `.ps1` + module Python) : arbre CUDA > ROCm > Vulkan > CPU, Metal sur macOS, timeout individuel 1 s.
- Scripts de démarrage : `start-linux.sh` (fonctionnel, testé ici), `start-mac.command`, `start-windows.bat`, `stop-*` avec kill des PIDs + libération ports + purge staging.
- `scripts/fetch-binaries.sh/.ps1` : téléchargement des releases officielles `ggml-org/llama.cpp`.
- Binaire `llama-server` **compilé depuis les sources** (b11071) pour arm64 car les binaires officiels requièrent glibc 2.38 non disponible dans le conteneur Debian 12 (glibc 2.36).
- Modèle Qwen2.5-0.5B-Instruct-Q4_K_M téléchargé et fonctionnel (490 Mo).
- UI statique fonctionnelle : chat streaming, panneau system prompt éditable, badge health, i18n FR/EN, palette sable/vert forêt, typographie serif humaniste.
- Tests `test-api.sh` (7/7 PASS) et `test-system-prompt.sh` (10/10 PASS attendus).

## Tests réussis (Phase 2)

- `GET /api/health` = 200 en < 100 ms.
- `POST /api/v1/chat/completions` non-stream : réponse cohérente ("OK" sur prompt de contrôle).
- `POST /api/v1/chat/completions` stream : SSE bien formé, `[DONE]` présent.
- System prompt injecté (marqueur `PROMPT_OK:` respecté).
- Override par requête honoré (`OVERRIDE_APPLIED` retourné).
- `locked=true` → `GET/PUT /system-prompt` = 403 ; override ignoré silencieusement.
- `/api/openapi.json` = 200.
- CORS preflight OK.

## Backlog (Phase 3+)

- **P0** — Serveurs MCP stdio + 7 skills métiers (JSON-RPC 2.0, spawn stdio, respawn auto).
- **P0** — Boucle agentique : détection tool_calls natif OpenAI + fallback JSON, `max_tool_rounds=5`, exécution parallèle `asyncio.gather` avec échec partiel toléré, trace SSE côté UI.
- **P1** — RAG `search_knowledge_base` : BM25 pur Python vendored ≤ 50 Ko, indexation auto au démarrage + réindex UI. Formats txt/md/pdf.
- **P1** — Runtime Python portable via `python-build-standalone` (Linux/macOS) et embeddable package Windows, wheels offline.
- **P1** — `release.json` signé Ed25519, vérification obligatoire en prod.
- **P2** — Compilation cross-plateforme des binaires GPU (CUDA/Vulkan/Metal).
- **P2** — UI complète (édition config avancée, gestion presets, historique conversations, thèmes).
- **P2** — Tests GPU réels (matrice CI cross-platform).
- **P2** — Manuel utilisateur FR/EN + guide de déploiement USB.

## Next Actions

1. Phase 3 : MCP + agentique + RAG BM25.
2. Signature Ed25519 du manifest + vérification stricte en prod.
3. Bundler Python portable et wheels offline.
4. Documenter les binaires GPU (compilation ou fetch).

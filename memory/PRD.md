# PRD — AfricAIsoft Portable Studio

Distribution portable USB 100% offline pour exécuter des LLM GGUF via
`llama.cpp` avec API OpenAI-compatible, skills MCP métiers, boucle
agentique, RAG local et UI web légère FR/EN.

## Personas
1. **Auditeur cyber terrain** : brancher une clé USB sur un poste isolé, analyser
   des logs auth.log, produire un rapport structuré, tout en offline.
2. **Contrôleur comptable** : vérifier des écritures, calculer des ratios, générer
   un rapport en local, sans exfiltrer les données.
3. **Formateur / démonstrateur IA** : montrer un stack agentique local sur un
   ordinateur invité sans installation.

## Contraintes clés
- 100 % offline, zero-trace (bind loopback strict, purge tmp au SIGTERM).
- Multi-plateforme : Linux (x64/arm64), macOS (Intel/Apple), Windows x64.
- Aucune dépendance JS runtime (Vanilla HTML/JS/CSS < 5 Mo).
- Aucun framework backend lourd hors FastAPI + stdlib.

## Architecture
Voir `docs/ARCHITECTURE.md`. Trois couches :
1. Bootstrap Bash/Batch (fetch binaires, launchers).
2. Orchestrateur FastAPI (proxy OpenAI, boucle agent, MCP registry).
3. UI Vanilla JS (chat, skills, RAG, modèles, config).

## Réalisé (par phase)
- **Phase 0** (24/08/2026) : `docs/ANALYSIS.md`.
- **Phase 1** (24/08/2026) : `docs/ARCHITECTURE.md`.
- **Phase 2** (24/08/2026) : FastAPI orchestrateur, proxy OpenAI, system prompt
  résolveur, launchers, tests API 20/20.
- **Phase 3** (24/08/2026) : MCP stdio, 4 skills (cybersec/accounting/rag/general,
  8 outils), boucle agentique, RAG BM25, SSE trace, tests 35/35.
- **Phase 3 correctifs** (24/08/2026) : context_size 8192 + override, respect
  tools client, schéma OpenAPI invoke, endpoint /skills/rag/documents,
  note honnête 0.5B → 3B/7B.
- **Phase 4** (24/08/2026) : UI Web complète (Vanilla JS strict, i18n FR/EN,
  dark/light, conversations persistées, streaming SSE + Markdown, trace
  agentique repliable, panneaux Skills/RAG/Modèles/System-prompt/Config,
  bascule de modèle). Tests UI 21/21 + Phase3 7/7 + 3-étapes 4/4.

## Backlog P1
- Compilation binaires macOS ARM/Intel + Windows x64 (Phase 5).
- `scripts/build-portable.sh` : bundle Python standalone + wheels offline.
- Checksums SHA256 des binaires téléchargés (supply-chain).
- Mode zero-trace hardened : tmpfs chiffré + purge SIGKILL.
- Playwright E2E complet dans CI.

## Backlog P2
- Génération de modèles fine-tunés spécifiques métier (cybersec-3B, compta-3B).
- Skill supplémentaires : legal_qa, iot_diag, data_privacy.
- Upload de documents RAG via UI (drag & drop).
- Multi-user (mode LAN équipe).

## Tests
- `tests/test-api.sh` (7/7)
- `tests/test-system-prompt.sh` (10/13 — 3 fails = refus systématique du 0.5B)
- `tests/test-mcp.sh` (10/10)
- `tests/test-agentic-loop.sh` (5/5)
- `tests/test-phase3-fixes.sh` (7/7)
- `tests/test-agentic-3steps.sh` (4/4)
- `tests/test-ui.sh` (21/21)

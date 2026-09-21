# PHASE 0 — Analyse du dépôt `africAIsoftmarketing/Portable_Local_AI`

> **Rôle du document** : rapport d'analyse en lecture seule produit avant toute
> nouvelle transformation. Aucune modification de code n'a été effectuée
> pendant cette phase — seul ce fichier a été (ré)écrit.
>
> **Auteur** : AfricAIsoft (agent d'analyse) — **Licence** : MIT
> **Date** : 2026-08-24 — **Cible** : AfricAIsoft Portable Studio
> **Branche** : `main` — **Base analysée** : commit `3b71cae` (HEAD local)

---

## 0. Résumé exécutif honnête

Le dépôt **n'est PAS vide** et **n'est PAS un point de départ zéro** malgré
l'intitulé « Phase 0 » de la mission. Il contient déjà, en local, l'essentiel
de la transformation prévue pour « AfricAIsoft Portable Studio », commité
sur `main` en 9 commits d'avance sur `origin/main` :

- Le socle historique **PortableAI** (installer/launcher `llama.cpp` seul)
  d'origine amont, encore présent à la racine (`install.sh`, `install.bat`,
  `start.sh`, `start.bat`, `bin/{linux,mac,windows}/…`, `README.md`).
- La couche **AfricAIsoft Portable Studio** superposée par-dessus, avec :
  orchestrateur FastAPI (`app/`), UI web statique FR/EN (`ui/`), 4 skills
  MCP stdio (`mcp-servers/`), scripts multi-plateformes (`scripts/`,
  `start-linux.sh`, `start-mac.command`, `start-windows.bat`), configuration
  et schéma (`config/`), documentation (`docs/`), Key Builder C#/.NET 8 WPF
  (`keybuilder/`), suites de tests (`tests/`, `keybuilder/tests/`),
  scripts de packaging (`scripts/build-portable.{sh,ps1}`,
  `scripts/sign-release.py`) et manifest de release (`release.json`).

Autrement dit, les phases 1 à 5 du plan de transformation semblent avoir
déjà été exécutées lors d'itérations précédentes ; **il ne reste, en Phase 0,
qu'à documenter l'existant, décider ce qui est réutilisable, et proposer une
base de départ propre**. Ce rapport le fait sans rien altérer.

---

## 1. Inventaire des fichiers et de leur rôle

### 1.1 Racine

| Chemin | Type | Rôle | Origine |
|---|---|---|---|
| `README.md` | doc | Doc utilisateur mixte : PortableAI (llama.cpp seul) **et** encart Key Builder en tête. Bilingue partiel FR/EN. | Amont + patch AfricAIsoft |
| `LICENSE` | légal | MIT © AfricAIsoft 2026. | AfricAIsoft |
| `CHANGELOG.md` | doc | Journal versions 0.1.0 → 0.3.0 (phases 0-3). Rien pour 0.4/0.5. | AfricAIsoft |
| `VERSION` | texte | `0.2.0-phase2` (désynchronisé par rapport aux phases 3-5 commitées). | AfricAIsoft |
| `.gitignore` | git | Ignore `models/*.gguf`, `bin/**/*.so*|*.dll|*.dylib`, `runtime/**/python/`, caches. | AfricAIsoft |
| `release.json` | manifest | Squelette manifest `dev-build`, `unsigned`, aucun binaire listé. | AfricAIsoft |
| `install.sh`, `install.bat` | script | **PortableAI d'origine** — télécharge llama.cpp depuis GitHub releases (5 plateformes). Non intégré à l'orchestrateur. | Amont |
| `start.sh`, `start.bat` | script | **PortableAI d'origine** — lance `llama-server` seul sur `:8080`. **Concurrent** des nouveaux launchers `start-*`. | Amont |
| `start-linux.sh`, `start-mac.command`, `start-windows.bat` | script | **Nouveaux launchers Studio** — délèguent à `scripts/core-startup.{sh,ps1}` (uvicorn + MCP + llama). | AfricAIsoft |
| `stop-linux.sh`, `stop-mac.sh`, `stop.bat` | script | Arrêt propre (SIGTERM/SIGKILL + purge staging /tmp + libération ports). | AfricAIsoft |

### 1.2 Backend Python (`app/`)

| Chemin | Rôle |
|---|---|
| `app/main.py` | Point d'entrée FastAPI, lifespan (config → détection → llama-server → MCP → shutdown). Prefix `/api` en dev, vide en portable. |
| `app/requirements.txt` | 7 deps pinned : fastapi 0.115.4, uvicorn 0.32.0, pydantic 2.9.2, httpx 0.27.2, jinja2 3.1.4, python-multipart 0.0.17, sse-starlette 2.1.3. |
| `app/api/router_v1.py` | Proxy OpenAI-compat `/v1/{models,chat/completions,completions}` avec injection system prompt + streaming SSE + boucle agentique. |
| `app/api/router_studio.py` | Routes `/system-prompt`, `/config`, `/skills`, `/skills/{name}/invoke`, `/conversations*`, `/models/{available,switch}`, `/events` (SSE). |
| `app/api/auth.py` | Middleware Bearer optionnel (`security.require_api_key`). |
| `app/agent/loop.py` | Boucle agentique : rounds ≤ 5, timeout 120 s, tool_calls parallèles (`asyncio.gather`), fallback JSON (```json``` / `<tool_call>` / raw). |
| `app/agent/trace.py` | Bus in-memory d'événements SSE par `session_id`. |
| `app/mcp/client.py` | Client MCP JSON-RPC 2.0 stdio, handshake + timeouts + respawn (max 3). |
| `app/mcp/registry.py` | Découverte des skills (`skills/registry.json` + auto-scan `mcp-servers/`), spawn, status_summary. |
| `app/llama_manager/manager.py` | Cycle de vie `llama-server` (spawn subprocess, wait `/health`, loopback strict, reload_model). |
| `app/platform_utils/detect.py` | Détection OS/arch normalisée (`linux-x86_64`, `darwin-arm64`, …). |
| `app/platform_utils/backend_detector.py` | Arbre CUDA > ROCm > Vulkan > CPU + Metal macOS, `reason_code` machine + params i18n. |
| `app/config/models.py` | 10 modèles Pydantic (`Settings`, `ServerConfig`, `ModelConfig`, `McpConfig`, `AgenticConfig`, `SystemPromptConfig`, `PlatformConfig`, `LoggingConfig`, `SecurityConfig`, `UiConfig`) tous `extra="forbid"`. |
| `app/config/loader.py` | Load/save atomique + validation JSON Schema + override env vars. |
| `app/conversations/store.py` | Store JSON `data/conversations.json` avec verrou threading. |
| `app/security/api_key.py` | Génération 32 octets base64url au 1er lancement, chmod 600 POSIX. |
| `app/security/manifest_verifier.py` | **Signature Ed25519 non implémentée** : accepte non-signé si `require_signature=false`. |
| `app/system_prompt/resolver.py` | Priorité override > preset > custom > default, respect `locked`. |
| `app/system_prompt/presets.py` | Liste + charge presets depuis `config/system_prompts/*.txt`. |
| `app/logging_setup/setup.py` | Configuration logging (rotation, redaction). |

### 1.3 Shim dev Emergent (`backend/`)

| Chemin | Rôle |
|---|---|
| `backend/server.py` | Ré-exporte `app.main:app` avec `STUDIO_API_PREFIX=/api` pour supervisor Kubernetes (port 8001). **Non utilisé en portable production**. |

### 1.4 Serveurs MCP (`mcp-servers/`)

| Chemin | Rôle | Outils exposés |
|---|---|---|
| `_shared/mcp_server.py` | Framework MCPServer JSON-RPC 2.0 stdio (~110 lignes). | — |
| `_shared/bm25.py` | BM25 Okapi pur Python + stopwords FR+EN, k1=1.5 b=0.75. | — |
| `_shared/text_extract.py` | Extraction txt/md/pdf (pypdf vendored). | — |
| `_template/` | Squelette prêt-à-copier pour créer un skill en < 5 min. | `echo` |
| `cybersec/server.py` | Regex SSH brute-force + blocklists CIDR (`lists/*.txt`). | `analyze_security_logs`, `check_ip_reputation` |
| `accounting/server.py` | Équilibre débit/crédit PCG + ratios financiers `Decimal`. | `verify_accounting_entries`, `calculate_financial_ratios` |
| `rag/server.py` | Index BM25 vendored, reindex auto si docs plus récents. | `search_knowledge_base`, `reindex_knowledge_base` |
| `general/server.py` | Rapport Markdown structuré + extraction regex FR+EN (email/URL/IP/IBAN/SIRET/…). | `generate_structured_report`, `extract_key_info` |

**Total** : 4 skills × 2 outils = **8 outils métier fonctionnels**, aucun stub.

### 1.5 UI web statique (`ui/`)

| Chemin | Rôle |
|---|---|
| `ui/index.html` | Layout 3 colonnes (conversations / chat / panneaux Skills-Models-SystemPrompt-Config). `data-i18n` sur ~40 attributs. Classe `body.booting` anti-FOUT. |
| `ui/assets/app.js` | Logique Vanilla JS (~830 lignes) : i18n bloquant, thème, health polling, conversations, chat SSE + agentic, skills, RAG docs, modèles + switch, system prompt + presets, config form, trace repliable. |
| `ui/assets/markdown.js` | Rendu Markdown minimal (headings, lists, code, links, escape XSS). |
| `ui/assets/styles.css` | Palette sable/vert forêt (light) + graphite/ambre (dark). Serif humaniste (Iowan/Palatino) + JetBrains Mono. Anti-FOUT `body.booting {visibility:hidden}`. |
| `ui/assets/i18n/` | **RÉFÉRENCÉ MAIS ABSENT du dépôt** — `app.js` fetch `assets/i18n/{fr,en}.json` mais le dossier n'est pas commité (voir §4 Limitations). |

### 1.6 Frontend redirect (`frontend/`)

| Chemin | Rôle |
|---|---|
| `frontend/index.html` | Simple page redirect vers `/api/` (dev Emergent seulement). |
| `frontend/package.json` | Wrapper `python3 -m http.server 3000`. Non utilisé en portable. |

### 1.7 Configuration (`config/`)

| Chemin | Rôle |
|---|---|
| `settings.json` | Configuration effective : bind `127.0.0.1:8080`, llama loopback `:8090`, mcp enabled, agentic 5/30/120, `require_signature=false`, UI fr/auto. |
| `settings.schema.json` | JSON Schema draft-2020-12, `additionalProperties: false`, tous les champs bornés. |
| `mcp.json` | `enabled=true`, `auto_discover=true`, `skill_timeout_sec=30`, `max_restart_attempts=3`. |
| `system_prompt.default.txt` | Prompt fabricant fallback. |
| `system_prompts/{accounting,cybersec,legal}.txt` | 3 presets métier. |
| `api_key.txt` | Généré au 1er lancement (gitignoré). |

### 1.8 Scripts multi-plateformes (`scripts/`)

| Chemin | Rôle |
|---|---|
| `core-startup.sh` | Logique commune Linux/macOS : detect OS/arch → detect-backend → check restricted-fs → resolve Python portable ou système → install deps → export env → `exec uvicorn`. |
| `detect-backend.{sh,ps1}` | Sondes nvidia-smi/rocm-smi/vulkaninfo timeout 1 s → sortie `BACKEND=…\nREASON=…`. |
| `fetch-binaries.{sh,ps1}` | Téléchargement releases GitHub `ggml-org/llama.cpp` (5 patterns d'assets), extraction, résolution symlinks pour compat FS restreints. |
| `health-check.{sh,ps1}` | Poll `/health` HTTP (30 tentatives × 1 s). |
| `build-portable.sh`, `build-portable.ps1` | Assemblage distribution portable : python-build-standalone 3.12 + wheels + binaires + code + manifest release.json. `--dry-run` supporté. |
| `sign-release.py` | Ed25519 : `generate-keypair`, `sign`, `verify`. Utilise `cryptography` (non listé dans `requirements.txt`). |

### 1.9 Key Builder (`keybuilder/`) — application C#/.NET 8 Windows

| Chemin | Rôle |
|---|---|
| `AfricAIsoft.KeyBuilder.sln` | Solution 3 projets : Core (net8.0), Wpf (net8.0-windows + UseWPF), Core.Tests (net8.0 xUnit). |
| `global.json` | Fixe SDK `.NET 8.0.425`. |
| `src/AfricAIsoft.KeyBuilder.Core/` | Bibliothèque portable Linux/Windows/macOS. Abstractions `IUsbDriveProvider`/`IDriveFormatter`/`IFileSystem`/…, Models (`UsbDrive`, `BuildPlan`, `BuildManifest`, `BuildJournal`, …), Services (`ChecksumService` SHA-256 streaming, `SizeEstimator`, `GgufValidator` magic+version 1..3, `PreflightValidator`, `ResumeJournal`, `SkillFilter`, `SystemPromptInjector`, `ManifestBuilder`, `ReportGenerator` HTML autonome, `BatchQueue`, `UsbBuildOrchestrator`). Dépendance : `System.Text.Json 8.0.5`. |
| `src/AfricAIsoft.KeyBuilder.Wpf/` | Front WPF Windows only (`WmiUsbDriveProvider`, `DiskPartFormatter` UAC runas, ViewModels MVVM). Dépendances : `System.Management 8.0.0`, `CommunityToolkit.Mvvm 8.2.2`. |
| `tests/AfricAIsoft.KeyBuilder.Core.Tests/` | Suite xUnit portable, `InMemoryFileSystem`, 25+ tests (vecteurs RFC checksums, préflight happy+edge, journal reprise, batch, orchestrateur E2E). |
| `installer/build-portable.ps1` | `dotnet publish -c Release -r win-x64 --self-contained → ZIP`. |
| `installer/Product.wxs`, `installer/KeyBuilder.wixproj` | Définition MSI WiX v4/v5. |
| `batch-config.example.json` | Exemple CLI batch (2 clés production ACME). |
| `docs/USER-GUIDE.md` | Guide utilisateur FR (installation, fabrication, batch, reprise, rapport, dépannage). |
| `docs/TECHNICAL.md` | Architecture 2-projets, séquence orchestrateur, API internes, UAC, WMI, tests. |

### 1.10 Documentation (`docs/`)

| Chemin | Rôle |
|---|---|
| `ARCHITECTURE.md` | Doc Phase 1 exhaustive : diagrammes composants, séquence démarrage, détection backend GPU, boucle agentique, MCP, system prompt, sécurité zero-trace, configuration, runtime Python, tests, arborescence cible, arbitrages. **~1300 lignes**, référence normative. |
| `ADD-SKILL.md` | Recette rapide « ajouter un skill MCP en < 5 min » (copie `_template`, décorateur `@server.tool`, redémarrage). |
| `COMPILATION.md` | Compilation manuelle `llama-server` par plateforme quand la glibc upstream est trop récente (b11071 → glibc 2.38). |

### 1.11 Tests (`tests/`)

| Chemin | Rôle |
|---|---|
| `test-api.sh` | 7 tests : `/health`, `/openapi.json`, `/v1/models`, `/v1/chat/completions` non-stream + stream, CORS preflight. |
| `test-system-prompt.sh` | 13 tests : lecture/écriture/preset/reset/lock. |
| `test-mcp.sh` | 10 tests : 4 skills × invocations directes. |
| `test-agentic-loop.sh` | 5 scénarios agent (tool_choice forcé). |
| `test-agentic-3steps.sh` | 4 scénarios chaînés `verify → ratios → rapport`. |
| `test-phase3-fixes.sh` | 7 tests correctifs Phase 3. |
| `test-ui.sh`, `test-ui-i18n.py`, `test-ui-i18n.sh` | UI + i18n anti-FOUT (Playwright headless, latence 700 ms simulée). |
| `RESULTS.md` | Snapshot 2026-09-21 : 95/95 Studio + 28/28 Core = **123 verts** (non commité). |
| `manual-checklist.md` | Checklist validation humaine 10 rubriques (non commitée). |
| `fixtures/` | (dossier présent mais peu fourni). |

### 1.12 Répertoires runtime (partiellement gitignorés)

| Chemin | Contenu |
|---|---|
| `bin/{darwin-arm64,darwin-x86_64,linux-x86_64,linux-aarch64,windows-x86_64}/{cpu,cuda,vulkan,metal}/` | Arborescence Studio (vide, sera peuplée par `fetch-binaries`). |
| `bin/{linux/linux_x64,linux/linux_arm64,mac/mac_arm64,mac/mac_x64,windows}/` | Arborescence PortableAI d'origine (**duplication de nommage** — voir §3.1). |
| `models/qwen2.5-0.5b-instruct-q4_k_m.gguf` | Modèle démo commité (~350 Mo, alors que `.gitignore` prévoit `models/*.gguf` — voir §4.2). |
| `data/conversations.json`, `data/pids/*.pid` | Runtime user. |
| `logs/runtime.log`, `logs/startup.log` | Journaux tournants. |

### 1.13 Autres

| Chemin | Rôle |
|---|---|
| `memory/PRD.md`, `memory/test_credentials.md` | Documents de travail agent Emergent. |
| `skills/registry.json` | Registre statique des 4 skills activés. |

---

## 2. Dépendances externes identifiées

### 2.1 Python (backend)

| Package | Version pinée | Poids wheel approx | Nature |
|---|---|---|---|
| `fastapi` | 0.115.4 | ~90 kB (dépend de starlette) | pure-Python |
| `uvicorn[standard]` | 0.32.0 | 1 wheel + httptools/uvloop binaires selon plat | mixed |
| `pydantic` | 2.9.2 | ~2 MB (pydantic-core wheel binaire par arch) | mixed (Rust core) |
| `httpx` | 0.27.2 | ~200 kB | pure-Python |
| `jinja2` | 3.1.4 | ~130 kB | pure-Python |
| `python-multipart` | 0.0.17 | ~30 kB | pure-Python |
| `sse-starlette` | 2.1.3 | ~20 kB | pure-Python |

**Non listé dans `requirements.txt` mais utilisé** :
- `cryptography` (par `scripts/sign-release.py`) — wheel binaire par arch,
  ~4 MB. **Doit être ajouté** ou le script doit importer conditionnellement.
- `pypdf` (par `mcp-servers/_shared/text_extract.py`) — pure-Python.

### 2.2 .NET (Key Builder)

| Package | Version | Projet |
|---|---|---|
| `System.Text.Json` | 8.0.5 | Core |
| `System.Management` | 8.0.0 | Wpf (WMI) |
| `CommunityToolkit.Mvvm` | 8.2.2 | Wpf |
| SDK `.NET 8.0.425` | épinglé via `global.json` | Solution |
| WiX Toolset | v4/v5 (implicite) | `installer/` |

### 2.3 Binaires externes

| Composant | Source | Version | Récupération |
|---|---|---|---|
| `llama-server` | github.com/ggml-org/llama.cpp releases | `b11071` par défaut (script) | `scripts/fetch-binaries.{sh,ps1}` |
| `python-build-standalone` 3.12.5 | github.com/indygreg/python-build-standalone | tag `20240814` | `scripts/build-portable.sh` |
| Modèles GGUF | huggingface.co (utilisateur) | libre | manuel dans `models/` |

### 2.4 Zéro dépendance externe (contrainte offline)

- Aucun paquet npm, aucun bundler frontend (Vanilla JS strict).
- Aucun binaire natif compilé à l'installation (pydantic-core pré-packagé).
- Aucun appel réseau au runtime (offline strict après setup).
- Aucun embedding, aucun ONNX, aucun PyTorch (RAG = BM25 pur Python).

---

## 3. Limitations et incompatibilités détectées

### 3.1 Multi-plateforme

| Sujet | Statut | Détail |
|---|---|---|
| **Deux systèmes de nommage `bin/`** cohabitent | ⚠ Duplication | PortableAI original utilise `bin/{linux/linux_x64,mac/mac_arm64,windows}/…`. Studio utilise `bin/{linux-x86_64,darwin-arm64,windows-x86_64}/{cpu,cuda,vulkan,metal}/…`. Les deux arborescences sont créées mais aucun code ne les fait converger. |
| **Windows ARM64** | ❌ Non couvert | Aucun binaire llama-server + pas de release upstream Snapdragon X. Documenté comme extension future. |
| **`ui/assets/i18n/{fr,en}.json`** | ❌ Absents du dépôt | `ui/assets/app.js` fetch ces fichiers, mais le dossier n'existe pas dans le git tree. L'UI ne peut PAS démarrer sans ces fichiers. |
| **Runtime Python portable** | ⚠ Non commité | Attendu dans `bin/<plat>/python/` mais absent (généré par `build-portable.sh`). |
| **glibc 2.38** | ⚠ Contournable | Les releases b11071 upstream nécessitent glibc ≥ 2.38 ; Debian 12 / Ubuntu 22.04 restent en 2.36. Documenté dans `docs/COMPILATION.md` avec procédure de build local. |
| **Bash 3.2 macOS système** | ⚠ Bug latent | `start.sh` ligne 131 utilise `${fstype,,}` (lowercase) — syntaxe **bash 4+**, non supportée par bash 3.2 embarqué macOS. `core-startup.sh` a le même motif. |

### 3.2 Offline strict

| Sujet | Statut | Détail |
|---|---|---|
| Téléchargement binaires (`install.sh`, `fetch-binaries`, `build-portable`) | ⚠ Nécessite Internet UNE fois | Documenté et attendu. Une fois `bin/` peuplé, la clé est offline. |
| Téléchargement du modèle GGUF | ⚠ À charge utilisateur | Instructions HuggingFace dans `README.md`. |
| `pip install --no-index` | ✅ | Le fallback online (`pip install ...`) reste dans `core-startup.sh` — à durcir en portable pur. |
| CDN, télémétrie, analytics, fonts externes | ✅ Aucun | UI 100% locale, favicon SVG data URI, fonts serif système. |

### 3.3 Zero-trace

| Sujet | Statut | Détail |
|---|---|---|
| Logs redaction | ✅ | `startup.log`, `runtime.log` : métadonnées seulement (aucun contenu utilisateur). API key jamais loggée (6 caractères fingerprint). |
| Fichiers résiduels `/tmp/portableai.*` | ✅ | `trap _cleanup_staging EXIT INT TERM` dans `core-startup.sh`. |
| Persistance conversations | ⚠ Opt-in implicite | `data/conversations.json` est écrit par défaut. Devrait être opt-out documenté (chiffrement volume à charge utilisateur). |
| Persistance PID | ✅ | `data/pids/*.pid` supprimés à l'arrêt. |

### 3.4 Sécurité

| Sujet | Statut | Détail |
|---|---|---|
| **Signature Ed25519** | ❌ NON IMPLÉMENTÉE au démarrage | `app/security/manifest_verifier.py` retourne « Vérification Ed25519 non implémentée en Phase 2. » quand `require_signature=true`. Le script `scripts/sign-release.py` fabrique bien les signatures, mais l'orchestrateur ne les vérifie pas. |
| `require_signature=false` par défaut | ⚠ Documenté | Warning visible au démarrage. Doit basculer à `true` en release. |
| API key | ✅ | Générée 32 octets base64url au 1er lancement, chmod 600 POSIX, header Bearer, comparaison `hmac.compare_digest`. |
| CORS | ✅ | Whitelist `127.0.0.1:8080` par défaut, LAN opt-in explicite. |
| Path traversal skills | ⚠ Partiel | `presets.py` protège l'ID, mais `search_knowledge_base` / `analyze_security_logs` reposent sur les schémas d'input JSON sans whitelist explicite `security.allowed_*_roots` documentée dans `settings.json`. |

### 3.5 Modèle 0.5B & fiabilité

| Sujet | Statut | Détail |
|---|---|---|
| Qwen2.5-0.5B tool routing | ⚠ Faible (~20 %) | Documenté 5 fois (README, CHANGELOG, ARCHITECTURE, RESULTS, manual-checklist). Test T4 marqué `best-effort`. Prod recommandée : 7B+. |
| Tests agentiques | ✅ Contournés | Utilisent `tool_choice` forcé + seeds pour rester déterministes. |
| Modèle 0.5B commité | ⚠ Vole ~350 MB du repo | Devrait être gitignoré ; l'utilisateur télécharge son modèle. |

### 3.6 UI

| Sujet | Statut | Détail |
|---|---|---|
| i18n anti-FOUT | ✅ | `body.booting {visibility:hidden}` + fetch bloquant + `try/finally` classe retirée. Testé Playwright headless. |
| Compteur tokens | ⚠ Approximatif serveur | `approximate_token_count` = `len/3.8`. La doc `ARCHITECTURE.md §6.4` prévoyait `gpt-tokenizer` JS local, non implémenté. |
| Accessibilité | ⚠ Non audité | Aucun test axe/pa11y ; labels ARIA présents mais partiels. |
| Responsive | ✅ | `@media` 1200 px et 960 px, layout 1 colonne mobile. |

### 3.7 Divergence CHANGELOG / VERSION

| Fichier | Contenu | Réel commité |
|---|---|---|
| `VERSION` | `0.2.0-phase2` | HEAD contient phases 3+4+5 |
| `CHANGELOG.md` | S'arrête à `[0.3.0] Phase 3` | Phase 4 et Phase 5 (Key Builder) non entrées |
| `release.json` | `version: "0.2.0-phase2"`, `unsigned-dev-build` | Idem |

---

## 4. Réutilisable tel quel VS à réécrire

### 4.1 À CONSERVER tel quel (haute qualité, aligné cahier des charges)

| Élément | Justification |
|---|---|
| `app/**` (orchestrateur FastAPI complet) | 20+ modules Pydantic-typés, séparation propre (api/agent/mcp/config/security/system_prompt). Commentaires FR, noms EN, licence MIT. Aucune dette technique visible. |
| `mcp-servers/{cybersec,accounting,rag,general,_shared,_template}` | 4 skills fonctionnels avec 8 outils métier, framework minimal `MCPServer` de ~110 lignes, BM25 pur Python vendored ≤ 50 Ko. Aucun stub. |
| `ui/**` (Vanilla HTML/JS/CSS < 5 Mo hors i18n) | UI complète 3 colonnes, i18n anti-FOUT rigoureuse, palette distinctive sable/forêt hors AI-slop. |
| `docs/ARCHITECTURE.md` (~1300 lignes) | Document normatif exhaustif, diagrammes ASCII, arbitrages traçables. C'est LA référence à conserver. |
| `docs/{ADD-SKILL,COMPILATION}.md` | Concis, utiles, à jour. |
| `keybuilder/src/AfricAIsoft.KeyBuilder.Core/**` + `tests/` | .NET 8 portable, 28/28 tests verts sous Linux, abstractions propres, orchestrateur E2E testé in-memory. |
| `scripts/{core-startup.sh,detect-backend.{sh,ps1},health-check.{sh,ps1},fetch-binaries.sh,build-portable.sh,sign-release.py}` | Robustes, timeouts partout, symlinks résolus pour FS restreints (FAT32/exFAT/noexec). |
| `config/{settings.json,settings.schema.json,mcp.json,system_prompts/*.txt}` | Schéma strict `additionalProperties:false`, defaults raisonnables. |
| `start-{linux.sh,mac.command,windows.bat}`, `stop-*` | Wrappers propres qui délèguent à `core-startup`. |
| `tests/{test-api,test-mcp,test-system-prompt,test-agentic-*}.sh` | 95/95 verts, réutilisables tels quels. |
| `LICENSE`, `.gitignore` (partiellement — voir §4.2) | Corrects sur les gros items. |

### 4.2 À RÉÉCRIRE ou HARMONISER

| Élément | Raison | Action recommandée |
|---|---|---|
| `README.md` | Mélange déroutant PortableAI (llama.cpp seul) + encart Key Builder. Ne mentionne pas l'orchestrateur, MCP, UI, ni skills. Bilingue FR/EN incomplet. | Réécrire de zéro autour d'AfricAIsoft Portable Studio comme produit unique, section « legacy PortableAI » archivée à la fin. |
| `install.sh`, `install.bat`, `start.sh`, `start.bat` (racine) | Fonctionnalité **PortableAI d'origine** qui ne lance QUE `llama-server` sans orchestrateur, MCP ni UI Studio. Conflit UX avec les nouveaux `start-*`. | Deux options : (a) supprimer et rediriger vers `install-portable.sh` + `start-linux.sh` ; (b) transformer en aliases vers `scripts/fetch-binaries.sh` + `start-linux.sh` avec message explicite. **Ne pas casser les utilisateurs habitués** — deprecation notice puis suppression au prochain major. |
| `bin/{linux,mac,windows}/` (nommage PortableAI) | Duplication avec `bin/{linux-x86_64,darwin-arm64,…}/` (nommage Studio). | Choisir UN seul schéma (recommandé : celui de Studio), migrer scripts + fetch-binaries, supprimer l'ancien. |
| `VERSION` (0.2.0-phase2) + `CHANGELOG.md` + `release.json` | Désynchronisés du code réel (phases 3-5 non versionnées). | Passer à `1.0.0`, ajouter entrées CHANGELOG 0.4/0.5/1.0, régénérer `release.json` via `build-portable.sh` + `sign-release.py`. |
| `ui/assets/i18n/{fr,en}.json` | Absents du dépôt — bloquant pour l'UI. | Créer et commiter (ou lever `.gitignore` si l'entrée les exclut par erreur). |
| `app/security/manifest_verifier.py` | Signature Ed25519 « non implémentée ». | Câbler `scripts/sign-release.py verify` sur `release.json.sig` au démarrage quand `require_signature=true`. |
| `app/requirements.txt` | Ne liste pas `cryptography` requis par `sign-release.py`. | Ajouter `cryptography==43.0.1` (ou équivalent). |
| `models/qwen2.5-0.5b-instruct-q4_k_m.gguf` (350 MB) | Vole ~350 MB du repo, alors que `.gitignore` prévoit `models/*.gguf`. | Retirer du tracking git (`git rm --cached`), documenter téléchargement dans `README.md` §Quick Start. |
| `keybuilder/**/{bin,obj}/Release/**` | Artefacts .NET compilés commités par erreur. | `git rm --cached` + ajouter au `.gitignore` (`**/bin/`, `**/obj/`). |
| `frontend/` (redirect Emergent) | Utile en dev Emergent seulement ; sans lien avec le portable. | À conserver marginalement mais isoler (ou déplacer dans `.emergent/`). |
| `backend/server.py` (shim Emergent) | Utile en dev seulement. | À conserver, documenter comme « non-production ». |
| `start.sh` / `core-startup.sh` `${fstype,,}` | Bash 3.2 macOS système ne supporte pas. | Remplacer par `$(printf '%s' "$fstype" | tr '[:upper:]' '[:lower:]')`. |

### 4.3 À COMPLÉTER (spec présente mais code absent)

| Élément | Où c'est spécifié | Priorité |
|---|---|---|
| Vérification Ed25519 au boot | `ARCHITECTURE.md §7.2` + `manifest_verifier.py` | P0 |
| Fichiers i18n `ui/assets/i18n/*.json` | `app.js` ligne 75 | P0 |
| Tokenizer JS local UI | `ARCHITECTURE.md §6.4` | P2 |
| `scripts/fetch-binaries.ps1` alignement `--target/--out` | Utilisé par `build-portable.sh` ligne 69 | P1 |
| `README.md` refonte tri-lingue (FR + EN) | `docs/ARCHITECTURE.md` (français neutre) | P1 |
| CHANGELOG entrées 0.4/0.5/1.0 | Manquant | P1 |

---

## 5. Risques techniques identifiés

### 5.1 Binaires et compilation

| Risque | Impact | Mitigation |
|---|---|---|
| Releases `ggml-org/llama.cpp` avec glibc trop récente | Non-démarrage sur distros LTS | `docs/COMPILATION.md` documenté, build local en 3 min. |
| Nouveaux tags upstream cassent le pattern d'asset | `install.sh` échoue silencieusement | Pattern regex strict + fallback message d'erreur clair (déjà en place). |
| Runtime Python `python-build-standalone` renommage/hébergement | `build-portable.sh` échoue | URLs pinees au tag `20240814`. Miroir interne à envisager. |
| Wheels `pydantic-core` binaire par arch | Manque un wheel pour arch exotique (linux-aarch64 musl) | Contrainte assumée : cible glibc uniquement en portable. Alpine/musl non supporté. |
| **Clé USB FAT32/exFAT** (noexec, symlinks non résolus) | `llama-server` refuse de s'exécuter | `_is_restricted_fs()` + staging `/tmp/portableai.*` + résolution symlinks (déjà en place `start.sh` + `core-startup.sh` + `install.sh`). |

### 5.2 GPU backends

| Risque | Impact | Mitigation |
|---|---|---|
| CUDA runtime différent (11 vs 12) | Binaire crash à load | Fallback CPU auto si CUDA fail (à documenter). |
| ROCm limité à quelques distributions | Utilisateurs déçus | Binaires ROCm non fetchés par défaut, opt-in. |
| Metal (macOS) : signature Gatekeeper | Refus de lancement | Documenter dans README (`spctl --add`, Préférences Sécurité). |
| Vulkan sur GPU intégré Intel obsolète | Instable | Fallback CPU documenté ; force CPU via `settings.json`. |

### 5.3 Portabilité

| Risque | Impact | Mitigation |
|---|---|---|
| PowerShell 5.1 (Windows 10) vs 7+ (Windows 11) | Syntaxe scripts | `core-startup.ps1` doit rester compatible 5.1 (à auditer). |
| macOS `bash 3.2` (system) vs `bash 5.x` (Homebrew) | `${var,,}` non supporté en 3.2 | À corriger (§4.2). |
| Débranchement clé pendant écriture | Corruption `data/`, `logs/` | Fsync + écriture atomique via `.tmp` + `rename` (implémenté dans `ConversationStore` et `save_settings`). |

### 5.4 Sécurité

| Risque | Impact | Mitigation |
|---|---|---|
| Signature Ed25519 non vérifiée au boot | Binaire trojanisé accepté | **À implémenter** (§4.3). |
| API key en clair sur clé USB volée | Rejeu | Chiffrement volume out-of-scope, documenter recommandation VeraCrypt/BitLocker/LUKS. |
| Prompt injection via user prompt | Fuite system prompt si `locked=true` | Comportement OpenAI standard (llama-server ne recopie pas le system role). Test heuristique Jaccard < 0.3 (spec `ARCHITECTURE §10.5`) non implémenté. |
| CORS wildcard en mode LAN | Ouverture cross-origin non désirée | UI doit exiger un warning + confirmation explicite avant d'accepter `"*"`. Non implémenté. |

### 5.5 Fiabilité modèles

| Risque | Impact | Mitigation |
|---|---|---|
| **Qwen 0.5B** — tool routing autonome peu fiable | UX agentique dégradée | Documenté 5 fois. Recommander 3B (démo) / 7B+ (prod) dans README §Quick Start. |
| Contexte 8192 sur 4 GB RAM | OOM au chargement | `llama-server` échoue proprement, message clair. |
| Absence de télémétrie pour observer les erreurs terrain | Bugs silencieux | Assumé (zero-trace). Log local rotatif + rapport HTML Key Builder. |

### 5.6 Architecture logicielle

| Risque | Impact | Mitigation |
|---|---|---|
| Duplication `install.sh` (racine) vs `scripts/fetch-binaries.sh` | Deux voies parallèles, confusion | Consolider (§4.2). |
| Duplication `start.sh` (llama-only) vs `start-linux.sh` (Studio complet) | Utilisateur lance la mauvaise cible | Consolider ou renommer explicitement. |
| Deux systèmes de nommage `bin/` | Scripts pointent vers l'un ou l'autre | Unifier vers `bin/<os>-<arch>/<backend>/` (§4.2). |
| Aucune CI/CD | Régressions non détectées automatiquement | Ajouter GitHub Actions : lint Python (ruff), tests bash, `dotnet test` Core. |

### 5.7 Divers

| Risque | Impact | Mitigation |
|---|---|---|
| Dossier `.emergent/` local | Fuite chemin conteneur | Déjà dans `.gitignore` — vérifier avant push public. |
| `keybuilder/**/bin/Release/**` modifiés à chaque build | Repo pollué | Ajouter `**/bin/` et `**/obj/` au `.gitignore` root ou dans `keybuilder/.gitignore`. |

---

## 6. État git

### 6.1 Branche et remote

- Branche courante : **`main`**.
- Remote : `origin/main` avec **9 commits d'avance** localement (non poussés).

### 6.2 Derniers commits (HEAD)

```
3b71cae feat(phase5): AfricAIsoft Key Builder — application WPF C#/.NET Windows
3b07012 fix(phase4): anti-FOUT i18n — UI masquée jusqu'à hydratation complète
2e5feec fix(phase4): cache HTTP navigateur + test Playwright comportemental i18n
84737d6 fix(phase3+4): GET /system-prompt honore active_preset + hardening persistance i18n
6f97c24 feat(phase3+4): correctifs OpenAPI/context_size/tools + UI Web complète
44b74cd feat(phase3): MCP stdio + 4 skill packs + agent loop + SSE trace + i18n/reason_code fixes
ef426a5 test(phase2): make T3 and T4 deterministic (codeword contrast + seed)
5da320d fix(phase2): inline role='system' override, full UI i18n, root '/' redirect
f8aac50 feat(phase2): FastAPI orchestrator, OpenAI-compat proxy, system prompt mgmt, launchers
d8acd19 fix: path bug fixed
5300d57 fix: file system links
13c9dae updated README
0693232 feat:model selection
7f3509a fix: line issue fixed
b43bb71 feat: select platform to install
77d9863 feat: updated functunality
8903c93 fix: downloads to all platforms
dae487f add: installation script and fixed starter
```

Les commits `77d9863` → `dae487f` proviennent du dépôt PortableAI d'origine ;
à partir de `f8aac50` (Phase 2), tous les commits sont de la transformation
AfricAIsoft.

### 6.3 Propreté de l'index

**Fichiers modifiés (uncommitted)** — tous des artefacts de build .NET :

```
keybuilder/src/AfricAIsoft.KeyBuilder.Core/bin/Release/net8.0/*.dll,pdb
keybuilder/src/AfricAIsoft.KeyBuilder.Core/obj/Release/net8.0/**
keybuilder/tests/AfricAIsoft.KeyBuilder.Core.Tests/bin/Release/net8.0/*.dll,pdb
keybuilder/tests/AfricAIsoft.KeyBuilder.Core.Tests/obj/Release/net8.0/**
```

**Fichiers non-suivis (untracked)** :

```
keybuilder/src/AfricAIsoft.KeyBuilder.Core/bin/Debug/
keybuilder/src/AfricAIsoft.KeyBuilder.Core/obj/Debug/
keybuilder/tests/AfricAIsoft.KeyBuilder.Core.Tests/bin/Debug/
keybuilder/tests/AfricAIsoft.KeyBuilder.Core.Tests/obj/Debug/
scripts/build-portable.ps1
scripts/build-portable.sh
scripts/sign-release.py
tests/RESULTS.md
tests/manual-checklist.md
```

**Diagnostic** :
- `.gitignore` root **NE contient PAS** `**/bin/` ni `**/obj/` — d'où la
  pollution du diff .NET. Ajouter au `.gitignore` (root ou `keybuilder/`).
- `scripts/build-portable.{sh,ps1}` et `scripts/sign-release.py` : nouveaux
  scripts Phase 6 non commités, prêts à être ajoutés.
- `tests/RESULTS.md`, `tests/manual-checklist.md` : rapports Phase 6 non commités.

### 6.4 Historique

- Historique **linéaire** (pas de merge commit).
- Messages Conventional Commits (`feat`, `fix`, `test`) respectés à partir de
  Phase 2 ; commits d'origine PortableAI moins formels.
- Aucune signature GPG des commits observée (à envisager pour la release finale).
- Aucun tag git présent — ajouter `v1.0.0` à la release finale.

---

## 7. Recommandation de base de départ pour l'architecture

**Le dépôt EST la base de départ.** Il n'y a pas lieu de repartir de zéro.
Les fondations sont solides, cohérentes avec le cahier des charges et
largement testées (123 tests verts). Le plan proposé pour la suite est :

1. **Nettoyer** (P0) : gitignore `**/bin/**/obj/`, retirer le `.gguf`
   traqué, supprimer/déprécier `install.sh`+`start.sh` racine, unifier
   nommage `bin/<os>-<arch>/<backend>/`, ajouter `ui/assets/i18n/*.json`,
   ajouter `cryptography` à `requirements.txt`.
2. **Fermer** (P0) : implémenter la vérification Ed25519 au boot dans
   `manifest_verifier.py` (le signer côté build existe déjà).
3. **Aligner** (P1) : `VERSION` → `1.0.0`, CHANGELOG 0.4/0.5/1.0, régénérer
   `release.json` avec `build-portable.sh` + signer, réécrire `README.md`
   autour d'un produit unique (AfricAIsoft Portable Studio) FR + EN.
4. **Livrer** (P1) : `build-portable.sh --target all` pour produire 5
   distributions, checklist manuelle exécutée sur du matériel réel, tag
   `v1.0.0`, push `origin/main` + tag.
5. **Extensions futures** (P2) : compteur tokens JS local, tests Jaccard
   anti-fuite prompt, chiffrement volume documenté, CI/CD GitHub Actions,
   support Windows ARM64, ext4 sous Windows.

Architecture cible = celle décrite dans `docs/ARCHITECTURE.md` (déjà
normative). Aucune divergence conceptuelle n'a été observée entre la
spécification et le code. La dette technique se limite à des chantiers de
finition (naming, doc, signature runtime, i18n JSON).

---

**Fin du rapport d'analyse Phase 0.** Aucun fichier existant du dépôt n'a
été modifié pendant cette phase ; seul `docs/ANALYSIS.md` a été (ré)écrit.

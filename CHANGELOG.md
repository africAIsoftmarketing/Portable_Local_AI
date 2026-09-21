# CHANGELOG

## [1.0.2] Purge historique Ed25519 — token GitHub redacté — 2026-09-21

### Sécurité
- **Purge historique** : GitHub Secret Scanning a signalé un jeton GitHub
  OAuth (masqué : `ghu_gZ…pYbt`, type : *GitHub OAuth Access Token*) capté
  depuis `.git/config` du conteneur d'exécution et écrit involontairement
  dans le rapport `docs/ANALYSIS.md` §4 « État Git » du commit initial
  `f8aac50 feat(phase2): FastAPI orchestrator`.
- Le token avait déjà été retiré du HEAD lors de la Phase 6 (réécriture
  complète de `docs/ANALYSIS.md`), mais restait présent dans **2 commits**
  historiques (`f8aac50` initial + `21c6fc9` Phase 6 diff qui le supprimait).
- **Correctif** : `git filter-repo --replace-text` sur tout l'historique
  (28 commits réécrits), placeholder `REDACTED_GITHUB_TOKEN` avec note
  explicative dans le corps du rapport.
- **Preuve** : `git log --all -p -S "<token>"` retourne vide après purge ;
  `REDACTED_GITHUB_TOKEN` présent dans les commits historiques (attendu).
- **Ed25519 vérifiée intacte** post-filter-repo : `sign-release.py verify`
  → `Signature OK`.
- **Force-push requis** pour publier l'historique réécrit (rien n'a
  encore été publié sur origin).

### Ajouté
- `docs/ANALYSIS.md` §« Règle d'hygiène — rapports d'analyse et credentials » :
  interdit d'inclure des credentials d'environnement dans les rapports
  d'analyse versionnés, avec exigence de redaction préalable et placeholder
  explicite.

---

## [1.0.1] Sécurité pré-push — audit GitHub Secret Scanning — 2026-09-21

### Corrigé
- **Audit sécurité exhaustif** avant push initial :
  - Scan HEAD + historique complet (`git rev-list --all`) : **aucun secret**
    trouvé (pas de `BEGIN PRIVATE KEY`, ni `sk-*`, ni `AKIA*`, ni JWT `eyJ*`,
    ni chaîne base64url ≥ 32 caractères en dehors des SHA512 NuGet
    strictement contenus dans `keybuilder/**/bin,obj/` déjà `git rm --cached`).
  - Confirmé : `config/api_key.txt` et `keys/private.pem` n'ont jamais été
    introduits dans un commit (`git log --all --diff-filter=A` vide).
- **`.gitignore` renforcé** :
  - `config/settings.json` (fichier utilisateur — peut contenir CORS internes,
    chemins de modèles privés, overrides auth).
  - `data/conversations.json` (contenu utilisateur runtime).
  - Ces deux fichiers retirés du tracking (`git rm --cached`) — leurs valeurs
    initiales restent versionnées dans `config/settings.example.json` et
    `data/pids/.gitkeep`.
  - Déjà présents : `config/api_key.txt`, `keys/private.pem`, `logs/*.log`,
    `memory/`, `*.pem`, `*.key`, `.env*`, `credentials.json`.
- **`config/settings.example.json`** créé : template neutre versionné dont
  l'orchestrateur copie automatiquement le contenu dans `settings.json` au
  premier lancement (`app/config/loader.py`), sans valeurs sensibles.
- **`memory/test_credentials.md`** retiré du tracking (memory/ gitignored).

### Documenté
- Aucun secret n'a été purgé de l'historique **parce qu'aucun n'y figurait**.
  L'audit `git log -p --all -S` sur tous les patterns sensibles renvoie vide.
  Le premier `git push` peut donc procéder sans réécriture d'historique.

---

## [1.0.0] Livrable final — Phase 6 finalisation — 2026-08-24

### Ajouté
- **Vérification Ed25519 réelle du manifest** (`app/security/manifest_verifier.py`) :
  signature détachée `release.json.sig` + clé publique `config/public.pem`.
  `cryptography==43.0.1` ajouté à `app/requirements.txt`.
- **Paire Ed25519 fabricant générée** :
  - `keys/public.pem` + `config/public.pem` (fingerprint DER SHA-256 :
    `5fb63dd2af1dbef89ad954846b7b5c9315919daa90b8f7e84d651723bc9fb10b`).
  - `keys/private.pem` **jamais commitée** (gitignored + procédure hors ligne
    documentée dans `README.md §11`).
  - `release.json.sig` de la v1.0.0 embarqué dans le dépôt.
- **Scripts de packaging portable** :
  - `scripts/build-portable.sh` : distribution assemblée par plateforme
    (`--target linux-x64|linux-arm64|macos-x64|macos-arm64|windows-x64|all`),
    Python 3.12 embarqué + wheels + binaires llama.cpp + code + manifest.
  - `scripts/build-portable.ps1` : équivalent Windows PowerShell.
  - `scripts/sign-release.py` : `generate-keypair`, `sign`, `verify` Ed25519.
- **Documentation finale** :
  - `README.md` : réécriture complète en français (Studio uniquement,
    plus de mélange avec le socle PortableAI amont).
  - `docs/EXAMPLES.md` : 3 scénarios agentiques (cybersec / comptable /
    multi-outils) avec payloads JSON et traces réelles.
  - `docs/ANALYSIS.md` : rapport Phase 0 exhaustif de l'état du dépôt.
- **Tests finaux** :
  - `tests/RESULTS.md` : rapport horodaté 95/95 Studio + 28/28 Core.
  - `tests/manual-checklist.md` : critères d'acceptation Livrables 1 & 2
    avec procédure pas-à-pas par plateforme.

### Corrigé
- **Fichiers i18n `ui/assets/i18n/{fr,en}.json` maintenant trackés dans git**
  (bloquant : un clone frais cassait l'UI).
- **`${fstype,,}` (bash 4)** remplacé par `tr '[:upper:]' '[:lower:]'` dans
  `start.sh` et `scripts/core-startup.sh` : compatible avec le bash 3.2
  système de macOS.
- **Modèle GGUF de démo (~350 Mo)** retiré du tracking git (`git rm --cached`).
  Il reste sur disque pour les tests locaux ; `.gitignore` couvre
  `models/*.gguf`.
- **`.gitignore` .NET** : ajout de `keybuilder/**/bin/`, `keybuilder/**/obj/`,
  `keybuilder/artifacts/`, `keys/private.pem`, `dist/`. Le `bin/` racine
  (binaires llama-server) reste géré séparément.
- **VERSION** synchronisé à `1.0.0` (au lieu de `0.2.0-phase2`), idem
  `app/__init__.py` et `app/main.py`.

### Documenté (limites honnêtes)
- Le socle historique **PortableAI** (`install.sh`, `install.bat`, `start.sh`,
  `start.bat`) est conservé pour rétrocompatibilité (utilisateurs habitués
  au workflow amont), mais le workflow officiel est désormais `start-linux.sh`
  / `start-mac.command` / `start-windows.bat` (Studio complet).
- Le modèle par défaut Qwen 2.5-0.5B reste insuffisant pour le tool-routing
  autonome fiable (voir §Limites du README).
- Vérification Ed25519 : implémentée mais **désactivée par défaut**
  (`security.require_signature=false`). À activer en production sur les
  livraisons signées.

---

## [0.5.0] Phase 5 — Key Builder Windows — 2026-08-24

### Ajouté
- **`keybuilder/` — application C#/.NET 8 WPF Windows** pour fabriquer des
  clés USB portables prêtes à l'emploi.
  - `AfricAIsoft.KeyBuilder.Core` (net8.0) : bibliothèque portable
    Linux/Windows/macOS (aucune API Windows), 28 tests xUnit verts.
    Services : ChecksumService (SHA-256 streaming), SizeEstimator,
    GgufValidator (magic + version 1..3), PreflightValidator, ResumeJournal
    (reprise après interruption), SkillFilter, SystemPromptInjector,
    ManifestBuilder, ReportGenerator (HTML autonome), BatchQueue,
    UsbBuildOrchestrator.
  - `AfricAIsoft.KeyBuilder.Wpf` (net8.0-windows) : front WPF Windows-only,
    détection USB temps réel via WMI, DiskPartFormatter avec élévation UAC.
  - Installer WiX v4 (`installer/Product.wxs`, `KeyBuilder.wixproj`) +
    script portable ZIP (`installer/build-portable.ps1`).
  - Docs : `keybuilder/docs/USER-GUIDE.md`, `keybuilder/docs/TECHNICAL.md`,
    `keybuilder/batch-config.example.json`.

---

## [0.4.0] Phase 4 — UI Web complète FR/EN — 2026-08-24

### Ajouté
- **UI Vanilla HTML/CSS/JS** dans `ui/` (< 5 Mo, aucun bundler) :
  - Layout 3 colonnes (conversations / chat / panneaux
    Skills-Models-SystemPrompt-Config).
  - Chat streaming SSE + boucle agentique avec trace repliable.
  - Sidebar conversations avec persistance JSON (`data/conversations.json`).
  - Panneau Skills : état MCP + tools + réindexation RAG + liste des docs.
  - Panneau Modèles : liste .gguf + switch à chaud (unload/load llama-server).
  - Panneau System Prompt : édition + presets (cybersec/accounting/legal)
    + verrouillage `locked`.
  - Panneau Config : formulaire dynamique validé JSON Schema, warnings
    "requires_restart".
  - i18n FR/EN complète, ~40 attributs `data-i18n`.
  - Thème light/dark persistant, palette sable/vert forêt et graphite/ambre.
- **Endpoints** ajoutés :
  - `GET/POST/PUT/DELETE /api/conversations[...]`,
  - `POST /api/conversations/{id}/messages`,
  - `GET /api/models/available`, `POST /api/models/switch`.

### Corrigé
- **Anti-FOUT i18n** : `body.booting {visibility:hidden}` + `try/finally`
  retire la classe seulement après hydratation complète du dictionnaire i18n.
  Test Playwright headless couvre le cas d'une latence réseau de 700 ms.
- **`GET /system-prompt` honore `active_preset`** : retourne le contenu
  du preset actif au lieu du fichier custom.

---

## [0.3.0] Phase 3 — Système agentique complet — 2026-08-24

Cf. précédent CHANGELOG (MCP stdio, 4 skill packs, 8 outils, boucle
agentique max 5 rounds avec fallback JSON parsing, trace SSE `/api/events`).

---

## [0.2.0] Phase 2 — Cœur portable — 2026-08-24

Cf. précédent CHANGELOG (orchestrateur FastAPI, `/v1/chat/completions`
stream SSE, system prompt multi-source, détection backend GPU, launchers
`start-linux.sh` / `start-mac.command` / `start-windows.bat`).

---

## [0.1.0] Phases 0 & 1 — Analyse + architecture — 2026-08-24

- `docs/ANALYSIS.md` (Phase 0).
- `docs/ARCHITECTURE.md` (Phase 1).

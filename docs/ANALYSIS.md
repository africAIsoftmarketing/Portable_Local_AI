# Rapport d'analyse — Portable_Local_AI

> Phase 0 — Analyse en lecture seule du dépôt `africAIsoftmarketing/Portable_Local_AI` (branche `main`).
> Objectif : évaluer l'état actuel avant transformation en « AfricAIsoft Portable Studio ».
> Aucune modification du code existant n'a été effectuée. Seul ce fichier est créé.

---

## 1. Synthèse exécutive

Le dépôt est un **wrapper léger et non intrusif autour de `llama-server`** (binaire fourni par le projet upstream [`ggml-org/llama.cpp`](https://github.com/ggml-org/llama.cpp)). Il consiste essentiellement en :

- **Deux paires de scripts** (Bash + Batch) : un installateur qui télécharge les binaires CPU de `llama.cpp` depuis l'API GitHub Releases pour cinq cibles (Linux x64/arm64, macOS x64/arm64, Windows x64), et un lanceur qui détecte l'OS/arch, sélectionne un modèle GGUF puis exécute le serveur sur `0.0.0.0:8080`.
- **Un `README.md`** de bonne qualité (272 lignes) décrivant l'utilisation.
- **Une arborescence `bin/` et `models/` vide** (uniquement des `.gitkeep`) : les binaires ne sont pas commités, ils sont téléchargés à l'installation.

**Taille totale du dépôt** : ~124 Ko (hors `.git`).
**Zéro fichier Python, zéro fichier JavaScript, zéro package manager.** Il n'existe pas d'API FastAPI, pas de serveur MCP, pas de boucle agentique, pas d'UI custom (celle-ci a été **supprimée** dans un commit récent — voir §6).

**Conclusion courte** : le dépôt actuel n'est **pas** une base pour un « Portable Studio » complet ; il n'en est que **la couche de bootstrap binaire**. La quasi-totalité de la stack cible (API OpenAI-compatible custom, MCP skills, boucle agentique, UI FR/EN, runtime Python portable) reste à construire.

---

## 2. Inventaire des fichiers

| Chemin | Type | Taille | Rôle |
|---|---|---|---|
| `README.md` | doc Markdown | 9,0 Ko | Documentation utilisateur (features, arborescence cible, quick-start, requirements, configuration, dépannage, mise à jour). Écrit en anglais. |
| `install.sh` | Bash (executable) | 15 Ko | Installateur universel Linux/macOS. Menu interactif de sélection de plateforme(s), requête à l'API GitHub `releases/latest` pour `ggml-org/llama.cpp`, filtrage des assets CPU (exclut CUDA/Vulkan/ROCm/SYCL/…), téléchargement + extraction dans `/tmp`, résolution des symlinks pour compatibilité FAT/NTFS/exFAT, copie dans `bin/<plateforme>/` et renommage du binaire final. |
| `install.bat` | Batch Windows | 12,6 Ko | Équivalent Windows d'`install.sh` : mêmes rôles, utilise PowerShell (`Invoke-RestMethod`) pour l'API GitHub et `Expand-Archive`/`tar` pour l'extraction. Résout aussi les symlinks (via PowerShell) avant copie. |
| `start.sh` | Bash (executable) | 8,6 Ko | Lanceur Linux/macOS. Énumère `models/*.gguf`, propose une sélection interactive si >1, détecte OS/arch, teste si le FS est restreint (`noexec`, FAT/exFAT via `/proc/mounts`, `stat -f`, `chmod +x`, `--version`) et recopie binaires + libs dans `/tmp` si nécessaire. Configure `LD_LIBRARY_PATH` (Linux) ou `DYLD_LIBRARY_PATH` (macOS), calcule le nombre de threads (`nproc - 1`), ouvre le navigateur puis `exec` le binaire avec `-c 4096 --port 8080 --host 0.0.0.0`. Trap `EXIT/INT/TERM` pour cleanup du tmpdir. |
| `start.bat` | Batch Windows | 3,7 Ko | Équivalent Windows de `start.sh`. Vérifie la présence de `VCRUNTIME140_1.dll`, énumère les modèles, sélection interactive, prépend `bin\windows` au `PATH` pour la résolution des DLL, calcule threads (`NUMBER_OF_PROCESSORS - 1`), lance le binaire. |
| `models/.gitkeep` | placeholder | 0 o | Réserve le dossier vide pour les fichiers `.gguf` de l'utilisateur. |
| `bin/.gitkeep` | placeholder | 0 o | Réserve le dossier `bin/`. |
| `bin/linux/linux_x64/.gitkeep` | placeholder | 0 o | Dossier cible du binaire Linux x64. Vide au commit. |
| `bin/linux/linux_arm64/.gitkeep` | placeholder | 0 o | Dossier cible du binaire Linux ARM64. Vide au commit. |
| `bin/mac/mac_x64/.gitkeep` | placeholder | 0 o | Dossier cible du binaire macOS Intel. Vide au commit. |
| `bin/mac/mac_arm64/.gitkeep` | placeholder | 0 o | Dossier cible du binaire macOS Apple Silicon. Vide au commit. |
| `bin/windows/.gitkeep` | placeholder | 0 o | Dossier cible du binaire Windows x64. Vide au commit. |

**Total** : 5 fichiers de code (2 scripts × 2 OS + 1 README), 7 `.gitkeep`. Aucun fichier de configuration (`.env`, `.toml`, `.yml`), aucun fichier de test, aucune licence explicite, aucun `CHANGELOG`, aucun `.gitignore`.

---

## 3. Dépendances externes identifiées

### 3.1 Runtime (exécution finale)

| Dépendance | Origine | Version | Rôle |
|---|---|---|---|
| `llama-server` (binaire) | `github.com/ggml-org/llama.cpp` — release `latest` récupérée dynamiquement | flottante (dernière release au moment de l'install ; à ce jour la variable `RELEASE_TAG` est du type `bXXXX`) | Serveur d'inférence LLM GGUF, expose déjà nativement une API HTTP OpenAI-compatible et une UI web intégrée. |
| `libllama.so` / `libggml.so` / `libggml-cpu.so` (Linux) | fournies dans l'archive `llama.cpp` | idem release | Libs partagées nécessaires au binaire. |
| `libllama.dylib` / `libggml*.dylib` (macOS) | idem | idem | Libs Mach-O correspondantes. |
| `llama.dll` / `ggml*.dll` (Windows) | idem | idem | DLL Windows. |
| Microsoft Visual C++ Redistributable (`VCRUNTIME140_1.dll`) | Microsoft | 2015-2022 | Pré-requis Windows, l'utilisateur doit l'installer (le script vérifie sa présence et échoue sinon). |

### 3.2 Outils requis sur la machine hôte (au moment de l'install)

| Outil | Utilisé par | Notes |
|---|---|---|
| `bash` | `install.sh`, `start.sh` | Testé avec `set -uo pipefail`. |
| `curl` | `install.sh`, `install.bat` | Requis explicitement, échec si absent. |
| `tar` | `install.sh`, `install.bat` | Requis pour `.tar.gz`. Windows 10 build 17063+ le fournit. |
| `unzip` | `install.sh` (Windows target) | Optionnel — utilisé uniquement si on installe la cible Windows depuis Linux/macOS. |
| `powershell` | `install.bat` | Utilisé pour l'API GitHub et `Expand-Archive`. |
| `stat`, `find`, `readlink`/`realpath`, `chmod`, `mktemp`, `sysctl`/`nproc` | `install.sh`, `start.sh` | Utilitaires POSIX standards. |
| `xdg-open` (Linux) / `open` (macOS) / `start` (Windows) | `start.sh`, `start.bat` | Pour ouverture automatique du navigateur (fallback silencieux si absent). |

### 3.3 Dépendances **absentes du dépôt** mais nécessaires pour la cible « Portable Studio »

Aucune de ces dépendances n'est présente aujourd'hui, elles devront être introduites :

- Python (runtime portable) + `pip` (`fastapi`, `uvicorn`, `pydantic`, `httpx`, éventuellement `mcp`, etc.).
- Node/Yarn ou build statique (si UI React) — **ou** une UI purement HTML/CSS/JS statique.
- Serveur MCP (protocole Model Context Protocol) — pas de librairie installée.
- Fichiers de configuration `.env` / `config.toml` pour paramétrer ports, mode zero-trace, langue par défaut FR/EN.
- Système de i18n (fichiers de traduction FR/EN).

---

## 4. État Git

- **Branche courante** : `main`, à jour avec `origin/main`.
- **Remote** : `https://github.com/africAIsoftmarketing/Portable_Local_AI.git`. **Avertissement sécurité** : l'URL configurée localement inclut un jeton GitHub en clair (`REDACTED_GITHUB_TOKEN`). Ce token ne fait pas partie des fichiers du dépôt mais est stocké dans `.git/config` du workspace ; il ne sera pas versionné. À noter pour éviter toute fuite.
- **Working tree** : propre (le seul « untracked » est `.emergent/` propre à l'environnement d'exécution — ne fait pas partie du projet).
- **Historique** : 14 commits, tous de l'auteur `dinesh <dp973989@gmail.com>` / `Sdinzsh` / `Dinesh Tharun`, entre avril et mai 2026.
- **Derniers commits** (du plus récent au plus ancien) :

  | SHA | Date | Message |
  |---|---|---|
  | `d8acd19` | 2026-05-31 | fix: path bug fixed |
  | `5300d57` | 2026-05-31 | fix: file system links |
  | `13c9dae` | 2026-04-24 | updated README |
  | `df3b5c1` | 2026-04-24 | fix: lines removed |
  | `0693232` | 2026-04-24 | feat: model selection |
  | `dedba4d` | 2026-04-24 | **Delete ui directory** (suppression d'un `ui/index.html` de 1 396 lignes) |
  | `7f3509a` | 2026-04-23 | fix: line issue fixed |
  | `b43bb71` | 2026-04-23 | feat: select platform to install |
  | `77d9863` | 2026-04-23 | feat: updated functionality |
  | `8903c93` | 2026-04-23 | fix: downloads to all platforms |
  | `dae487f` | 2026-04-23 | add: installation script and fixed starter |
  | `1b904e7` / `71dfc54` / `7d0135c` / `cb276ef` | avant | commits initiaux |

- **Propreté** : historique linéaire, pas de merges, messages conventionnels (`fix:`, `feat:`) mais parfois vagues (« updated functionality »). Pas de tags, pas de releases. Aucune branche autre que `main`.
- **Point important** : le commit `dedba4d` a **supprimé** un fichier `ui/index.html` de 1396 lignes qui semblait constituer l'UI custom. Il faudrait consulter ce commit avant la phase de conception pour savoir si des idées d'UX de cette UI peuvent inspirer la nouvelle UI FR/EN — mais **rien de cette UI n'est actuellement présent dans le dépôt**.

---

## 5. Réutilisable tel quel vs. à réécrire

### 5.1 À conserver **tel quel** (ou avec ajustements mineurs)

| Élément | Justification |
|---|---|
| **`install.sh` et `install.bat`** | Logique de téléchargement robuste : requête API GitHub, filtrage des assets CPU-only (exclut CUDA/Vulkan/ROCm/SYCL/OpenCL/MPI/OpenVINO/OpenEuler/KleidiAI/Kompute), extraction dans `/tmp`, résolution des symlinks pour portabilité FAT/NTFS/exFAT, gestion multi-plateforme depuis un seul hôte (une clé USB préparée sous Linux peut booter sur Mac/Windows). Le code est explicite, coloré, gère les erreurs. **Réutilisable à 90 %**, seul l'ajout d'une étape « bundle Python portable » sera nécessaire. |
| **`start.sh`** | Détection OS/arch, sélection de modèle interactive, détection filesystème restreint (`noexec`, FAT32, exFAT) avec quatre couches de vérification (mount flags, fstype, `chmod +x`, `--version`), staging dans `/tmp` avec `trap` de nettoyage sur `EXIT/INT/TERM`. **Excellent socle**. Il faudra le compléter pour : (a) lancer aussi le serveur FastAPI + MCP + agent avant `llama-server`, (b) proposer une option « zero-trace » (chiffrement/purge du staging), (c) ajouter des paramètres CLI (port, langue, contexte). |
| **`start.bat`** | Idem `start.sh` côté Windows, avec check `VCRUNTIME140_1.dll`. **Réutilisable à ~80 %**, mais moins riche que `start.sh` (pas de gestion FS restreint côté Windows car NTFS gère nativement l'exécution). |
| **Arborescence `bin/{linux,mac,windows}/…/`** | Bien pensée, séparation propre par plateforme et par architecture. À conserver telle quelle. |
| **Dossier `models/`** | Convention simple et claire. À conserver. |
| **`README.md`** | Bien rédigé, à conserver comme **base** de documentation utilisateur (à traduire en FR et enrichir avec API/MCP/UI). |
| **Filtrage des variantes GPU** | La regex `grep -iv "cuda\|vulkan\|rocm\|kompute\|sycl\|opencl\|mpi\|openvino\|openeuler\|kleidiai"` évite proprement le mauvais binaire. À conserver, éventuellement à rendre paramétrable (« installer la variante Vulkan si l'utilisateur le demande »). |

### 5.2 À **réécrire ou compléter**

| Élément | Statut | Justification |
|---|---|---|
| **Serveur API OpenAI-compatible** | Absent | `llama-server` expose déjà `/v1/chat/completions` et `/v1/completions` nativement, mais le cahier des charges demande **notre propre couche FastAPI** (probablement pour ajouter auth, routing multi-modèles, journalisation zero-trace, hooks agentiques). À créer intégralement. |
| **Serveur MCP** | Absent | Aucune trace de librairie MCP ni de skills métiers. À créer intégralement. |
| **Boucle agentique** | Absente | À créer (probablement un orchestrateur Python appelant l'API OpenAI locale + MCP). |
| **UI web légère FR/EN** | Absente (supprimée) | Le fichier `ui/index.html` a été supprimé au commit `dedba4d`. À reconstruire. Le lanceur ne monte plus l'UI (`--path "$SCRIPT_DIR/ui"` est commenté). |
| **Runtime Python portable** | Absent | Nécessaire pour faire tourner FastAPI/MCP/agent sans installation Python sur l'hôte. Envisager `python-build-standalone` (Astral) ou un bundle `PyInstaller`/`Nuitka` par plateforme. |
| **Système de configuration** | Absent | Aucun `.env` / `config.toml`. À introduire. |
| **i18n FR/EN** | Absent | À prévoir dès la conception UI + backend (messages d'erreur, logs). |
| **Tests** | Absents | Aucun test unitaire ou d'intégration. À créer. |
| **`.gitignore`** | Absent | Nécessaire (au minimum : `bin/**/*.exe`, `bin/**/*.so*`, `bin/**/*.dll`, `bin/**/*.dylib`, `models/*.gguf`, `.venv/`, `__pycache__/`, `node_modules/`). |
| **Licence** | Absente | Aucun `LICENSE`. `llama.cpp` est MIT ; nous devons choisir et déclarer la nôtre. |

---

## 6. Limitations et incompatibilités détectées

### 6.1 Multi-plateforme

- **Windows ARM64** n'est pas couvert (l'installateur ne propose que `win-cpu-x64.zip`). Un Windows sur Snapdragon X ne fonctionnera pas nativement.
- **Linux musl** (Alpine) : les binaires téléchargés sont compilés contre `glibc 2.17+` (mentionné dans le README). Alpine nécessitera une variante — non gérée aujourd'hui.
- **FreeBSD/OpenBSD** : non pris en charge.

### 6.2 Contrainte « offline »

- L'**installation** requiert obligatoirement une connexion Internet (API GitHub + téléchargement des archives, ~50-100 Mo par plateforme). Ce n'est pas bloquant pour une distribution USB si l'installateur est exécuté **une fois** par le mainteneur avant expédition, mais il faut prévoir un **mode « install depuis cache local »** (archives déjà présentes dans un `cache/`).
- Rate-limit API GitHub : 60 req/h par IP non authentifiée. Documenté dans le README.
- Aucune **vérification d'intégrité** (checksum SHA256, signature GPG) des binaires téléchargés depuis GitHub. Risque supply-chain à mitiger.

### 6.3 Contrainte « zero-trace »

Sérieusement problématique aujourd'hui :

- `start.sh` **écrit dans `/tmp`** quand le FS est restreint (FAT32/exFAT/noexec). Le `trap` supprime le dossier à la sortie normale, mais un `kill -9` laisse des traces.
- `llama-server` peut écrire des logs sur stdout mais aussi (selon config) un fichier de cache — à vérifier et neutraliser.
- L'ouverture automatique du navigateur (`xdg-open`, `open`, `start`) laisse une entrée d'historique.
- Aucune purge de la RAM après extinction n'est faite (pas trivial en user-space, mais documentable).
- Le PATH est modifié par `start.bat` (prépendé avec `bin\windows`) — annulé automatiquement à la fin du process, donc pas de trace persistante.

Un vrai mode « zero-trace » impliquera : monter un tmpfs chiffré, désactiver l'ouverture navigateur, forcer stdout logs, purger `%TEMP%`/`/tmp` à l'extinction, éviter tout accès disque persistent en dehors de la clé USB.

### 6.4 Portabilité binaires

- Les libs partagées sont copiées **en dur** (les symlinks sont résolus par `_resolve_symlinks` dans `install.sh` et par le bloc PowerShell dans `install.bat`) — bien pensé pour FAT/exFAT qui ne supportent pas les symlinks POSIX.
- La détection d'exécution restreinte dans `start.sh` (`_is_restricted_fs`) est robuste (4 couches), mais **absente de `start.bat`** — sur Windows, l'exécution depuis une clé USB NTFS/exFAT fonctionne car Windows ne bloque pas l'exécution ; toutefois des politiques d'entreprise (AppLocker, SRP) pourraient poser problème et ne sont pas gérées.

### 6.5 GPU backends

- L'installateur exclut délibérément **toutes** les variantes GPU (CUDA/Vulkan/ROCm/SYCL/etc.). Volontaire pour la portabilité, mais empêche l'accélération sur des machines qui en disposent. À réintroduire de manière **optionnelle** dans la nouvelle version (ex. option `--with-gpu vulkan` ou détection auto).

### 6.6 Sécurité

- Port `--host 0.0.0.0` par défaut : le serveur est exposé sur tout le LAN sans authentification. Volontaire (« LAN sharing ») mais dangereux dans un contexte pro/entreprise. Pas de HTTPS, pas de token API.
- Token GitHub visible dans la config git locale (vu §4). Ne concerne pas les fichiers versionnés, mais est un risque opérationnel si `git config --get remote.origin.url` est loggé quelque part.

---

## 7. Risques techniques identifiés

| Risque | Sévérité | Impact | Mitigation suggérée |
|---|---|---|---|
| **API GitHub rate-limitée** (60/h) pendant l'installation | Moyen | L'installateur échoue silencieusement (documenté dans le README). | Ajouter un mode « cache local » avec archives pré-téléchargées, ou authentifier via un token embarqué à faible privilège pour build officiel. |
| **Pas de vérification d'intégrité** des binaires téléchargés | Élevé | Attaque supply-chain (compromission du release GitHub, MitM). | Ajouter checksums SHA256 pinned pour chaque release supportée, vérifier après téléchargement. |
| **UI supprimée** (`dedba4d`) | Moyen | Aucune interface actuellement, à reconstruire ex nihilo pour FR/EN. | Consulter le contenu du commit supprimé pour idées UX, ou repartir de zéro avec un design agent. |
| **Exposition LAN par défaut** (`0.0.0.0:8080` sans auth) | Élevé | Toute machine du réseau peut envoyer des requêtes. | Bind sur `127.0.0.1` par défaut ; option `--lan` explicite ; ajouter un token API généré au premier lancement. |
| **Zero-trace non implémenté** | Élevé (par rapport au cahier des charges) | `/tmp`, historique navigateur, cache modèles, logs. | Redesign complet : tmpfs chiffré, mode headless, purge sur SIGTERM. |
| **Windows ARM64 non supporté** | Faible | Machines Snapdragon X exclues. | Ajouter cible dans les installateurs quand `llama.cpp` publie l'asset. |
| **VCRUNTIME140_1.dll requis** (Windows) | Faible | L'utilisateur doit installer VC++ Redistributable manuellement, or on veut du portable. | Bundler la DLL dans `bin/windows/` (licence Microsoft à vérifier) ou statically-link. |
| **Aucun Python portable actuellement** | Structurel | Impossible d'ajouter FastAPI/MCP sans casser la promesse « zero install ». | Intégrer `python-build-standalone` (Astral) — ~30 Mo par plateforme, exécutable en place. |
| **Absence de licence** | Moyen | Ambiguïté légale sur la réutilisation. | Choisir (MIT/Apache-2.0/AGPL) et documenter dépendances (`llama.cpp` MIT, modèles GGUF variables). |
| **Aucun test ni CI** | Moyen | Régressions faciles sur 3 OS × 2 archs. | Ajouter GitHub Actions matrix (linux-x64, linux-arm64, macos-13, macos-14, windows-latest) avec au minimum un smoke test de l'installateur. |
| **`.gitignore` manquant** | Faible | Risque de commit accidentel de binaires (~100 Mo) ou de modèles (~4 Go). | Ajouter dès la phase 1. |
| **Historique commits d'un seul auteur** | Faible | Bus-factor = 1, pas de revue de code. | Non bloquant pour la reprise, mais à noter. |

---

## 8. Recommandation de base de départ pour l'architecture

Compte tenu de l'analyse :

1. **Conserver** `install.sh`, `install.bat`, `start.sh`, `start.bat` comme **couche de bootstrap binaire** — ils font bien leur travail. Les enrichir avec :
   - téléchargement d'un **runtime Python portable** (`python-build-standalone`) par plateforme, placé dans `runtime/<plateforme>/python/`,
   - installation offline des dépendances Python via un `wheels/` embarqué (résolu au préalable),
   - vérification SHA256 des archives,
   - support d'un mode « cache local » (`--offline`).
2. **Ajouter au-dessus** une nouvelle couche applicative Python en `app/` (ou `src/`) :
   - `app/api/` : serveur FastAPI OpenAI-compatible qui **délègue** l'inférence à `llama-server` en local via HTTP interne (loopback, port aléatoire), et ajoute auth, journalisation zero-trace, streaming SSE.
   - `app/mcp/` : serveur MCP exposant les skills métiers.
   - `app/agent/` : boucle agentique orchestrant API + MCP + outils.
   - `app/ui/` : UI statique (HTML/CSS/JS vanilla ou build React pré-compilé) FR/EN, servie soit par FastAPI soit par `llama-server` via `--path`.
3. **Refondre `start.sh`/`start.bat`** pour orchestrer 3 processus : `llama-server` (loopback), FastAPI (public), MCP (loopback), avec un gestionnaire de cycle de vie propre (trap/signaux).
4. **Introduire dès le début** : `.gitignore`, `LICENSE`, `pyproject.toml`, `docs/`, dossier `tests/`, GitHub Actions matrix.
5. **Mode « zero-trace »** : concevoir dès la phase 1 (tmpfs chiffré optionnel, purge au SIGTERM, logs mémoire uniquement, headless par défaut).

En résumé : **le dépôt actuel est un excellent point de départ pour la couche bootstrap, mais la totalité de la stack applicative reste à construire**. Aucune régression n'est nécessaire ; tout est en mode « addition ».

---

## 9. Résumé structuré (10-15 lignes)

- **État du dépôt** : minimal (~124 Ko, 5 fichiers de code + README), propre, sur `main` à jour, historique linéaire d'un seul auteur (14 commits, avril–mai 2026), working tree clean.
- **Nature actuelle** : uniquement un wrapper Bash/Batch autour de `llama-server` (upstream `ggml-org/llama.cpp`). Aucun Python, aucun Node, aucun MCP, aucune UI (la précédente `ui/index.html` a été supprimée au commit `dedba4d`).
- **Couvert aujourd'hui** : téléchargement multi-plateforme (Linux x64/arm64, macOS x64/arm64, Windows x64) des binaires CPU depuis l'API GitHub Releases, résolution des symlinks pour FAT/exFAT, détection filesystème restreint avec staging `/tmp`, sélection de modèle GGUF interactive, exposition sur `0.0.0.0:8080`.
- **Réutilisable tel quel** : `install.sh`/`install.bat` (à ~90 %), `start.sh` (à ~80 %, à enrichir pour orchestrer aussi FastAPI/MCP/agent), arborescence `bin/`/`models/`, README comme base doc.
- **À construire intégralement** : serveur API OpenAI-compatible custom (FastAPI), serveur MCP, boucle agentique, UI FR/EN, runtime Python portable, système de config, i18n, tests, `.gitignore`, licence.
- **Risques majeurs** : pas de checksum des binaires téléchargés (supply-chain), zero-trace absent (traces dans `/tmp`, historique navigateur), exposition LAN sans auth par défaut, Windows ARM64 non supporté, absence de licence, bus-factor = 1.
- **Recommandation architecture** : garder la couche bootstrap actuelle, ajouter **au-dessus** une couche applicative Python (`app/api`, `app/mcp`, `app/agent`, `app/ui`) qui communique avec `llama-server` en loopback, et refondre `start.sh`/`start.bat` en orchestrateur 3-processus avec cycle de vie propre. Base de départ **saine** mais **très partielle** : ~10 % du produit cible existe.

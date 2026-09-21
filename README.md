# AfricAIsoft Portable Studio

> **Distribution portable USB 100 % offline** pour faire tourner un modèle
> local (GGUF via `llama.cpp`) avec API OpenAI-compatible, boucle agentique
> multi-outils MCP, UI web FR/EN, et fabrication industrielle de clés via
> le **Key Builder** Windows.

**Version : 1.0.0** — Licence : MIT — © 2026 AfricAIsoft

---

## Table des matières

1. [Présentation](#1-présentation)
2. [Prérequis par plateforme](#2-prérequis-par-plateforme)
3. [Installation pas-à-pas](#3-installation-pas-à-pas)
4. [Démarrage & première utilisation](#4-démarrage--première-utilisation)
5. [Configuration](#5-configuration)
6. [Spécificités Linux](#6-spécificités-linux)
7. [Ajouter un modèle GGUF](#7-ajouter-un-modèle-gguf)
8. [Ajouter un skill MCP](#8-ajouter-un-skill-mcp)
9. [Key Builder (fabrication USB Windows)](#9-key-builder-fabrication-usb-windows)
10. [FAQ — dépannage](#10-faq--dépannage)
11. [Limites connues (honnêteté)](#11-limites-connues-honnêteté)
12. [Licence et crédits](#12-licence-et-crédits)

---

## 1. Présentation

AfricAIsoft Portable Studio est une distribution USB clé-en-main qui embarque :

- Un **serveur d'inférence local** (`llama-server` de `llama.cpp`) supportant
  CPU / CUDA / ROCm / Vulkan / Metal.
- Un **orchestrateur FastAPI** (Python 3.12 portable) exposant une API
  OpenAI-compatible `/v1/*` + endpoints studio `/api/*`.
- 4 **skills MCP** stdio prêts à l'emploi (8 outils métier) :
  cybersécurité, comptabilité, RAG local BM25, utilitaires généraux.
- Une **UI web FR/EN** en HTML/CSS/JS vanilla (aucun `node`, aucun `npm`).
- Un **Key Builder** C#/.NET 8 WPF pour Windows qui fabrique des clés USB
  personnalisées (modèle, skills, plateformes, marque cliente).

Tout fonctionne **entièrement hors ligne** après installation, sans télémétrie
et sans persistance en dehors des fichiers explicites de la clé USB.

```
┌─────────────────────────────────────────────────────────────────┐
│                    Navigateur (http://127.0.0.1:8080)           │
└────────────────────────────┬────────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────────┐
│  Orchestrateur FastAPI (Python 3.12 portable)                   │
│    /v1/chat/completions  ── boucle agentique ── /api/events SSE │
│    /api/system-prompt    /api/config    /api/skills             │
│    /api/conversations    /api/models/{available,switch}         │
└──┬────────────────────┬────────────────────┬────────────────────┘
   │ HTTP loopback      │ stdio JSON-RPC 2.0 │ FS
   ▼                    ▼                    ▼
llama-server         MCP servers          ui/  models/
127.0.0.1:8090       (cybersec,           config/  data/
                      accounting,
                      rag, general)
```

---

## 2. Prérequis par plateforme

| Plateforme          | Requis                                                                 |
|---------------------|-----------------------------------------------------------------------|
| **Linux x86_64**    | glibc ≥ 2.17 (toute distro moderne : Ubuntu 22.04+, Fedora 39+, Debian 12+). Outils : `curl`, `tar`. |
| **Linux ARM64**     | Idem, glibc ≥ 2.17. Raspberry Pi 5 / cloud ARM validés.               |
| **macOS Apple Silicon (M1/M2/M3)** | macOS 11 Big Sur ou plus récent. `bash`, `curl` fournis. |
| **macOS Intel**     | Idem.                                                                  |
| **Windows 10 (build 17063+) / 11 x64** | [Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe) requis. Optionnel : PowerShell 7+. |

**RAM recommandée** :

| RAM disponible | Tailles de modèle GGUF Q4_K_M recommandées |
|---|---|
| 4 Go  | modèles 1B – 3B                             |
| 8 Go  | modèles jusqu'à 7B                          |
| 16 Go | modèles jusqu'à 13B                         |
| 32 Go | modèles 30B et plus                         |

**Espace disque** : ~1,2 Go pour le studio 5 plateformes CPU-only,
+ 2-8 Go par modèle GGUF selon taille.

---

## 3. Installation pas-à-pas

### 3.1 Récupération des binaires `llama-server` (une seule fois, en ligne)

```
┌────────────────────────────────────────────────────────────────┐
│  Utilisez `scripts/fetch-binaries.sh` ou `.ps1` pour peupler   │
│  `bin/<os>-<arch>/<backend>/` depuis les releases officielles  │
│  github.com/ggml-org/llama.cpp                                 │
└────────────────────────────────────────────────────────────────┘
```

**Linux / macOS** :
```bash
bash scripts/fetch-binaries.sh -t b11071 -p linux-x86_64 -b cpu
bash scripts/fetch-binaries.sh -t b11071 -p darwin-arm64  -b cpu
bash scripts/fetch-binaries.sh -t b11071 -p windows-x86_64 -b cpu
# ... ou toutes les plateformes en boucle
```

**Windows** :
```powershell
pwsh -File scripts\fetch-binaries.ps1 -Tag b11071 -Platform windows-x86_64 -Backend cpu
```

Si votre distribution a une glibc plus ancienne que celle du binaire
upstream (ex. Debian 12 → glibc 2.36 vs release b11071 qui exige 2.38),
compilez localement en 3 minutes : voir `docs/COMPILATION.md`.

### 3.2 Runtime Python portable (recommandé pour clé USB)

Pour une clé USB **vraiment autonome** (sans dépendre du Python système
de la machine hôte), lancez :

```bash
bash scripts/build-portable.sh --target linux-x64      # ou linux-arm64, macos-arm64, macos-x64, windows-x64, all
```

Cela télécharge `python-build-standalone 3.12.5` dans `bin/<plat>/python/`,
installe toutes les wheels dans `vendor/wheels/`, et copie le code du studio.

Sans ce runtime portable, le studio utilisera le Python système de la
machine (fallback documenté dans `scripts/core-startup.sh`).

### 3.3 Placer un modèle GGUF

Téléchargez un `.gguf` depuis [huggingface.co](https://huggingface.co) et
placez-le dans `models/` :

```
models/
├── qwen2.5-3b-instruct-q4_k_m.gguf      ← recommandé démo
└── (ou tout autre .gguf de votre choix)
```

Q4_K_M offre le meilleur compromis taille/qualité pour la plupart des cas.

---

## 4. Démarrage & première utilisation

### Linux
```bash
chmod +x start-linux.sh
./start-linux.sh
```

### macOS
```bash
./start-mac.command       # ou double-clic depuis le Finder
```

### Windows
```
Double-cliquez sur start-windows.bat
```

Séquence attendue (~10-40 s selon la taille du modèle) :

```
╔════════════════════════════════════════════════════════╗
║   AfricAIsoft Portable Studio - démarrage              ║
╚════════════════════════════════════════════════════════╝
[INFO ] Plateforme : linux-x86_64
[INFO ] Backend détecté : cpu (no_gpu_detected)
[INFO ] Python portable : bin/linux-x86_64/python/bin/python3
[INFO ] API : http://127.0.0.1:8080
[INFO ] Démarrage orchestrateur...
=== Prêt ===
```

Le navigateur s'ouvre automatiquement sur `http://127.0.0.1:8080`. L'UI
propose : chat, sélecteur de modèle, panneau des skills, éditeur du system
prompt, panneau de configuration.

**Arrêt propre** : `Ctrl+C` dans le terminal (Linux/macOS) ou fermeture de
la fenêtre (Windows). Le script `stop-*` correspondant nettoie les PIDs et
le staging `/tmp/portableai.*`.

---

## 5. Configuration

Tout est concentré dans **`config/settings.json`** — validé par Pydantic +
JSON Schema (`config/settings.schema.json`, `additionalProperties:false`).

Extraits notables (avec défauts) :

```json
{
  "server": {
    "bind_host": "127.0.0.1",  // "0.0.0.0" = partage LAN (opt-in)
    "port": 8080,
    "llama_host": "127.0.0.1", // llama-server TOUJOURS loopback
    "llama_port": 8090
  },
  "model": {
    "path": null,              // null = 1er .gguf trouvé
    "context_size": 8192
  },
  "agentic": {
    "max_tool_rounds": 5,
    "total_timeout": 120,
    "allow_parallel_tools": true
  },
  "security": {
    "require_api_key": false,       // active l'auth Bearer
    "require_signature": false      // exige release.json.sig + public.pem
  },
  "ui": {
    "default_language": "fr",       // "fr" | "en"
    "theme": "auto"                 // "auto" | "light" | "dark"
  }
}
```

L'UI dispose d'un onglet **Config** qui édite ce fichier avec validation
en temps réel. Certaines clés (`server.port`, `model.context_size`, etc.)
nécessitent un redémarrage manuel.

---

## 6. Spécificités Linux

### 6.1 Systèmes de fichiers restreints (FAT32 / exFAT / noexec)

Si la clé USB est formatée en FAT32 ou exFAT, ou montée `noexec`, les
binaires `llama-server` refusent de s'exécuter. Le studio détecte
automatiquement ces cas et **stage les binaires dans `/tmp/portableai.XXXX/`**
avant lancement, avec résolution des symlinks `.so.N`. Aucune action
requise. Voir la logique `_is_restricted_fs()` dans `scripts/core-startup.sh`.

### 6.2 Dépendances système

Le studio exige `curl` (téléchargement binaires) et `python3` (fallback si
runtime portable absent). Le `glibc` du binaire téléchargé doit correspondre
à celui de la distro cible ; sinon, recompilez avec `docs/COMPILATION.md`.

### 6.3 GPU (CUDA, ROCm, Vulkan)

Le détecteur (`scripts/detect-backend.sh`) sonde en < 5 s :
1. `nvidia-smi` → backend CUDA si binaire `bin/<plat>/cuda/` présent.
2. `rocm-smi` → ROCm.
3. `vulkaninfo` → Vulkan.
4. Sinon → CPU.

Récupérez les binaires GPU (opt-in) via `scripts/fetch-binaries.sh -b cuda`
puis relancez le studio.

---

## 7. Ajouter un modèle GGUF

1. Téléchargez un `.gguf` (ex. `mistral-7b-instruct-v0.2.Q4_K_M.gguf`)
   depuis HuggingFace et déposez-le dans `models/`.
2. Rechargez l'onglet **Modèles** de l'UI, puis cliquez sur le nouveau modèle.
3. Confirmez la bascule : le studio décharge le modèle courant et charge
   le nouveau (~5-30 s selon taille).

Alternative CLI : éditez `config/settings.json → model.path`, puis
redémarrez le studio.

**Recommandations tool-calling** :

| Taille    | Fiabilité choix d'outils autonome | Usage recommandé          |
|-----------|-----------------------------------|---------------------------|
| **0.5B**  | ⚠ ~20 %                          | Test infrastructure       |
| **3B**    | ✅ ~70 %                          | Démo / poste léger        |
| **7B+**   | ✅✅ ~90 %+                        | Production                |

---

## 8. Ajouter un skill MCP

Recette < 5 min (détail complet dans `docs/ADD-SKILL.md`) :

```bash
cp -r mcp-servers/_template mcp-servers/mon-skill
# Éditer server.py : décorateur @server.tool(...) sur vos fonctions
# Redémarrer le studio → /api/skills liste automatiquement mon-skill
```

**Règles impératives** :
- Pure Python, zéro dépendance native.
- `stdout` = JSON-RPC uniquement (utilisez `stderr` pour les logs).
- Timeout 30 s par appel, respawn auto (3 tentatives).

---

## 9. Key Builder (fabrication USB Windows)

`keybuilder/` contient une application C#/.NET 8 WPF Windows pour fabriquer
des clés USB personnalisées en batch (mode production).

**Fonctionnalités** :
- Détection USB temps réel via WMI.
- Sélection modèle GGUF / skills / plateformes cibles / system prompt custom.
- Vérification GGUF (magic + version 1..3) avant copie.
- Copie SHA-256 streaming (mémoire O(1) même pour modèles 20 Go).
- Formatage exFAT via `diskpart` avec élévation UAC.
- Reprise après interruption (journal `.keybuilder-journal.json`).
- Rapport HTML autonome par clé produite + marker `.africaisoft-key.json`.
- Mode batch séquentiel (JSON de configuration).

**Compilation & tests** :
```powershell
cd keybuilder
dotnet test tests/AfricAIsoft.KeyBuilder.Core.Tests   # portable, passe sous Linux/Windows
dotnet build src/AfricAIsoft.KeyBuilder.Wpf           # Windows uniquement (WPF)
pwsh -File installer/build-portable.ps1               # ZIP portable
dotnet build installer/KeyBuilder.wixproj             # MSI (WiX v5)
```

Documentation détaillée : `keybuilder/docs/USER-GUIDE.md` (utilisateur)
et `keybuilder/docs/TECHNICAL.md` (architecture).

---

## 10. FAQ — dépannage

### Linux

| Symptôme | Cause probable | Solution |
|---|---|---|
| `llama-server: version GLIBC_2.38 not found` | Distro plus ancienne que le binaire | Recompiler localement : `docs/COMPILATION.md` |
| `error while loading shared libraries: libllama.so` | Libs manquantes à côté du binaire | Relancer `scripts/fetch-binaries.sh` (copie TOUT le contenu de l'archive) |
| Le studio ne trouve pas de modèle | `models/` vide | Déposez un `.gguf` |
| Port 8080 déjà occupé | Autre service | Modifiez `config/settings.json → server.port` |

### macOS

| Symptôme | Solution |
|---|---|
| Gatekeeper refuse `llama-server` | Préférences Système → Sécurité → « Autoriser quand même », ou `spctl --add bin/darwin-arm64/cpu/llama-server` |
| Metal absent | Le binaire `bin/darwin-arm64/metal/llama-server` doit exister ; sinon fallback CPU |
| `bash: syntax error near unexpected token` | Vous utilisez l'ancien bash 3.2 système ; le studio est compatible (correctif Phase 6) |

### Windows

| Symptôme | Solution |
|---|---|
| `VCRUNTIME140_1.dll` introuvable | Installez [VC++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe) |
| `curl not found` | Windows 10 ≥ 17063 l'inclut ; sinon [curl.se/windows](https://curl.se/windows/) |
| PowerShell bloque le script | `Set-ExecutionPolicy -Scope Process Bypass` |

### Général

- **Réponses très lentes** : réduisez `model.context_size` (4096 au lieu de 8192)
  ou passez à un modèle plus petit (Q4_K_M au lieu de Q8_0).
- **Boucle agent qui n'appelle jamais d'outils** : votre modèle n'est pas fait
  pour le function calling. Utilisez un modèle Instruct fine-tuné (Qwen 2.5-7B,
  Llama 3.1-8B, Mistral-Nemo).
- **UI qui affiche `chat.welcome` en dur** : les fichiers `ui/assets/i18n/*.json`
  n'ont pas été livrés. Vérifiez que `git ls-files ui/assets/i18n/` liste 2 fichiers.

---

## 11. Limites connues (honnêteté)

- **Qwen 2.5-0.5B** embarqué en démo : suffisant pour tester la stack, **trop
  petit pour un routage d'outils autonome fiable**. En production, prévoir
  un modèle 7B+.
- **Windows ARM64 (Snapdragon X)** non couvert par défaut : aucun binaire
  llama-server upstream.
- **ext4 en écriture sous Windows** : non supporté nativement. Utilisez une
  clé exFAT ou NTFS (le Key Builder refuse le formatage ext4 avec un message
  clair).
- **HTTPS local** : non fourni. Rester en `bind_host=127.0.0.1` pour toute
  utilisation sensible. Le mode LAN (`0.0.0.0`) est en HTTP en clair —
  documenté comme limitation.
- **Vérification Ed25519** : implémentée (Phase 6). La clé publique
  fabricant est embarquée dans `config/public.pem` (livrée avec la
  distribution). La clé privée `keys/private.pem` **ne doit JAMAIS être
  commitée ni distribuée** : elle est conservée hors ligne par l'opérateur
  fabricant (recommandé : HSM matériel, YubiKey PIV, coffre-fort chiffré
  VeraCrypt, ou clé USB dédiée air-gapped). Voir §Publier une release
  signée ci-dessous. La vérification est **désactivée par défaut**
  (`security.require_signature=false`) et à activer sur les livraisons
  signées uniquement.

### Publier une release signée (procédure fabricant)

```bash
# 1) Générer la paire (une fois pour toutes, à conserver hors ligne)
python3 scripts/sign-release.py generate-keypair --out keys
#    → keys/private.pem (0600, à archiver hors ligne)
#    → keys/public.pem  (à embarquer dans config/public.pem à chaque release)

# 2) Signer le manifest de la release
python3 scripts/sign-release.py sign release.json --key keys/private.pem
#    → release.json.sig (base64, à embarquer dans la distribution)

# 3) Copier la clé publique (une seule fois, ou à chaque rotation de clé)
cp keys/public.pem config/public.pem
git add config/public.pem release.json release.json.sig
git commit -m "release: v1.0.0 signée Ed25519"

# 4) Activer la vérification stricte côté utilisateur final
#    → dans config/settings.json : "security": { "require_signature": true }
```

**Le fingerprint de la clé publique** (SHA-256 DER) doit être publié hors bande
(site officiel, keybase, canal privé opérateur) pour que les clients puissent
valider `config/public.pem` avant d'activer `require_signature=true`.
- **Zero-trace forensique complet** : les logs sont métadonnées uniquement
  (aucun contenu utilisateur), les conversations sont écrites dans
  `data/conversations.json` (à chiffrer via VeraCrypt / BitLocker / LUKS
  si sensible — hors périmètre du studio).

---

## 12. Licence et crédits

**Licence** : MIT © 2026 AfricAIsoft. Voir `LICENSE`.

**Crédits** :
- [`llama.cpp`](https://github.com/ggml-org/llama.cpp) — moteur d'inférence.
- [`python-build-standalone`](https://github.com/astral-sh/python-build-standalone) — Python 3.12 portable.
- Modèles GGUF issus de la communauté HuggingFace.
- Protocole [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) — Anthropic.

Contributions bienvenues via pull request. Toute nouvelle fonctionnalité
doit rester **100 % offline**, **zéro dépendance native compilée à
l'installation**, et **respecter le principe zero-trace**.

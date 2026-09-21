# Checklist de validation humaine — AfricAIsoft Portable Studio v1.0.0

> Cette checklist reprend les critères d'acceptation des Livrables 1 & 2
> du cahier des charges. Chaque item se coche APRÈS validation manuelle
> sur du matériel physique. Ordre : préparation → démarrage → intégration →
> sécurité → Key Builder.

Passage validé le _________________ par _________________
Version testée : `1.0.0` — commit git : ______________

---

## Livrable 1 — Studio portable

### 1. Démarrage < 60 s (par OS et par taille de modèle)

- [ ] **Windows 10/11 x64** : brancher clé, double-cliquer `start-windows.bat`,
      chronométrer jusqu'à `http://127.0.0.1:8080/` UI répond.
      - Modèle 0.5B : cible ≤ 30 s.
      - Modèle 3B (Q4_K_M) : cible ≤ 60 s.
      - Modèle 7B (Q4_K_M) : cible ≤ 120 s.
- [ ] **macOS Intel** (Big Sur 11+) : `bash start-mac.command`, autoriser
      Gatekeeper si nécessaire.
- [ ] **macOS Apple Silicon (M1/M2/M3)** : idem, vérifier
      `/health → backend.backend = "metal"`.
- [ ] **Linux x86_64** (Ubuntu 22.04+, Fedora 39+, Debian 12+) :
      `bash start-linux.sh`.
- [ ] **Linux ARM64** (Raspberry Pi 5, ARM cloud) : idem avec binaire
      `linux-aarch64`.

### 2. Clé USB physique

- [ ] Copie de la distribution sur une clé exFAT 32 Go, lancement direct
      depuis la clé (aucune copie sur disque local nécessaire).
- [ ] Écriture correcte de `data/conversations.json`, `data/pids/*.pid`,
      `logs/*.log` sur la clé (permissions OK).
- [ ] Débranchement pendant utilisation → arrêt propre (SIGTERM llama +
      MCP + uvicorn, purge staging).
- [ ] Rebranchement sur un OS différent (Windows → macOS → Linux) :
      démarrage sans réinstallation ni migration.

### 3. Systèmes de fichiers Linux

- [ ] **exFAT** : `udisksctl mount /dev/sdX1`, lancer `start-linux.sh`.
      Le studio détecte le FS restreint et stage les binaires dans `/tmp`.
      Voir `logs/startup.log` pour le message
      `Filesystem restreint détecté ... Copie vers /tmp...`.
- [ ] **NTFS** (via `ntfs-3g`) : idem, perf disque acceptable.
- [ ] **ext4** : perf optimale, aucun workaround.
- [ ] **FAT32** : idem que exFAT (staging /tmp).

### 4. Backends d'inférence

- [ ] **CPU** (partout) : `/api/health → backend.backend = "cpu"`,
      `reason_code = "no_gpu_detected"`.
- [ ] **NVIDIA CUDA** (Windows/Linux) : `backend.backend = "cuda"`,
      `reason_code = "cuda_found"`, `gpu_layers > 0`.
- [ ] **Apple Metal** (macOS) : `backend = "metal"`,
      `reason_code = "metal_native"`.
- [ ] **AMD ROCm** (Linux) : `backend = "rocm"`,
      `reason_code = "rocm_found"`.
- [ ] **Vulkan** : `backend = "vulkan"`,
      `reason_code = "vulkan_found"`.
- [ ] **Override manuel CPU** via `platform.backend = "cpu"` dans
      `settings.json` : forcé même sur machine GPU.

### 5. Interface web

- [ ] Chargement UI en < 2 s sur navigateur moderne local.
- [ ] Persistance des conversations après F5.
- [ ] Bascule **FR ↔ EN** + F5 : la langue est conservée (test anti-FOUT).
- [ ] Bascule **dark ↔ light** + F5 : le thème est conservé.
- [ ] Éditeur **System Prompt** : activation preset cybersec / accounting /
      legal → effectif dans la conversation suivante.
- [ ] Panneau **Modèles** : bascule d'un `.gguf` vers un autre
      (unload → load propre).
- [ ] Panneau **RAG** : liste documents indexés + bouton Réindexer.
- [ ] **Trace agentique** : dépliable sous chaque réponse ayant utilisé
      des outils, événements horodatés lisibles.
- [ ] Le fichier `ui/assets/i18n/fr.json` **et** `en.json` sont **présents
      dans git** (`git ls-files ui/assets/i18n/` retourne 2 fichiers).

### 6. Skills MCP (invocation directe)

- [ ] `cybersec/analyze_security_logs` : détecte ≥ 1 brute-force SSH dans
      un `auth.log` réel de ≥ 5 tentatives.
- [ ] `cybersec/check_ip_reputation` : `185.220.101.5` (Tor connu) marqué
      `suspicious` ou `malicious` si la blocklist correspondante est
      présente dans `mcp-servers/cybersec/lists/`.
- [ ] `accounting/verify_accounting_entries` : détecte le déséquilibre
      dans le journal `docs/EXAMPLES.md §2.2`.
- [ ] `accounting/calculate_financial_ratios` : produit les 7 ratios
      standards avec formules interprétées.
- [ ] `rag/search_knowledge_base` : retourne le passage attendu pour une
      requête simple sur un corpus de 20 documents indexés.
- [ ] `general/generate_structured_report` : produit un Markdown propre
      avec sections + bullets + tableau.
- [ ] `general/extract_key_info` : identifie IPs, emails, IBAN, montants,
      dates FR+EN dans un texte mélangé.

### 7. Scénarios agentiques (nécessite modèle ≥ 3B pour fiabilité)

- [ ] **Scénario cybersécurité** (`docs/EXAMPLES.md §1`) : logs → détection
      → recommandation.
- [ ] **Scénario comptable** (`docs/EXAMPLES.md §2`) : équilibre → ratios
      → rapport structuré.
- [ ] **Scénario multi-outils** (`docs/EXAMPLES.md §3`) : RAG → extraction
      → rapport final Markdown.

### 8. Sécurité & zero-trace

- [ ] `/api/health` : `backend.backend` + `platform.os` + `components.mcp`
      cohérents.
- [ ] Génération API key à la première utilisation :
      `config/api_key.txt` créé, permissions 600 (POSIX).
- [ ] Auth activée (`security.require_api_key=true`) : requête sans
      `Authorization: Bearer …` → **401**.
- [ ] Auth avec mauvaise clé → **401**.
- [ ] System prompt `locked=true` : `GET /api/system-prompt` → 403,
      `PUT` → 403, override par payload silencieusement ignoré.
- [ ] Logs `startup.log` + `runtime.log` : aucun contenu utilisateur
      loggé (seulement métadonnées, timestamps, PIDs, tailles).
- [ ] Purge du staging à l'arrêt : `/tmp/portableai.*` supprimés
      (Linux/macOS).
- [ ] **Vérification Ed25519** :
      - [ ] `python3 scripts/sign-release.py generate-keypair` → génère
            `keys/private.pem` (0600) + `keys/public.pem`.
      - [ ] `python3 scripts/sign-release.py sign release.json` → génère
            `release.json.sig`.
      - [ ] Copier `keys/public.pem` vers `config/public.pem`.
      - [ ] `security.require_signature=true` + démarrage : `/api/health`
            → warnings vide (signature vérifiée).
      - [ ] Altérer 1 octet de `release.json` → démarrage refusé avec
            message clair « Signature Ed25519 INVALIDE ».
      - [ ] `security.require_signature=false` + manifest non signé :
            démarrage OK avec warning visible.

### 9. Packaging portable

- [ ] `bash scripts/build-portable.sh --target linux-x64 --dry-run` :
      trace toutes les étapes sans exécution.
- [ ] `bash scripts/build-portable.sh --target linux-x64` : produit
      `dist/linux-x64/` avec Python 3.12 + wheels + code + `release.json`.
- [ ] Le `release.json` produit contient un `sha256` par fichier + taille +
      version 1.0.0.
- [ ] Signature du manifest produit :
      `python3 scripts/sign-release.py sign dist/linux-x64/release.json`
      → `.sig` produit, `verify` OK.

---

## Livrable 2 — Key Builder Windows

### 10. Compilation & tests

- [ ] `dotnet test keybuilder/tests/AfricAIsoft.KeyBuilder.Core.Tests` sur
      Linux : **28/28 verts** en < 10 s.
- [ ] `dotnet build keybuilder/src/AfricAIsoft.KeyBuilder.Wpf` sous
      Windows 10/11 : 0 erreur, 0 warning bloquant.
- [ ] `pwsh -File keybuilder/installer/build-portable.ps1` :
      ZIP autonome `AfricAIsoft.KeyBuilder-1.0.0-portable-win-x64.zip` produit.
- [ ] `dotnet build keybuilder/installer/KeyBuilder.wixproj` (WiX v4/v5
      installé) : MSI produit dans `keybuilder/artifacts/` (ou output
      du wixproj).

### 11. Fabrication d'une clé

- [ ] Lancement de `AfricAIsoft.KeyBuilder.exe` : fenêtre WPF s'ouvre en
      français en < 2 s.
- [ ] Détection des clés USB branchées via WMI en < 2 s.
- [ ] Brancher / débrancher une clé pendant que l'app tourne : liste
      rafraîchie en temps réel.
- [ ] Sélection : 1 clé + 1 modèle GGUF valide + 2 skills (ex. accounting,
      rag) + 1 plateforme cible (Windows x64) → estimation taille correcte.
- [ ] Cliquer « Fabriquer la clé » sur une clé de test :
      - [ ] Préflight OK, invite UAC affichée si formatage cochée.
      - [ ] Copie SHA-256 streaming affiche progression fichier par fichier.
      - [ ] Rapport HTML autonome généré à la racine de la clé.
      - [ ] Marker `.africaisoft-key.json` présent avec n° série UUID.
      - [ ] `manifest.json` liste tous les fichiers avec leur SHA-256.

### 12. Modes avancés

- [ ] Refus explicite (message clair, pas de crash) lors d'une tentative
      de formatage ext4 sous Windows.
- [ ] Refus explicite pour un `.gguf` avec magic invalide ou version > 3.
- [ ] Refus explicite si capacité clé < taille estimée.
- [ ] Refus explicite si un skill demandé n'existe pas dans la source.
- [ ] Refus UAC utilisateur → `FormatResult.Success=false` avec message
      « L'utilisateur a refusé l'élévation. ».

### 13. Batch (production en série)

- [ ] Fabriquer une file de 3 clés via l'UI (« Ajouter au batch » ×3, puis
      « Exécuter batch ») : progression séquentielle, résumé final 3/3 OK.
- [ ] Alternative CLI :
      `AfricAIsoft.KeyBuilder.exe --batch=batch-config.example.json` →
      idem, avec log par item.

### 14. Reprise après interruption

- [ ] Débrancher la clé physiquement mi-copie → statut échec dans l'UI.
- [ ] Rebrancher la même clé (n° série identique lu dans
      `.africaisoft-key.json`), cliquer « Fabriquer la clé » à nouveau.
- [ ] Le journal `.keybuilder-journal.json` est lu, les fichiers déjà
      copiés + validés SHA-256 sont **skippés** (log
      « fichier déjà validé, skip »).
- [ ] Le build se termine sans redémarrage complet ni doublon.

---

## Résumé de validation

| Bloc | Items | Cochés | Statut |
|---|---:|---:|---|
| 1. Démarrage OS × modèle    | 5 | ___ / 5 | ⬜ |
| 2. Clé USB physique         | 4 | ___ / 4 | ⬜ |
| 3. FS Linux                 | 4 | ___ / 4 | ⬜ |
| 4. Backends                 | 6 | ___ / 6 | ⬜ |
| 5. UI web                   | 9 | ___ / 9 | ⬜ |
| 6. Skills MCP directs       | 7 | ___ / 7 | ⬜ |
| 7. Scénarios agentiques     | 3 | ___ / 3 | ⬜ |
| 8. Sécurité & zero-trace    | 14| ___ / 14| ⬜ |
| 9. Packaging portable       | 4 | ___ / 4 | ⬜ |
| 10. Key Builder compile     | 4 | ___ / 4 | ⬜ |
| 11. Fabrication clé         | 8 | ___ / 8 | ⬜ |
| 12. Modes avancés Key B.    | 5 | ___ / 5 | ⬜ |
| 13. Batch                   | 2 | ___ / 2 | ⬜ |
| 14. Reprise                 | 4 | ___ / 4 | ⬜ |
| **TOTAL**                   |**79**| **___ / 79** | ⬜ |

Seuil de validation Livrable 1 : bloc 1-9 = 100 %.
Seuil de validation Livrable 2 : bloc 10-14 = 100 %.

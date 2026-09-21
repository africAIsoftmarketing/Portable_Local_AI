# AfricAIsoft Key Builder — Guide utilisateur

## 1. Installation

1. Exécutez `AfricAIsoft.KeyBuilder-Setup.msi` (droits utilisateur suffisants).
2. Le raccourci est créé dans le menu Démarrer.
3. Au premier lancement, l'application détecte automatiquement les clés USB
   branchées et charge la source du studio depuis
   `C:\Program Files\AfricAIsoft\Portable_Local_AI` (modifiable via
   `Options → Source`).

## 2. Fabriquer une clé — vue d'ensemble

```
+---------------------+  +------------------------+  +-----------------+
| Clés USB détectées  |  | Modèle GGUF            |  | Personnalisation|
| ● E:\  ACME  32GB   |  | ● qwen2.5-3b Q4_K_M    |  | Nom volume      |
| ○ F:\  DATA  64GB   |  |                        |  | Version         |
+---------------------+  | Skills MCP             |  | Client ID       |
                         | [x] accounting         |  | Format : exFAT  |
                         | [x] rag                |  | [x] Format      |
                         | [ ] cybersec           |  | [x] Vérif SHA   |
                         | Plateformes cibles     |  | [x] Smoke test  |
                         | [x] Windows x64        |  +-----------------+
                         | [ ] Linux x64          |  | Estimation      |
                         | System Prompt          |  | 3.4 GB / 32 GB  |
                         | [ éditeur multi-ligne] |  | [====        ]  |
                         +------------------------+  +-----------------+
[Fabriquer la clé]  [Ajouter au batch]  [Exécuter batch]
```

## 3. Étapes détaillées

1. **Brancher** la clé USB (elle apparaît en temps réel via WMI).
2. **Sélectionner** la clé dans la colonne de gauche.
3. **Choisir** le modèle GGUF (nom, taille, quantification affichés).
4. **Cocher** les skills MCP à embarquer. Les skills non cochés seront filtrés
   à la copie (dossier `mcp-servers/<skill>` non copié + entrée retirée de
   `config/mcp.json`).
5. **Cocher** les plateformes cibles. Les binaires `bin/<platform>/` sont
   copiés seulement pour les plateformes cochées.
6. **Saisir** un System Prompt personnalisé, ou importer un fichier `.txt/.md`.
7. **Personnaliser** : nom volume, version, ID client. Cocher « Formater » si
   la clé doit être formatée avant copie (élévation UAC demandée par Windows).
8. **Vérifier l'estimation** de taille en bas à droite (met à jour dynamiquement).
9. **Cliquer** « Fabriquer la clé ». La barre de progression indique l'étape
   en cours : préflight → format → copie → vérification SHA-256 → configuration
   → smoke test → rapport.
10. **À la fin**, une boîte affiche le chemin du rapport de production HTML.

## 4. Mode batch (production en série)

1. Configurer une clé comme ci-dessus.
2. Cliquer « Ajouter au batch » au lieu de « Fabriquer ».
3. Répéter pour chaque clé (les clés physiques peuvent être différentes).
4. Cliquer « Exécuter batch » — les items sont traités séquentiellement.
5. À la fin, un rapport agrégé affiche le nombre de succès/échecs.

Alternative : `AfricAIsoft.KeyBuilder.exe --batch=batch-config.json` en CLI
(voir `batch-config.example.json`).

## 5. Reprise après interruption

Si le build est interrompu (coupure, débranchement…), il suffit de :
- Rebrancher la même clé (SN identique lu dans `.africaisoft-key.json`).
- Cliquer « Fabriquer la clé ».
L'orchestrateur lit `.keybuilder-journal.json` et saute les fichiers déjà
copiés + validés SHA-256. Aucun redémarrage complet nécessaire.

## 6. Rapport de production

Chaque clé produite reçoit à la racine :
- `manifest.json` — liste des fichiers + SHA-256.
- `.africaisoft-key.json` — n° série UUID + métadonnées.
- `production-report.html` — rapport auto-suffisant (aucun asset externe).

Le rapport inclut : date de production, opérateur, client, modèle + hash,
plateformes, skills, hash du system prompt, résultat d'intégrité, n° de série.

## 7. Test de démarrage rapide

Si l'option est cochée, après la copie l'application lance
`start-windows.bat` dans un process ; elle attend jusqu'à **120 s** que le
port de l'API réponde. Si le modèle prend plus de temps à charger (7B+),
l'outcome sera `TIMEOUT` — ce n'est PAS un échec du build, uniquement
l'indication que le smoke test n'a pas confirmé le démarrage.

## 8. Dépannage

| Symptôme                              | Cause probable                         |
|---------------------------------------|----------------------------------------|
| Aucune clé détectée                   | Service WMI arrêté / clé non montée   |
| Formatage échoue avec UAC refusé      | L'utilisateur a refusé l'élévation    |
| ext4 non supporté                     | Windows n'offre pas ext4 nativement   |
| Modèle .gguf invalide                 | Fichier tronqué ou non-GGUF           |
| Espace insuffisant                    | Capacité clé < taille estimée         |
| Smoke test TIMEOUT                    | Modèle 7B+ : normal, non bloquant     |

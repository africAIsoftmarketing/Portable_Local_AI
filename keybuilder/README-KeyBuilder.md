# AfricAIsoft Key Builder — Guide opérateur

**Version** : 0.1.0 · **Plateforme** : Windows 10/11 x64 · **Licence** : MIT

Le Key Builder est l'outil de fabrication de clés USB portables AfricAIsoft
Portable Studio. Il n'a besoin ni de Python, ni du runtime .NET côté opérateur
(l'exécutable embarque tout), ni d'accès internet au moment de la fabrication.

## 1. Ce que vous recevez

L'archive `AfricAIsoft-KeyBuilder-vX.Y.Z-win64.zip` contient :

| Fichier                         | Rôle                                                              |
|---------------------------------|-------------------------------------------------------------------|
| `AfricAIsoft-KeyBuilder.exe`    | Exécutable Windows self-contained, single-file (LZ compressé).    |
| `README-KeyBuilder.md`          | Ce guide.                                                         |
| `config/settings.example.json`  | Modèle de configuration (uniquement si présent).                  |
| `logs/`                         | Dossier vide — l'application y écrira le journal de fabrication.  |

## 2. Ce dont vous avez besoin (avant de lancer)

1. **Master copy AfricAIsoft Portable Studio décompressée** sur votre disque
   (dossier extrait de `AfricAIsoft-Portable-vX.Y.Z-<plat>.zip`, obtenu depuis
   la release GitHub officielle). Le Key Builder **ne télécharge rien** :
   c'est vous qui pointez le dossier local.
2. **Clé USB** (32 Go minimum recommandé, formatée exFAT — le Key Builder
   peut reformater si vous l'autorisez).
3. **Droits administrateur** Windows (nécessaires pour formater / écrire un
   marqueur système). L'application déclenche UAC au démarrage.

## 3. Fabrication pas-à-pas

1. Double-cliquez sur `AfricAIsoft-KeyBuilder.exe`.
2. **Étape 1 — Master copy** : deux façons de charger la release.
   - « Parcourir... » : sélectionnez le dossier de la master copy
     décompressée. Si vous choisissez le dossier *parent* de l'extraction,
     la racine `AfricAIsoft-Portable-vX.Y.Z/` est détectée automatiquement.
   - « Ouvrir une archive .zip... » : sélectionnez directement l'archive de
     la release ; elle est extraite dans
     `%LOCALAPPDATA%\AfricAIsoft\KeyBuilder\MasterCopies\` puis chargée.

   Les modèles GGUF et skills MCP sont rechargés, les plateformes absentes
   de la release sont grisées, et la dernière master copy est mémorisée
   pour le prochain lancement. La release n'embarquant **aucun modèle**,
   utilisez « Importer un modèle... » pour copier un `.gguf` dans son
   dossier `models/`. Le Key Builder valide la structure attendue :
   - Dossiers obligatoires : `config/`, `mcp-servers/`, `bin/`, `ui/`, `scripts/`.
   - Au moins un lanceur : `start-windows.bat`, `start-linux.sh` ou `start-mac.command`.
   - Plateformes reconnues sous `bin/` (nommage verrouillé) :
     `windows/`, `darwin-arm64/`, `darwin-x86_64/`, `linux-x86_64/`, `linux-aarch64/`.
   - Backends valides par plateforme : `cpu` partout, `cuda`/`vulkan` sous
     Windows et Linux x86_64, `metal` sous Darwin, `python/wheels/` embarqué.
   - Vérification `CHECKSUMS.sha256` (si présent à la racine) : chaque
     fichier listé est re-hashé en streaming (mémoire O(1) même pour 20 GB).
     Un rapport détaillé s'affiche en cas d'écart.
3. **Étape 2 — Backends à embarquer** : cochez les combinaisons
   `<plateforme>/<backend>` que vous voulez copier sur la clé (utile pour
   réduire la taille : par exemple ne garder que `windows/cuda` +
   `linux-x86_64/cuda` pour une clé destinée à des postes NVIDIA).
4. **Étape 3 — Clé USB** : sélectionnez la clé dans la liste (les lecteurs
   internes ne sont **jamais** proposés).
5. **Étape 4 — Options** : formatage exFAT (recommandé), écriture du
   marqueur `.africaisoft-portable` pour identifier la clé au prochain
   branchement, injection du system prompt éventuel.
6. Cliquez « Lancer ». Une barre de progression détaillée s'affiche
   (fichier courant, débit MB/s, ETA). En cas de retrait accidentel de la
   clé, le journal `logs/build-<horodatage>.json` permet la reprise au
   redémarrage.

## 4. Vérifications post-fabrication

Le Key Builder produit à la racine de la clé :

- `CHECKSUMS.sha256` (nouvelle empreinte, propre à la clé).
- `logs/build-<horodatage>.json` : détail des fichiers copiés, hash, temps,
  éventuels warnings.
- `report.md` : résumé lisible humain de la session (à joindre au support).

Vérification manuelle en console (facultative) :

```powershell
cd D:\
sha256sum -c CHECKSUMS.sha256   # gitforwindows fournit sha256sum
```

## 5. Sécurité et vie privée

- **Aucun réseau** : le Key Builder ne contacte aucun serveur externe.
  Vous pouvez le lancer sur un poste air-gapped sans problème.
- **Aucun secret embarqué** : `config/settings.json` opérateur n'est
  **jamais** livré dans l'exécutable ni dans l'archive.
- **Vérification supply-chain** : la master copy peut être signée
  Ed25519 (`release.json.sig` + `config/public.pem`). Le Key Builder
  affiche un avertissement si la signature manque ou est invalide.

## 6. Dépannage rapide

| Symptôme                                                         | Cause probable                                        | Résolution                                                         |
|------------------------------------------------------------------|-------------------------------------------------------|--------------------------------------------------------------------|
| « Master copy invalide : dossier `bin/` manquant »               | Archive extraite partiellement                        | Ré-extraire l'archive complète.                                    |
| « CHECKSUMS.sha256 : 3 divergences »                             | Archive corrompue au téléchargement                   | Re-télécharger et re-vérifier depuis la release GitHub.            |
| L'UAC ne s'affiche pas                                           | Politique de groupe                                   | Lancer via clic droit → « Exécuter en tant qu'administrateur ».    |
| L'exe met > 10 s à démarrer                                      | Extraction single-file au premier lancement           | Comportement normal — les lancements suivants sont instantanés.    |
| SmartScreen « Éditeur inconnu »                                  | Exécutable non signé Authenticode (à venir en v1.x)   | Cliquez « Informations complémentaires » → « Exécuter quand même ». |

## 7. Support

- Journal complet : `logs/build-<horodatage>.json` (joindre au ticket).
- Version du Key Builder : menu « ? » → « À propos ».
- Documentation projet : voir `docs/USER-GUIDE.md` et `docs/TECHNICAL.md`
  dans le dépôt AfricAIsoft.

---
*AfricAIsoft — MIT — Fabriqué avec .NET 8 WPF, publication self-contained.*

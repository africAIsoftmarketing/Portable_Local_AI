# AfricAIsoft Key Builder — Documentation technique

## 1. Architecture 2-projets

```
+--------------------------------------------------+
|          AfricAIsoft.KeyBuilder.Wpf              |
|  (net8.0-windows, UseWPF, System.Management)     |
|                                                  |
|  - App.xaml / MainWindow.xaml (MVVM + CTK.Mvvm)  |
|  - Providers/                                    |
|      WmiUsbDriveProvider   (WMI, IUsbDriveProv.) |
|      DiskPartFormatter     (UAC runas)           |
|      WindowsSystemInfo                           |
|  - Properties/Resources.{fr-FR,en-US}.resx       |
+----------------------|---------------------------+
                       | ProjectReference
                       v
+--------------------------------------------------+
|         AfricAIsoft.KeyBuilder.Core              |
|  (net8.0, AUCUNE API Windows — testable Linux)   |
|                                                  |
|  Abstractions/                                   |
|    IUsbDriveProvider    IDriveFormatter          |
|    ISystemInfo          IFileSystem              |
|    IProgressReporter                             |
|                                                  |
|  Models/                                         |
|    UsbDrive  ModelGgufInfo  BuildPlan            |
|    BuildManifest  BuildJournal  BuildResult      |
|    PreflightReport  SkillPack  TargetPlatform    |
|                                                  |
|  Services/                                       |
|    ChecksumService (SHA-256 streaming)           |
|    SizeEstimator                                 |
|    GgufValidator (magic GGUF + version 1..3)     |
|    PreflightValidator                            |
|    ResumeJournal                                 |
|    SkillFilter (+ patch mcp.json)                |
|    SystemPromptInjector                          |
|    SettingsPatcher                               |
|    ManifestBuilder                               |
|    MarkerFileWriter                              |
|    ReportGenerator (HTML autonome)               |
|    BatchQueue                                    |
|    UsbBuildOrchestrator (chef d'orchestre)       |
+--------------------------------------------------+
```

## 2. Séquence d'un build (orchestrateur)

```
Preflight ──► Copy ──► Configure ──► Verify ──► SmokeTest? ──► Report
    |            |          |           |             |           |
    v            v          v           v             v           v
  Journal     Journal    Journal    Journal        Journal     Journal
```

Chaque étape met à jour `.keybuilder-journal.json` (via `ResumeJournal`).
La reprise consulte le journal pour skipper les fichiers déjà validés.

## 3. API internes clés

### `UsbBuildOrchestrator.RunAsync(BuildPlan, UsbDrive?, IProgressReporter, Func<TargetPlatform, Task<string?>>?, CancellationToken) : Task<BuildResult>`

Chef d'orchestre. Enchaîne toutes les étapes et retourne un `BuildResult`
complet (fichiers copiés/vérifiés/échoués, chemin du rapport HTML, outcome
du smoke test optionnel).

### `PreflightValidator.Validate(BuildPlan, UsbDrive?) : PreflightReport`

Vérifie :
- Présence des dossiers source obligatoires (`app`, `ui`, `config`, `scripts`,
  `mcp-servers`, `models`).
- Fichier `.gguf` : existence + magic GGUF + version 1..3.
- Au moins une plateforme cible + binaires correspondants présents.
- Skills demandés existent réellement.
- Système de fichiers demandé supporté par l'OS (via `IDriveFormatter`).
- Capacité clé ≥ taille estimée par `SizeEstimator`.

### `SkillFilter.Compute(...) / PatchMcpConfig(...)`

- `Compute` : détermine quels dossiers `mcp-servers/*` copier (skills inclus
  + `_shared` + `_template`) et lesquels ignorer.
- `PatchMcpConfig` : réécrit `config/mcp.json` sur la clé pour ne conserver
  que les serveurs inclus. Conserve les autres clés (`timeout`, etc.).

### `ChecksumService`

- `ComputeFileAsync` : SHA-256 en streaming (buffer 1 MB) — mémoire O(1)
  même pour un modèle 20 GB.
- `ComputeString` : SHA-256 d'une chaîne UTF-8 (utilisé pour le hash du
  system prompt).

## 4. Élévation UAC

- Le manifest applicatif est en `asInvoker`. L'application démarre en
  utilisateur standard, sans invite UAC.
- Le formatage est la SEULE étape nécessitant l'élévation. `DiskPartFormatter`
  relance `diskpart` avec `Verb = "runas"` — Windows affiche alors l'invite
  UAC. Si l'utilisateur refuse, `FormatResult.Success = false` avec message
  explicite.
- Toute la copie / vérification / configuration s'exécute sous compte standard.

## 5. Détection USB temps réel

`WmiUsbDriveProvider` utilise `ManagementEventWatcher` sur
`Win32_DeviceChangeEvent` (EventType 2/3) et énumère à la demande via
`Win32_DiskDrive` filtré sur `InterfaceType='USB'`. Chaque événement rafraîchit
la collection `Drives` du ViewModel via `Dispatcher.Invoke`.

## 6. Rapport HTML

`ReportGenerator` produit un HTML autonome (CSS inline, aucun asset externe).
Sections : Identification, Contenu embarqué, Résultats, Manifest (top 50).
Palette identique à celle de l'UI web du studio (sable/vert forêt).

## 7. Tests xUnit (portables Linux/Windows/macOS)

- `InMemoryFileSystem` : implémentation d'`IFileSystem` en mémoire pour
  isoler les tests des I/O réelles.
- 25+ tests couvrant : checksums (vecteurs RFC), GGUF (magic + versions),
  estimation, préflight (happy + edge cases), journal, filter skills,
  settings patcher, prompt injector, report, batch queue, orchestrateur
  complet (E2E in-memory : full run + reprise).

## 8. Extensions futures (non implémentées)

- Format ext4 sous Windows : nécessite un binaire tiers (WSL2 mount, e2fsprogs
  natif). Pour l'instant, message clair « non supporté, utilisez une clé
  pré-formatée en ext4 depuis Linux ».
- Signature Authenticode du .exe (achat cert code-signing hors périmètre).
- Publication en `PublishSingleFile` + `PublishTrimmed` pour un livrable
  compact — ajouter au `.wixproj` selon besoin client.

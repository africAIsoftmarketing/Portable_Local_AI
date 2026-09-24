// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : extension partial du MainViewModel — chargement de la release
//           « master copy » (étape 1 du README) et exposition des services
//           Core MasterCopyValidator + SelectiveCopier.
//             • Parcourir… : dossier décompressé (racine auto-détectée si
//               l'opérateur choisit le dossier parent de l'extraction) ;
//             • Ouvrir une archive .zip… : extraction sécurisée dans
//               %LOCALAPPDATA%\AfricAIsoft\KeyBuilder\MasterCopies puis
//               chargement ;
//             • rechargement des modèles / skills à chaque changement de
//               racine, validation en arrière-plan (annulable) et rapport ;
//             • plateformes absentes de la release désactivées dans l'UI ;
//             • import d'un modèle .gguf dans models/ (la release n'en
//               contient aucun) ;
//             • dernière master copy mémorisée entre deux lancements.
//           Fichier séparé (partial) : les autres fichiers du ViewModel ne
//           sont modifiés qu'à la marge (voir MainViewModel.cs).
// Version : 0.6.0 (2026-09-24) — chargement de la release master copy.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AfricAIsoft.KeyBuilder.Wpf.ViewModels;

public partial class MainViewModel
{
    // ─── Services Core (initialisés paresseusement, réutilisent _fs) ───────
    private MasterCopyValidator? _mcValidator;
    private SelectiveCopier?     _selectiveCopier;
    private MasterCopyLocator?   _mcLocator;

    private MasterCopyValidator McValidator =>
        _mcValidator ??= new MasterCopyValidator(_fs, new ChecksumService(_fs));
    private SelectiveCopier SelectiveCopier =>
        _selectiveCopier ??= new SelectiveCopier(_fs);
    private MasterCopyLocator McLocator =>
        _mcLocator ??= new MasterCopyLocator(_fs);

    // ─── État interne du chargement ────────────────────────────────────────
    /// <summary>false tant que le constructeur n'a pas fini : évite de
    /// recharger / valider pendant l'initialisation.</summary>
    private bool _sourcesLoaded;
    private bool _restoredFromSettings;
    private CancellationTokenSource? _mcCts;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AfricAIsoft", "KeyBuilder");
    private static readonly string UserSettingsPath =
        Path.Combine(AppDataDir, "keybuilder.user.json");
    private static readonly string ExtractedCopiesDir =
        Path.Combine(AppDataDir, "MasterCopies");

    // ─── Propriétés bindables (flux master copy) ───────────────────────────
    [ObservableProperty] private string  _masterCopyValidationSummary = "";
    [ObservableProperty] private bool    _masterCopyIsValid;
    [ObservableProperty] private bool    _masterCopyChecksumsVerified;
    [ObservableProperty] private string  _selectiveCopyDestination = "";
    [ObservableProperty] private int     _selectiveCopyFilesCount;
    /// <summary>Validation en cours (structure et/ou SHA-256).</summary>
    [ObservableProperty] private bool    _isMasterCopyValidating;
    /// <summary>Extraction d'archive ou import de modèle en cours.</summary>
    [ObservableProperty] private bool    _isMasterCopyBusy;
    /// <summary>Résumé lisible des plateformes/backends détectés.</summary>
    [ObservableProperty] private string  _masterCopyDetectedText = "";

    /// <summary>Combinaisons plateforme/backend disponibles à cocher dans l'UI.</summary>
    public ObservableCollection<PlatformBackendVm> AvailablePlatformBackends { get; } = new();

    /// <summary>Issues du dernier `ValidateMasterCopyAsync`, pour affichage détaillé.</summary>
    public ObservableCollection<ValidationIssue> MasterCopyIssues { get; } = new();

    // ─── Cycle de vie ──────────────────────────────────────────────────────

    /// <summary>
    /// Appelé par le générateur CommunityToolkit à chaque changement de
    /// SourceRoot (Parcourir, archive, saisie manuelle du chemin).
    /// </summary>
    partial void OnSourceRootChanged(string value)
    {
        if (!_sourcesLoaded) return;
        ReloadFromMasterCopy();
    }

    /// <summary>Recharge modèles/skills, mémorise la racine, relance la validation.</summary>
    private void ReloadFromMasterCopy()
    {
        try
        {
            LoadSources();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Lecture du dossier impossible : " + ex.Message;
        }
        if (McLocator.LooksLikeRoot(SourceRoot))
            SaveLastMasterCopy(SourceRoot);
        _ = ValidateMasterCopyAsync();
    }

    /// <summary>Applique une nouvelle racine (après résolution du dossier parent).</summary>
    private void SetMasterCopyRoot(string selected)
    {
        var resolved = McLocator.Resolve(selected);
        if (string.Equals(resolved, SourceRoot, StringComparison.OrdinalIgnoreCase))
            ReloadFromMasterCopy();          // même dossier : forcer le rechargement
        else
            SourceRoot = resolved;           // → OnSourceRootChanged
        if (!string.Equals(resolved, selected, StringComparison.OrdinalIgnoreCase))
            StatusText = $"Racine master copy détectée : {resolved}";
    }

    /// <summary>
    /// Appelé par le constructeur AVANT LoadSources : restaure la dernière
    /// master copy si elle existe encore. Sinon, comportement historique
    /// (dossier de l'exécutable).
    /// </summary>
    private void RestoreLastMasterCopy()
    {
        try
        {
            if (!File.Exists(UserSettingsPath)) return;
            var s = JsonSerializer.Deserialize<KeyBuilderUserSettings>(
                File.ReadAllText(UserSettingsPath));
            var p = s?.LastMasterCopyPath;
            if (!string.IsNullOrWhiteSpace(p) && McLocator.LooksLikeRoot(p))
            {
                SourceRoot = p;              // _sourcesLoaded == false : pas de rechargement
                _restoredFromSettings = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or JsonException)
        {
            // Préférences illisibles : on ignore, sans bloquer le démarrage.
        }
    }

    /// <summary>Appelé par le constructeur APRÈS LoadSources.</summary>
    private void CompleteMasterCopyInitialization()
    {
        _sourcesLoaded = true;
        if (_restoredFromSettings)
            _ = ValidateMasterCopyAsync();
    }

    private static void SaveLastMasterCopy(string path)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(UserSettingsPath, JsonSerializer.Serialize(
                new KeyBuilderUserSettings(path),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non bloquant : la mémorisation n'est qu'un confort.
        }
    }

    // ─── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Boîte de dialogue de sélection du dossier master copy décompressée.</summary>
    [RelayCommand]
    private void BrowseMasterCopyFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Sélectionnez le dossier master copy décompressée",
        };
        if (Directory.Exists(SourceRoot))
            dlg.InitialDirectory = SourceRoot;
        if (dlg.ShowDialog() == true)
            SetMasterCopyRoot(dlg.FolderName);
    }

    /// <summary>
    /// Ouvre une archive .zip de release, l'extrait (protection « zip slip »,
    /// contrôle d'espace disque) puis la charge comme master copy.
    /// </summary>
    [RelayCommand]
    private async Task OpenMasterCopyArchiveAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Sélectionnez l'archive de la release master copy",
            Filter = "Archive master copy (*.zip)|*.zip",
        };
        if (dlg.ShowDialog() != true) return;

        var zip  = dlg.FileName;
        var dest = Path.Combine(ExtractedCopiesDir, Path.GetFileNameWithoutExtension(zip));

        if (Directory.Exists(dest))
        {
            var existing = McLocator.Resolve(dest);
            if (McLocator.LooksLikeRoot(existing))
            {
                var answer = MessageBox.Show(
                    $"Cette archive a déjà été extraite :\n{existing}\n\n"
                    + "Oui : réutiliser l'extraction existante.\n"
                    + "Non : extraire à nouveau (remplace ce dossier).",
                    "Master copy déjà extraite",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) { SetMasterCopyRoot(existing); return; }
            }
        }

        IsMasterCopyBusy = true;
        StatusText = "Extraction de l'archive master copy…";
        var progress = new Progress<double>(p =>
        {
            OverallProgress = p;
            CurrentStepText = $"Extraction… {p:0} %";
        });
        try
        {
            await Task.Run(() => ExtractArchive(zip, dest, progress));
            StatusText = "Archive extraite : " + dest;
            SetMasterCopyRoot(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or InvalidDataException)
        {
            StatusText = "Extraction impossible : " + ex.Message;
        }
        finally
        {
            IsMasterCopyBusy = false;
            OverallProgress = 0;
            CurrentStepText = "";
        }
    }

    /// <summary>
    /// Extraction entrée par entrée vers un dossier temporaire, puis bascule
    /// atomique vers <paramref name="dest"/>. Toute entrée qui sortirait du
    /// dossier cible (« ../ », chemin absolu) est refusée.
    /// </summary>
    private static void ExtractArchive(string zipPath, string dest, IProgress<double> progress)
    {
        Directory.CreateDirectory(ExtractedCopiesDir);
        var tmp = dest + ".extracting";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            long total = archive.Entries.Sum(e => e.Length);
            var root = Path.GetPathRoot(Path.GetFullPath(ExtractedCopiesDir));
            if (!string.IsNullOrEmpty(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < total)
                    throw new IOException(
                        $"Espace disque insuffisant : {total / 1_048_576} Mo requis, "
                        + $"{free / 1_048_576} Mo disponibles sur {root}.");
            }

            var baseDir = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            long done = 0;
            int lastPct = -1;
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(tmp, entry.FullName));
                if (!target.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(
                        $"Entrée d'archive refusée (hors du dossier cible) : {entry.FullName}");

                if (string.IsNullOrEmpty(entry.Name))       // entrée « dossier »
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);

                done += entry.Length;
                var pct = total > 0 ? (int)(done * 100 / total) : 100;
                if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
            }
        }

        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        Directory.Move(tmp, dest);
    }

    /// <summary>
    /// Valide la structure et (si l'option SHA-256 est cochée) CHECKSUMS.sha256.
    /// Exécutée hors du thread UI ; une validation plus récente annule la
    /// précédente (changement de dossier pendant un hachage de plusieurs Go).
    /// </summary>
    [RelayCommand]
    private async Task ValidateMasterCopyAsync()
    {
        _mcCts?.Cancel();
        var cts = new CancellationTokenSource();
        _mcCts = cts;

        var root      = SourceRoot;
        var verify    = VerifyChecksums;
        var validator = McValidator;

        IsMasterCopyValidating = true;
        MasterCopyIsValid = false;
        MasterCopyChecksumsVerified = false;
        MasterCopyIssues.Clear();
        AvailablePlatformBackends.Clear();
        MasterCopyDetectedText = "";
        MasterCopyValidationSummary = verify
            ? "Validation master copy en cours (structure + SHA-256)…"
            : "Validation master copy en cours (structure)…";
        StatusText = MasterCopyValidationSummary;
        try
        {
            var res = await Task.Run(
                () => validator.ValidateAsync(root, verify, cts.Token), cts.Token);
            if (!ReferenceEquals(_mcCts, cts)) return;   // résultat périmé

            MasterCopyIsValid = res.IsValid;
            MasterCopyChecksumsVerified = res.ChecksumsVerified;
            foreach (var i in res.Issues) MasterCopyIssues.Add(i);
            foreach (var plat in res.DetectedPlatforms)
            foreach (var be in res.DetectedBackends[plat])
                AvailablePlatformBackends.Add(new PlatformBackendVm(
                    new PlatformBackend(plat, be), isSelected: be == "cpu"));
            ApplyPlatformAvailability(res.DetectedPlatforms);
            MasterCopyDetectedText = string.Join(" · ", res.DetectedPlatforms.Select(p =>
                $"{p} ({string.Join(", ", res.DetectedBackends[p])})"));

            var errs = res.Errors.Count();
            var warns = res.Warnings.Count();
            MasterCopyValidationSummary = res.IsValid
                ? $"OK — {res.DetectedPlatforms.Count} plateforme(s), "
                  + (res.ChecksumsFilePresent
                     ? (res.ChecksumsVerified ? "CHECKSUMS vérifiés."
                        : verify ? "CHECKSUMS INVALIDES." : "CHECKSUMS non vérifiés (option SHA-256 décochée).")
                     : "CHECKSUMS absent.")
                  + $" {warns} avertissement(s)."
                  + (Models.Count == 0 ? " Aucun modèle GGUF : utilisez « Importer un modèle… »." : "")
                : $"INVALIDE — {errs} erreur(s), {warns} avertissement(s).";
            StatusText = MasterCopyValidationSummary;
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une validation plus récente : rien à afficher.
        }
        catch (System.Exception ex)
        {
            if (!ReferenceEquals(_mcCts, cts)) return;
            MasterCopyIsValid = false;
            MasterCopyValidationSummary = "Erreur : " + ex.Message;
            StatusText = MasterCopyValidationSummary;
        }
        finally
        {
            if (ReferenceEquals(_mcCts, cts)) IsMasterCopyValidating = false;
        }
    }

    /// <summary>
    /// Désactive les plateformes absentes de bin/ (et les décoche), puis
    /// garantit au moins une plateforme cochée parmi celles disponibles.
    /// Sans information exploitable (bin/ vide ou absent), toutes les
    /// plateformes restent actives : comportement historique.
    /// </summary>
    private void ApplyPlatformAvailability(IReadOnlyList<string> detected)
    {
        if (detected.Count == 0)
        {
            foreach (var p in Platforms) p.IsAvailable = true;
            return;
        }
        foreach (var p in Platforms)
        {
            p.IsAvailable = detected.Contains(p.BinaryDirName, StringComparer.Ordinal);
            if (!p.IsAvailable) p.IsSelected = false;
        }
        if (!Platforms.Any(p => p.IsSelected))
        {
            var fallback = Platforms.FirstOrDefault(p => p.IsAvailable && p.Value == TargetPlatform.WindowsX64)
                        ?? Platforms.FirstOrDefault(p => p.IsAvailable);
            if (fallback is not null) fallback.IsSelected = true;
        }
    }

    /// <summary>
    /// Copie un .gguf choisi par l'opérateur dans models/ de la master copy
    /// chargée (le Core lit le modèle à SourceRoot/models/&lt;fichier&gt;).
    /// En-tête GGUF contrôlé avant copie ; copie vers un fichier .partial
    /// renommé à la fin (jamais de modèle tronqué visible).
    /// </summary>
    [RelayCommand]
    private async Task ImportGgufModelAsync()
    {
        if (!McLocator.LooksLikeRoot(SourceRoot))
        {
            StatusText = "Chargez d'abord une master copy (Étape 1) avant d'importer un modèle.";
            return;
        }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Sélectionnez un modèle GGUF",
            Filter = "Modèle GGUF (*.gguf)|*.gguf",
        };
        if (dlg.ShowDialog() != true) return;

        var src  = dlg.FileName;
        var name = Path.GetFileName(src);
        var info = new GgufValidator(_fs).Inspect(src);
        if (!info.ValidHeader)
        {
            MessageBox.Show($"« {name} » n'est pas un fichier GGUF valide (en-tête incorrect).",
                            "Modèle refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modelsDir = Path.Combine(SourceRoot, "models");
        var dest      = Path.Combine(modelsDir, name);
        IsMasterCopyBusy = true;
        try
        {
            Directory.CreateDirectory(modelsDir);
            var sameFile = string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest),
                                         StringComparison.OrdinalIgnoreCase);
            var length = new FileInfo(src).Length;
            var alreadyThere = File.Exists(dest) && new FileInfo(dest).Length == length;
            if (!sameFile && !alreadyThere)
            {
                var free = _fs.GetAvailableFreeSpace(modelsDir);
                if (free < length)
                {
                    StatusText = $"Espace insuffisant pour importer {name} : "
                               + $"{length / 1_048_576} Mo requis, {free / 1_048_576} Mo disponibles.";
                    return;
                }
                StatusText = $"Import du modèle {name}…";
                await CopyWithProgressAsync(src, dest, length);
            }
            LoadSources();
            SelectedModel = Models.FirstOrDefault(m =>
                string.Equals(m.Info.FileName, name, StringComparison.OrdinalIgnoreCase));
            StatusText = $"Modèle prêt : {name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Import du modèle impossible : " + ex.Message;
        }
        finally
        {
            IsMasterCopyBusy = false;
            OverallProgress = 0;
            CurrentStepText = "";
        }
    }

    private async Task CopyWithProgressAsync(string src, string dest, long length)
    {
        const int BufferSize = 4 * 1024 * 1024;
        var partial = dest + ".partial";
        try
        {
            await using (var input = new FileStream(src, FileMode.Open, FileAccess.Read,
                                                    FileShare.Read, BufferSize, useAsync: true))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write,
                                                     FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                long done = 0;
                int read, lastPct = -1;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    var pct = length > 0 ? (int)(done * 100 / length) : 100;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        OverallProgress = pct;
                        CurrentStepText = $"Import du modèle… {pct} %";
                    }
                }
            }
            File.Move(partial, dest, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>Copie sélective vers la clé USB (backends cochés uniquement).</summary>
    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        var drive = SelectedDrive?.Raw;
        if (drive is null) { StatusText = "Sélectionnez une clé USB."; return; }
        if (!MasterCopyIsValid) { StatusText = "Validez d'abord la master copy."; return; }
        var selection = AvailablePlatformBackends
            .Where(p => p.IsSelected)
            .Select(p => p.Value)
            .ToList();
        if (selection.Count == 0)
        {
            StatusText = "Cochez au moins une combinaison plateforme/backend.";
            return;
        }
        StatusText = "Copie sélective en cours…";
        try
        {
            var res = await SelectiveCopier.CopyAsync(
                SourceRoot, drive.RootPath, selection, this, CancellationToken.None);
            SelectiveCopyDestination = res.DestinationRoot;
            SelectiveCopyFilesCount = res.CopiedCount;
            StatusText = $"Copie OK — {res.CopiedCount} fichiers vers {res.DestinationRoot}"
                       + (res.SkippedEntries.Count > 0
                          ? $" ({res.SkippedEntries.Count} entrée(s) ignorée(s))."
                          : ".");
        }
        catch (System.Exception ex)
        {
            StatusText = "Erreur copie : " + ex.Message;
        }
    }
}

/// <summary>ViewModel item pour une combinaison plateforme/backend cochable.</summary>
public sealed partial class PlatformBackendVm : ObservableObject
{
    public PlatformBackend Value { get; }
    public string DisplayName => Value.ToString();
    [ObservableProperty] private bool _isSelected;

    public PlatformBackendVm(PlatformBackend value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }
}

/// <summary>Préférences opérateur persistées dans %LOCALAPPDATA%.</summary>
internal sealed record KeyBuilderUserSettings(string? LastMasterCopyPath);

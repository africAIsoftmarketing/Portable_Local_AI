// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : extension partial du MainViewModel — expose au ViewModel les
//           deux nouveaux services Core (MasterCopyValidator +
//           SelectiveCopier) sous forme de commandes et propriétés bindables.
//           Fichier séparé (partial) pour rester en ajout PUR.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.ObjectModel;
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

    private MasterCopyValidator McValidator =>
        _mcValidator ??= new MasterCopyValidator(_fs, new ChecksumService(_fs));
    private SelectiveCopier SelectiveCopier =>
        _selectiveCopier ??= new SelectiveCopier(_fs);

    // ─── Propriétés bindables (nouveau flux master copy) ───────────────────
    [ObservableProperty] private string  _masterCopyValidationSummary = "";
    [ObservableProperty] private bool    _masterCopyIsValid;
    [ObservableProperty] private bool    _masterCopyChecksumsVerified;
    [ObservableProperty] private string  _selectiveCopyDestination = "";
    [ObservableProperty] private int     _selectiveCopyFilesCount;

    /// <summary>Combinaisons plateforme/backend disponibles à cocher dans l'UI.</summary>
    public ObservableCollection<PlatformBackendVm> AvailablePlatformBackends { get; } = new();

    /// <summary>Issues du dernier `ValidateMasterCopyAsync`, pour affichage détaillé.</summary>
    public ObservableCollection<ValidationIssue> MasterCopyIssues { get; } = new();

    // ─── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Boîte de dialogue de sélection du dossier master copy décompressée.</summary>
    [RelayCommand]
    private void BrowseMasterCopyFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Sélectionnez le dossier master copy décompressée",
        };
        if (dlg.ShowDialog() == true)
            SourceRoot = dlg.FolderName;
    }

    /// <summary>Valide la structure et (si présent) CHECKSUMS.sha256.</summary>
    [RelayCommand]
    private async Task ValidateMasterCopyAsync()
    {
        StatusText = "Validation master copy en cours…";
        MasterCopyIssues.Clear();
        AvailablePlatformBackends.Clear();
        try
        {
            var res = await McValidator.ValidateAsync(SourceRoot, VerifyChecksums,
                                                       CancellationToken.None);
            MasterCopyIsValid = res.IsValid;
            MasterCopyChecksumsVerified = res.ChecksumsVerified;
            foreach (var i in res.Issues) MasterCopyIssues.Add(i);
            foreach (var plat in res.DetectedPlatforms)
            foreach (var be in res.DetectedBackends[plat])
                AvailablePlatformBackends.Add(new PlatformBackendVm(
                    new PlatformBackend(plat, be), isSelected: be == "cpu"));
            var errs = res.Errors.Count();
            var warns = res.Warnings.Count();
            MasterCopyValidationSummary = res.IsValid
                ? $"OK — {res.DetectedPlatforms.Count} plateforme(s), "
                  + (res.ChecksumsFilePresent
                     ? (res.ChecksumsVerified ? "CHECKSUMS vérifiés." : "CHECKSUMS INVALIDES.")
                     : "CHECKSUMS absent.")
                  + $" {warns} avertissement(s)."
                : $"INVALIDE — {errs} erreur(s), {warns} avertissement(s).";
            StatusText = MasterCopyValidationSummary;
        }
        catch (System.Exception ex)
        {
            MasterCopyIsValid = false;
            MasterCopyValidationSummary = "Erreur : " + ex.Message;
            StatusText = MasterCopyValidationSummary;
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

// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : extension partial du MainViewModel — expose l'écran « Knowledge
//           Base » du wizard (à insérer entre Skills et SystemPrompt côté
//           XAML). Fichier séparé (partial) → ajout PUR.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AfricAIsoft.KeyBuilder.Wpf.ViewModels;

public partial class MainViewModel
{
    // ─── Services (initialisés paresseusement) ─────────────────────────────
    private KnowledgeBasePlanner? _kbPlanner;
    private KnowledgeBaseCopier?  _kbCopier;

    public KnowledgeBasePlanner KbPlanner =>
        _kbPlanner ??= new KnowledgeBasePlanner(_fs);
    private KnowledgeBaseCopier KbCopier =>
        _kbCopier ??= new KnowledgeBaseCopier(_fs, new ChecksumService(_fs));

    // ─── Propriétés bindables (nouveau flux Knowledge Base) ─────────────────
    /// <summary>Documents actuellement planifiés (affichés dans la liste UI).</summary>
    public ObservableCollection<KnowledgeItem> KnowledgeItems { get; } = new();
    [ObservableProperty] private long   _knowledgeTotalBytes;
    [ObservableProperty] private int    _knowledgeLargeFileCount;
    [ObservableProperty] private bool   _knowledgeReindexOnFirstLaunch = true;
    [ObservableProperty] private string _knowledgeSummary = "";
    [ObservableProperty] private bool   _knowledgeSkipStep;   // écran optionnel

    // ─── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Ouvre un OpenFileDialog multi-sélection (.pdf/.txt/.md).</summary>
    [RelayCommand]
    private void AddKnowledgeFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title  = "Sélectionnez un ou plusieurs documents",
            Filter = "Documents (*.pdf;*.txt;*.md)|*.pdf;*.txt;*.md",
        };
        if (dlg.ShowDialog() != true) return;
        var added = KbPlanner.AddFiles(dlg.FileNames);
        RefreshFromPlanner();
        StatusText = $"{added} document(s) ajouté(s).";
    }

    /// <summary>Ouvre un OpenFolderDialog (import récursif).</summary>
    [RelayCommand]
    private void AddKnowledgeFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Sélectionnez un dossier à importer (récursif)",
        };
        if (dlg.ShowDialog() != true) return;
        var added = KbPlanner.AddFolder(dlg.FolderName, recursive: true);
        RefreshFromPlanner();
        StatusText = $"{added} document(s) ajouté(s) depuis {dlg.FolderName}.";
    }

    /// <summary>Retire un document précis de la liste.</summary>
    [RelayCommand]
    private void RemoveKnowledgeItem(KnowledgeItem? item)
    {
        if (item is null) return;
        int idx = KnowledgeItems.IndexOf(item);
        if (KbPlanner.RemoveAt(idx))
        {
            KnowledgeItems.Remove(item);
            KnowledgeTotalBytes    = KbPlanner.TotalBytes;
            KnowledgeLargeFileCount = KbPlanner.LargeFiles.Count;
            RefreshSummary();
        }
    }

    /// <summary>Vide la liste (bouton « Tout retirer »).</summary>
    [RelayCommand]
    private void ClearKnowledge()
    {
        KbPlanner.Clear();
        KnowledgeItems.Clear();
        KnowledgeTotalBytes = 0;
        KnowledgeLargeFileCount = 0;
        RefreshSummary();
    }

    /// <summary>
    /// Exécute la copie effective vers la clé (invoqué par le pipeline de
    /// build à l'étape correspondante). writeReindexMarker suit la case
    /// à cocher « Réindexer au premier démarrage ».
    /// </summary>
    public async Task<KnowledgeBaseCopyResult> ExecuteKnowledgeCopyAsync(
        string usbRoot, MissingFileStrategy missing = MissingFileStrategy.Skip,
        CancellationToken ct = default)
        => await KbCopier.CopyAsync(
            KbPlanner, usbRoot, KnowledgeReindexOnFirstLaunch, missing, ct);

    // ─── Helpers ───────────────────────────────────────────────────────────
    private void RefreshFromPlanner()
    {
        KnowledgeItems.Clear();
        foreach (var i in KbPlanner.Items) KnowledgeItems.Add(i);
        KnowledgeTotalBytes    = KbPlanner.TotalBytes;
        KnowledgeLargeFileCount = KbPlanner.LargeFiles.Count;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var mb = KnowledgeTotalBytes / (1024d * 1024d);
        KnowledgeSummary = KnowledgeItems.Count == 0
            ? "Aucun document sélectionné (écran optionnel)."
            : $"{KnowledgeItems.Count} document(s), {mb:F1} Mo"
              + (KnowledgeLargeFileCount > 0
                  ? $"  —  ⚠ {KnowledgeLargeFileCount} fichier(s) > 50 Mo (non bloquant)"
                  : "");
    }
}

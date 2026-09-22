// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : ViewModel principal (MVVM). Orchestre détection USB, plan de build,
//           estimation dynamique, exécution + batch. S'appuie exclusivement sur
//           le Core (aucune API Windows directement).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using AfricAIsoft.KeyBuilder.Wpf.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AfricAIsoft.KeyBuilder.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject, IProgressReporter
{
    private readonly IFileSystem _fs = new DefaultFileSystem();
    private readonly IUsbDriveProvider _drivesProv;
    private readonly IDriveFormatter _formatter;
    private readonly ISystemInfo _sysInfo;
    private readonly UsbBuildOrchestrator _orch;
    private readonly SizeEstimator _estimator;
    private readonly BatchQueue _batch = new();

    public MainViewModel()
    {
        _drivesProv = new WmiUsbDriveProvider();
        _formatter  = new DiskPartFormatter();
        _sysInfo    = new WindowsSystemInfo();

        var checksum = new ChecksumService(_fs);
        _estimator = new SizeEstimator(_fs);
        _orch = new UsbBuildOrchestrator(
            _fs, _estimator, new GgufValidator(_fs),
            new PreflightValidator(_fs, _estimator, new GgufValidator(_fs), _formatter),
            checksum,
            new ResumeJournal(_fs),
            new SkillFilter(_fs),
            new SystemPromptInjector(_fs, checksum),
            new SettingsPatcher(_fs),
            new ManifestBuilder(_fs, checksum),
            new MarkerFileWriter(_fs),
            new ReportGenerator(_fs));

        AvailableFileSystems = new(new[] { FileSystemKind.ExFat, FileSystemKind.Ntfs });
        SelectedFileSystem = FileSystemKind.ExFat;

        _drivesProv.DrivesChanged += (_, e) => RefreshDrivesFrom(e.Current);
        _drivesProv.StartMonitoring();
        RefreshDrives();
        LoadSources();
    }

    // ─── Champs bindés ──────────────────────────────────────────────────────
    [ObservableProperty] private string _sourceRoot = System.AppContext.BaseDirectory;
    [ObservableProperty] private string _statusText = "Prêt.";
    [ObservableProperty] private string _currentStepText = "";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _customSystemPrompt = "";
    [ObservableProperty] private string _volumeLabel = "AFRICAISOFT";
    [ObservableProperty] private string _version = "0.5.0";
    [ObservableProperty] private string _clientId = "";
    [ObservableProperty] private bool _formatBeforeCopy = true;
    [ObservableProperty] private bool _verifyChecksums = true;
    [ObservableProperty] private bool _runSmokeTest = true;
    [ObservableProperty] private FileSystemKind _selectedFileSystem;
    [ObservableProperty] private string _sizeEstimationText = "–";
    [ObservableProperty] private double _sizeFillPercent;

    public ObservableCollection<DriveVm>    Drives    { get; } = new();
    public ObservableCollection<ModelVm>    Models    { get; } = new();
    public ObservableCollection<SkillVm>    Skills    { get; } = new();
    public ObservableCollection<PlatformVm> Platforms { get; } = new();
    public ObservableCollection<FileSystemKind> AvailableFileSystems { get; }
    public ObservableCollection<BatchItem>  Batch     => new(_batch.Items);

    [ObservableProperty] private DriveVm? _selectedDrive;
    [ObservableProperty] private ModelVm? _selectedModel;

    // ─── Commandes ──────────────────────────────────────────────────────────
    [RelayCommand] private void RefreshDrives()
        => RefreshDrivesFrom(_drivesProv.Enumerate());

    [RelayCommand]
    private void ImportPrompt()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Fichiers texte (*.txt;*.md)|*.txt;*.md|Tous|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        CustomSystemPrompt = _fs.ReadAllText(dlg.FileName);
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        var plan = BuildPlanFromUi();
        var drive = SelectedDrive?.Raw;
        if (drive is null) { StatusText = "Sélectionnez une clé USB."; return; }
        try
        {
            // Base de connaissances : planner (potentiellement vide) + option réindexation.
            var res = await _orch.RunAsync(plan, drive, this, null, CancellationToken.None,
                                            KbPlanner, KnowledgeReindexOnFirstLaunch);
            StatusText = res.Success
                ? $"OK — {res.FilesVerified} fichiers vérifiés."
                : $"Échec — {res.Errors.Count} erreur(s).";
            if (res.ReportHtmlPath is not null)
                MessageBox.Show($"Rapport : {res.ReportHtmlPath}", "Rapport de production");
        }
        catch (System.Exception ex) { StatusText = "Erreur : " + ex.Message; }
    }

    [RelayCommand]
    private void AddToBatch()
        => _batch.Enqueue(BuildPlanFromUi(),
            $"{SelectedDrive?.DisplayText ?? "?"} — {SelectedModel?.Info.FileName ?? "?"}");

    [RelayCommand]
    private async Task RunBatchAsync()
    {
        StatusText = "Batch en cours…";
        await _batch.RunAsync((plan, prog, ct) =>
            _orch.RunAsync(plan, SelectedDrive?.Raw, prog, null, ct,
                            KbPlanner, KnowledgeReindexOnFirstLaunch),
            this, CancellationToken.None);
        StatusText = "Batch terminé.";
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private BuildPlan BuildPlanFromUi() => new()
    {
        SourceRoot = SourceRoot,
        TargetRoot = SelectedDrive?.Raw.RootPath ?? "",
        ModelFile = SelectedModel?.Info.FileName ?? "",
        IncludedSkillIds = new(Skills.Where(s => s.IsSelected).Select(s => s.Id)),
        TargetPlatforms = new(Platforms.Where(p => p.IsSelected).Select(p => p.Value)),
        CustomSystemPrompt = string.IsNullOrWhiteSpace(CustomSystemPrompt) ? null : CustomSystemPrompt,
        FormatWith = FormatBeforeCopy ? SelectedFileSystem : null,
        VolumeLabel = VolumeLabel, Version = Version, ClientId = ClientId,
        VerifyChecksums = VerifyChecksums, RunSmokeTest = RunSmokeTest,
        OperatorName = _sysInfo.UserName,
    };

    private void RefreshDrivesFrom(IReadOnlyList<UsbDrive> list)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Drives.Clear();
            foreach (var d in list) Drives.Add(new DriveVm(d));
        });
    }

    private void LoadSources()
    {
        // Modèles GGUF
        var mdir = _fs.CombinePath(SourceRoot, "models");
        if (_fs.DirectoryExists(mdir))
            foreach (var f in _fs.EnumerateFiles(mdir, "*.gguf", false))
                Models.Add(new ModelVm(new GgufValidator(_fs).Inspect(f)));
        // Skills
        var sd = _fs.CombinePath(SourceRoot, "mcp-servers");
        if (_fs.DirectoryExists(sd))
            foreach (var d in _fs.EnumerateDirectories(sd))
            {
                var id = _fs.GetFileName(d);
                if (id.StartsWith("_")) continue;
                Skills.Add(new SkillVm(id, id, true));
            }
        // Plateformes
        foreach (var p in Enum.GetValues<TargetPlatform>())
            Platforms.Add(new PlatformVm(p, p == TargetPlatform.WindowsX64));
    }

    // ─── IProgressReporter ──────────────────────────────────────────────────
    public void ReportStep(string stepId, string localizedLabel)
        => Application.Current?.Dispatcher.Invoke(() => CurrentStepText = localizedLabel);
    public void ReportFile(string relativePath, long bytesDone, long bytesTotal,
                           double bps, TimeSpan? eta) { }
    public void ReportOverall(int done, int total, double percent)
        => Application.Current?.Dispatcher.Invoke(() => OverallProgress = percent);
    public void ReportMessage(string message, ProgressSeverity severity)
        => Application.Current?.Dispatcher.Invoke(() => StatusText = message);
}

public sealed record DriveVm(UsbDrive Raw)
{
    public string DisplayText =>
        $"{Raw.RootPath}  {Raw.VolumeLabel}  {FmtBytes(Raw.CapacityBytes)}  {Raw.FileSystem}";
    private static string FmtBytes(long n)
    {
        string[] u = { "B","KB","MB","GB","TB" }; double v = n; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
public sealed record ModelVm(ModelGgufInfo Info)
{
    public string Description => $"{Info.FileName}  ({Info.ParametersHint} · {Info.Quantization})";
}
public sealed class SkillVm
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsSelected { get; set; }
    public SkillVm(string id, string name, bool selected)
        { Id = id; DisplayName = name; IsSelected = selected; }
}
public sealed class PlatformVm
{
    public TargetPlatform Value { get; }
    public string DisplayName { get; }
    public bool IsSelected { get; set; }
    public PlatformVm(TargetPlatform v, bool sel)
        { Value = v; DisplayName = v.DisplayName(); IsSelected = sel; }
}

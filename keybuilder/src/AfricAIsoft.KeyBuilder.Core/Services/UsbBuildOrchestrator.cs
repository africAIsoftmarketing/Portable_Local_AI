// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : orchestrateur global du build. Enchaîne : preflight → format →
//           copie sélective (skill filter) → checksums post-copie → injection
//           system prompt → patch settings → marker → manifest → rapport HTML
//           → smoke test optionnel. À chaque étape, met à jour le journal
//           pour permettre la reprise.
//           Toutes les opérations passent par IFileSystem/IProgressReporter,
//           donc entièrement testable.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class UsbBuildOrchestrator
{
    private readonly IFileSystem _fs;
    private readonly SizeEstimator _estimator;
    private readonly GgufValidator _gguf;
    private readonly PreflightValidator _preflight;
    private readonly ChecksumService _checksum;
    private readonly ResumeJournal _journal;
    private readonly SkillFilter _skillFilter;
    private readonly SystemPromptInjector _promptInjector;
    private readonly SettingsPatcher _settingsPatcher;
    private readonly ManifestBuilder _manifestBuilder;
    private readonly MarkerFileWriter _markerWriter;
    private readonly ReportGenerator _reporter;

    public UsbBuildOrchestrator(
        IFileSystem fs,
        SizeEstimator estimator,
        GgufValidator gguf,
        PreflightValidator preflight,
        ChecksumService checksum,
        ResumeJournal journal,
        SkillFilter skillFilter,
        SystemPromptInjector promptInjector,
        SettingsPatcher settingsPatcher,
        ManifestBuilder manifestBuilder,
        MarkerFileWriter markerWriter,
        ReportGenerator reporter)
    {
        _fs = fs;
        _estimator = estimator;
        _gguf = gguf;
        _preflight = preflight;
        _checksum = checksum;
        _journal = journal;
        _skillFilter = skillFilter;
        _promptInjector = promptInjector;
        _settingsPatcher = settingsPatcher;
        _manifestBuilder = manifestBuilder;
        _markerWriter = markerWriter;
        _reporter = reporter;
    }

    public async Task<BuildResult> RunAsync(
        BuildPlan plan,
        UsbDrive? targetDrive,
        IProgressReporter progress,
        Func<TargetPlatform, Task<string?>>? runSmokeTest,
        CancellationToken ct,
        KnowledgeBasePlanner? knowledgeBase = null,
        bool reindexOnFirstLaunch = true)
    {
        var result = new BuildResult
        {
            SerialNumber = plan.SerialNumber,
            StartedAtUtc = DateTime.UtcNow,
        };
        var journal = _journal.Load(plan.TargetRoot);
        if (journal.SerialNumber == "")
        {
            journal.SerialNumber = plan.SerialNumber;
            journal.StartedAtUtc = DateTime.UtcNow;
        }

        // 1. Preflight.
        progress.ReportStep("preflight", "Vérifications pré-vol");
        var pre = _preflight.Validate(plan, targetDrive);
        foreach (var i in pre.Issues.Where(x => x.Severity == PreflightSeverity.Warning))
            result.Warnings.Add($"[{i.Code}] {i.Message}");
        if (!pre.CanProceed)
        {
            result.Errors.AddRange(pre.Issues
                .Where(i => i.Severity == PreflightSeverity.Error)
                .Select(i => $"[{i.Code}] {i.Message}"));
            result.EndedAtUtc = DateTime.UtcNow;
            return result;
        }
        journal.LastCompletedStage = BuildStage.Preflight;
        _journal.Save(plan.TargetRoot, journal);

        // 1b. Preflight de la base de connaissances (marge +10 %). Bloquant
        //     si l'espace résiduel après le contenu cœur ne suffit pas.
        if (knowledgeBase is not null && knowledgeBase.Items.Count > 0 && targetDrive is not null)
        {
            var remaining = Math.Max(0, targetDrive.FreeSpaceBytes - pre.RequiredBytes);
            var kbPre = knowledgeBase.Preflight(remaining);
            if (!kbPre.EnoughSpace)
            {
                result.Errors.Add($"[KB_SPACE] Espace insuffisant pour la base de connaissances : "
                    + $"{kbPre.RequiredBytesWithMargin:N0} o requis vs {kbPre.AvailableBytes:N0} o disponibles.");
                result.EndedAtUtc = DateTime.UtcNow;
                return result;
            }
            foreach (var lf in kbPre.LargeFileWarnings)
                result.Warnings.Add($"[KB_LARGE] Document > 50 Mo (non bloquant) : "
                    + $"{lf.RelativePath} ({lf.SizeBytes:N0} o)");
        }

        // 2. Copie sélective.
        progress.ReportStep("copy", "Copie des fichiers");
        var files = PlanFiles(plan);
        journal.TotalPlannedFiles = files.Count;
        int copied = 0;
        foreach (var (rel, cat, sourceAbs) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (_journal.IsFileValidated(journal, rel))
            {
                progress.ReportMessage($"Ignoré (déjà validé) : {rel}", ProgressSeverity.Info);
                continue;
            }
            var destAbs = _fs.CombinePath(plan.TargetRoot, rel);
            _fs.CopyFile(sourceAbs, destAbs, overwrite: true);
            copied++;
            progress.ReportFile(rel, _fs.GetFileSize(destAbs),
                _fs.GetFileSize(sourceAbs), 0, null);
            progress.ReportOverall(copied, files.Count,
                100.0 * copied / Math.Max(1, files.Count));
        }
        result.FilesCopied = copied;
        journal.LastCompletedStage = BuildStage.Copy;
        _journal.Save(plan.TargetRoot, journal);

        // 2b. Copie de la base de connaissances (si planifiée) vers
        //     knowledge/documents/. Les hashs SHA-256 sont recalculés par le
        //     ManifestBuilder à l'étape 4 (source de vérité unique).
        KnowledgeBaseCopyResult? kbResult = null;
        if (knowledgeBase is not null && knowledgeBase.Items.Count > 0)
        {
            progress.ReportStep("knowledge", "Copie de la base de connaissances");
            var kbCopier = new KnowledgeBaseCopier(_fs, _checksum);
            kbResult = await kbCopier.CopyAsync(knowledgeBase, plan.TargetRoot,
                reindexOnFirstLaunch, MissingFileStrategy.Skip, ct);
            result.KnowledgeFilesCopied = kbResult.CopiedFiles.Count;
            result.KnowledgeBytes = kbResult.CopiedFiles.Sum(f => f.SizeBytes);
            result.KnowledgeReindexRequested = kbResult.ReindexMarkerWritten;
            foreach (var (item, reason) in kbResult.SkippedFiles)
            {
                result.KnowledgeSkipped.Add($"{item.RelativePath}: {reason}");
                result.Warnings.Add($"[KB_SKIP] {item.RelativePath}: {reason}");
            }
        }

        // 3. Injection system prompt + patch settings + filtre skills sur clé.
        progress.ReportStep("configure", "Configuration de la clé");
        string? spSha = null;
        if (!string.IsNullOrWhiteSpace(plan.CustomSystemPrompt))
            spSha = _promptInjector.Inject(plan.TargetRoot, plan.CustomSystemPrompt!);
        _settingsPatcher.Apply(plan.TargetRoot, plan);
        _skillFilter.PatchMcpConfig(plan.TargetRoot, plan.IncludedSkillIds);
        _markerWriter.Write(plan.TargetRoot, plan);
        journal.LastCompletedStage = BuildStage.Configure;
        _journal.Save(plan.TargetRoot, journal);

        // 4. Vérification SHA-256 post-copie + manifest.
        progress.ReportStep("verify", "Vérification SHA-256 post-copie");
        var manifestFiles = files.Select(f => (f.rel, f.cat)).ToList();
        if (kbResult is not null)
        {
            foreach (var kb in kbResult.CopiedFiles)
                manifestFiles.Add(($"{KnowledgeBaseFormats.DocumentsSubdir}/{kb.RelativePath}",
                                   "knowledge"));
            if (kbResult.ReindexMarkerWritten)
                manifestFiles.Add((KnowledgeBaseFormats.ReindexMarker, "knowledge"));
        }
        var manifest = await _manifestBuilder.BuildAsync(plan,
            manifestFiles,
            spSha, progress, ct);
        result.FilesVerified = manifest.Files.Count;
        result.TotalBytes = manifest.TotalBytes;
        // Écriture du CHECKSUMS.sha256 racine (agrège manifest + KB).
        result.ChecksumsFilePath = WriteChecksumsFile(plan.TargetRoot, manifest);
        journal.LastCompletedStage = BuildStage.Verify;
        _journal.Save(plan.TargetRoot, journal);
        foreach (var f in manifest.Files)
            _journal.MarkFileValidated(plan.TargetRoot, journal, f.RelativePath);

        // 5. Smoke test optionnel.
        if (plan.RunSmokeTest && runSmokeTest is not null &&
            plan.TargetPlatforms.Count > 0)
        {
            progress.ReportStep("smoke", "Test de démarrage rapide");
            var plat = plan.TargetPlatforms[0];
            var outcome = await runSmokeTest(plat);
            result.SmokeTestOutcome = outcome;
            journal.LastCompletedStage = BuildStage.SmokeTest;
            _journal.Save(plan.TargetRoot, journal);
        }

        // 6. Rapport HTML.
        progress.ReportStep("report", "Génération du rapport de production");
        result.ManifestPath = _fs.CombinePath(plan.TargetRoot, ManifestBuilder.ManifestFileName);
        result.EndedAtUtc = DateTime.UtcNow;
        result.Success = result.FilesFailed == 0;
        result.ReportHtmlPath = _reporter.Generate(plan.TargetRoot, plan, manifest, result);
        journal.LastCompletedStage = BuildStage.Reported;
        _journal.Save(plan.TargetRoot, journal);

        return result;
    }

    /// <summary>Énumération DÉTERMINISTE des fichiers à copier.</summary>
    public List<(string rel, string cat, string sourceAbs)> PlanFiles(BuildPlan plan)
    {
        var files = new List<(string, string, string)>();
        var coreDirs = new[] { "app", "ui", "scripts", "docs", "skills" };
        foreach (var d in coreDirs)
        {
            var abs = _fs.CombinePath(plan.SourceRoot, d);
            if (!_fs.DirectoryExists(abs)) continue;
            foreach (var f in _fs.EnumerateFiles(abs, "*", true))
            {
                if (ShouldSkipPath(f)) continue;
                var rel = _fs.GetRelativePath(plan.SourceRoot, f);
                files.Add((rel, "core", f));
            }
        }
        // config/ : on copie tout SAUF les fichiers spécifiques à un client précédent.
        var cfg = _fs.CombinePath(plan.SourceRoot, "config");
        if (_fs.DirectoryExists(cfg))
            foreach (var f in _fs.EnumerateFiles(cfg, "*", true))
            {
                if (ShouldSkipPath(f)) continue;
                var rel = _fs.GetRelativePath(plan.SourceRoot, f);
                files.Add((rel, "config", f));
            }

        // Modèle.
        var modelSrc = _fs.CombinePath(plan.SourceRoot, "models", plan.ModelFile);
        if (_fs.FileExists(modelSrc))
            files.Add(($"models/{plan.ModelFile}", "model", modelSrc));

        // Skills MCP (uniquement ceux inclus + _shared/_template).
        var mcpRoot = _fs.CombinePath(plan.SourceRoot, "mcp-servers");
        if (_fs.DirectoryExists(mcpRoot))
        {
            var kept = _skillFilter.Compute(plan.SourceRoot, plan.IncludedSkillIds);
            foreach (var subDir in _fs.EnumerateDirectories(mcpRoot))
            {
                var id = _fs.GetFileName(subDir);
                if (!kept.ToCopy.Contains(id)) continue;
                foreach (var f in _fs.EnumerateFiles(subDir, "*", true))
                {
                    if (ShouldSkipPath(f)) continue;
                    var rel = _fs.GetRelativePath(plan.SourceRoot, f);
                    files.Add((rel, "skill", f));
                }
            }
        }

        // Binaires par plateforme.
        foreach (var p in plan.TargetPlatforms)
        {
            var bd = _fs.CombinePath(plan.SourceRoot, p.ToBinaryDir());
            if (!_fs.DirectoryExists(bd)) continue;
            foreach (var f in _fs.EnumerateFiles(bd, "*", true))
            {
                if (ShouldSkipPath(f)) continue;
                var rel = _fs.GetRelativePath(plan.SourceRoot, f);
                files.Add((rel, "binary", f));
            }
        }

        // Fichiers racine.
        var rootFiles = new[]
        {
            "README.md", "LICENSE", "VERSION", "CHANGELOG.md",
            "install.sh", "install.bat", "start.sh",
            "start-windows.bat", "start-macos.sh", "start-linux.sh",
        };
        foreach (var rf in rootFiles)
        {
            var abs = _fs.CombinePath(plan.SourceRoot, rf);
            if (_fs.FileExists(abs)) files.Add((rf, "core", abs));
        }

        // Logo personnalisé.
        if (!string.IsNullOrWhiteSpace(plan.CustomLogoPath) &&
            _fs.FileExists(plan.CustomLogoPath!))
        {
            var name = _fs.GetFileName(plan.CustomLogoPath!);
            files.Add(($"ui/assets/{name}", "custom", plan.CustomLogoPath!));
        }
        return files;
    }

    private static bool ShouldSkipPath(string path) =>
        path.Contains("__pycache__", StringComparison.Ordinal)
        || path.Contains("/.git/", StringComparison.Ordinal)
        || path.Contains("\\.git\\", StringComparison.Ordinal)
        || path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Écrit un fichier CHECKSUMS.sha256 à la racine de la clé au format
    /// standard shasum(1) : « &lt;hash&gt;  &lt;chemin&gt;\n » trié
    /// lexicographiquement pour reproductibilité (utile pour la signature
    /// Ed25519 côté master copy).
    /// </summary>
    private string WriteChecksumsFile(string targetRoot, BuildManifest manifest)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in manifest.Files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
            sb.Append(f.Sha256).Append("  ").Append(f.RelativePath).Append('\n');
        var path = _fs.CombinePath(targetRoot, "CHECKSUMS.sha256");
        _fs.WriteAllText(path, sb.ToString());
        return path;
    }
}

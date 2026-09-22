// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : copie sélective d'une master copy vers la clé USB — seules les
//           combinaisons bin/<plat>/<backend> et bin/<plat>/python/wheels/
//           choisies par l'opérateur sont copiées. Le reste des dossiers
//           communs (config/, mcp-servers/, ui/, scripts/, skills/, launchers)
//           est copié intégralement (petit volume).
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class SelectiveCopier
{
    private readonly IFileSystem _fs;

    public SelectiveCopier(IFileSystem fs) => _fs = fs;

    /// <summary>Dossiers communs copiés intégralement (hors bin/).</summary>
    public static readonly IReadOnlyList<string> CommonDirectories =
        new[] { "config", "mcp-servers", "ui", "scripts", "skills",
                "knowledge", "docs", "app" };

    /// <summary>Fichiers racine copiés s'ils existent.</summary>
    public static readonly IReadOnlyList<string> RootFileCandidates =
        new[] { "start-windows.bat", "start-linux.sh", "start-mac.command",
                "stop-linux.sh", "stop-mac.sh",
                "install.sh", "install.bat", "start.sh", "start.bat",
                "README.md", "CHANGELOG.md", "LICENSE", "VERSION",
                "release.json", "release.json.sig",
                PortableLayout.ChecksumsFile };

    /// <summary>
    /// Copie sous <paramref name="destinationRoot"/> :
    ///   - les dossiers communs (récursifs),
    ///   - les fichiers racine reconnus,
    ///   - uniquement les <c>bin/&lt;plat&gt;/&lt;backend&gt;</c> et
    ///     <c>bin/&lt;plat&gt;/python/</c> pour chaque plateforme distincte
    ///     de <paramref name="selection"/>.
    /// </summary>
    public async Task<SelectiveCopyResult> CopyAsync(
        string sourceRoot,
        string destinationRoot,
        IReadOnlyCollection<PlatformBackend> selection,
        IProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        if (!_fs.DirectoryExists(sourceRoot))
            throw new DirectoryNotFoundException($"Source introuvable : {sourceRoot}");
        if (selection.Count == 0)
            throw new ArgumentException(
                "Aucune plateforme/backend sélectionnée — la clé serait inutilisable.",
                nameof(selection));

        _fs.CreateDirectory(destinationRoot);
        var copied = new List<string>();
        var skipped = new List<string>();

        // 1) Dossiers communs.
        foreach (var dir in CommonDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var src = _fs.CombinePath(sourceRoot, dir);
            if (!_fs.DirectoryExists(src)) continue;
            await CopyTreeAsync(src, _fs.CombinePath(destinationRoot, dir), copied, ct);
            progress?.ReportStep("copy.common", dir);
        }

        // 2) Fichiers racine.
        foreach (var file in RootFileCandidates)
        {
            ct.ThrowIfCancellationRequested();
            var src = _fs.CombinePath(sourceRoot, file);
            if (!_fs.FileExists(src)) continue;
            _fs.CopyFile(src, _fs.CombinePath(destinationRoot, file), overwrite: true);
            copied.Add(file);
        }

        // 3) bin/<plat>/{python, backend} — uniquement les plats/backends sélectionnés.
        var platsSelected = selection
            .Select(s => s.Platform)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var plat in platsSelected)
        {
            ct.ThrowIfCancellationRequested();
            if (!PortableLayout.SupportedPlatforms.Contains(plat, StringComparer.Ordinal))
            {
                skipped.Add($"bin/{plat}  (plateforme non reconnue)");
                continue;
            }
            var srcPlat = _fs.CombinePath(sourceRoot, "bin", plat);
            var dstPlat = _fs.CombinePath(destinationRoot, "bin", plat);
            if (!_fs.DirectoryExists(srcPlat))
            {
                skipped.Add($"bin/{plat}  (absent dans la source)");
                continue;
            }
            // 3a) Python embarqué (toujours copié entier s'il existe).
            var srcPy = _fs.CombinePath(srcPlat, PortableLayout.PythonSubdir);
            if (_fs.DirectoryExists(srcPy))
            {
                await CopyTreeAsync(srcPy,
                    _fs.CombinePath(dstPlat, PortableLayout.PythonSubdir),
                    copied, ct);
                progress?.ReportStep("copy.python", plat);
            }
            // 3b) Backends sélectionnés uniquement.
            foreach (var pb in selection.Where(s => s.Platform == plat))
            {
                var srcBe = _fs.CombinePath(srcPlat, pb.Backend);
                if (!_fs.DirectoryExists(srcBe))
                {
                    skipped.Add($"{pb.RelativePath}  (backend absent)");
                    continue;
                }
                await CopyTreeAsync(srcBe,
                    _fs.CombinePath(dstPlat, pb.Backend),
                    copied, ct);
                progress?.ReportStep("copy.backend", pb.ToString());
            }
        }

        return new SelectiveCopyResult
        {
            SourceRoot = sourceRoot,
            DestinationRoot = destinationRoot,
            CopiedFiles = copied,
            SkippedEntries = skipped,
            SelectedPlatforms = platsSelected,
        };
    }

    private async Task CopyTreeAsync(
        string src,
        string dst,
        List<string> copied,
        CancellationToken ct)
    {
        _fs.CreateDirectory(dst);
        foreach (var file in _fs.EnumerateFiles(src, "*", recursive: true))
        {
            ct.ThrowIfCancellationRequested();
            var rel = _fs.GetRelativePath(src, file);
            var target = _fs.CombinePath(dst, rel);
            var td = _fs.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(td)) _fs.CreateDirectory(td);
            _fs.CopyFile(file, target, overwrite: true);
            copied.Add(_fs.GetRelativePath(src, file));
            await Task.Yield(); // laisse la main à l'UI (progression fluide)
        }
    }
}

/// <summary>Résultat d'une copie sélective.</summary>
public sealed class SelectiveCopyResult
{
    public required string SourceRoot { get; init; }
    public required string DestinationRoot { get; init; }
    public required IReadOnlyList<string> CopiedFiles { get; init; }
    public required IReadOnlyList<string> SkippedEntries { get; init; }
    public required IReadOnlyList<string> SelectedPlatforms { get; init; }
    public int CopiedCount => CopiedFiles.Count;
}

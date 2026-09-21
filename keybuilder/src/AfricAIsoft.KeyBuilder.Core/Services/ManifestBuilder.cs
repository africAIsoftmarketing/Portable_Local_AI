// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : construit le manifest final (BuildManifest) à partir du plan
//           exécuté + calcule les checksums SHA-256 de chaque fichier copié.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class ManifestBuilder
{
    public const string ManifestFileName = "manifest.json";
    private readonly IFileSystem _fs;
    private readonly ChecksumService _checksum;

    public ManifestBuilder(IFileSystem fs, ChecksumService checksum)
    {
        _fs = fs;
        _checksum = checksum;
    }

    public async Task<BuildManifest> BuildAsync(
        BuildPlan plan,
        IEnumerable<(string relativePath, string sourceCategory)> files,
        string? systemPromptSha256,
        IProgressReporter progress,
        CancellationToken ct)
    {
        var m = new BuildManifest
        {
            SerialNumber = plan.SerialNumber,
            ClientId = plan.ClientId,
            OperatorName = plan.OperatorName,
            Version = plan.Version,
            ModelFile = plan.ModelFile,
            SystemPromptSha256 = systemPromptSha256,
            Skills = new List<string>(plan.IncludedSkillIds),
            Platforms = plan.TargetPlatforms.Select(p => p.ToString()).ToList(),
            GeneratedAtUtc = DateTime.UtcNow,
        };
        long total = 0;
        int done = 0;
        var list = files.ToList();
        foreach (var (rel, cat) in list)
        {
            ct.ThrowIfCancellationRequested();
            var full = _fs.CombinePath(plan.TargetRoot, rel);
            if (!_fs.FileExists(full)) continue;
            var size = _fs.GetFileSize(full);
            var sha = await _checksum.ComputeFileAsync(full, null, ct);
            m.Files.Add(new ManifestEntry(rel, size, sha, cat));
            total += size;
            if (rel.EndsWith(plan.ModelFile, StringComparison.OrdinalIgnoreCase))
                m.ModelSha256 = sha;
            done++;
            progress.ReportOverall(done, list.Count, 100.0 * done / list.Count);
        }
        m.TotalBytes = total;
        // Écriture du manifest sur la clé.
        var path = _fs.CombinePath(plan.TargetRoot, ManifestFileName);
        _fs.WriteAllText(path, JsonSerializer.Serialize(m,
            new JsonSerializerOptions { WriteIndented = true }));
        return m;
    }

    public BuildManifest? Load(string targetRoot)
    {
        var p = _fs.CombinePath(targetRoot, ManifestFileName);
        if (!_fs.FileExists(p)) return null;
        return JsonSerializer.Deserialize<BuildManifest>(_fs.ReadAllText(p));
    }
}

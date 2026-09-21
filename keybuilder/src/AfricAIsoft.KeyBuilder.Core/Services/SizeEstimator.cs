// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : estimation de la taille totale du build sans copier réellement.
//           Additionne : cœur (app/, ui/, scripts/, docs/), modèle sélectionné,
//           skills inclus, binaires par plateforme cible, éventuel logo custom.
//           Utilisé par l'UI pour le curseur "espace requis vs disponible".
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class SizeEstimator
{
    private readonly IFileSystem _fs;

    public SizeEstimator(IFileSystem fs) => _fs = fs;

    /// <summary>Répertoires dits « cœur » toujours copiés vers la clé.</summary>
    private static readonly string[] CoreDirs = new[]
    {
        "app", "ui", "config", "docs", "scripts", "skills",
    };

    /// <summary>Fichiers cœur (à la racine) toujours copiés.</summary>
    private static readonly string[] CoreFiles = new[]
    {
        "README.md", "LICENSE", "VERSION", "CHANGELOG.md",
        "install.sh", "install.bat", "start.sh", "start-windows.bat",
        "start-macos.sh", "start-linux.sh",
    };

    public SizeEstimate Estimate(BuildPlan plan)
    {
        long total = 0;
        var breakdown = new Dictionary<string, long>();

        // Cœur (app/, ui/, config/, docs/, scripts/, skills/).
        foreach (var dir in CoreDirs)
        {
            var full = _fs.CombinePath(plan.SourceRoot, dir);
            long size = _fs.DirectoryExists(full) ? SumDir(full) : 0;
            breakdown["core:" + dir] = size;
            total += size;
        }

        foreach (var f in CoreFiles)
        {
            var full = _fs.CombinePath(plan.SourceRoot, f);
            long size = _fs.FileExists(full) ? _fs.GetFileSize(full) : 0;
            if (size > 0) { total += size; breakdown["core:" + f] = size; }
        }

        // Modèle sélectionné.
        if (!string.IsNullOrWhiteSpace(plan.ModelFile))
        {
            var mp = _fs.CombinePath(plan.SourceRoot, "models", plan.ModelFile);
            if (_fs.FileExists(mp))
            {
                var s = _fs.GetFileSize(mp);
                total += s;
                breakdown["model:" + plan.ModelFile] = s;
            }
        }

        // Skills MCP inclus (les autres ne seront pas copiés).
        foreach (var skill in plan.IncludedSkillIds)
        {
            var sd = _fs.CombinePath(plan.SourceRoot, "mcp-servers", skill);
            long s = _fs.DirectoryExists(sd) ? SumDir(sd) : 0;
            if (s > 0) { total += s; breakdown["skill:" + skill] = s; }
        }
        // Skills partagés (mcp-servers/_shared).
        var shared = _fs.CombinePath(plan.SourceRoot, "mcp-servers", "_shared");
        if (_fs.DirectoryExists(shared))
        {
            long s = SumDir(shared);
            total += s; breakdown["skill:_shared"] = s;
        }

        // Binaires par plateforme.
        foreach (var plat in plan.TargetPlatforms)
        {
            var bd = _fs.CombinePath(plan.SourceRoot, plat.ToBinaryDir());
            long s = _fs.DirectoryExists(bd) ? SumDir(bd) : 0;
            if (s > 0) { total += s; breakdown["binary:" + plat] = s; }
        }

        // Logo personnalisé.
        if (!string.IsNullOrWhiteSpace(plan.CustomLogoPath) &&
            _fs.FileExists(plan.CustomLogoPath!))
        {
            var s = _fs.GetFileSize(plan.CustomLogoPath!);
            total += s; breakdown["custom:logo"] = s;
        }

        // Marge de 5 % pour manifests, marker, rapport, structure FS.
        long safetyMargin = (long)(total * 0.05);
        return new SizeEstimate(total, safetyMargin, breakdown);
    }

    private long SumDir(string path)
    {
        long sum = 0;
        foreach (var f in _fs.EnumerateFiles(path, "*", recursive: true))
        {
            // Filtre les traces (__pycache__, .git, models/*.gguf non sélectionné).
            var name = _fs.GetFileName(f);
            if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (f.Contains("__pycache__", StringComparison.Ordinal)) continue;
            sum += _fs.GetFileSize(f);
        }
        return sum;
    }
}

public sealed record SizeEstimate(
    long ContentBytes,
    long SafetyMarginBytes,
    IReadOnlyDictionary<string, long> Breakdown)
{
    public long TotalBytes => ContentBytes + SafetyMarginBytes;
}

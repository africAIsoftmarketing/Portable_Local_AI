// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : constantes et modèles décrivant la structure attendue d'une
//           master copy AfricAIsoft Portable Studio décompressée. Nommage
//           des plateformes VERROUILLÉ : les launchers (start-*.sh /
//           start-windows.bat / scripts/core-startup.sh) résolvent
//           `bin/${PLAT_KEY}/${BACKEND}` avec les mêmes noms.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Models;

/// <summary>Noms verrouillés des plateformes reconnues sous bin/.</summary>
public static class PortableLayout
{
    /// <summary>Dossiers obligatoires à la racine de la master copy.</summary>
    public static readonly IReadOnlyList<string> RequiredRootDirectories =
        new[] { "config", "mcp-servers", "bin", "ui", "scripts" };

    /// <summary>Au moins un de ces launchers doit être présent à la racine.</summary>
    public static readonly IReadOnlyList<string> LauncherCandidates =
        new[] { "start-windows.bat", "start-linux.sh", "start-mac.command" };

    /// <summary>Noms de plateformes verrouillés (alignés core-startup.sh).</summary>
    public static readonly IReadOnlyList<string> SupportedPlatforms =
        new[] { "windows", "darwin-arm64", "darwin-x86_64",
                "linux-x86_64", "linux-aarch64" };

    /// <summary>Backends valides par plateforme.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
        ValidBackendsPerPlatform =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["windows"]        = new[] { "cpu", "cuda", "vulkan" },
                ["darwin-arm64"]   = new[] { "cpu", "metal" },
                ["darwin-x86_64"]  = new[] { "cpu", "metal" },
                ["linux-x86_64"]   = new[] { "cpu", "cuda", "vulkan", "rocm" },
                ["linux-aarch64"]  = new[] { "cpu" },
            };

    /// <summary>Sous-dossier optionnel Python embarqué (wheels au 1er lancement).</summary>
    public const string PythonSubdir = "python";
    public const string WheelsSubdir = "python/wheels";
    public const string ChecksumsFile = "CHECKSUMS.sha256";
}

/// <summary>Une paire plateforme/backend sélectionnable par l'opérateur.</summary>
public readonly record struct PlatformBackend(string Platform, string Backend)
{
    public string RelativePath => $"bin/{Platform}/{Backend}";
    public override string ToString() => $"{Platform}/{Backend}";
}

/// <summary>Sévérité d'un item d'un rapport de validation.</summary>
public enum ValidationSeverity { Info, Warning, Error }

/// <summary>Un item d'un rapport de validation (dossier manquant, hash divergent, …).</summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? Path = null);

/// <summary>
/// Résultat de la validation de structure d'une master copy décompressée.
/// </summary>
public sealed class MasterCopyValidationResult
{
    public required string RootPath { get; init; }
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }
    /// <summary>Plateformes détectées effectivement présentes sous bin/.</summary>
    public required IReadOnlyList<string> DetectedPlatforms { get; init; }
    /// <summary>Backends détectés par plateforme.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DetectedBackends { get; init; }
    /// <summary>true si CHECKSUMS.sha256 était présent ET valide.</summary>
    public required bool ChecksumsVerified { get; init; }
    public required bool ChecksumsFilePresent { get; init; }

    public IEnumerable<ValidationIssue> Errors =>
        Issues.Where(i => i.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == ValidationSeverity.Warning);
}

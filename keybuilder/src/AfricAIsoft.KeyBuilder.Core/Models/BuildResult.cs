// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : résultats et rapports (rapport de production HTML + résumé batch).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Models;

public sealed record PreflightIssue(
    PreflightSeverity Severity,
    string Code,
    string Message);

public enum PreflightSeverity { Info, Warning, Error }

public sealed class PreflightReport
{
    public List<PreflightIssue> Issues { get; set; } = new();
    public long RequiredBytes { get; set; }
    public long AvailableBytes { get; set; }
    public bool CanProceed => !Issues.Any(i => i.Severity == PreflightSeverity.Error);
}

public sealed class BuildResult
{
    public bool Success { get; set; }
    public string SerialNumber { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public int FilesCopied { get; set; }
    public int FilesVerified { get; set; }
    public int FilesFailed { get; set; }
    public long TotalBytes { get; set; }
    public string? ReportHtmlPath { get; set; }
    public string? ManifestPath { get; set; }
    public string? SmokeTestOutcome { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

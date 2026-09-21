// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : manifest listant les fichiers copiés vers la clé avec hashes,
//           tailles, plateformes cibles, hash du system prompt, etc.
//           Sérialisable JSON, sert de source de vérité pour la vérification
//           SHA-256 et le rapport HTML.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Models;

public sealed class BuildManifest
{
    public string SerialNumber { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string OperatorName { get; set; } = "";
    public string? ClientId { get; set; }
    public string Version { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public string? ModelSha256 { get; set; }
    public string? SystemPromptSha256 { get; set; }
    public List<string> Skills { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<ManifestEntry> Files { get; set; } = new();
    public long TotalBytes { get; set; }
}

public sealed record ManifestEntry(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string SourceCategory);   // "core", "model", "skill", "binary", "ui", "config", "custom"

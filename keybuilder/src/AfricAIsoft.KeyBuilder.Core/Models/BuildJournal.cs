// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : journal de reprise. Chaque fichier VALIDÉ (copié + checksum vérifié)
//           est ajouté au journal. Si le build est interrompu, la reprise
//           lit le journal, skippe les fichiers déjà validés et redémarre à
//           partir du fichier suivant.
//           Persistance : journal.json à la racine de la clé (pas dans /data,
//           qui pourrait être filtré).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class BuildJournal
{
    public string SerialNumber { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastUpdateUtc { get; set; }
    public BuildStage LastCompletedStage { get; set; } = BuildStage.NotStarted;
    public HashSet<string> ValidatedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastError { get; set; }
    public int TotalPlannedFiles { get; set; }
}

public enum BuildStage
{
    NotStarted = 0,
    Preflight = 1,
    Format = 2,
    Copy = 3,
    Verify = 4,
    Configure = 5,   // system prompt injection + settings patch + marker
    SmokeTest = 6,
    Reported = 7,
}

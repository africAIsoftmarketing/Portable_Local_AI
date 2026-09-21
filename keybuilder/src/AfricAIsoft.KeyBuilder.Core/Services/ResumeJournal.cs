// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : lecture/écriture atomique du journal de reprise (journal.json à
//           la racine de la clé). Permet de reprendre un build interrompu.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class ResumeJournal
{
    public const string JournalFileName = ".keybuilder-journal.json";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly IFileSystem _fs;
    public ResumeJournal(IFileSystem fs) => _fs = fs;

    public string JournalPath(string targetRoot) => _fs.CombinePath(targetRoot, JournalFileName);

    public BuildJournal Load(string targetRoot)
    {
        var p = JournalPath(targetRoot);
        if (!_fs.FileExists(p)) return new BuildJournal();
        try
        {
            var txt = _fs.ReadAllText(p);
            return JsonSerializer.Deserialize<BuildJournal>(txt, JsonOpts)
                   ?? new BuildJournal();
        }
        catch (JsonException) { return new BuildJournal(); }
    }

    public void Save(string targetRoot, BuildJournal journal)
    {
        journal.LastUpdateUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(journal, JsonOpts);
        _fs.WriteAllText(JournalPath(targetRoot), json);
    }

    public void MarkFileValidated(string targetRoot, BuildJournal journal, string relativePath)
    {
        journal.ValidatedFiles.Add(NormalizePath(relativePath));
        Save(targetRoot, journal);
    }

    public bool IsFileValidated(BuildJournal journal, string relativePath)
        => journal.ValidatedFiles.Contains(NormalizePath(relativePath));

    private static string NormalizePath(string p) => p.Replace('\\', '/').TrimStart('/');
}

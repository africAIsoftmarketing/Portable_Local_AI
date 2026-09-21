// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : écrit le fichier marker.json à la racine de la clé, identifiant
//           unique + métadonnées humaines (client, opérateur, date).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class MarkerFileWriter
{
    public const string MarkerFileName = ".africaisoft-key.json";
    private readonly IFileSystem _fs;
    public MarkerFileWriter(IFileSystem fs) => _fs = fs;

    public string Write(string targetRoot, BuildPlan plan)
    {
        var marker = new
        {
            product = "AfricAIsoft Portable Studio",
            serial_number = plan.SerialNumber,
            version = plan.Version,
            client_id = plan.ClientId,
            operator_name = plan.OperatorName,
            created_at_utc = DateTime.UtcNow.ToString("o"),
            model_file = plan.ModelFile,
            platforms = plan.TargetPlatforms.Select(p => p.ToString()).ToArray(),
            skills = plan.IncludedSkillIds.ToArray(),
        };
        var path = _fs.CombinePath(targetRoot, MarkerFileName);
        _fs.WriteAllText(path, JsonSerializer.Serialize(marker,
            new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}

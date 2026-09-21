// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : filtre les skills MCP à embarquer sur la clé + patch de mcp.json
//           (retire les entrées désactivées) + patch de skills/registry.json.
//           Retourne la liste des chemins relatifs à COPIER et ceux à IGNORER.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class SkillFilter
{
    private readonly IFileSystem _fs;
    public SkillFilter(IFileSystem fs) => _fs = fs;

    public SkillFilterResult Compute(string sourceRoot, IEnumerable<string> includedIds)
    {
        var included = new HashSet<string>(includedIds, StringComparer.OrdinalIgnoreCase);
        var mcpRoot = _fs.CombinePath(sourceRoot, "mcp-servers");
        var toCopy = new List<string>();
        var toIgnore = new List<string>();
        if (!_fs.DirectoryExists(mcpRoot))
            return new SkillFilterResult(toCopy, toIgnore, included);
        foreach (var dir in _fs.EnumerateDirectories(mcpRoot))
        {
            var id = _fs.GetFileName(dir);
            if (id.StartsWith("_", StringComparison.Ordinal))
            { toCopy.Add(id); continue; }   // _shared, _template : toujours copiés
            if (included.Contains(id)) toCopy.Add(id);
            else toIgnore.Add(id);
        }
        return new SkillFilterResult(toCopy, toIgnore, included);
    }

    /// <summary>Réécrit mcp.json sur la clé en ne gardant que les skills inclus.</summary>
    public void PatchMcpConfig(string targetRoot, IEnumerable<string> includedIds)
    {
        var cfgPath = _fs.CombinePath(targetRoot, "config", "mcp.json");
        if (!_fs.FileExists(cfgPath)) return;
        var included = new HashSet<string>(includedIds, StringComparer.OrdinalIgnoreCase);
        var json = _fs.ReadAllText(cfgPath);
        using var doc = JsonDocument.Parse(json);
        var options = new JsonWriterOptions { Indented = true };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, options))
        {
            w.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("servers"))
                {
                    w.WritePropertyName("servers");
                    w.WriteStartObject();
                    foreach (var srv in prop.Value.EnumerateObject())
                    {
                        if (included.Contains(srv.Name))
                        {
                            w.WritePropertyName(srv.Name);
                            srv.Value.WriteTo(w);
                        }
                    }
                    w.WriteEndObject();
                }
                else
                {
                    prop.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }
        _fs.WriteAllText(cfgPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}

public sealed record SkillFilterResult(
    IReadOnlyList<string> ToCopy,
    IReadOnlyList<string> ToIgnore,
    IReadOnlySet<string> IncludedIds);

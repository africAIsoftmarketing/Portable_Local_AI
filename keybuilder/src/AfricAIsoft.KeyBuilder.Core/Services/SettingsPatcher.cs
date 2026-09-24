// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : patch de settings.json sur la clé (modèle actif, version, features
//           désactivées, id client). Écriture atomique via tmp + move.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// Version : 0.6.1 (2026-09-24) — si settings.json est absent, part de
//           config/settings.example.json copié sur la clé (configuration
//           complète : port 8080 attendu par les lanceurs, MCP activé…)
//           au lieu d'un minimum codé en dur ; « version » écrite aussi
//           lorsque settings.json existait déjà.
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text.Json;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class SettingsPatcher
{
    private readonly IFileSystem _fs;
    public SettingsPatcher(IFileSystem fs) => _fs = fs;

    public void Apply(string targetRoot, BuildPlan plan)
    {
        var path = _fs.CombinePath(targetRoot, "config", "settings.json");
        // 0.6.1 : la release ne livre jamais settings.json (secrets possibles),
        // mais toujours settings.example.json. On part de ce modèle complet,
        // puis la branche « fichier existant » ci-dessous le personnalise.
        var example = _fs.CombinePath(targetRoot, "config", "settings.example.json");
        if (!_fs.FileExists(path) && _fs.FileExists(example))
            _fs.WriteAllText(path, _fs.ReadAllText(example));
        if (!_fs.FileExists(path))
        {
            // Repli historique (aucun modèle disponible) : minimum viable.
            // Créer un minimum viable si absent.
            var defaults = new
            {
                server = new { port = 8001, bind_host = "127.0.0.1" },
                model = new { path = $"models/{plan.ModelFile}", context_size = 8192 },
                ui = new { default_language = "fr", theme = "auto" },
                version = plan.Version,
                client_id = plan.ClientId,
                serial_number = plan.SerialNumber,
                disabled_features = plan.DisabledFeatures,
            };
            _fs.WriteAllText(path, JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var raw = _fs.ReadAllText(path);
        using var doc = JsonDocument.Parse(raw);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            var seen = new HashSet<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                seen.Add(prop.Name);
                if (prop.NameEquals("model"))
                {
                    w.WritePropertyName("model");
                    w.WriteStartObject();
                    var subSeen = new HashSet<string>();
                    foreach (var mp in prop.Value.EnumerateObject())
                    {
                        subSeen.Add(mp.Name);
                        if (mp.NameEquals("path"))
                            w.WriteString("path", $"models/{plan.ModelFile}");
                        else mp.WriteTo(w);
                    }
                    if (!subSeen.Contains("path"))
                        w.WriteString("path", $"models/{plan.ModelFile}");
                    w.WriteEndObject();
                }
                else prop.WriteTo(w);
            }
            // Correctif : si aucune section "model" n'existait, on la crée.
            if (!seen.Contains("model"))
            {
                w.WritePropertyName("model");
                w.WriteStartObject();
                w.WriteString("path", $"models/{plan.ModelFile}");
                w.WriteEndObject();
            }
            if (!seen.Contains("version"))
                w.WriteString("version", plan.Version);
            if (!seen.Contains("client_id") && plan.ClientId is not null)
                w.WriteString("client_id", plan.ClientId);
            if (!seen.Contains("serial_number"))
                w.WriteString("serial_number", plan.SerialNumber);
            if (!seen.Contains("disabled_features"))
            {
                w.WritePropertyName("disabled_features");
                w.WriteStartArray();
                foreach (var f in plan.DisabledFeatures) w.WriteStringValue(f);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        _fs.WriteAllText(path, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}

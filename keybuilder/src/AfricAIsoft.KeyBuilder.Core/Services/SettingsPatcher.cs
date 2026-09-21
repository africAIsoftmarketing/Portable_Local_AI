// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : patch de settings.json sur la clé (modèle actif, version, features
//           désactivées, id client). Écriture atomique via tmp + move.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
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
        if (!_fs.FileExists(path))
        {
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

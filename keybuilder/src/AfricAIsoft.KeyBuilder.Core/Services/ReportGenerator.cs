// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : génère un rapport de production HTML autonome (aucune ressource
//           externe) résumant : date, opérateur, client, modèle+hash, plate-
//           formes, skills, hash system prompt, résultat vérification
//           d'intégrité, numéro de série. Un CSS minimal est intégré inline.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class ReportGenerator
{
    public const string ReportFileName = "production-report.html";
    private readonly IFileSystem _fs;
    public ReportGenerator(IFileSystem fs) => _fs = fs;

    public string Generate(string targetRoot, BuildPlan plan,
                           BuildManifest manifest, BuildResult result)
    {
        var sb = new StringBuilder();
        var okBadge = "<span style=\"background:#2f5c3a;color:#fff;padding:2px 8px;border-radius:4px\">OK</span>";
        var koBadge = "<span style=\"background:#a43a2e;color:#fff;padding:2px 8px;border-radius:4px\">KO</span>";
        var integrity = result.FilesFailed == 0 ? okBadge : koBadge;
        sb.Append(@"<!DOCTYPE html><html lang=""fr""><head>
<meta charset=""UTF-8""><title>Rapport de production AfricAIsoft Portable Studio</title>
<style>
body{font-family:Georgia,serif;background:#f6f2ea;color:#23241f;margin:0;padding:32px;line-height:1.55}
h1{border-bottom:2px solid #2f5c3a;padding-bottom:8px}
h2{color:#2f5c3a;margin-top:24px}
.card{background:#fff;border:1px solid #d9d3c4;border-radius:8px;padding:16px;margin-bottom:16px}
dl{display:grid;grid-template-columns:220px 1fr;gap:6px 12px;margin:0}
dt{color:#5c5b52}
dd{margin:0;font-family:'JetBrains Mono',monospace;font-size:13px;word-break:break-all}
table{width:100%;border-collapse:collapse;font-family:monospace;font-size:12px}
th,td{border-bottom:1px solid #ede8d9;padding:4px 6px;text-align:left}
th{background:#eee8db}
.mono{font-family:'JetBrains Mono',monospace;font-size:12px}
</style></head><body>");
        sb.Append("<h1>Rapport de production — clé USB portable</h1>");
        sb.Append("<div class=\"card\"><h2>Identification</h2><dl>");
        Kv(sb, "N° de série", plan.SerialNumber);
        Kv(sb, "Court", plan.ShortSerial);
        Kv(sb, "Date de production", manifest.GeneratedAtUtc.ToString("u"));
        Kv(sb, "Opérateur", plan.OperatorName);
        Kv(sb, "Client", plan.ClientId ?? "(non renseigné)");
        Kv(sb, "Version", plan.Version);
        // Badge intégrité — injecté en HTML brut (bypass encodage).
        sb.Append("<dt>Intégrité</dt><dd>").Append(integrity).Append("</dd>");
        sb.Append("</dl></div>");

        sb.Append("<div class=\"card\"><h2>Contenu embarqué</h2><dl>");
        Kv(sb, "Modèle GGUF", plan.ModelFile);
        Kv(sb, "SHA-256 modèle", manifest.ModelSha256 ?? "(non calculé)");
        Kv(sb, "System prompt SHA-256", manifest.SystemPromptSha256 ?? "(défaut)");
        Kv(sb, "Skills MCP",
            plan.IncludedSkillIds.Count == 0 ? "(aucun)" : string.Join(", ", plan.IncludedSkillIds));
        Kv(sb, "Plateformes cibles",
            string.Join(", ", plan.TargetPlatforms.Select(p => p.DisplayName())));
        Kv(sb, "Système de fichiers", plan.FormatWith?.ToString() ?? "(pas de formatage)");
        Kv(sb, "Étiquette volume", plan.VolumeLabel);
        Kv(sb, "Features désactivées",
            plan.DisabledFeatures.Count == 0 ? "(aucune)" : string.Join(", ", plan.DisabledFeatures));
        sb.Append("</dl></div>");

        sb.Append("<div class=\"card\"><h2>Résultats</h2><dl>");
        Kv(sb, "Démarré (UTC)", result.StartedAtUtc.ToString("u"));
        Kv(sb, "Terminé (UTC)", result.EndedAtUtc.ToString("u"));
        Kv(sb, "Durée",
            (result.EndedAtUtc - result.StartedAtUtc).ToString(@"hh\:mm\:ss"));
        Kv(sb, "Fichiers copiés", result.FilesCopied.ToString());
        Kv(sb, "Fichiers vérifiés", result.FilesVerified.ToString());
        Kv(sb, "Fichiers en échec", result.FilesFailed.ToString());
        Kv(sb, "Taille totale", FmtBytes(result.TotalBytes));
        Kv(sb, "Smoke test", result.SmokeTestOutcome ?? "(non exécuté)");
        sb.Append("</dl>");
        if (result.Warnings.Count > 0)
        {
            sb.Append("<p><b>Avertissements :</b></p><ul>");
            foreach (var w in result.Warnings) sb.Append("<li>").Append(WebEncode(w)).Append("</li>");
            sb.Append("</ul>");
        }
        if (result.Errors.Count > 0)
        {
            sb.Append("<p><b>Erreurs :</b></p><ul>");
            foreach (var e in result.Errors) sb.Append("<li>").Append(WebEncode(e)).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><h2>Manifest (extrait — 50 fichiers max)</h2>");
        sb.Append("<table><thead><tr><th>Fichier</th><th>Taille</th><th>SHA-256</th></tr></thead><tbody>");
        int rows = 0;
        foreach (var e in manifest.Files.OrderByDescending(x => x.SizeBytes))
        {
            if (rows++ >= 50) break;
            sb.Append("<tr><td>").Append(WebEncode(e.RelativePath))
              .Append("</td><td>").Append(FmtBytes(e.SizeBytes))
              .Append("</td><td class=\"mono\">").Append(e.Sha256[..16]).Append('…')
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (manifest.Files.Count > 50)
            sb.Append("<p>").Append(manifest.Files.Count - 50)
              .Append(" fichier(s) supplémentaires dans le manifest.json.</p>");
        sb.Append("</div>");

        sb.Append("</body></html>");

        var outPath = _fs.CombinePath(targetRoot, ReportFileName);
        _fs.WriteAllText(outPath, sb.ToString());
        return outPath;
    }

    private static void Kv(StringBuilder sb, string k, string v)
    {
        sb.Append("<dt>").Append(WebEncode(k)).Append("</dt><dd>")
          .Append(WebEncode(v)).Append("</dd>");
    }

    private static string WebEncode(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string FmtBytes(long n)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = n;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}

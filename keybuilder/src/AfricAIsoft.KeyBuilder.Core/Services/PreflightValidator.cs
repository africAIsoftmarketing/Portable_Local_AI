// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : validation pré-vol du plan de build.
//           - Espace disponible sur la clé cible vs. taille estimée.
//           - Structure de la source (app/, ui/, config/, mcp-servers/, models/).
//           - Modèle GGUF valide (header + taille non nulle).
//           - Au moins une plateforme cible + binaires correspondants présents.
//           - Système de fichiers demandé (compat OS courant).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class PreflightValidator
{
    private readonly IFileSystem _fs;
    private readonly SizeEstimator _estimator;
    private readonly GgufValidator _gguf;
    private readonly IDriveFormatter? _formatter;

    public PreflightValidator(
        IFileSystem fs,
        SizeEstimator estimator,
        GgufValidator gguf,
        IDriveFormatter? formatter = null)
    {
        _fs = fs;
        _estimator = estimator;
        _gguf = gguf;
        _formatter = formatter;
    }

    public PreflightReport Validate(BuildPlan plan, UsbDrive? targetDrive)
    {
        var report = new PreflightReport();

        // Structure source.
        var required = new[] { "app", "ui", "config", "scripts", "mcp-servers", "models" };
        foreach (var d in required)
        {
            if (!_fs.DirectoryExists(_fs.CombinePath(plan.SourceRoot, d)))
                report.Issues.Add(new PreflightIssue(
                    PreflightSeverity.Error, "SRC_MISSING_" + d.ToUpperInvariant(),
                    $"Dossier source manquant : {d}"));
        }

        // Modèle GGUF.
        if (string.IsNullOrWhiteSpace(plan.ModelFile))
        {
            report.Issues.Add(new(PreflightSeverity.Error, "MODEL_NONE",
                "Aucun modèle GGUF sélectionné."));
        }
        else
        {
            var modelPath = _fs.CombinePath(plan.SourceRoot, "models", plan.ModelFile);
            if (!_fs.FileExists(modelPath))
                report.Issues.Add(new(PreflightSeverity.Error, "MODEL_MISSING",
                    $"Modèle absent : {plan.ModelFile}"));
            else
            {
                var info = _gguf.Inspect(modelPath);
                if (!info.ValidHeader)
                    report.Issues.Add(new(PreflightSeverity.Error, "MODEL_INVALID_GGUF",
                        $"Fichier .gguf invalide : {plan.ModelFile} (magic ou version incorrects)."));
                if (info.ParametersHint is null)
                    report.Issues.Add(new(PreflightSeverity.Info, "MODEL_PARAMS_UNKNOWN",
                        $"Impossible de déduire le nombre de paramètres depuis le nom '{plan.ModelFile}'."));
            }
        }

        // Plateformes.
        if (plan.TargetPlatforms.Count == 0)
        {
            report.Issues.Add(new(PreflightSeverity.Error, "PLAT_NONE",
                "Aucune plateforme cible sélectionnée."));
        }
        else
        {
            foreach (var p in plan.TargetPlatforms)
            {
                var bd = _fs.CombinePath(plan.SourceRoot, p.ToBinaryDir());
                if (!_fs.DirectoryExists(bd) ||
                    !_fs.EnumerateFiles(bd, "*", true).Any())
                {
                    report.Issues.Add(new(PreflightSeverity.Warning,
                        "BINARIES_MISSING_" + p, $"Binaires absents pour {p.DisplayName()} — la clé sera livrée sans llama-server pour cette plateforme."));
                }
            }
        }

        // Skills : au moins un.
        if (plan.IncludedSkillIds.Count == 0)
        {
            report.Issues.Add(new(PreflightSeverity.Info, "SKILLS_NONE",
                "Aucun skill MCP inclus — l'assistant fonctionnera en chat pur."));
        }
        else
        {
            foreach (var s in plan.IncludedSkillIds)
            {
                var sd = _fs.CombinePath(plan.SourceRoot, "mcp-servers", s);
                if (!_fs.DirectoryExists(sd))
                    report.Issues.Add(new(PreflightSeverity.Error, "SKILL_MISSING_" + s,
                        $"Skill introuvable : {s}"));
            }
        }

        // Système de fichiers supporté.
        if (plan.FormatWith is not null && _formatter is not null &&
            !_formatter.Supports(plan.FormatWith.Value))
        {
            report.Issues.Add(new(PreflightSeverity.Error, "FS_UNSUPPORTED",
                $"Le formatage {plan.FormatWith} n'est pas supporté nativement sur cette machine."));
        }

        // Estimation taille vs espace disponible.
        var est = _estimator.Estimate(plan);
        report.RequiredBytes = est.TotalBytes;

        if (targetDrive is not null)
        {
            report.AvailableBytes = targetDrive.CapacityBytes;
            if (targetDrive.CapacityBytes < est.TotalBytes)
                report.Issues.Add(new(PreflightSeverity.Error, "DRIVE_TOO_SMALL",
                    $"Capacité insuffisante : {FmtBytes(targetDrive.CapacityBytes)} vs. {FmtBytes(est.TotalBytes)} requis."));
            else if (targetDrive.FreeSpaceBytes < est.TotalBytes && plan.FormatWith is null)
                report.Issues.Add(new(PreflightSeverity.Error, "DRIVE_NOT_ENOUGH_FREE",
                    $"Espace libre insuffisant sans formatage : {FmtBytes(targetDrive.FreeSpaceBytes)} < {FmtBytes(est.TotalBytes)}."));
        }
        else
        {
            report.Issues.Add(new(PreflightSeverity.Error, "DRIVE_NONE",
                "Aucune clé USB cible sélectionnée."));
        }

        return report;
    }

    private static string FmtBytes(long n)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = n;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}

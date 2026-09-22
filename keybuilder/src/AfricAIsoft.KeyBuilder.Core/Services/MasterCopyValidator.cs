// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : validation de structure d'une master copy AfricAIsoft Portable
//           Studio décompressée sur disque + vérification optionnelle du
//           fichier CHECKSUMS.sha256 racine (hash en streaming, mémoire O(1)).
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class MasterCopyValidator
{
    private readonly IFileSystem _fs;
    private readonly ChecksumService _checksums;

    public MasterCopyValidator(IFileSystem fs, ChecksumService checksums)
    {
        _fs = fs;
        _checksums = checksums;
    }

    /// <summary>
    /// Valide la structure sous <paramref name="rootPath"/> et vérifie
    /// éventuellement CHECKSUMS.sha256 si présent à la racine.
    /// </summary>
    public async Task<MasterCopyValidationResult> ValidateAsync(
        string rootPath,
        bool verifyChecksums = true,
        CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        var detectedPlats = new List<string>();
        var detectedBackends = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        // 1) La racine existe ?
        if (!_fs.DirectoryExists(rootPath))
        {
            issues.Add(new(ValidationSeverity.Error, "ROOT_MISSING",
                "Dossier master copy introuvable.", rootPath));
            return Build(rootPath, issues, detectedPlats, detectedBackends, false, false);
        }

        // 2) Dossiers obligatoires à la racine.
        foreach (var dir in PortableLayout.RequiredRootDirectories)
        {
            var p = _fs.CombinePath(rootPath, dir);
            if (!_fs.DirectoryExists(p))
                issues.Add(new(ValidationSeverity.Error, "REQUIRED_DIR_MISSING",
                    $"Dossier obligatoire absent : {dir}/", p));
        }

        // 3) Au moins un launcher.
        var foundLauncher = PortableLayout.LauncherCandidates.Any(l =>
            _fs.FileExists(_fs.CombinePath(rootPath, l)));
        if (!foundLauncher)
            issues.Add(new(ValidationSeverity.Error, "NO_LAUNCHER",
                "Aucun launcher (start-windows.bat / start-linux.sh / start-mac.command) trouvé à la racine.",
                rootPath));

        // 4) Structure bin/<plat>/<backend> — nommage verrouillé.
        var binRoot = _fs.CombinePath(rootPath, "bin");
        if (_fs.DirectoryExists(binRoot))
        {
            foreach (var platDir in _fs.EnumerateDirectories(binRoot))
            {
                var platName = _fs.GetFileName(platDir);
                if (!PortableLayout.SupportedPlatforms.Contains(platName, StringComparer.Ordinal))
                {
                    issues.Add(new(ValidationSeverity.Warning, "UNKNOWN_PLATFORM",
                        $"Plateforme inconnue sous bin/ (ignorée par les launchers) : {platName}",
                        platDir));
                    continue;
                }
                detectedPlats.Add(platName);
                var backends = new List<string>();
                var validBackends = PortableLayout.ValidBackendsPerPlatform[platName];
                foreach (var backendDir in _fs.EnumerateDirectories(platDir))
                {
                    var bName = _fs.GetFileName(backendDir);
                    if (bName == PortableLayout.PythonSubdir) continue; // python/ à part
                    if (!validBackends.Contains(bName, StringComparer.Ordinal))
                    {
                        issues.Add(new(ValidationSeverity.Warning, "UNKNOWN_BACKEND",
                            $"Backend inattendu pour {platName} : {bName}", backendDir));
                        continue;
                    }
                    // Un backend doit contenir au moins un fichier (llama-server*).
                    if (!_fs.EnumerateFiles(backendDir, "*", recursive: true).Any())
                    {
                        issues.Add(new(ValidationSeverity.Warning, "EMPTY_BACKEND",
                            $"Backend vide : {platName}/{bName}", backendDir));
                        continue;
                    }
                    backends.Add(bName);
                }
                detectedBackends[platName] = backends;
                // wheels/ recommandé (offline first-run).
                var wheels = _fs.CombinePath(platDir, PortableLayout.WheelsSubdir);
                if (!_fs.DirectoryExists(wheels))
                    issues.Add(new(ValidationSeverity.Info, "WHEELS_MISSING",
                        $"Aucun bin/{platName}/python/wheels/ — le 1er lancement sera bloqué offline.",
                        wheels));
            }
            if (detectedPlats.Count == 0)
                issues.Add(new(ValidationSeverity.Error, "NO_PLATFORM",
                    "Aucune plateforme reconnue sous bin/.", binRoot));
        }

        // 5) CHECKSUMS.sha256 — vérification streaming si demandé + présent.
        var checksumFile = _fs.CombinePath(rootPath, PortableLayout.ChecksumsFile);
        bool checksumsPresent = _fs.FileExists(checksumFile);
        bool checksumsOk = false;
        if (checksumsPresent && verifyChecksums)
        {
            checksumsOk = await VerifyChecksumsAsync(rootPath, checksumFile, issues, ct);
        }
        else if (!checksumsPresent)
        {
            issues.Add(new(ValidationSeverity.Info, "CHECKSUMS_ABSENT",
                "CHECKSUMS.sha256 absent — impossible de vérifier l'intégrité.",
                checksumFile));
        }

        return Build(rootPath, issues, detectedPlats, detectedBackends,
                     checksumsPresent, checksumsPresent && checksumsOk);
    }

    private async Task<bool> VerifyChecksumsAsync(
        string rootPath,
        string checksumFile,
        List<ValidationIssue> issues,
        CancellationToken ct)
    {
        var content = _fs.ReadAllText(checksumFile);
        bool allOk = true;
        foreach (var raw in content.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // Format `sha256sum` : "<hash>  <path>" (2 espaces) ou "<hash> *<path>".
            var idx = line.IndexOf("  ", StringComparison.Ordinal);
            if (idx < 0) idx = line.IndexOf(" *", StringComparison.Ordinal);
            if (idx < 0) idx = line.IndexOf(' ');
            if (idx < 0 || idx == line.Length - 1)
            {
                issues.Add(new(ValidationSeverity.Warning, "CHECKSUMS_MALFORMED",
                    $"Ligne mal formée dans CHECKSUMS.sha256 : « {line} »", checksumFile));
                allOk = false;
                continue;
            }
            var expected = line[..idx].Trim().ToLowerInvariant();
            var rel = line[(idx + 1)..].TrimStart(' ', '*').Trim().TrimStart('.', '/');
            var target = _fs.CombinePath(rootPath, rel);
            if (!_fs.FileExists(target))
            {
                issues.Add(new(ValidationSeverity.Error, "CHECKSUM_TARGET_MISSING",
                    $"Fichier listé dans CHECKSUMS mais absent : {rel}", target));
                allOk = false; continue;
            }
            var actual = await _checksums.ComputeFileAsync(target, progress: null, ct);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(ValidationSeverity.Error, "CHECKSUM_MISMATCH",
                    $"Hash divergent pour {rel} (attendu {expected[..12]}…, obtenu {actual[..12]}…).",
                    target));
                allOk = false;
            }
        }
        if (!allOk)
            issues.Add(new(ValidationSeverity.Error, "CHECKSUMS_FAILED",
                "Vérification CHECKSUMS.sha256 échouée — archive potentiellement corrompue.",
                checksumFile));
        return allOk;
    }

    private static MasterCopyValidationResult Build(
        string root,
        IReadOnlyList<ValidationIssue> issues,
        IReadOnlyList<string> plats,
        IReadOnlyDictionary<string, IReadOnlyList<string>> backends,
        bool checksumsPresent,
        bool checksumsVerified)
        => new()
        {
            RootPath = root,
            IsValid = !issues.Any(i => i.Severity == ValidationSeverity.Error),
            Issues = issues,
            DetectedPlatforms = plats,
            DetectedBackends = backends,
            ChecksumsFilePresent = checksumsPresent,
            ChecksumsVerified = checksumsVerified,
        };
}

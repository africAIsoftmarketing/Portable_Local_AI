// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : copie effective de la base de connaissances vers la clé USB.
//           Chaque fichier est copié sous knowledge/documents/<rel>,
//           haché en SHA-256 (streaming), et inclus dans la table de
//           checksums retournée. L'index existant knowledge/index/ n'est
//           JAMAIS embarqué depuis la source dès qu'au moins un document
//           est ajouté (déclenche la réindexation au 1er démarrage).
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class KnowledgeBaseCopier
{
    private readonly IFileSystem _fs;
    private readonly ChecksumService _checksums;

    public KnowledgeBaseCopier(IFileSystem fs, ChecksumService checksums)
    {
        _fs = fs;
        _checksums = checksums;
    }

    /// <summary>
    /// Copie les items sous <paramref name="destinationRoot"/>/knowledge/documents.
    /// Retourne un rapport avec les fichiers copiés/skippés et les hash SHA-256
    /// par chemin relatif (utilisables dans le CHECKSUMS.sha256 de la clé).
    /// </summary>
    /// <exception cref="FileNotFoundException">Levé quand un fichier source
    /// est introuvable et que la stratégie est <see cref="MissingFileStrategy.Abort"/>.</exception>
    public async Task<KnowledgeBaseCopyResult> CopyAsync(
        KnowledgeBasePlanner planner,
        string destinationRoot,
        bool writeReindexMarker,
        MissingFileStrategy missingStrategy = MissingFileStrategy.Skip,
        CancellationToken ct = default)
    {
        var docsRoot = _fs.CombinePath(destinationRoot, KnowledgeBaseFormats.DocumentsSubdir);
        _fs.CreateDirectory(docsRoot);

        var copied  = new List<KnowledgeItem>();
        var skipped = new List<(KnowledgeItem, string)>();
        var hashes  = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in planner.Items)
        {
            ct.ThrowIfCancellationRequested();
            if (!_fs.FileExists(item.SourcePath))
            {
                var reason = $"Fichier source introuvable : {item.SourcePath}";
                if (missingStrategy == MissingFileStrategy.Abort)
                    throw new FileNotFoundException(reason, item.SourcePath);
                skipped.Add((item, reason));
                continue;
            }
            var target = _fs.CombinePath(docsRoot, item.RelativePath);
            var targetDir = _fs.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir)) _fs.CreateDirectory(targetDir);
            _fs.CopyFile(item.SourcePath, target, overwrite: true);
            var hash = await _checksums.ComputeFileAsync(target, progress: null, ct);
            hashes[$"{KnowledgeBaseFormats.DocumentsSubdir}/{item.RelativePath}"] = hash;
            copied.Add(item);
        }

        // Exclusion explicite de knowledge/index/ : si un index existe côté
        // destination (héritage master copy), on le retire pour forcer la
        // réindexation au 1er démarrage par le skill RAG existant.
        var indexDir = _fs.CombinePath(destinationRoot, KnowledgeBaseFormats.IndexSubdir);
        // On ne supprime pas récursivement ici (l'IFileSystem ne l'expose pas),
        // mais on écrit le marqueur .reindex qui a la même sémantique cible.
        // NOTE : le launcher courant ne lit pas ce fichier ; en revanche le
        // skill RAG détecte l'absence d'index → réindexe automatiquement.
        // La copie sélective de la master copy (SelectiveCopier) doit donc
        // exclure bin/.../index — ce qui est le cas par défaut car nous ne
        // copions PAS knowledge/index/ (dossier hors CommonDirectories).

        bool markerWritten = false;
        if (writeReindexMarker && copied.Count > 0)
        {
            var markerPath = _fs.CombinePath(destinationRoot, KnowledgeBaseFormats.ReindexMarker);
            var markerDir = _fs.GetDirectoryName(markerPath);
            if (!string.IsNullOrEmpty(markerDir)) _fs.CreateDirectory(markerDir);
            _fs.WriteAllText(markerPath,
                $"reindex requested at {DateTime.UtcNow:O}\n");
            markerWritten = true;
        }

        return new KnowledgeBaseCopyResult(
            DestinationRoot: destinationRoot,
            CopiedFiles: copied,
            SkippedFiles: skipped,
            Sha256ByRelativePath: hashes,
            ReindexMarkerWritten: markerWritten);
    }
}

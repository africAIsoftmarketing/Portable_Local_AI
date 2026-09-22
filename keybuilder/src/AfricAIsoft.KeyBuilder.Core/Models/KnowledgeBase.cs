// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : modèles de données pour la base de connaissances (RAG) — fichiers
//           .pdf/.txt/.md sélectionnés par l'opérateur pour être embarqués
//           sur la clé USB sous knowledge/documents/.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Models;

/// <summary>Extensions de fichiers acceptées par la base de connaissances.</summary>
public static class KnowledgeBaseFormats
{
    public const long LargeFileThresholdBytes = 50L * 1024 * 1024; // 50 Mo

    public static readonly IReadOnlyList<string> AllowedExtensions =
        new[] { ".pdf", ".txt", ".md" };

    /// <summary>Sous-dossier destination sur la clé USB (préservé littéral).</summary>
    public const string DocumentsSubdir = "knowledge/documents";

    /// <summary>Sous-dossier index RAG — JAMAIS embarqué depuis la source dès
    /// qu'un document est ajouté par l'opérateur (déclenche la réindexation
    /// automatique au premier démarrage : le skill RAG existant recrée
    /// l'index si absent ou périmé).</summary>
    public const string IndexSubdir = "knowledge/index";

    /// <summary>Marqueur écrit à la racine knowledge/ pour signaler qu'une
    /// réindexation est explicitement demandée. Non lu par les launchers
    /// actuels (documenté), mais compatible avec le mécanisme d'auto-
    /// réindexation du skill RAG.</summary>
    public const string ReindexMarker = "knowledge/.reindex";

    public static bool IsAllowed(string path) =>
        AllowedExtensions.Contains(
            System.IO.Path.GetExtension(path).ToLowerInvariant(),
            StringComparer.Ordinal);
}

/// <summary>Un document sélectionné (chemin source + chemin relatif cible).</summary>
public sealed record KnowledgeItem(
    string SourcePath,
    string RelativePath, // ex. "manuels/produit-x.pdf" → knowledge/documents/manuels/produit-x.pdf
    long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(SourcePath);
    public string Extension => System.IO.Path.GetExtension(SourcePath).ToLowerInvariant();
    public bool IsLarge => SizeBytes > KnowledgeBaseFormats.LargeFileThresholdBytes;
}

/// <summary>Stratégie appliquée quand un fichier source est introuvable
/// pendant la copie (raccourci Windows cassé, drive USB éjecté, …).</summary>
public enum MissingFileStrategy
{
    /// <summary>Ignore le fichier absent et continue avec les autres (le
    /// rapport final liste chaque skip).</summary>
    Skip,
    /// <summary>Interrompt la copie immédiatement et lève une exception.</summary>
    Abort,
}

/// <summary>Rapport produit par le planificateur avant tout côté clé.</summary>
public sealed record KnowledgeBasePreflight(
    long TotalBytes,
    int FileCount,
    IReadOnlyList<KnowledgeItem> LargeFileWarnings,
    bool EnoughSpace,
    long RequiredBytesWithMargin,
    long AvailableBytes);

/// <summary>Résultat d'une copie effectuée sur la clé USB.</summary>
public sealed record KnowledgeBaseCopyResult(
    string DestinationRoot,
    IReadOnlyList<KnowledgeItem> CopiedFiles,
    IReadOnlyList<(KnowledgeItem Item, string Reason)> SkippedFiles,
    IReadOnlyDictionary<string, string> Sha256ByRelativePath,
    bool ReindexMarkerWritten);

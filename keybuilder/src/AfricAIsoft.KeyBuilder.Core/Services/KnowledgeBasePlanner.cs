// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : planificateur de la base de connaissances — l'opérateur ajoute
//           fichiers/dossiers, le planner filtre les formats acceptés,
//           préserve la structure relative des sous-dossiers, calcule la
//           taille totale en temps réel et signale les fichiers > 50 Mo
//           (avertissement non bloquant).
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class KnowledgeBasePlanner
{
    private readonly IFileSystem _fs;
    private readonly List<KnowledgeItem> _items = new();

    public KnowledgeBasePlanner(IFileSystem fs) => _fs = fs;

    /// <summary>Items actuellement planifiés (lecture seule).</summary>
    public IReadOnlyList<KnowledgeItem> Items => _items;

    /// <summary>Taille totale de tous les items (temps réel).</summary>
    public long TotalBytes => _items.Sum(i => i.SizeBytes);

    /// <summary>Compte de fichiers > 50 Mo (avertissement, non bloquant).</summary>
    public IReadOnlyList<KnowledgeItem> LargeFiles =>
        _items.Where(i => i.IsLarge).ToList();

    /// <summary>
    /// Ajoute une liste de fichiers individuels (chemins absolus). Les
    /// extensions non acceptées et les doublons sont silencieusement filtrés.
    /// Retourne le nombre effectivement ajouté.
    /// </summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var raw in paths)
        {
            if (!KnowledgeBaseFormats.IsAllowed(raw)) continue;
            if (!_fs.FileExists(raw)) continue;
            var name = _fs.GetFileName(raw);
            if (_items.Any(i => string.Equals(i.RelativePath, name,
                                              StringComparison.OrdinalIgnoreCase)))
                continue; // doublon
            var size = SizeOf(raw);
            _items.Add(new KnowledgeItem(raw, name, size));
            added++;
        }
        return added;
    }

    /// <summary>
    /// Ajoute récursivement le contenu d'un dossier en préservant la
    /// structure relative (rel. path = chemin depuis <paramref name="folder"/>).
    /// </summary>
    public int AddFolder(string folder, bool recursive = true)
    {
        if (!_fs.DirectoryExists(folder)) return 0;
        int added = 0;
        foreach (var abs in _fs.EnumerateFiles(folder, "*", recursive))
        {
            if (!KnowledgeBaseFormats.IsAllowed(abs)) continue;
            var rel = _fs.GetRelativePath(folder, abs).Replace('\\', '/');
            if (_items.Any(i => string.Equals(i.RelativePath, rel,
                                              StringComparison.OrdinalIgnoreCase)))
                continue; // doublon
            _items.Add(new KnowledgeItem(abs, rel, SizeOf(abs)));
            added++;
        }
        return added;
    }

    /// <summary>Retire l'item à l'index donné (ordre stable UI).</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        _items.RemoveAt(index);
        return true;
    }

    public void Clear() => _items.Clear();

    /// <summary>
    /// Vérifie l'espace disque disponible sur la clé cible AVANT copie.
    /// Applique une marge de 10 % pour l'index reconstruit + FS overhead.
    /// </summary>
    public KnowledgeBasePreflight Preflight(long availableBytesOnUsb)
    {
        var required = TotalBytes;
        // Marge : +10 % pour l'index reconstruit + FS overhead.
        var requiredWithMargin = (long)(required * 1.10);
        return new KnowledgeBasePreflight(
            TotalBytes: required,
            FileCount: _items.Count,
            LargeFileWarnings: LargeFiles,
            EnoughSpace: availableBytesOnUsb >= requiredWithMargin,
            RequiredBytesWithMargin: requiredWithMargin,
            AvailableBytes: availableBytesOnUsb);
    }

    private long SizeOf(string path)
    {
        using var s = _fs.OpenRead(path);
        return s.Length;
    }
}

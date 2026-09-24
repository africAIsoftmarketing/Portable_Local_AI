// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : localise la racine d'une master copy à partir du dossier choisi
//           par l'opérateur. Une release décompressée contient un dossier
//           intermédiaire (AfricAIsoft-Portable-vX.Y.Z/) : si l'opérateur
//           sélectionne le dossier PARENT, la racine réelle est retrouvée
//           automatiquement (un seul niveau, un seul candidat).
//           Service pur, sans effet de bord, testable sur InMemoryFileSystem.
// Version : 0.6.0 (2026-09-24) — ajout pur, aucun service existant modifié.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class MasterCopyLocator
{
    private readonly IFileSystem _fs;

    public MasterCopyLocator(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <summary>
    /// true si <paramref name="path"/> contient TOUS les dossiers obligatoires
    /// (<see cref="PortableLayout.RequiredRootDirectories"/>). Contrôle rapide
    /// de forme : la validation complète reste celle de MasterCopyValidator.
    /// </summary>
    public bool LooksLikeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fs.DirectoryExists(path))
            return false;
        return PortableLayout.RequiredRootDirectories.All(d =>
            _fs.DirectoryExists(_fs.CombinePath(path, d)));
    }

    /// <summary>
    /// Renvoie la racine à utiliser pour <paramref name="selectedPath"/> :
    /// le dossier lui-même s'il ressemble à une master copy ; sinon son UNIQUE
    /// sous-dossier qui y ressemble ; sinon le dossier choisi tel quel (la
    /// validation détaillée signalera alors précisément ce qui manque).
    /// Ne lève jamais d'exception.
    /// </summary>
    public string Resolve(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return selectedPath;
        try
        {
            if (!_fs.DirectoryExists(selectedPath) || LooksLikeRoot(selectedPath))
                return selectedPath;

            var candidates = _fs.EnumerateDirectories(selectedPath)
                                .Where(LooksLikeRoot)
                                .Take(2)
                                .ToList();
            return candidates.Count == 1 ? candidates[0] : selectedPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return selectedPath;
        }
    }
}

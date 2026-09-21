// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : abstraction du système de fichiers (lecture, écriture, listing).
//           Toutes les opérations du Core passent par cette interface, ce qui
//           permet de la mocker facilement dans les tests xUnit.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Abstractions;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern = "*",
                                        bool recursive = true);
    IEnumerable<string> EnumerateDirectories(string path);
    long GetFileSize(string path);
    DateTime GetLastWriteTimeUtc(string path);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination, bool overwrite = true);
    void DeleteFile(string path);
    void WriteAllText(string path, string content);
    string ReadAllText(string path);
    long GetAvailableFreeSpace(string path);
    string CombinePath(params string[] parts);
    string GetFileName(string path);
    string GetDirectoryName(string path);
    string GetRelativePath(string from, string to);
}

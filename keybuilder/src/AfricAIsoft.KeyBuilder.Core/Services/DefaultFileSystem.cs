// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : implémentation par défaut d'IFileSystem — wrapper direct de
//           System.IO. Utilisée par le WPF ; les tests utilisent un mock.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class DefaultFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IEnumerable<string> EnumerateFiles(string path, string pattern = "*",
                                              bool recursive = true) =>
        Directory.EnumerateFiles(path, pattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateDirectories(string path)
        => Directory.EnumerateDirectories(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                          bufferSize: 1024 * 1024, useAsync: true);

    public Stream OpenWrite(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                              bufferSize: 1024 * 1024, useAsync: true);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string source, string destination, bool overwrite = true)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(source, destination, overwrite);
    }

    public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }

    public void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Écriture atomique via fichier temporaire + move.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public string ReadAllText(string path) => File.ReadAllText(path);

    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return -1; }
    }

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? "";
    public string GetRelativePath(string from, string to) => Path.GetRelativePath(from, to);
}

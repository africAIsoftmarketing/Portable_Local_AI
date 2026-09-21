// Filesystem en mémoire pour les tests — pas d'I/O réelle.
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using System.Text;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs =
        new(StringComparer.OrdinalIgnoreCase) { "/" };
    private long _availableSpace = long.MaxValue;

    public InMemoryFileSystem SetAvailableSpace(long bytes)
    {
        _availableSpace = bytes; return this;
    }

    private static string Norm(string p)
        => p.Replace('\\', '/').TrimEnd('/');

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));
    public bool DirectoryExists(string path)
    {
        var n = Norm(path);
        return _dirs.Contains(n) || _files.Keys.Any(k =>
            k.StartsWith(n + "/", StringComparison.OrdinalIgnoreCase));
    }
    public IEnumerable<string> EnumerateFiles(string path, string pattern = "*",
                                               bool recursive = true)
    {
        var n = Norm(path);
        var prefix = n + "/";
        var re = pattern == "*" ? null
            : new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var k in _files.Keys.OrderBy(x => x))
        {
            if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!recursive && k[prefix.Length..].Contains('/')) continue;
            if (re is not null && !re.IsMatch(GetFileName(k))) continue;
            yield return k;
        }
    }
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var n = Norm(path); var prefix = n + "/";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _dirs)
        {
            if (!d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = d[prefix.Length..];
            if (rest.Length == 0) continue;
            var slash = rest.IndexOf('/');
            var first = slash < 0 ? rest : rest[..slash];
            set.Add(prefix + first);
        }
        foreach (var f in _files.Keys)
        {
            if (!f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = f[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0) continue;
            set.Add(prefix + rest[..slash]);
        }
        return set.OrderBy(x => x);
    }
    public long GetFileSize(string path) => _files[Norm(path)].LongLength;
    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UtcNow;
    public Stream OpenRead(string path) => new MemoryStream(_files[Norm(path)], writable: false);
    public Stream OpenWrite(string path)
    {
        var n = Norm(path);
        var ms = new MemoryStreamWithCallback(bytes => _files[n] = bytes);
        var dir = GetDirectoryName(n);
        if (!string.IsNullOrEmpty(dir)) CreateDirectory(dir);
        return ms;
    }
    public void CreateDirectory(string path)
    {
        var n = Norm(path);
        while (!string.IsNullOrEmpty(n) && n != "/")
        {
            _dirs.Add(n);
            n = GetDirectoryName(n);
        }
    }
    public void CopyFile(string source, string destination, bool overwrite = true)
    {
        var s = Norm(source); var d = Norm(destination);
        if (!_files.ContainsKey(s)) throw new FileNotFoundException(s);
        if (_files.ContainsKey(d) && !overwrite) throw new IOException("exists");
        var dir = GetDirectoryName(d);
        if (!string.IsNullOrEmpty(dir)) CreateDirectory(dir);
        _files[d] = (byte[])_files[s].Clone();
    }
    public void DeleteFile(string path) { _files.Remove(Norm(path)); }
    public void WriteAllText(string path, string content)
    {
        var n = Norm(path);
        var dir = GetDirectoryName(n);
        if (!string.IsNullOrEmpty(dir)) CreateDirectory(dir);
        _files[n] = Encoding.UTF8.GetBytes(content);
    }
    public void WriteBytes(string path, byte[] content)
    {
        var n = Norm(path); var dir = GetDirectoryName(n);
        if (!string.IsNullOrEmpty(dir)) CreateDirectory(dir);
        _files[n] = content;
    }
    public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[Norm(path)]);
    public long GetAvailableFreeSpace(string path) => _availableSpace;
    public string CombinePath(params string[] parts)
    {
        var joined = string.Join("/", parts.Select(p => p.Replace('\\', '/').Trim('/'))
                                            .Where(p => p.Length > 0));
        return "/" + joined;
    }
    public string GetFileName(string path)
    {
        var n = Norm(path);
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }
    public string GetDirectoryName(string path)
    {
        var n = Norm(path);
        var i = n.LastIndexOf('/');
        return i <= 0 ? "" : n[..i];
    }
    public string GetRelativePath(string from, string to)
    {
        var f = Norm(from).TrimStart('/'); var t = Norm(to).TrimStart('/');
        if (t.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase))
            return t[(f.Length + 1)..];
        return t;
    }

    private sealed class MemoryStreamWithCallback : MemoryStream
    {
        private readonly Action<byte[]> _cb;
        public MemoryStreamWithCallback(Action<byte[]> cb) { _cb = cb; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _cb(ToArray());
            base.Dispose(disposing);
        }
    }
}

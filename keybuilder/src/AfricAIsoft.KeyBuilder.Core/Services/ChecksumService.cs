// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : calcul SHA-256 en streaming (mémoire O(1) même pour 20 GB).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Security.Cryptography;
using System.Text;
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class ChecksumService
{
    private readonly IFileSystem _fs;
    private const int BufferSize = 1024 * 1024;   // 1 MB

    public ChecksumService(IFileSystem fs) => _fs = fs;

    /// <summary>Hash SHA-256 hex minuscule d'un fichier, avec progression.</summary>
    public async Task<string> ComputeFileAsync(
        string path,
        IProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        using var stream = _fs.OpenRead(path);
        long total = _fs.GetFileSize(path);
        long done = 0;
        var buffer = new byte[BufferSize];
        var swStart = DateTime.UtcNow;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            done += read;
            if (progress is not null)
            {
                var elapsed = (DateTime.UtcNow - swStart).TotalSeconds;
                var speed = elapsed > 0 ? done / elapsed : 0;
                var remainSec = speed > 0 ? (total - done) / speed : (double?)null;
                progress.ReportFile(path, done, total, speed,
                    remainSec.HasValue ? TimeSpan.FromSeconds(remainSec.Value) : null);
            }
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return ToHex(sha.Hash!);
    }

    public string ComputeString(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return ToHex(hash);
    }

    public static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

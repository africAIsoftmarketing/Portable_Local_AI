// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : validation minimale d'un fichier .gguf.
//           Vérifie le magic "GGUF" (0x47 0x47 0x55 0x46) sur les 4 premiers
//           octets + lit la version (uint32 LE) et confirme qu'elle appartient
//           à la plage officielle {1, 2, 3}. Ne parse pas les tenseurs.
// Réfs    : https://github.com/ggerganov/ggml/blob/master/docs/gguf.md
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using System.Text.RegularExpressions;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class GgufValidator
{
    private readonly IFileSystem _fs;
    public GgufValidator(IFileSystem fs) => _fs = fs;

    private static readonly byte[] Magic = new byte[] { 0x47, 0x47, 0x55, 0x46 };
    private static readonly uint[] AcceptedVersions = new uint[] { 1u, 2u, 3u };

    public ModelGgufInfo Inspect(string path)
    {
        long size = _fs.GetFileSize(path);
        string name = _fs.GetFileName(path);
        var quant = ParseQuantization(name);
        var pars = ParseParameters(name);

        if (size < 8)
        {
            return new ModelGgufInfo(name, path, size, quant, pars,
                                     ValidHeader: false, HeaderVersion: 0);
        }

        using var s = _fs.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        int read = 0;
        while (read < 8)
        {
            int n = s.Read(header[read..]);
            if (n == 0) break;
            read += n;
        }
        if (read < 8)
        {
            return new ModelGgufInfo(name, path, size, quant, pars, false, 0);
        }
        bool magicOk = header[0] == Magic[0] && header[1] == Magic[1]
                    && header[2] == Magic[2] && header[3] == Magic[3];
        uint version = (uint)(header[4] | (header[5] << 8)
                            | (header[6] << 16) | (header[7] << 24));
        bool valid = magicOk && Array.IndexOf(AcceptedVersions, version) >= 0;
        return new ModelGgufInfo(name, path, size, quant, pars, valid, version);
    }

    private static readonly string[] QuantTokens = new[]
    {
        "q2_k","q3_k_s","q3_k_m","q3_k_l","q4_0","q4_k_s","q4_k_m",
        "q5_0","q5_k_s","q5_k_m","q6_k","q8_0","f16","f32",
    };

    private static string? ParseQuantization(string fileName)
    {
        var low = fileName.ToLowerInvariant();
        foreach (var tok in QuantTokens)
            if (low.Contains(tok, StringComparison.Ordinal)) return tok.ToUpperInvariant();
        return null;
    }

    private static string? ParseParameters(string fileName)
    {
        var m = Regex.Match(fileName.ToLowerInvariant(),
                            @"[-_](\d+(?:\.\d+)?)\s*b\b");
        return m.Success ? m.Groups[1].Value + "B" : null;
    }
}

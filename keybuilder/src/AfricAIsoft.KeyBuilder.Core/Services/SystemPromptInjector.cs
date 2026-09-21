// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : injection du system prompt personnalisé dans la copie sur clé.
//           - Écrit config/system_prompt.txt avec le contenu fourni.
//           - Ne modifie PAS la source (idempotent, non-destructif).
//           - Retourne le hash SHA-256 UTF-8 du prompt injecté.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class SystemPromptInjector
{
    private readonly IFileSystem _fs;
    private readonly ChecksumService _checksum;

    public SystemPromptInjector(IFileSystem fs, ChecksumService checksum)
    {
        _fs = fs;
        _checksum = checksum;
    }

    /// <summary>Écrit config/system_prompt.txt sur la clé et retourne son SHA-256.</summary>
    public string Inject(string targetRoot, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("prompt vide", nameof(prompt));
        var dir = _fs.CombinePath(targetRoot, "config");
        _fs.CreateDirectory(dir);
        var path = _fs.CombinePath(dir, "system_prompt.txt");
        _fs.WriteAllText(path, prompt);
        return _checksum.ComputeString(prompt);
    }
}

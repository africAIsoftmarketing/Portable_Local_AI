// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : abstraction du formatage d'une clé USB (exFAT / NTFS / ext4).
//           Ext4 n'est PAS supporté nativement sous Windows — l'implémentation
//           Windows renvoie NotSupportedResult pour ce cas et l'UI doit
//           afficher un message explicite (voir US-FMT-01 du cahier).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Abstractions;

public interface IDriveFormatter
{
    /// <summary>Formate la clé cible ; peut nécessiter une élévation UAC.</summary>
    Task<FormatResult> FormatAsync(
        UsbDrive drive,
        FileSystemKind fileSystem,
        string volumeLabel,
        IProgressReporter progress,
        CancellationToken ct);

    /// <summary>Indique si le formatage est supporté sous l'OS courant.</summary>
    bool Supports(FileSystemKind fileSystem);
}

public enum FileSystemKind { ExFat, Ntfs, Ext4 }

public sealed record FormatResult(
    bool Success,
    string Message,
    bool RequiredElevation,
    bool NotSupported = false);

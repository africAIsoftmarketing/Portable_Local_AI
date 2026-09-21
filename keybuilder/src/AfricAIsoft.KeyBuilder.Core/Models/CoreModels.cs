// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : modèles de données du Core (POCO). Regroupés pour lisibilité.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Core.Models;

/// <summary>Clé USB physique détectée sur la machine.</summary>
public sealed record UsbDrive(
    string DeviceId,        // \\.\PHYSICALDRIVE2 (Windows) ou /dev/sdX (Linux)
    string RootPath,        // "E:\\" ou "/media/usb0"
    string VolumeLabel,
    string Model,           // "SanDisk Ultra USB 3.0"
    long CapacityBytes,
    long FreeSpaceBytes,
    FileSystemKind? FileSystem,
    bool IsRemovable,
    bool IsReady);

/// <summary>Métadonnées d'un modèle GGUF présent dans models/.</summary>
public sealed record ModelGgufInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    string? Quantization,   // "Q4_K_M", "Q8_0", ...
    string? ParametersHint, // "3B", "7B", ...
    bool ValidHeader,       // "GGUF" magic présent + version reconnue
    uint HeaderVersion);

/// <summary>Un skill / pack MCP disponible dans la source.</summary>
public sealed record SkillPack(
    string Id,              // "accounting", "cybersec", "rag", "general"
    string DisplayName,
    string RelativeDir,     // "mcp-servers/accounting"
    long SizeBytes,
    IReadOnlyList<string> Tools);

/// <summary>Plateforme cible (binaires embarqués).</summary>
public enum TargetPlatform
{
    WindowsX64,
    LinuxX64,
    LinuxArm64,
    MacOsX64,
    MacOsArm64,
}

public static class TargetPlatformExtensions
{
    public static string ToBinaryDir(this TargetPlatform p) => p switch
    {
        TargetPlatform.WindowsX64 => "bin/windows-x64",
        TargetPlatform.LinuxX64   => "bin/linux-x64",
        TargetPlatform.LinuxArm64 => "bin/linux-arm64",
        TargetPlatform.MacOsX64   => "bin/macos-x64",
        TargetPlatform.MacOsArm64 => "bin/macos-arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    public static string DisplayName(this TargetPlatform p) => p switch
    {
        TargetPlatform.WindowsX64 => "Windows x64",
        TargetPlatform.LinuxX64   => "Linux x64",
        TargetPlatform.LinuxArm64 => "Linux ARM64",
        TargetPlatform.MacOsX64   => "macOS Intel",
        TargetPlatform.MacOsArm64 => "macOS Apple Silicon",
        _ => p.ToString(),
    };
}

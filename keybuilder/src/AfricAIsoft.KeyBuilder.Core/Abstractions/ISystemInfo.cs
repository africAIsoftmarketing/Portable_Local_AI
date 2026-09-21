// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : informations système utiles au Key Builder (OS, arch, espace disque
//           temporaire, chemin utilisateur). Abstraction pour tests.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Abstractions;

public interface ISystemInfo
{
    string OperatingSystem { get; }        // "Windows 10", "Windows 11", ...
    string Architecture { get; }           // "x64", "arm64"
    string UserName { get; }
    string MachineName { get; }
    long AvailableTempSpaceBytes { get; }
    Version RuntimeVersion { get; }
    bool IsElevated { get; }               // true si le process tourne en admin
}

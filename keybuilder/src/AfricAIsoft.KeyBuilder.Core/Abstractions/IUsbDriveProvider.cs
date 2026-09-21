// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : abstraction énumérant les clés USB détectées sur le système hôte.
//           L'implémentation WPF utilise WMI (Win32_DiskDrive/Win32_LogicalDisk),
//           mais tout le code Core reste agnostique et testable.
// Auteur  : AfricAIsoft
// Licence : MIT
// Date    : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Abstractions;

public interface IUsbDriveProvider
{
    /// <summary>Liste les clés amovibles actuellement branchées.</summary>
    IReadOnlyList<UsbDrive> Enumerate();

    /// <summary>Événement émis lorsqu'une clé est branchée ou débranchée.</summary>
    event EventHandler<UsbDriveChangedEventArgs>? DrivesChanged;

    /// <summary>Démarre le monitoring temps réel (WMI event watcher côté Windows).</summary>
    void StartMonitoring();

    /// <summary>Arrête proprement le monitoring.</summary>
    void StopMonitoring();
}

public sealed class UsbDriveChangedEventArgs : EventArgs
{
    public IReadOnlyList<UsbDrive> Current { get; }
    public UsbDriveChangedEventArgs(IReadOnlyList<UsbDrive> current) => Current = current;
}

// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : détection des clés USB via WMI (Win32_DiskDrive/Win32_LogicalDisk)
//           + monitoring temps réel via Win32_DeviceChangeEvent.
//           Compile UNIQUEMENT sous Windows (utilise System.Management).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Management;
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Wpf.Providers;

public sealed class WmiUsbDriveProvider : IUsbDriveProvider, IDisposable
{
    private ManagementEventWatcher? _watcher;
    public event EventHandler<UsbDriveChangedEventArgs>? DrivesChanged;

    public IReadOnlyList<UsbDrive> Enumerate()
    {
        var results = new List<UsbDrive>();
        // Récupère les disques amovibles (InterfaceType = "USB").
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'");
        foreach (ManagementObject disk in searcher.Get())
        {
            var deviceId = (string?)disk["DeviceID"] ?? "";
            var model = (string?)disk["Model"] ?? "";
            long capacity = System.Convert.ToInt64(disk["Size"] ?? 0);
            // Chemin vers partitions/volumes logiques.
            foreach (ManagementObject part in disk.GetRelated("Win32_DiskPartition"))
            {
                foreach (ManagementObject drv in part.GetRelated("Win32_LogicalDisk"))
                {
                    var root = ((string?)drv["DeviceID"] ?? "") + "\\";
                    var label = (string?)drv["VolumeName"] ?? "";
                    var fsName = ((string?)drv["FileSystem"] ?? "").ToUpperInvariant();
                    var free = System.Convert.ToInt64(drv["FreeSpace"] ?? 0);
                    var fsKind = fsName switch
                    {
                        "EXFAT" => (FileSystemKind?)FileSystemKind.ExFat,
                        "NTFS"  => (FileSystemKind?)FileSystemKind.Ntfs,
                        _        => null,
                    };
                    results.Add(new UsbDrive(deviceId, root, label, model,
                                              capacity, free, fsKind, true, true));
                }
            }
        }
        return results;
    }

    public void StartMonitoring()
    {
        // Événements de branchement/débranchement (EventType 2 = arrival, 3 = removal).
        var q = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType=2 OR EventType=3");
        _watcher = new ManagementEventWatcher(q);
        _watcher.EventArrived += (_, _) => DrivesChanged?.Invoke(this, new(Enumerate()));
        try { _watcher.Start(); } catch { /* WMI non disponible : ignore. */ }
    }

    public void StopMonitoring() { _watcher?.Stop(); _watcher?.Dispose(); _watcher = null; }
    public void Dispose() => StopMonitoring();
}

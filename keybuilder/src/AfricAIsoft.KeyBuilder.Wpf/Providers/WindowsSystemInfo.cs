// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : ISystemInfo pour Windows.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Wpf.Providers;

public sealed class WindowsSystemInfo : ISystemInfo
{
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    public string UserName => Environment.UserName;
    public string MachineName => Environment.MachineName;
    public long AvailableTempSpaceBytes
    {
        get
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!).AvailableFreeSpace; }
            catch { return -1; }
        }
    }
    public Version RuntimeVersion => Environment.Version;
    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}

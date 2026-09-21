// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : formatage d'une clé via DiskPart scripté (exFAT / NTFS).
//           Élévation UAC demandée UNIQUEMENT ici via un relaunch `runas`
//           d'un binaire d'aide (voir README). Ext4 n'est PAS supporté sous
//           Windows — la méthode Supports() renvoie false et FormatAsync
//           produit un FormatResult NotSupported=true avec message explicite.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Diagnostics;
using System.IO;
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Wpf.Providers;

public sealed class DiskPartFormatter : IDriveFormatter
{
    public bool Supports(FileSystemKind fs)
        => fs == FileSystemKind.ExFat || fs == FileSystemKind.Ntfs;

    public async Task<FormatResult> FormatAsync(
        UsbDrive drive, FileSystemKind fs, string label,
        IProgressReporter progress, CancellationToken ct)
    {
        if (!Supports(fs))
        {
            return new FormatResult(false,
                "Le formatage ext4 n'est pas supporté nativement sous Windows. " +
                "Fournissez une clé déjà formatée en ext4 depuis Linux, ou choisissez exFAT/NTFS.",
                RequiredElevation: false, NotSupported: true);
        }

        var fsLabel = fs == FileSystemKind.ExFat ? "exFAT" : "NTFS";
        var driveLetter = drive.RootPath.TrimEnd('\\', '/').TrimEnd(':') + ":";

        // Génère un script diskpart temporaire.
        var script = Path.Combine(Path.GetTempPath(), $"kb-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(script,
            $"select volume {driveLetter[0]}\n" +
            $"format fs={fsLabel.ToLowerInvariant()} label={label} quick\n" +
            "exit\n", ct);

        // Relance en mode élevé (UAC) si le processus courant n'est pas admin.
        var psi = new ProcessStartInfo("diskpart", $"/s \"{script}\"")
        {
            UseShellExecute = true,   // requis pour Verb=runas
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        bool elevated = false;
        try
        {
            progress.ReportStep("format",
                $"Formatage en {fsLabel} (élévation UAC requise…)");
            using var proc = Process.Start(psi);
            if (proc is null) throw new InvalidOperationException("diskpart start failed");
            elevated = true;
            await proc.WaitForExitAsync(ct);
            File.Delete(script);
            if (proc.ExitCode != 0)
                return new FormatResult(false, $"diskpart exit={proc.ExitCode}", elevated);
            return new FormatResult(true, "OK", elevated);
        }
        catch (Exception ex)
        {
            File.Delete(script);
            return new FormatResult(false, ex.Message, elevated);
        }
    }
}

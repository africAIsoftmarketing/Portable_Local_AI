// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : tests xUnit du MasterCopyValidator — validation de structure et
//           vérification CHECKSUMS.sha256 sur InMemoryFileSystem.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using System.Security.Cryptography;
using System.Text;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class MasterCopyValidatorTests
{
    private static InMemoryFileSystem Seed(bool skipConfig = false, bool skipLauncher = false,
                                            bool badPlatform = false, bool emptyBackend = false)
    {
        var fs = new InMemoryFileSystem();
        // Dossiers obligatoires — on peut sauter config/ pour tester la détection
        // d'un dossier obligatoire manquant (config/ n'accueille pas de fichier
        // dans ce fixture, donc son absence est réellement détectable).
        foreach (var d in PortableLayout.RequiredRootDirectories)
            if (!(skipConfig && d == "config"))
                fs.CreateDirectory($"/master/{d}");
        // Launcher
        if (!skipLauncher)
            fs.WriteAllText("/master/start-linux.sh", "#!/bin/sh\n");
        // Plateforme valide + backend cpu.
        if (!emptyBackend)
            fs.WriteAllText("/master/bin/linux-x86_64/cpu/llama-server", "ELF");
        else
            fs.CreateDirectory("/master/bin/linux-x86_64/cpu");
        fs.CreateDirectory("/master/bin/linux-x86_64/python/wheels");
        // Windows CUDA
        fs.WriteAllText("/master/bin/windows/cuda/llama-server.exe", "MZ");
        fs.CreateDirectory("/master/bin/windows/python/wheels");
        // Plateforme non reconnue
        if (badPlatform)
            fs.WriteAllText("/master/bin/macos-arm64/cpu/x", "?");
        return fs;
    }

    [Fact]
    public async Task Valid_structure_yields_isValid_true()
    {
        var fs = Seed();
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.IsValid.Should().BeTrue();
        r.Errors.Should().BeEmpty();
        r.DetectedPlatforms.Should().BeEquivalentTo("linux-x86_64", "windows");
        r.DetectedBackends["linux-x86_64"].Should().Contain("cpu");
        r.DetectedBackends["windows"].Should().Contain("cuda");
    }

    [Fact]
    public async Task Missing_required_dir_yields_error()
    {
        var fs = Seed(skipConfig: true);
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(i => i.Code == "REQUIRED_DIR_MISSING");
    }

    [Fact]
    public async Task Missing_launcher_yields_error()
    {
        var fs = Seed(skipLauncher: true);
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(i => i.Code == "NO_LAUNCHER");
    }

    [Fact]
    public async Task Unknown_platform_yields_warning_not_error()
    {
        var fs = Seed(badPlatform: true);
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.IsValid.Should().BeTrue();
        r.Warnings.Should().Contain(i => i.Code == "UNKNOWN_PLATFORM"
                                       && i.Message.Contains("macos-arm64"));
    }

    [Fact]
    public async Task Empty_backend_yields_warning()
    {
        var fs = Seed(emptyBackend: true);
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.Warnings.Should().Contain(i => i.Code == "EMPTY_BACKEND");
    }

    [Fact]
    public async Task Missing_root_yields_error()
    {
        var fs = new InMemoryFileSystem();
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/absent");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(i => i.Code == "ROOT_MISSING");
    }

    [Fact]
    public async Task Missing_wheels_yields_info()
    {
        var fs = Seed();
        // Retire wheels/ pour linux-x86_64 en remplaçant par un dossier vide sans wheels
        // (In-memory : on ne peut pas supprimer un dir, mais l'absence est déjà testée si on omet).
        var fs2 = new InMemoryFileSystem();
        foreach (var d in PortableLayout.RequiredRootDirectories)
            fs2.CreateDirectory($"/m/{d}");
        fs2.WriteAllText("/m/start-linux.sh", "#!/bin/sh\n");
        fs2.WriteAllText("/m/bin/linux-x86_64/cpu/llama-server", "ELF");
        // pas de bin/linux-x86_64/python/wheels
        var v = new MasterCopyValidator(fs2, new ChecksumService(fs2));
        var r = await v.ValidateAsync("/m");
        r.Issues.Should().Contain(i => i.Code == "WHEELS_MISSING");
    }

    [Fact]
    public async Task Absent_checksums_yields_info()
    {
        var fs = Seed();
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.ChecksumsFilePresent.Should().BeFalse();
        r.ChecksumsVerified.Should().BeFalse();
        r.Issues.Should().Contain(i => i.Code == "CHECKSUMS_ABSENT");
    }

    [Fact]
    public async Task Valid_checksums_yields_verified_true()
    {
        var fs = Seed();
        // Écrit un CHECKSUMS.sha256 correct pour tous les fichiers.
        var lines = new StringBuilder();
        foreach (var file in fs.EnumerateFiles("/master"))
        {
            var bytes = ReadAll(fs, file);
            var hash = Sha256Hex(bytes);
            var rel = file[("/master/".Length)..];
            lines.Append(hash).Append("  ").Append(rel).Append('\n');
        }
        fs.WriteAllText("/master/CHECKSUMS.sha256", lines.ToString());
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.ChecksumsFilePresent.Should().BeTrue();
        r.ChecksumsVerified.Should().BeTrue();
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Tampered_checksums_yields_mismatch_error()
    {
        var fs = Seed();
        // Hash volontairement faux.
        var badLine = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                     + "  start-linux.sh\n";
        fs.WriteAllText("/master/CHECKSUMS.sha256", badLine);
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.ChecksumsVerified.Should().BeFalse();
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(i => i.Code == "CHECKSUM_MISMATCH");
    }

    [Fact]
    public async Task Checksums_target_missing_yields_error()
    {
        var fs = Seed();
        fs.WriteAllText("/master/CHECKSUMS.sha256",
            "0000000000000000000000000000000000000000000000000000000000000000  ghost.txt\n");
        var v = new MasterCopyValidator(fs, new ChecksumService(fs));
        var r = await v.ValidateAsync("/master");
        r.Errors.Should().Contain(i => i.Code == "CHECKSUM_TARGET_MISSING");
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static byte[] ReadAll(InMemoryFileSystem fs, string path)
    {
        using var s = fs.OpenRead(path);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
    private static string Sha256Hex(byte[] bytes)
    {
        var sb = new StringBuilder(64);
        foreach (var b in SHA256.HashData(bytes)) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

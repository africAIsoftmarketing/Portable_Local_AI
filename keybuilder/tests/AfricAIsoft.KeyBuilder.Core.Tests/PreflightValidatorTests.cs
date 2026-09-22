using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class PreflightValidatorTests
{
    private static (InMemoryFileSystem fs, BuildPlan plan, UsbDrive drive) MakeRepo(
        long modelBytes = 1_000_000, long capacityBytes = 32L * 1024 * 1024 * 1024)
    {
        var fs = new InMemoryFileSystem();
        foreach (var d in new[] { "app", "ui", "config", "scripts", "mcp-servers", "models" })
            fs.CreateDirectory("/src/" + d);
        fs.WriteAllText("/src/app/main.py", "#");
        fs.WriteAllText("/src/ui/index.html", "#");
        fs.WriteAllText("/src/config/settings.json", "{}");
        fs.WriteAllText("/src/scripts/start.sh", "#");
        fs.CreateDirectory("/src/mcp-servers/accounting");
        fs.WriteAllText("/src/mcp-servers/accounting/server.py", "#");
        // Modèle GGUF valide
        var buf = new byte[modelBytes];
        buf[0] = 0x47; buf[1] = 0x47; buf[2] = 0x55; buf[3] = 0x46;
        buf[4] = 3;
        fs.WriteBytes("/src/models/m-3b.gguf", buf);
        // Binaire (nommage plateformes verrouillé — cf. PortableLayout).
        fs.WriteBytes("/src/bin/windows/llama.exe", new byte[100]);
        var plan = new BuildPlan
        {
            SourceRoot = "/src",
            TargetRoot = "/usb",
            ModelFile = "m-3b.gguf",
            IncludedSkillIds = new() { "accounting" },
            TargetPlatforms = new() { TargetPlatform.WindowsX64 },
            FormatWith = null,
        };
        var drive = new UsbDrive(
            "\\\\.\\PHYSICALDRIVE1", "/usb", "AFRICAI", "SanDisk",
            capacityBytes, capacityBytes, FileSystemKind.ExFat, true, true);
        return (fs, plan, drive);
    }

    [Fact]
    public void Happy_path_can_proceed()
    {
        var (fs, plan, drive) = MakeRepo();
        var est = new SizeEstimator(fs);
        var gguf = new GgufValidator(fs);
        var pre = new PreflightValidator(fs, est, gguf).Validate(plan, drive);
        pre.CanProceed.Should().BeTrue();
    }

    [Fact]
    public void Missing_model_produces_error()
    {
        var (fs, plan, drive) = MakeRepo();
        plan.ModelFile = "nope.gguf";
        var pre = new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs))
            .Validate(plan, drive);
        pre.CanProceed.Should().BeFalse();
        pre.Issues.Should().Contain(i => i.Code == "MODEL_MISSING");
    }

    [Fact]
    public void Small_drive_rejects_build()
    {
        var (fs, plan, drive) = MakeRepo(modelBytes: 500_000_000);
        drive = drive with { CapacityBytes = 100_000, FreeSpaceBytes = 100_000 };
        var pre = new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs))
            .Validate(plan, drive);
        pre.CanProceed.Should().BeFalse();
        pre.Issues.Should().Contain(i => i.Code == "DRIVE_TOO_SMALL");
    }

    [Fact]
    public void Missing_source_dir_is_error()
    {
        var (fs, plan, drive) = MakeRepo();
        // Retire toutes les entrées models/*.
        foreach (var f in fs.EnumerateFiles("/src/models").ToList()) fs.DeleteFile(f);
        var pre = new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs))
            .Validate(plan, drive);
        pre.Issues.Should().Contain(i => i.Code == "MODEL_MISSING");
    }
}

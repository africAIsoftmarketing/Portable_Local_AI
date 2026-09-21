using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class ReportGeneratorTests
{
    [Fact]
    public void Generates_html_with_required_fields()
    {
        var fs = new InMemoryFileSystem();
        var plan = new BuildPlan
        {
            SerialNumber = "abcd1234-ef56-7890",
            OperatorName = "Alice",
            ClientId = "ACME-01",
            ModelFile = "m-3b.gguf",
            IncludedSkillIds = new() { "accounting", "rag" },
            TargetPlatforms = new() { TargetPlatform.WindowsX64 },
            VolumeLabel = "AFRICA_KEY",
        };
        var manifest = new BuildManifest
        {
            SerialNumber = plan.SerialNumber,
            ModelSha256 = "deadbeef",
            SystemPromptSha256 = "cafebabe",
            Files = new()
            {
                new ManifestEntry("app/main.py", 1000, new string('a', 64), "core"),
                new ManifestEntry("models/m-3b.gguf", 2_000_000, new string('b', 64), "model"),
            },
            TotalBytes = 2_001_000,
        };
        var result = new BuildResult
        {
            Success = true, FilesCopied = 2, FilesVerified = 2, FilesFailed = 0,
            TotalBytes = manifest.TotalBytes,
            StartedAtUtc = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc),
            EndedAtUtc   = new DateTime(2026, 8, 24, 10, 5, 0, DateTimeKind.Utc),
            SmokeTestOutcome = "OK - port répond en 12s",
        };
        var path = new ReportGenerator(fs).Generate("/usb", plan, manifest, result);
        path.Should().EndWith("production-report.html");
        var html = fs.ReadAllText(path);
        html.Should().Contain("abcd1234-ef56-7890");
        html.Should().Contain("Alice");
        html.Should().Contain("ACME-01");
        html.Should().Contain("m-3b.gguf");
        html.Should().Contain("accounting, rag");
        html.Should().Contain("Windows x64");
        html.Should().Contain("cafebabe");
        html.Should().Contain("OK - port répond");
        html.Should().Contain("<html");
    }

    [Fact]
    public void Marks_integrity_KO_when_failed_files()
    {
        var fs = new InMemoryFileSystem();
        var plan = new BuildPlan { SerialNumber = "s", ModelFile = "m.gguf" };
        var mf = new BuildManifest();
        var res = new BuildResult { FilesFailed = 3 };
        var html = fs.ReadAllText(new ReportGenerator(fs).Generate("/usb", plan, mf, res));
        html.Should().Contain(">KO<");
    }
}

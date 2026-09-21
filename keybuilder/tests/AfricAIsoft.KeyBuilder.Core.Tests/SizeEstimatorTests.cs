using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class SizeEstimatorTests
{
    private static (InMemoryFileSystem fs, BuildPlan plan) MakeRepo()
    {
        var fs = new InMemoryFileSystem();
        // Structure minimale
        fs.WriteAllText("/src/app/main.py", new string('x', 1000));
        fs.WriteAllText("/src/ui/index.html", new string('x', 2000));
        fs.WriteAllText("/src/config/settings.json", "{}");
        fs.WriteAllText("/src/scripts/start.sh", "#");
        fs.WriteAllText("/src/docs/README.md", "#");
        fs.WriteAllText("/src/skills/registry.json", "{}");
        // Modèle
        fs.WriteBytes("/src/models/m-3b-q4_k_m.gguf", new byte[2_000_000]);
        // Skills MCP
        fs.WriteAllText("/src/mcp-servers/_shared/base.py", new string('x', 500));
        fs.WriteAllText("/src/mcp-servers/accounting/server.py", new string('x', 3000));
        fs.WriteAllText("/src/mcp-servers/cybersec/server.py", new string('x', 4000));
        // Binaires
        fs.WriteBytes("/src/bin/windows-x64/llama-server.exe", new byte[50_000]);
        fs.WriteBytes("/src/bin/linux-x64/llama-server",       new byte[40_000]);
        var plan = new BuildPlan
        {
            SourceRoot = "/src",
            ModelFile = "m-3b-q4_k_m.gguf",
            IncludedSkillIds = new() { "accounting" },
            TargetPlatforms = new() { TargetPlatform.WindowsX64 },
        };
        return (fs, plan);
    }

    [Fact]
    public void Total_bytes_includes_core_model_included_skills_and_selected_platforms()
    {
        var (fs, plan) = MakeRepo();
        var est = new SizeEstimator(fs).Estimate(plan);
        // Cœur ≈ 1000+2000+2+1+1+2 ; modèle 2 000 000 ; skill accounting 3000 +
        // _shared 500 ; binaire windows 50 000. cybersec (4000) exclu.
        est.Breakdown.Should().ContainKey("model:m-3b-q4_k_m.gguf");
        est.Breakdown.Should().ContainKey("skill:accounting");
        est.Breakdown.Should().ContainKey("skill:_shared");
        est.Breakdown.Should().ContainKey("binary:WindowsX64");
        est.Breakdown.Should().NotContainKey("skill:cybersec");
        est.Breakdown.Should().NotContainKey("binary:LinuxX64");
        est.ContentBytes.Should().BeGreaterThan(2_050_000);   // au moins modèle + accounting + shared
        est.TotalBytes.Should().BeGreaterThan(est.ContentBytes);
    }

    [Fact]
    public void Adding_a_platform_grows_the_estimate()
    {
        var (fs, plan) = MakeRepo();
        var before = new SizeEstimator(fs).Estimate(plan).TotalBytes;
        plan.TargetPlatforms.Add(TargetPlatform.LinuxX64);
        var after = new SizeEstimator(fs).Estimate(plan).TotalBytes;
        after.Should().BeGreaterThan(before);
    }
}

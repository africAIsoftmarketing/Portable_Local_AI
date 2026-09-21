using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class SkillFilterTests
{
    [Fact]
    public void Compute_keeps_included_and_shared_ignores_others()
    {
        var fs = new InMemoryFileSystem();
        foreach (var s in new[] { "_shared", "_template", "accounting", "cybersec", "rag", "general" })
            fs.CreateDirectory("/src/mcp-servers/" + s);
        var res = new SkillFilter(fs).Compute("/src", new[] { "accounting", "rag" });
        res.ToCopy.Should().Contain(new[] { "_shared", "_template", "accounting", "rag" });
        res.ToIgnore.Should().Contain(new[] { "cybersec", "general" });
    }

    [Fact]
    public void PatchMcpConfig_removes_disabled_servers()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/usb/config/mcp.json", """
        {
          "servers": {
            "accounting": {"command": "python", "args": ["a"]},
            "cybersec":   {"command": "python", "args": ["c"]},
            "rag":        {"command": "python", "args": ["r"]}
          },
          "timeout": 30
        }
        """);
        new SkillFilter(fs).PatchMcpConfig("/usb", new[] { "accounting" });
        var txt = fs.ReadAllText("/usb/config/mcp.json");
        txt.Should().Contain("accounting").And.NotContain("cybersec").And.NotContain("rag");
        txt.Should().Contain("timeout");
    }
}

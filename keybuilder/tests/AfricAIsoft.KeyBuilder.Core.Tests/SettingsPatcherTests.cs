using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class SettingsPatcherTests
{
    [Fact]
    public void Applies_model_path_and_serial_when_settings_exists()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/usb/config/settings.json", """
        {"server":{"port":8001},"model":{"context_size":8192}}
        """);
        var plan = new BuildPlan
        {
            ModelFile = "m-3b.gguf",
            ClientId = "ACME-01",
            SerialNumber = "abcd-1234",
            DisabledFeatures = new() { "batch" },
        };
        new SettingsPatcher(fs).Apply("/usb", plan);
        var txt = fs.ReadAllText("/usb/config/settings.json");
        txt.Should().Contain("models/m-3b.gguf");
        txt.Should().Contain("abcd-1234");
        txt.Should().Contain("ACME-01");
        txt.Should().Contain("batch");
        txt.Should().Contain("8192");   // conservé
    }

    [Fact]
    public void Creates_defaults_when_no_settings()
    {
        var fs = new InMemoryFileSystem();
        var plan = new BuildPlan { ModelFile = "m.gguf", Version = "1.2.3" };
        new SettingsPatcher(fs).Apply("/usb", plan);
        var txt = fs.ReadAllText("/usb/config/settings.json");
        txt.Should().Contain("models/m.gguf");
        txt.Should().Contain("1.2.3");
    }

    // ─── 0.6.1 : settings.json absent mais settings.example.json copié ──────
    [Fact]
    public void Starts_from_example_when_settings_absent()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/usb/config/settings.example.json", """
        {"server":{"port":8080,"bind_host":"127.0.0.1"},
         "model":{"path":null,"context_size":8192},
         "mcp":{"enabled":true,"skills_dir":"skills"}}
        """);
        var plan = new BuildPlan
        {
            ModelFile = "m-3b.gguf", Version = "1.0.1",
            ClientId = "ACME-01", SerialNumber = "abcd-1234",
        };
        new SettingsPatcher(fs).Apply("/usb", plan);
        var txt = fs.ReadAllText("/usb/config/settings.json");
        txt.Should().Contain("8080");            // port du modèle, pas 8001
        txt.Should().Contain("\"enabled\": true"); // MCP conservé
        txt.Should().Contain("models/m-3b.gguf");
        txt.Should().Contain("1.0.1");
        txt.Should().Contain("ACME-01");
        txt.Should().Contain("abcd-1234");
        txt.Should().NotContain("8001");
        fs.ReadAllText("/usb/config/settings.example.json")
          .Should().NotContain("m-3b.gguf");    // modèle laissé intact
    }

    [Fact]
    public void Writes_version_when_settings_exists_without_it()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/usb/config/settings.json", """{"server":{"port":8080}}""");
        new SettingsPatcher(fs).Apply("/usb", new BuildPlan { ModelFile = "m.gguf", Version = "2.0.0" });
        fs.ReadAllText("/usb/config/settings.json").Should().Contain("2.0.0");
    }
}

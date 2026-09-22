// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : tests xUnit du SelectiveCopier — copie ciblée par plateforme/backend
//           et exclusion effective des combinaisons non sélectionnées.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class SelectiveCopierTests
{
    private static InMemoryFileSystem SeedFull()
    {
        var fs = new InMemoryFileSystem();
        // Dossiers communs.
        fs.WriteAllText("/src/config/settings.example.json", "{}");
        fs.WriteAllText("/src/mcp-servers/cybersec/server.py", "print(1)");
        fs.WriteAllText("/src/ui/index.html", "<html></html>");
        fs.WriteAllText("/src/scripts/core-startup.sh", "#!/bin/sh\n");
        fs.WriteAllText("/src/skills/registry.json", "{}");
        // Fichiers racine.
        fs.WriteAllText("/src/start-linux.sh", "#!/bin/sh\n");
        fs.WriteAllText("/src/start-windows.bat", "@echo\n");
        fs.WriteAllText("/src/README.md", "# hi");
        fs.WriteAllText("/src/CHECKSUMS.sha256", "");
        // Plateformes / backends.
        fs.WriteAllText("/src/bin/linux-x86_64/cpu/llama-server", "ELF");
        fs.WriteAllText("/src/bin/linux-x86_64/cuda/llama-server", "ELF");
        fs.WriteAllText("/src/bin/linux-x86_64/python/bin/python3", "PY");
        fs.WriteAllText("/src/bin/linux-x86_64/python/wheels/foo.whl", "WHL");
        fs.WriteAllText("/src/bin/windows/cpu/llama-server.exe", "MZ");
        fs.WriteAllText("/src/bin/windows/cuda/llama-server.exe", "MZ");
        fs.WriteAllText("/src/bin/windows/python/python.exe", "MZ");
        fs.WriteAllText("/src/bin/darwin-arm64/metal/llama-server", "MO");
        fs.WriteAllText("/src/bin/darwin-arm64/python/bin/python3", "MO");
        return fs;
    }

    [Fact]
    public async Task Copies_only_selected_platform_and_backend()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        var sel = new[] { new PlatformBackend("linux-x86_64", "cuda") };
        var res = await copier.CopyAsync("/src", "/usb", sel);

        res.CopiedCount.Should().BeGreaterThan(0);
        fs.FileExists("/usb/bin/linux-x86_64/cuda/llama-server").Should().BeTrue();
        // Python doit être copié pour la plateforme sélectionnée.
        fs.FileExists("/usb/bin/linux-x86_64/python/bin/python3").Should().BeTrue();
        fs.FileExists("/usb/bin/linux-x86_64/python/wheels/foo.whl").Should().BeTrue();
        // Le backend NON sélectionné ne doit PAS être copié.
        fs.FileExists("/usb/bin/linux-x86_64/cpu/llama-server").Should().BeFalse();
        // Les autres plateformes ne doivent PAS être copiées.
        fs.DirectoryExists("/usb/bin/windows").Should().BeFalse();
        fs.DirectoryExists("/usb/bin/darwin-arm64").Should().BeFalse();
    }

    [Fact]
    public async Task Copies_common_directories_regardless_of_selection()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        var sel = new[] { new PlatformBackend("windows", "cpu") };
        await copier.CopyAsync("/src", "/usb", sel);
        fs.FileExists("/usb/config/settings.example.json").Should().BeTrue();
        fs.FileExists("/usb/mcp-servers/cybersec/server.py").Should().BeTrue();
        fs.FileExists("/usb/ui/index.html").Should().BeTrue();
        fs.FileExists("/usb/scripts/core-startup.sh").Should().BeTrue();
        fs.FileExists("/usb/skills/registry.json").Should().BeTrue();
        fs.FileExists("/usb/start-linux.sh").Should().BeTrue();
        fs.FileExists("/usb/start-windows.bat").Should().BeTrue();
        fs.FileExists("/usb/README.md").Should().BeTrue();
        fs.FileExists("/usb/CHECKSUMS.sha256").Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_selection_copies_each()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        var sel = new[]
        {
            new PlatformBackend("windows", "cuda"),
            new PlatformBackend("darwin-arm64", "metal"),
        };
        var res = await copier.CopyAsync("/src", "/usb", sel);
        fs.FileExists("/usb/bin/windows/cuda/llama-server.exe").Should().BeTrue();
        fs.FileExists("/usb/bin/darwin-arm64/metal/llama-server").Should().BeTrue();
        // Backends non sélectionnés absents.
        fs.FileExists("/usb/bin/windows/cpu/llama-server.exe").Should().BeFalse();
        // Plateforme non sélectionnée absente.
        fs.DirectoryExists("/usb/bin/linux-x86_64").Should().BeFalse();
        res.SelectedPlatforms.Should().BeEquivalentTo("windows", "darwin-arm64");
    }

    [Fact]
    public async Task Empty_selection_throws()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        Func<Task> act = () => copier.CopyAsync("/src", "/usb",
            Array.Empty<PlatformBackend>());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Missing_source_throws()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        Func<Task> act = () => copier.CopyAsync("/absent", "/usb",
            new[] { new PlatformBackend("windows", "cpu") });
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Unknown_platform_is_skipped_not_thrown()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        var sel = new[]
        {
            new PlatformBackend("windows", "cpu"),
            new PlatformBackend("macos-arm64", "metal"), // ancien nom, non reconnu
        };
        var res = await copier.CopyAsync("/src", "/usb", sel);
        res.SkippedEntries.Should().Contain(s => s.Contains("macos-arm64"));
        fs.FileExists("/usb/bin/windows/cpu/llama-server.exe").Should().BeTrue();
    }

    [Fact]
    public async Task Missing_backend_is_skipped_not_thrown()
    {
        var fs = SeedFull();
        var copier = new SelectiveCopier(fs);
        var sel = new[]
        {
            new PlatformBackend("linux-x86_64", "vulkan"), // absent de la source
            new PlatformBackend("linux-x86_64", "cpu"),
        };
        var res = await copier.CopyAsync("/src", "/usb", sel);
        res.SkippedEntries.Should().Contain(s => s.Contains("vulkan"));
        fs.FileExists("/usb/bin/linux-x86_64/cpu/llama-server").Should().BeTrue();
    }
}

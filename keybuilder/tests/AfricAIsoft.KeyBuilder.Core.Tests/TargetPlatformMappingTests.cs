// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : tests xUnit des mappings TargetPlatform → chemin bin/ verrouillé.
//           Garantit que le Core n'écrit JAMAIS un chemin bin/ divergent du
//           nommage résolu par les launchers (scripts/core-startup.sh) et de
//           PortableLayout.SupportedPlatforms.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class TargetPlatformMappingTests
{
    [Theory]
    [InlineData(TargetPlatform.WindowsX64, "bin/windows")]
    [InlineData(TargetPlatform.LinuxX64,   "bin/linux-x86_64")]
    [InlineData(TargetPlatform.LinuxArm64, "bin/linux-aarch64")]
    [InlineData(TargetPlatform.MacOsX64,   "bin/darwin-x86_64")]
    [InlineData(TargetPlatform.MacOsArm64, "bin/darwin-arm64")]
    public void ToBinaryDir_uses_locked_naming(TargetPlatform p, string expected)
    {
        p.ToBinaryDir().Should().Be(expected);
    }

    [Fact]
    public void All_target_platforms_map_to_supported_platform_names()
    {
        // Chaque TargetPlatform doit produire un chemin dont le segment plate-forme
        // (après "bin/") figure dans PortableLayout.SupportedPlatforms — sinon
        // les launchers ne trouveront pas les binaires sur la clé.
        foreach (TargetPlatform p in Enum.GetValues<TargetPlatform>())
        {
            var dir = p.ToBinaryDir();
            dir.Should().StartWith("bin/");
            var platName = dir["bin/".Length..];
            PortableLayout.SupportedPlatforms.Should().Contain(platName,
                $"le mapping TargetPlatform.{p} produit '{dir}' — segment '{platName}' "
                + "absent de PortableLayout.SupportedPlatforms (nommage divergerait des launchers).");
        }
    }

    [Fact]
    public void No_target_platform_uses_legacy_naming()
    {
        var legacy = new[] { "macos-arm64", "macos-x86_64", "linux-arm64",
                              "linux-x64", "windows-x64", "macos-x64" };
        foreach (TargetPlatform p in Enum.GetValues<TargetPlatform>())
        {
            var dir = p.ToBinaryDir();
            legacy.Should().NotContain(l => dir.EndsWith("/" + l),
                $"TargetPlatform.{p} → '{dir}' utilise encore un nom hérité.");
        }
    }

    [Fact]
    public void DisplayName_is_never_empty()
    {
        foreach (TargetPlatform p in Enum.GetValues<TargetPlatform>())
            p.DisplayName().Should().NotBeNullOrWhiteSpace();
    }
}

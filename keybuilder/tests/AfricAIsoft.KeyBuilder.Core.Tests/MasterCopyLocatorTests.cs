// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : tests xUnit du MasterCopyLocator — détection de la racine d'une
//           master copy (dossier choisi directement, ou dossier parent issu
//           de la décompression d'une release) sur InMemoryFileSystem.
// Version : 0.6.0 (2026-09-24)
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class MasterCopyLocatorTests
{
    private static void SeedRoot(InMemoryFileSystem fs, string root, string? skip = null)
    {
        foreach (var d in PortableLayout.RequiredRootDirectories)
            if (d != skip)
                fs.CreateDirectory($"{root}/{d}");
        fs.WriteAllText($"{root}/start-windows.bat", "@echo off");
    }

    [Fact]
    public void Resolve_returns_selected_folder_when_it_is_a_root()
    {
        var fs = new InMemoryFileSystem();
        SeedRoot(fs, "/mc");
        new MasterCopyLocator(fs).Resolve("/mc").Should().Be("/mc");
    }

    [Fact]
    public void Resolve_descends_into_the_single_release_folder_of_an_extraction()
    {
        var fs = new InMemoryFileSystem();
        SeedRoot(fs, "/extract/AfricAIsoft-Portable-v1.0.0");
        new MasterCopyLocator(fs).Resolve("/extract")
            .Should().Be("/extract/AfricAIsoft-Portable-v1.0.0");
    }

    [Fact]
    public void Resolve_keeps_selected_folder_when_two_candidates_are_ambiguous()
    {
        var fs = new InMemoryFileSystem();
        SeedRoot(fs, "/extract/AfricAIsoft-Portable-v1.0.0");
        SeedRoot(fs, "/extract/AfricAIsoft-Portable-v1.1.0");
        new MasterCopyLocator(fs).Resolve("/extract").Should().Be("/extract");
    }

    [Fact]
    public void Resolve_keeps_selected_folder_when_no_candidate_so_validator_reports()
    {
        var fs = new InMemoryFileSystem();
        SeedRoot(fs, "/extract/incomplete", skip: "ui");
        new MasterCopyLocator(fs).Resolve("/extract").Should().Be("/extract");
    }

    [Fact]
    public void Resolve_returns_missing_path_unchanged()
    {
        new MasterCopyLocator(new InMemoryFileSystem())
            .Resolve("/nowhere").Should().Be("/nowhere");
    }

    [Fact]
    public void LooksLikeRoot_requires_every_mandatory_directory()
    {
        var fs = new InMemoryFileSystem();
        SeedRoot(fs, "/ok");
        SeedRoot(fs, "/no-bin", skip: "bin");
        var locator = new MasterCopyLocator(fs);
        locator.LooksLikeRoot("/ok").Should().BeTrue();
        locator.LooksLikeRoot("/no-bin").Should().BeFalse();
        locator.LooksLikeRoot("").Should().BeFalse();
        locator.LooksLikeRoot(null).Should().BeFalse();
    }
}

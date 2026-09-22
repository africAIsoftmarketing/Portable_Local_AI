// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : tests xUnit du KnowledgeBasePlanner + KnowledgeBaseCopier.
//           Couvre : sélection multi-format, ajout dossier récursif (structure
//           préservée), remove/clear, taille temps réel, warning > 50 Mo,
//           préflight espace insuffisant, copie avec sous-dossiers, checksums
//           SHA-256 inclus, fichier manquant skip/abort, exclusion index/,
//           marqueur .reindex écrit.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class KnowledgeBaseTests
{
    private static InMemoryFileSystem SeedSourceTree()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/docs/readme.md", "# Test");
        fs.WriteAllText("/docs/manuel.pdf", new string('A', 100_000));
        fs.WriteAllText("/docs/notes.txt", "hello");
        fs.WriteAllText("/docs/skip.docx", "?");                         // ignoré (ext)
        fs.WriteAllText("/docs/sub/chapter1.md", "ch1");
        fs.WriteAllText("/docs/sub/deep/ref.txt", "ref");
        fs.WriteAllText("/orphan/isolated.pdf", new string('B', 5000));
        return fs;
    }

    // ── Planificateur ─────────────────────────────────────────────────────

    [Fact]
    public void AddFiles_filters_by_extension_and_dedupes()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        var n = p.AddFiles(new[] {
            "/docs/readme.md",
            "/docs/manuel.pdf",
            "/docs/notes.txt",
            "/docs/skip.docx",       // filtré
            "/docs/absent.md",       // absent
            "/docs/readme.md",       // doublon
        });
        n.Should().Be(3);
        p.Items.Should().HaveCount(3);
        p.TotalBytes.Should().Be(100_000 + 5 + "# Test".Length);
    }

    [Fact]
    public void AddFolder_recursive_preserves_relative_structure()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        var n = p.AddFolder("/docs", recursive: true);
        n.Should().Be(5); // readme.md + manuel.pdf + notes.txt + sub/chapter1.md + sub/deep/ref.txt
        p.Items.Select(i => i.RelativePath).Should()
            .Contain(new[] { "readme.md", "manuel.pdf", "notes.txt",
                             "sub/chapter1.md", "sub/deep/ref.txt" });
    }

    [Fact]
    public void RemoveAt_and_Clear_work()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        p.AddFolder("/docs");
        var before = p.Items.Count;
        p.RemoveAt(0).Should().BeTrue();
        p.Items.Should().HaveCount(before - 1);
        p.Clear();
        p.Items.Should().BeEmpty();
        p.TotalBytes.Should().Be(0);
    }

    [Fact]
    public void Large_file_warning_is_nonblocking()
    {
        var fs = new InMemoryFileSystem();
        var big = new string('X', 60 * 1024 * 1024);        // 60 Mo
        fs.WriteAllText("/big.pdf", big);
        fs.WriteAllText("/tiny.md", "x");
        var p = new KnowledgeBasePlanner(fs);
        p.AddFiles(new[] { "/big.pdf", "/tiny.md" });
        p.Items.Should().HaveCount(2);          // rien de bloqué
        p.LargeFiles.Should().HaveCount(1);
        p.LargeFiles[0].RelativePath.Should().Be("big.pdf");
    }

    [Fact]
    public void Preflight_flags_insufficient_space_before_copy()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        p.AddFolder("/docs");
        // 105% de la taille effective : marge de 10% NON couverte.
        var tooSmall = (long)(p.TotalBytes * 1.05);
        var pre1 = p.Preflight(tooSmall);
        pre1.EnoughSpace.Should().BeFalse();
        pre1.RequiredBytesWithMargin.Should().Be((long)(p.TotalBytes * 1.10));
        pre1.AvailableBytes.Should().Be(tooSmall);
        // 200% : largement suffisant
        var pre2 = p.Preflight(p.TotalBytes * 2);
        pre2.EnoughSpace.Should().BeTrue();
    }

    // ── Copier ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Copy_preserves_subfolder_structure_and_includes_checksums()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        p.AddFolder("/docs");
        var copier = new KnowledgeBaseCopier(fs, new ChecksumService(fs));
        var res = await copier.CopyAsync(p, "/usb", writeReindexMarker: true);

        // Structure préservée sous knowledge/documents/
        fs.FileExists("/usb/knowledge/documents/readme.md").Should().BeTrue();
        fs.FileExists("/usb/knowledge/documents/sub/chapter1.md").Should().BeTrue();
        fs.FileExists("/usb/knowledge/documents/sub/deep/ref.txt").Should().BeTrue();
        res.CopiedFiles.Should().HaveCount(5);
        res.SkippedFiles.Should().BeEmpty();
        // Checksums indexés par chemin relatif COMPLET (avec préfixe).
        res.Sha256ByRelativePath.Should().ContainKey("knowledge/documents/readme.md");
        res.Sha256ByRelativePath.Values.Should().OnlyContain(h => h.Length == 64);
        // Marqueur .reindex écrit.
        res.ReindexMarkerWritten.Should().BeTrue();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeTrue();
    }

    [Fact]
    public async Task Copy_missing_file_skip_continues_others()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        p.AddFiles(new[] { "/docs/readme.md", "/docs/notes.txt" });
        // Simule un fichier qui disparaît entre planification et copie
        // (ex. drive USB éjecté, raccourci Windows cassé).
        var missing = p.Items.First(i => i.RelativePath == "readme.md");
        // On altère manuellement : on ajoute un item vers un chemin inexistant.
        var p2 = new KnowledgeBasePlanner(fs);
        fs.WriteAllText("/tmp/ephemeral.txt", "ephem");
        p2.AddFiles(new[] { "/tmp/ephemeral.txt", "/docs/notes.txt" });
        fs.DeleteFile("/tmp/ephemeral.txt"); // fichier disparaît après planif

        var copier = new KnowledgeBaseCopier(fs, new ChecksumService(fs));
        var res = await copier.CopyAsync(p2, "/usb", writeReindexMarker: false,
                                          missingStrategy: MissingFileStrategy.Skip);
        res.SkippedFiles.Should().HaveCount(1);
        res.SkippedFiles[0].Item.RelativePath.Should().Be("ephemeral.txt");
        res.SkippedFiles[0].Reason.Should().Contain("introuvable");
        res.CopiedFiles.Should().HaveCount(1);
        fs.FileExists("/usb/knowledge/documents/notes.txt").Should().BeTrue();
    }

    [Fact]
    public async Task Copy_missing_file_abort_throws()
    {
        var fs = SeedSourceTree();
        fs.WriteAllText("/tmp/eph.md", "e");
        var p = new KnowledgeBasePlanner(fs);
        p.AddFiles(new[] { "/tmp/eph.md" });
        fs.DeleteFile("/tmp/eph.md");
        var copier = new KnowledgeBaseCopier(fs, new ChecksumService(fs));
        Func<Task> act = () => copier.CopyAsync(p, "/usb", writeReindexMarker: false,
                                                 missingStrategy: MissingFileStrategy.Abort);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Reindex_marker_not_written_when_no_docs()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);   // vide
        var copier = new KnowledgeBaseCopier(fs, new ChecksumService(fs));
        var res = await copier.CopyAsync(p, "/usb", writeReindexMarker: true);
        res.CopiedFiles.Should().BeEmpty();
        res.ReindexMarkerWritten.Should().BeFalse();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeFalse();
    }

    [Fact]
    public async Task Reindex_marker_disabled_by_option()
    {
        var fs = SeedSourceTree();
        var p = new KnowledgeBasePlanner(fs);
        p.AddFolder("/docs");
        var copier = new KnowledgeBaseCopier(fs, new ChecksumService(fs));
        var res = await copier.CopyAsync(p, "/usb", writeReindexMarker: false);
        res.CopiedFiles.Should().HaveCount(5);
        res.ReindexMarkerWritten.Should().BeFalse();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeFalse();
    }

    [Fact]
    public void Extensions_allowed_matches_spec()
    {
        KnowledgeBaseFormats.AllowedExtensions
            .Should().BeEquivalentTo(new[] { ".pdf", ".txt", ".md" });
        KnowledgeBaseFormats.IsAllowed("/x/y.PDF").Should().BeTrue();  // case-insensitive
        KnowledgeBaseFormats.IsAllowed("/x/y.docx").Should().BeFalse();
    }
}

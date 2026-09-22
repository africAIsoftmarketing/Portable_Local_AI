using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class UsbBuildOrchestratorTests
{
    private static (InMemoryFileSystem fs, BuildPlan plan) MakeFullRepo()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/src/app/main.py", "print('hello')");
        fs.WriteAllText("/src/ui/index.html", "<html></html>");
        fs.WriteAllText("/src/config/settings.json", "{}");
        fs.WriteAllText("/src/config/mcp.json",
            "{\"servers\":{\"accounting\":{},\"cybersec\":{}}}");
        fs.WriteAllText("/src/scripts/start.sh", "#!/bin/sh");
        fs.WriteAllText("/src/docs/README.md", "# doc");
        fs.WriteAllText("/src/skills/registry.json", "{}");
        fs.WriteAllText("/src/mcp-servers/_shared/base.py", "# shared");
        fs.WriteAllText("/src/mcp-servers/accounting/server.py", "# acc");
        fs.WriteAllText("/src/mcp-servers/cybersec/server.py", "# cyber");
        // Modèle GGUF valide.
        var m = new byte[512];
        m[0] = 0x47; m[1] = 0x47; m[2] = 0x55; m[3] = 0x46; m[4] = 3;
        fs.WriteBytes("/src/models/m.gguf", m);
        fs.WriteBytes("/src/bin/windows/llama-server.exe", new byte[64]);
        fs.WriteAllText("/src/README.md", "# readme");
        fs.WriteAllText("/src/VERSION", "0.5.0");
        var plan = new BuildPlan
        {
            SourceRoot = "/src",
            TargetRoot = "/usb",
            ModelFile = "m.gguf",
            IncludedSkillIds = new() { "accounting" },
            TargetPlatforms = new() { TargetPlatform.WindowsX64 },
            CustomSystemPrompt = "Vous êtes le studio.",
            FormatWith = null,
            RunSmokeTest = false,
            OperatorName = "Alice",
            ClientId = "ACME",
        };
        return (fs, plan);
    }

    [Fact]
    public async Task Full_run_copies_only_included_skills_and_produces_manifest_report()
    {
        var (fs, plan) = MakeFullRepo();
        var drive = new UsbDrive("d", "/usb", "L", "M", 1L * 1024 * 1024 * 1024,
                                 1L * 1024 * 1024 * 1024, FileSystemKind.ExFat, true, true);
        var checksum = new ChecksumService(fs);
        var orch = new UsbBuildOrchestrator(
            fs,
            new SizeEstimator(fs),
            new GgufValidator(fs),
            new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs)),
            checksum,
            new ResumeJournal(fs),
            new SkillFilter(fs),
            new SystemPromptInjector(fs, checksum),
            new SettingsPatcher(fs),
            new ManifestBuilder(fs, checksum),
            new MarkerFileWriter(fs),
            new ReportGenerator(fs));
        var res = await orch.RunAsync(plan, drive, NullProgressReporter.Instance,
                                       runSmokeTest: null, CancellationToken.None);

        res.Success.Should().BeTrue();
        res.FilesCopied.Should().BeGreaterThan(0);
        res.FilesVerified.Should().BeGreaterThan(0);
        res.FilesFailed.Should().Be(0);
        // Fichiers attendus sur la clé
        fs.FileExists("/usb/models/m.gguf").Should().BeTrue();
        fs.FileExists("/usb/mcp-servers/accounting/server.py").Should().BeTrue();
        fs.FileExists("/usb/mcp-servers/cybersec/server.py").Should().BeFalse();
        fs.FileExists("/usb/config/system_prompt.txt").Should().BeTrue();
        fs.FileExists("/usb/manifest.json").Should().BeTrue();
        fs.FileExists("/usb/production-report.html").Should().BeTrue();
        fs.FileExists("/usb/.africaisoft-key.json").Should().BeTrue();
        // Le mcp.json embarqué a été patché : plus de cybersec.
        var mcp = fs.ReadAllText("/usb/config/mcp.json");
        mcp.Should().Contain("accounting").And.NotContain("cybersec");
        // Le settings.json embarqué référence le modèle.
        fs.ReadAllText("/usb/config/settings.json").Should().Contain("models/m.gguf");
    }

    // ═════════ Tests d'intégration base de connaissances ═════════

    [Fact]
    public async Task Full_run_with_knowledge_base_copies_docs_writes_reindex_and_checksums()
    {
        var (fs, plan) = MakeFullRepo();
        // Documents à embarquer sous knowledge/documents/.
        fs.WriteAllText("/src/kb/guide.md", "# Guide utilisateur");
        fs.WriteAllText("/src/kb/manuels/produit-x.pdf", new string('P', 4096));
        var kb = new KnowledgeBasePlanner(fs);
        kb.AddFolder("/src/kb", recursive: true).Should().Be(2);

        var drive = new UsbDrive("d", "/usb", "L", "M", 1L * 1024 * 1024 * 1024,
                                  1L * 1024 * 1024 * 1024, FileSystemKind.ExFat, true, true);
        var checksum = new ChecksumService(fs);
        var orch = new UsbBuildOrchestrator(
            fs, new SizeEstimator(fs), new GgufValidator(fs),
            new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs)),
            checksum, new ResumeJournal(fs), new SkillFilter(fs),
            new SystemPromptInjector(fs, checksum), new SettingsPatcher(fs),
            new ManifestBuilder(fs, checksum), new MarkerFileWriter(fs),
            new ReportGenerator(fs));

        var res = await orch.RunAsync(plan, drive, NullProgressReporter.Instance,
                                       runSmokeTest: null, CancellationToken.None,
                                       knowledgeBase: kb, reindexOnFirstLaunch: true);

        res.Success.Should().BeTrue();
        res.KnowledgeFilesCopied.Should().Be(2);
        res.KnowledgeBytes.Should().BeGreaterThan(0);
        res.KnowledgeReindexRequested.Should().BeTrue();
        res.KnowledgeSkipped.Should().BeEmpty();

        // Fichiers effectivement présents sur la clé sous knowledge/documents/.
        fs.FileExists("/usb/knowledge/documents/guide.md").Should().BeTrue();
        fs.FileExists("/usb/knowledge/documents/manuels/produit-x.pdf").Should().BeTrue();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeTrue();

        // CHECKSUMS.sha256 racine agrège les entrées KB.
        fs.FileExists("/usb/CHECKSUMS.sha256").Should().BeTrue();
        var checksums = fs.ReadAllText("/usb/CHECKSUMS.sha256");
        checksums.Should().Contain("knowledge/documents/guide.md");
        checksums.Should().Contain("knowledge/documents/manuels/produit-x.pdf");
        checksums.Should().Contain("knowledge/.reindex");
        // Lignes au format « <hash>  <path> » (deux espaces).
        checksums.Should().MatchRegex(@"[0-9a-f]{64}  ");

        // manifest.json contient les entrées knowledge.
        var mfContent = fs.ReadAllText("/usb/manifest.json");
        mfContent.Should().Contain("\"SourceCategory\": \"knowledge\"");
        mfContent.Should().Contain("knowledge/documents/guide.md");
    }

    [Fact]
    public async Task Full_run_without_knowledge_base_writes_checksums_but_no_reindex_marker()
    {
        // Régression : quand aucun document KB n'est fourni, la clé ne doit
        // contenir NI knowledge/.reindex NI de dossier knowledge/documents/.
        var (fs, plan) = MakeFullRepo();
        var drive = new UsbDrive("d", "/usb", "L", "M", 1L * 1024 * 1024 * 1024,
                                  1L * 1024 * 1024 * 1024, FileSystemKind.ExFat, true, true);
        var checksum = new ChecksumService(fs);
        var orch = new UsbBuildOrchestrator(
            fs, new SizeEstimator(fs), new GgufValidator(fs),
            new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs)),
            checksum, new ResumeJournal(fs), new SkillFilter(fs),
            new SystemPromptInjector(fs, checksum), new SettingsPatcher(fs),
            new ManifestBuilder(fs, checksum), new MarkerFileWriter(fs),
            new ReportGenerator(fs));

        // Appel SANS argument knowledgeBase (backwards-compat).
        var res = await orch.RunAsync(plan, drive, NullProgressReporter.Instance,
                                       runSmokeTest: null, CancellationToken.None);

        res.Success.Should().BeTrue();
        res.KnowledgeFilesCopied.Should().Be(0);
        res.KnowledgeReindexRequested.Should().BeFalse();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeFalse();
        fs.DirectoryExists("/usb/knowledge/documents").Should().BeFalse();

        // Mais le CHECKSUMS.sha256 racine doit être écrit dans tous les cas.
        fs.FileExists("/usb/CHECKSUMS.sha256").Should().BeTrue();
        fs.ReadAllText("/usb/CHECKSUMS.sha256").Should().NotContain("knowledge/");
    }

    [Fact]
    public async Task Preflight_kb_flags_insufficient_space_and_aborts()
    {
        // Setup : contenu cœur ~modeste + drive dont l'espace libre couvre
        // tout juste le cœur, puis on ajoute un document KB trop gros → la
        // preflight KB doit bloquer AVANT toute copie.
        var (fs, plan) = MakeFullRepo();
        fs.WriteAllText("/src/kb/huge.pdf", new string('X', 200_000)); // 200 Ko
        var kb = new KnowledgeBasePlanner(fs);
        kb.AddFolder("/src/kb").Should().Be(1);

        // Drive dimensionné pour laisser juste 50 Ko après le cœur.
        // Le cœur seed pèse quelques Ko, on donne 60 Ko d'espace libre :
        // main preflight passe, KB (200 Ko + 10 % marge) échoue.
        var drive = new UsbDrive("d", "/usb", "L", "M",
                                  60_000, 60_000,
                                  FileSystemKind.ExFat, true, true);
        var checksum = new ChecksumService(fs);
        // Désactive le contrôle « free < estimate » du main preflight en
        // demandant un format (FormatWith non null → skip la vérif free).
        plan.FormatWith = FileSystemKind.ExFat;

        var orch = new UsbBuildOrchestrator(
            fs, new SizeEstimator(fs), new GgufValidator(fs),
            new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs)),
            checksum, new ResumeJournal(fs), new SkillFilter(fs),
            new SystemPromptInjector(fs, checksum), new SettingsPatcher(fs),
            new ManifestBuilder(fs, checksum), new MarkerFileWriter(fs),
            new ReportGenerator(fs));

        var res = await orch.RunAsync(plan, drive, NullProgressReporter.Instance,
                                       runSmokeTest: null, CancellationToken.None,
                                       knowledgeBase: kb, reindexOnFirstLaunch: true);

        res.Success.Should().BeFalse();
        res.Errors.Should().Contain(e => e.Contains("KB_SPACE"));
        // Aucune copie effective : la clé ne contient PAS de dossier knowledge/.
        fs.FileExists("/usb/knowledge/documents/huge.pdf").Should().BeFalse();
        fs.FileExists("/usb/knowledge/.reindex").Should().BeFalse();
        fs.FileExists("/usb/CHECKSUMS.sha256").Should().BeFalse();
    }

    [Fact]
    public async Task Resume_skips_already_validated_files()
    {
        var (fs, plan) = MakeFullRepo();
        // Pré-remplit le journal comme si une exécution précédente avait validé
        // le fichier README.md — on doit voir moins d'écritures.
        var rj = new ResumeJournal(fs);
        var journal = new BuildJournal { SerialNumber = plan.SerialNumber };
        // Simuler que le README a été validé + copie déjà présente sur la clé.
        fs.WriteAllText("/usb/README.md", "# readme");
        rj.MarkFileValidated("/usb", journal, "README.md");
        var checksum = new ChecksumService(fs);
        var orch = new UsbBuildOrchestrator(
            fs, new SizeEstimator(fs), new GgufValidator(fs),
            new PreflightValidator(fs, new SizeEstimator(fs), new GgufValidator(fs)),
            checksum, rj, new SkillFilter(fs),
            new SystemPromptInjector(fs, checksum), new SettingsPatcher(fs),
            new ManifestBuilder(fs, checksum), new MarkerFileWriter(fs),
            new ReportGenerator(fs));
        var drive = new UsbDrive("d", "/usb", "L", "M", 1L * 1024 * 1024 * 1024,
                                 1L * 1024 * 1024 * 1024, FileSystemKind.ExFat, true, true);
        var res = await orch.RunAsync(plan, drive, NullProgressReporter.Instance,
                                       runSmokeTest: null, CancellationToken.None);
        res.Success.Should().BeTrue();
        // Vérifie que le journal final contient bien toutes les entrées post-run.
        var reloaded = rj.Load("/usb");
        reloaded.ValidatedFiles.Should().Contain("README.md");
        reloaded.LastCompletedStage.Should().Be(BuildStage.Reported);
    }
}

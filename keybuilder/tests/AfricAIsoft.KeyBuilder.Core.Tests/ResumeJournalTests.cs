using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class ResumeJournalTests
{
    [Fact]
    public void Load_returns_empty_when_no_file()
    {
        var fs = new InMemoryFileSystem();
        var j = new ResumeJournal(fs).Load("/usb");
        j.ValidatedFiles.Should().BeEmpty();
        j.LastCompletedStage.Should().Be(BuildStage.NotStarted);
    }

    [Fact]
    public void Save_and_reload_preserves_validated_files_and_stage()
    {
        var fs = new InMemoryFileSystem();
        var rj = new ResumeJournal(fs);
        var j = new BuildJournal
        {
            SerialNumber = "abc",
            StartedAtUtc = DateTime.UtcNow,
            LastCompletedStage = BuildStage.Verify,
        };
        rj.MarkFileValidated("/usb", j, "app/main.py");
        rj.MarkFileValidated("/usb", j, "\\models\\m.gguf");
        var reloaded = rj.Load("/usb");
        reloaded.SerialNumber.Should().Be("abc");
        reloaded.LastCompletedStage.Should().Be(BuildStage.Verify);
        rj.IsFileValidated(reloaded, "app/main.py").Should().BeTrue();
        rj.IsFileValidated(reloaded, "models/m.gguf").Should().BeTrue();
        rj.IsFileValidated(reloaded, "unknown.py").Should().BeFalse();
    }

    [Fact]
    public void Corrupted_journal_returns_empty()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/usb/" + ResumeJournal.JournalFileName, "{ not json");
        var j = new ResumeJournal(fs).Load("/usb");
        j.ValidatedFiles.Should().BeEmpty();
    }
}

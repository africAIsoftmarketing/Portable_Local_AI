using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class SystemPromptInjectorTests
{
    [Fact]
    public void Inject_writes_file_and_returns_sha256()
    {
        var fs = new InMemoryFileSystem();
        var checksum = new ChecksumService(fs);
        var inj = new SystemPromptInjector(fs, checksum);
        var sha = inj.Inject("/usb", "Vous êtes un assistant AfricAIsoft.");
        fs.FileExists("/usb/config/system_prompt.txt").Should().BeTrue();
        sha.Should().HaveLength(64);
        sha.Should().Be(checksum.ComputeString("Vous êtes un assistant AfricAIsoft."));
    }

    [Fact]
    public void Inject_empty_throws()
    {
        var fs = new InMemoryFileSystem();
        var inj = new SystemPromptInjector(fs, new ChecksumService(fs));
        Assert.Throws<ArgumentException>(() => inj.Inject("/usb", "   "));
    }
}

using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class ChecksumServiceTests
{
    [Fact]
    public void ComputeString_matches_known_vector()
    {
        var fs = new InMemoryFileSystem();
        var svc = new ChecksumService(fs);
        // Vecteur RFC — SHA-256 de "abc"
        svc.ComputeString("abc").Should()
            .Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public async Task ComputeFileAsync_matches_known_vector()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/a.txt", "hello");
        var svc = new ChecksumService(fs);
        // SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        (await svc.ComputeFileAsync("/a.txt")).Should()
            .Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }

    [Fact]
    public async Task Different_content_yields_different_hash()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/a.txt", "abc");
        fs.WriteAllText("/b.txt", "abd");
        var svc = new ChecksumService(fs);
        var ha = await svc.ComputeFileAsync("/a.txt");
        var hb = await svc.ComputeFileAsync("/b.txt");
        ha.Should().NotBe(hb);
    }
}

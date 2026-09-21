using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class GgufValidatorTests
{
    private static byte[] BuildHeader(uint version, bool goodMagic = true)
    {
        var buf = new byte[16];
        if (goodMagic) { buf[0] = 0x47; buf[1] = 0x47; buf[2] = 0x55; buf[3] = 0x46; }
        else           { buf[0] = 0xff; buf[1] = 0x00; buf[2] = 0xba; buf[3] = 0xd0; }
        buf[4] = (byte)(version & 0xff);
        buf[5] = (byte)((version >> 8) & 0xff);
        buf[6] = (byte)((version >> 16) & 0xff);
        buf[7] = (byte)((version >> 24) & 0xff);
        return buf;
    }

    [Fact]
    public void Valid_magic_and_version_3()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteBytes("/models/m-3b-q4_k_m.gguf", BuildHeader(3));
        var info = new GgufValidator(fs).Inspect("/models/m-3b-q4_k_m.gguf");
        info.ValidHeader.Should().BeTrue();
        info.HeaderVersion.Should().Be(3u);
        info.Quantization.Should().Be("Q4_K_M");
        info.ParametersHint.Should().Be("3B");
    }

    [Fact]
    public void Invalid_magic_returns_false()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteBytes("/models/bad.gguf", BuildHeader(3, goodMagic: false));
        new GgufValidator(fs).Inspect("/models/bad.gguf").ValidHeader.Should().BeFalse();
    }

    [Fact]
    public void Unknown_version_returns_false()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteBytes("/models/future.gguf", BuildHeader(99u));
        new GgufValidator(fs).Inspect("/models/future.gguf").ValidHeader.Should().BeFalse();
    }

    [Fact]
    public void Truncated_file_returns_false()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteBytes("/models/tiny.gguf", new byte[] { 0x47, 0x47 });
        new GgufValidator(fs).Inspect("/models/tiny.gguf").ValidHeader.Should().BeFalse();
    }
}

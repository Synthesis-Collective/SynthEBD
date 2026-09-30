using System.IO;
using CharacterViewer.Rendering;
using Xunit;

namespace SynthEBD.Tests;

public class CompressedDdsTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public CompressedDdsTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void SynthEbdHostReferenceResolvesToTestedRenderer()
    {
        var reference = Assert.Single(typeof(SynthEBD.App).Assembly.GetReferencedAssemblies(),
            name => name.Name == "CharacterViewer.Rendering");
        var loaded = System.Reflection.Assembly.Load(reference);
        Assert.Same(typeof(GlTextureManager).Assembly, loaded);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(loaded.Location)));
        _output.WriteLine($"SynthEBD host renderer: {loaded.Location}; SHA256={hash}");
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(127)] [InlineData(147)]
    public void ShortHeadersReturnInvalidInsteadOfThrowing(int length)
    {
        var data = Valid();
        AssertRead(data[..length], DdsReadStatus.Invalid);
    }

    [Theory]
    [InlineData(4, 0)] [InlineData(12, 0)] [InlineData(16, int.MaxValue)]
    [InlineData(28, 31)] [InlineData(76, 0)] [InlineData(140, 0)]
    public void InvalidFieldsReturnDiagnostics(int offset, int value)
    {
        var data = Valid(); BitConverter.GetBytes(value).CopyTo(data, offset);
        AssertRead(data, DdsReadStatus.Invalid);
    }

    [Theory]
    [InlineData(128, 81)] [InlineData(128, 84)] [InlineData(128, 95)]
    [InlineData(128, 96)] [InlineData(128, 97)] [InlineData(140, 2)]
    [InlineData(132, 4)] [InlineData(136, 4)] [InlineData(144, 2)]
    public void UnsupportedSemanticsAreDistinctFromCorruption(int offset, int value)
    {
        var data = Valid(); BitConverter.GetBytes(value).CopyTo(data, offset);
        AssertRead(data, DdsReadStatus.Fallback);
    }

    [Fact]
    public void TinyTextureAccountsForWholeBlock()
    {
        WithFile(Valid(), path => {
            Assert.Equal(DdsReadStatus.Eligible, CompressedDds.Read(path, out var dds, out _));
            Assert.Equal(16, dds!.PayloadBytes);
            Assert.True(dds.HasFullChain);
            Assert.Single(dds.Mips);
        });
    }

    private static byte[] Valid()
    {
        var bytes = new byte[164];
        foreach (var (offset, value) in new[] { (0,0x20534444),(4,124),(12,1),(16,1),(28,1),
            (76,32),(80,4),(84,0x30315844),(128,77),(132,3),(140,1) })
            BitConverter.GetBytes(value).CopyTo(bytes, offset);
        return bytes;
    }

    [Theory]
    [InlineData(null, null)] [InlineData("", null)] [InlineData("decoded", null)]
    [InlineData("1", 1)] [InlineData("4", 4)] [InlineData(" 16 ", 16)]
    [InlineData("unlimited", int.MaxValue)]
    public void PolicyParsesExplicitLimits(string? value, int? expected)
        => Assert.Equal(expected, DdsMipPolicy.Parse(value));

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("nonsense")]
    [InlineData("2147483648")] [InlineData("4.0")]
    public void InvalidPolicyIsDiagnosable(string value)
        => Assert.Throws<ArgumentException>(() => DdsMipPolicy.Parse(value));

    [Theory]
    [InlineData(8, 4, 2, 4)] [InlineData(4, 8, 2, 4)]
    [InlineData(7, 5, 2, 3)] [InlineData(4, 4, 1, 4)]
    public void LastMipDimensionControlsEligibility(int width, int height, int count, int terminal)
    {
        // BC3 blocks: payload includes exactly the declared levels.
        int length = 148, w = width, h = height;
        for (int i = 0; i < count; i++) { length += ((w + 3) / 4) * ((h + 3) / 4) * 16; w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
        var bytes = new byte[length]; Array.Copy(Valid(), bytes, 148);
        BitConverter.GetBytes(width).CopyTo(bytes, 16); BitConverter.GetBytes(height).CopyTo(bytes, 12);
        BitConverter.GetBytes(count).CopyTo(bytes, 28);
        WithFile(bytes, path => {
            Assert.Equal(DdsReadStatus.Fallback, CompressedDds.Read(path, out _, out var reason, terminal - 1));
            Assert.Equal("final-mip-exceeds-limit", reason);
            Assert.Equal(DdsReadStatus.Eligible, CompressedDds.Read(path, out var dds, out _, terminal));
            Assert.Equal(count, dds!.Mips.Count);
            Assert.Equal(length - 148, dds.PayloadBytes);
            Assert.Equal(DdsReadStatus.Eligible, CompressedDds.Read(path, out _, out _, int.MaxValue));
            File.WriteAllBytes(path, bytes[..^1]);
            Assert.Equal(DdsReadStatus.Invalid, CompressedDds.Read(path, out _, out _, int.MaxValue));
        });
    }
    private static void AssertRead(byte[] bytes, DdsReadStatus expected) => WithFile(bytes, path => {
        Assert.Equal(expected, CompressedDds.Read(path, out var dds, out var reason));
        Assert.Null(dds); Assert.False(string.IsNullOrWhiteSpace(reason));
    });
    private static void WithFile(byte[] bytes, Action<string> check)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllBytes(path, bytes); check(path); }
        finally { File.Delete(path); }
    }
}

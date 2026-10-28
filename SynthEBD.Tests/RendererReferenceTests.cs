using System.IO;
using SkyRender;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>Checks that the SynthEBD host build binds the same core renderer assembly the tests run
/// against (kept here when the renderer's own tests moved to SkyRender.Tests).</summary>
public class RendererReferenceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public RendererReferenceTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void SynthEbdHostReferenceResolvesToTestedRenderer()
    {
        var reference = Assert.Single(typeof(SynthEBD.App).Assembly.GetReferencedAssemblies(),
            name => name.Name == "SkyRender");
        var loaded = System.Reflection.Assembly.Load(reference);
        Assert.Same(typeof(GlTextureManager).Assembly, loaded);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(loaded.Location)));
        _output.WriteLine($"SynthEBD host renderer: {loaded.Location}; SHA256={hash}");
    }
}

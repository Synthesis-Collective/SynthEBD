using FluentAssertions;
using OpenTK.Mathematics;
using Xunit;

namespace SkyRender.Tests;

/// <summary>The sky-by-elevation gradient water reflects (GlRenderer.SkyGradient).</summary>
public class SkyGradientTests
{
    [Fact]
    public void At_InterpolatesBetweenKeys_AndHoldsBeyondThem()
    {
        // Keys out of order on purpose: the gradient sorts them by elevation.
        var g = new SkyGradient(new[]
        {
            new SkyGradientKey(90f, new Vector3(0f, 0f, 1f)),
            new SkyGradientKey(0f, new Vector3(1f, 0f, 0f)),
            new SkyGradientKey(30f, new Vector3(0f, 1f, 0f)),
        });
        g.At(-10f).Should().Be(new Vector3(1f, 0f, 0f));
        g.At(15f).X.Should().BeApproximately(0.5f, 1e-6f);
        g.At(15f).Y.Should().BeApproximately(0.5f, 1e-6f);
        g.At(60f).Z.Should().BeApproximately(0.5f, 1e-6f);
        g.At(120f).Should().Be(new Vector3(0f, 0f, 1f));
    }

    [Fact]
    public void ShaderKeys_AreCappedAtMaxKeys()
    {
        var keys = Enumerable.Range(0, SkyGradient.MaxKeys + 5)
            .Select(i => new SkyGradientKey(i, Vector3.One)).ToArray();
        new SkyGradient(keys).ShaderKeys.Should().HaveCount(SkyGradient.MaxKeys);
        new SkyGradient(System.Array.Empty<SkyGradientKey>()).At(10f).Should().Be(Vector3.Zero);
    }
}

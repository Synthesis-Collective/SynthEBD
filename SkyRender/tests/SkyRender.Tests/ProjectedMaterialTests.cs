using SkyRender;
using FluentAssertions;
using Xunit;

namespace SkyRender.Tests;

/// <summary><see cref="GlMesh.ProjectedParamsFor"/>: the engine's ProjectedUVParams for a
/// single-pass material, and the off-by-default projected state a NIF-built mesh carries.</summary>
public class ProjectedMaterialTests
{
    [Fact]
    public void MaxAngle90_LeavesScaleAndBiasUnchanged()
    {
        var p = GlMesh.ProjectedParamsFor(falloffScale: 0.46f, falloffBias: 0.5f, noiseUvScale: 50f, maxAngleDegrees: 90f);
        p.X.Should().BeApproximately(0.46f, 1e-5f);
        p.Z.Should().BeApproximately(1f / 50f, 1e-7f);
        p.W.Should().BeApproximately(0.5f, 1e-5f);
    }

    [Fact]
    public void SmallerMaxAngle_FoldsCosIntoTheThreshold()
    {
        // (1 - cos) * scale and (1 - cos) * bias + cos, cos(60) = 0.5.
        var p = GlMesh.ProjectedParamsFor(0.4f, 0.2f, 100f, 60f);
        p.X.Should().BeApproximately(0.2f, 1e-5f);
        p.W.Should().BeApproximately(0.6f, 1e-5f);
    }

    [Fact]
    public void ZeroNoiseScale_DoesNotDivideByZero()
    {
        GlMesh.ProjectedParamsFor(0.4f, 0.2f, 0f, 90f).Z.Should().Be(0f);
    }

    [Fact]
    public void NewMesh_HasNoProjectedMaterial()
    {
        // Property defaults only; no GL context is touched.
        var m = new GlMesh();
        m.HasProjectedMaterial.Should().BeFalse();
        m.ProjectedNoiseTexture.Should().Be(0);
    }
}

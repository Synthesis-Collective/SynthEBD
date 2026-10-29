using FluentAssertions;
using OpenTK.Mathematics;
using Xunit;

namespace SkyRender.Tests;

/// <summary>The engine image-space finish (GlRenderer.ImageSpace).</summary>
public class ImageSpaceFinishTests
{
    [Fact]
    public void ReinhardCoefficient_IsTheEnginesPackedValue()
    {
        // The engine uploads 1 / (White * 1.1)^2: 0.8264462 for White 1.
        var f = new ImageSpaceFinish(1f, 1f, 1f, 1f, Vector3.One, 0f, 0.367f);
        f.ReinhardCoefficient.Should().BeApproximately(0.8264462f, 1e-6f);
    }

    [Fact]
    public void Apply_NeutralGrade_IsTheCurveOnLuminance()
    {
        var f = new ImageSpaceFinish(1f, 1f, 1f, 1f, Vector3.One, 0f, 0.367f);
        float y = 0.5f, k = f.ReinhardCoefficient;
        float q = y * (1f + k * y) / (1f + y);
        var o = f.Apply(new Vector3(y));
        o.X.Should().BeApproximately(q, 1e-5f);
        o.Z.Should().BeApproximately(q, 1e-5f);
    }

    [Fact]
    public void Apply_GreyAtThePivot_IgnoresContrastAndSaturation()
    {
        var f = new ImageSpaceFinish(1f, 1.5f, 1f, 1.4f, Vector3.One, 0f, 0.367f);
        float k = f.ReinhardCoefficient, lo = 0f, hi = 2f;
        for (int i = 0; i < 60; i++)
        {
            float m = (lo + hi) / 2f;
            if (m * (1f + k * m) / (1f + m) < 0.367f) lo = m; else hi = m;
        }
        var o = f.Apply(new Vector3(lo));
        o.X.Should().BeApproximately(0.367f, 1e-4f);
        o.Z.Should().BeApproximately(0.367f, 1e-4f);
    }
}

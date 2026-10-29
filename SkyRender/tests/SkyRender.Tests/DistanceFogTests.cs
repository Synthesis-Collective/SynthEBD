using FluentAssertions;
using OpenTK.Mathematics;
using Xunit;

namespace SkyRender.Tests;

/// <summary>The engine-form distance fog amount (GlRenderer.Fog).</summary>
public class DistanceFogTests
{
    [Fact]
    public void Amount_RampsFromNearToFar_ThenPowerAndMax()
    {
        var linear = new DistanceFog(Vector3.Zero, Vector3.One, Near: 1000f, Far: 3000f);
        linear.Amount(500f).Should().Be(0f);
        linear.Amount(2000f).Should().BeApproximately(0.5f, 1e-6f);
        linear.Amount(9000f).Should().Be(1f);

        var shaped = new DistanceFog(Vector3.Zero, Vector3.One, Near: 0f, Far: 1000f, Power: 2f, Max: 0.8f);
        shaped.Amount(500f).Should().BeApproximately(0.25f, 1e-6f);
        shaped.Amount(5000f).Should().BeApproximately(0.8f, 1e-6f);
    }
}

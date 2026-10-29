using FluentAssertions;
using OpenTK.Mathematics;
using SkyRender.World;
using Xunit;
using NVec3 = System.Numerics.Vector3;

namespace SkyRender.Tests;

/// <summary>Placing a reference (ReferencePlacement), the sky-dome gradient builder
/// (SkyGradient.FromSkyDome) and the climate sun path (SceneEnvironment.SunDirection). Maths only;
/// no GL context.</summary>
public class ReferencePlacementTests
{
    private static NVec3 PlaceWorld(Matrix4 model, NVec3 localZUp)
    {
        var mesh = ReferencePlacement.ToMeshSpace(localZUp);
        var placed = new Vector4(mesh, 1f) * model;
        return ReferencePlacement.ToWorldSpace(placed.Xyz);
    }

    private static void ShouldBeNear(NVec3 actual, NVec3 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-3f);
        actual.Y.Should().BeApproximately(expected.Y, 1e-3f);
        actual.Z.Should().BeApproximately(expected.Z, 1e-3f);
    }

    [Fact]
    public void MeshSpaceSwap_RoundTrips()
    {
        var p = new NVec3(1f, 2f, 3f);
        ReferencePlacement.ToMeshSpace(p).Should().Be(new Vector3(1f, 3f, -2f));
        ReferencePlacement.ToWorldSpace(ReferencePlacement.ToMeshSpace(p)).Should().Be(p);
    }

    [Fact]
    public void Heading_IsClockwise_NorthTurnsEast()
    {
        // A heading of 90 degrees (the record's Z angle) turns the model's north (+Y) to east (+X).
        var m = ReferencePlacement.ModelTransform(new NVec3(100f, 200f, 300f), new NVec3(0f, 0f, MathF.PI / 2f), 1f);
        ShouldBeNear(PlaceWorld(m, new NVec3(0f, 10f, 0f)), new NVec3(110f, 200f, 300f));
    }

    [Fact]
    public void Scale_AppliesBeforeTranslation()
    {
        var m = ReferencePlacement.ModelTransform(new NVec3(5f, 0f, 0f), NVec3.Zero, 2f);
        ShouldBeNear(PlaceWorld(m, new NVec3(1f, 1f, 1f)), new NVec3(7f, 2f, 2f));
    }

    [Fact]
    public void Rotation_AppliesZFirst()
    {
        // x = 90 and z = 90 degrees. Z first, then X, takes +X to +Z; X first would leave +X where
        // it is and then turn it to -Y.
        var r = ReferencePlacement.Rotation(new NVec3(MathF.PI / 2f, 0f, MathF.PI / 2f));
        var p = System.Numerics.Vector3.Transform(new NVec3(1f, 0f, 0f), r);
        // Z (clockwise 90) takes +X to -Y; X (clockwise 90 about +X) takes -Y to +Z.
        ShouldBeNear(p, new NVec3(0f, 0f, 1f));
    }

    [Fact]
    public void ModelRoot_IsUndoneBeforePlacement()
    {
        // A root that lifts the model by 50 (Z-up) and turns it 90 degrees about Z: the builder's
        // vertices carry it, the placement must not.
        var root = ReferencePlacement.FromNifTransform(new NVec3(0f, 1f, 0f), new NVec3(-1f, 0f, 0f), new NVec3(0f, 0f, 1f),
            new NVec3(0f, 0f, 50f), 1f);
        root.Should().NotBeNull();
        var local = new NVec3(3f, 4f, 5f);
        // As the builder emits it: the root applied (column-vector R p + t).
        var built = new NVec3(-local.Y, local.X, local.Z + 50f);
        var m = ReferencePlacement.ModelTransform(new NVec3(1000f, 0f, 0f), NVec3.Zero, 1f, root);
        ShouldBeNear(PlaceWorld(m, built), new NVec3(1003f, 4f, 5f));
    }

    [Fact]
    public void IdentityRoot_IsNull()
    {
        ReferencePlacement.FromNifTransform(NVec3.UnitX, NVec3.UnitY, NVec3.UnitZ, NVec3.Zero, 1f).Should().BeNull();
        ReferencePlacement.ReadRootTransform(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-file.nif")).Should().BeNull();
    }

    [Fact]
    public void FromSkyDome_RebuildsTheVanillaClearDaySky()
    {
        static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
        var g = SkyGradient.FromSkyDome(Rgb(125, 163, 183), Rgb(60, 135, 183), Rgb(21, 77, 117), Rgb(116, 168, 203), 0.85f, 0.05f);
        var clear = SceneEnvironment.VanillaClearDay.SkyGradient!;
        g.Keys.Should().HaveCount(clear.Keys.Count);
        for (int i = 0; i < g.Keys.Count; i++)
        {
            g.Keys[i].ElevationDegrees.Should().Be(clear.Keys[i].ElevationDegrees);
            (g.Keys[i].Color - clear.Keys[i].Color).Length.Should().BeLessThan(1e-6f);
        }
        // Fog max 0: the bare dome, horizon colour at the rim, sky-upper at the zenith.
        var bare = SkyGradient.FromSkyDome(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.One, 0f, 0f);
        bare.At(0f).Should().Be(Vector3.UnitX);
        bare.At(90f).Should().Be(Vector3.UnitZ);
    }

    [Fact]
    public void SunDirection_MatchesTheVanillaClearDaySun()
    {
        // Sunrise 7:30 and sunset 18:30 after the quarter-hour offsets: begin/end means 7.75 and 18.25.
        var sun = SceneEnvironment.SunDirection(7f, 8.5f, 17.5f, 19f, 12f);
        sun.Should().NotBeNull();
        sun!.Value.AzimuthDegrees.Should().BeApproximately(SceneEnvironment.VanillaClearDay.SunAzimuthDegrees, 0.1f);
        sun.Value.ElevationDegrees.Should().BeApproximately(SceneEnvironment.VanillaClearDay.SunElevationDegrees, 0.1f);
        // At sunrise the sun is on the horizon in the east (azimuth 90).
        var rise = SceneEnvironment.SunDirection(7f, 8.5f, 17.5f, 19f, 7.5f)!.Value;
        rise.ElevationDegrees.Should().BeApproximately(0f, 0.01f);
        rise.AzimuthDegrees.Should().BeApproximately(90f, 0.01f);
        SceneEnvironment.SunDirection(10f, 10f, 9f, 9f, 12f).Should().BeNull();
    }
}

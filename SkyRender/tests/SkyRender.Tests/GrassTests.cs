using System;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using SkyRender.World;
using Xunit;

namespace SkyRender.Tests;

/// <summary>Near-field grass, GL-free: texture shares, placement rules and the instance buffer.</summary>
public class GrassTests
{
    private static GrassType Grass(uint id = 1, byte density = 100, float positionRange = 64f) => new()
    {
        Id = id, ModelPath = @"meshes\landscape\grass\test.nif", Density = density,
        MinSlope = 0, MaxSlope = 90, PositionRange = positionRange,
    };

    private static TerrainCell FlatCell(float z = 100f, int gx = 0, int gy = 0, Func<int, int, float>? height = null)
    {
        var h = new float[TerrainCell.Side * TerrainCell.Side];
        for (int y = 0; y < TerrainCell.Side; y++)
            for (int x = 0; x < TerrainCell.Side; x++)
                h[y * TerrainCell.Side + x] = height?.Invoke(x, y) ?? z;
        return new TerrainCell(gx, gy, h);
    }

    private static void Paint(TerrainCell cell, TerrainTexture baseTex, params TerrainLayer[] layers)
    {
        for (int q = 0; q < 4; q++) cell.Quadrants[q] = new TerrainQuadrant { Base = baseTex, Layers = layers };
    }

    private static float[] Opacity(float o) => Enumerable.Repeat(o, 17 * 17).ToArray();

    [Fact]
    public void TextureShares_FollowTheOverBlend()
    {
        var quad = new TerrainQuadrant
        {
            Base = new TerrainTexture("b.dds"),
            Layers = new[] { new TerrainLayer(new TerrainTexture("l0.dds"), Opacity(0.5f)),
                             new TerrainLayer(new TerrainTexture("l1.dds"), Opacity(0.5f)) },
        };
        var s = GrassPlacer.TextureShares(quad);
        s.Should().HaveCount(3);
        s[0][0].Should().BeApproximately(0.25f, 1e-6f);   // (1 - 0.5)(1 - 0.5)
        s[1][0].Should().BeApproximately(0.25f, 1e-6f);   // 0.5 x (1 - 0.5)
        s[2][0].Should().BeApproximately(0.5f, 1e-6f);    // the top layer
        (s[0][100] + s[1][100] + s[2][100]).Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void FullDensityOnFlatGround_FillsEveryCandidate()
    {
        var cell = FlatCell();
        Paint(cell, new TerrainTexture("b.dds") { Grasses = new[] { Grass() } });
        var p = GrassPlacer.Place(new[] { cell });
        // 256 / 64 = 4 candidates a side per 2 x 2-square patch, 8 x 8 patches a quadrant, 4 quadrants.
        var t = p.Types.Single();
        t.Candidates.Should().Be(4 * 4 * 64 * 4);
        t.Instances.Should().HaveCount(t.Candidates);
        t.Instances.Should().OnlyContain(i => i.Position.Z == 100f
            && i.Position.X >= 0f && i.Position.X <= TerrainCell.CellSize
            && i.Position.Y >= 0f && i.Position.Y <= TerrainCell.CellSize);
    }

    [Fact]
    public void MinGrassSize_CapsTheCandidateSpacing()
    {
        var cell = FlatCell();
        Paint(cell, new TerrainTexture("b.dds") { Grasses = new[] { Grass(positionRange: 8f) } });
        var p = GrassPlacer.Place(new[] { cell }, new GrassSettings { MinGrassSize = 64 });
        p.Types.Single().Candidates.Should().Be(4 * 4 * 64 * 4);   // 256 / max(8, 64) = 4
    }

    [Fact]
    public void Placement_IsRepeatable_AndTheSeedChangesIt()
    {
        TerrainCell Make()
        {
            var c = FlatCell();
            Paint(c, new TerrainTexture("b.dds") { Grasses = new[] { Grass(density: 40) } });
            return c;
        }
        var a = GrassPlacer.Place(new[] { Make() }).Types.Single().Instances;
        var b = GrassPlacer.Place(new[] { Make() }).Types.Single().Instances;
        var c = GrassPlacer.Place(new[] { Make() }, new GrassSettings { Seed = 7 }).Types.Single().Instances;
        b.Should().Equal(a);
        c.Select(i => i.Position).Should().NotEqual(a.Select(i => i.Position));
        // About 40% of the 4096 candidates survive.
        a.Count.Should().BeInRange(1400, 1880);
    }

    [Fact]
    public void ACoveringLayer_TakesTheGroundFromTheBasesGrass()
    {
        var cell = FlatCell();
        var baseGrass = Grass(1); var layerGrass = Grass(2);
        Paint(cell, new TerrainTexture("b.dds") { Grasses = new[] { baseGrass } },
              new TerrainLayer(new TerrainTexture("l.dds") { Grasses = new[] { layerGrass } }, Opacity(1f)));
        var p = GrassPlacer.Place(new[] { cell });
        p.Types.Should().ContainSingle(t => t.Type == layerGrass && t.Instances.Count == 4096);
        p.Types.Where(t => t.Type == baseGrass).Sum(t => t.Instances.Count).Should().Be(0);
    }

    [Fact]
    public void OnlyTheFirstGrassesOfATexture_AreUsed()
    {
        var cell = FlatCell();
        Paint(cell, new TerrainTexture("b.dds") { Grasses = new[] { Grass(1), Grass(2), Grass(3) } });
        GrassPlacer.Place(new[] { cell }).Types.Select(t => t.Type.Id).Should().BeEquivalentTo(new uint[] { 1, 2 });
        GrassPlacer.Place(new[] { cell }, new GrassSettings { MaxGrassTypesPerTexture = 3 }).Types.Should().HaveCount(3);
    }

    [Fact]
    public void Slope_LimitsWhereGrassGrows()
    {
        // A 45-degree ramp: z rises 128 per grid step along x.
        var ramp = FlatCell(height: (x, _) => x * 128f);
        var flatOnly = Grass(1) with { MaxSlope = 30 };
        var steep = Grass(2) with { MinSlope = 30, MaxSlope = 60 };
        Paint(ramp, new TerrainTexture("b.dds") { Grasses = new[] { flatOnly, steep } });
        var p = GrassPlacer.Place(new[] { ramp });
        p.Types.Single(t => t.Type == flatOnly).Instances.Should().BeEmpty();
        p.Types.Single(t => t.Type == steep).Instances.Should().HaveCount(4096);
        GrassPlacer.SlopeCos(0).Should().Be(1f);
        GrassPlacer.SlopeCos(90).Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void WaterRule_IsTestedAgainstTheCellsWater()
    {
        var cell = FlatCell(z: 100f);
        var dry = Grass(1) with { WaterRule = GrassWaterRule.AboveAtLeast };
        Paint(cell, new TerrainTexture("b.dds") { Grasses = new[] { dry } });
        GrassPlacer.Place(new[] { cell }, waterHeight: _ => 500f).InstanceCount.Should().Be(0);
        GrassPlacer.Place(new[] { cell }, waterHeight: _ => 0f).InstanceCount.Should().Be(4096);
        GrassPlacer.Place(new[] { cell }, waterHeight: _ => null).InstanceCount.Should().Be(4096);
        GrassPlacer.AcceptWater(GrassWaterRule.BelowAtMost, 50f, 100f, 60f).Should().BeTrue();
        GrassPlacer.AcceptWater(GrassWaterRule.BelowAtMost, 50f, 100f, 40f).Should().BeFalse();
        GrassPlacer.AcceptWater(GrassWaterRule.EitherAtMostAbove, 50f, 100f, -900f).Should().BeTrue();
    }

    [Fact]
    public void Camera_SkipsCellsBeyondTheFade()
    {
        var near = FlatCell(); var far = FlatCell(gx: 5);
        foreach (var c in new[] { near, far }) Paint(c, new TerrainTexture("b.dds") { Grasses = new[] { Grass() } });
        var p = GrassPlacer.Place(new[] { near, far }, camera: new Vector3(2048f, 2048f, 200f));
        p.CellsPlaced.Should().Be(1);
        p.CellsOutOfReach.Should().Be(1);
    }

    [Fact]
    public void Shade_IsGroundLuminanceTimesTheColourRange()
    {
        GrassPlacer.Shade(new Vector3(0.5f), 0f, 0f).Should().BeApproximately(0.5f, 1e-6f);
        GrassPlacer.Shade(Vector3.One, 0f, 0f).Should().Be(0.99f);   // kept below 1
        GrassPlacer.Shade(new Vector3(0.5f), 1f, 0.4f).Should().BeApproximately(0.5f * (0.1f + 0.6f), 1e-6f);
    }

    [Fact]
    public void Orientation_IsARotation_AndFitToSlopeStandsOnTheNormal()
    {
        var turn = GrassPlacer.Orientation(0.3f, null);
        new GrassInstance(Vector3.Zero, turn, 0f, 1f).Up.Should().Be(Vector3.UnitZ);
        var n = Vector3.Normalize(new Vector3(0.3f, -0.2f, 0.9f));
        var fit = GrassPlacer.Orientation(-0.6f, n);
        var up = new GrassInstance(Vector3.Zero, fit, 0f, 1f).Up;
        (up - n).Length().Should().BeLessThan(1e-5f);
        Matrix4x4.Multiply(fit, Matrix4x4.Transpose(fit)).Should().Match<Matrix4x4>(m =>
            MathF.Abs(m.M11 - 1) < 1e-5f && MathF.Abs(m.M22 - 1) < 1e-5f && MathF.Abs(m.M33 - 1) < 1e-5f
            && MathF.Abs(m.M12) < 1e-5f && MathF.Abs(m.M13) < 1e-5f && MathF.Abs(m.M23) < 1e-5f);
    }

    [Fact]
    public void InstanceData_PlacesTheYUpModelInYUpWorld()
    {
        var type = Grass();
        var inst = new[]
        {
            new GrassInstance(new Vector3(100, 200, 300), Matrix4x4.Identity, 0f, 0.7f),
            new GrassInstance(new Vector3(100, 200, 300), Matrix4x4.Identity, 0.5f, 0.7f),
        };
        var d = GrassInstanceData.Build(type, inst);
        d.Count.Should().Be(2);
        // Y-up model (1, 2, 3) is Z-up (1, -3, 2); placed (101, 197, 302); Y-up (101, 302, -197).
        d.Transform(0, new Vector3(1, 2, 3)).Should().Be(new Vector3(101, 302, -197));
        // Without uniform scaling only the height scales: Z-up (1, -3, 3) -> (101, 197, 303).
        d.Transform(1, new Vector3(1, 2, 3)).Should().Be(new Vector3(101, 303, -197));
        var du = GrassInstanceData.Build(type with { Flags = GrassFlags.UniformScaling }, inst);
        du.Transform(1, new Vector3(1, 2, 3)).Should().Be(new Vector3(101.5f, 303, -195.5f));
        // Up axis (Y-up) and brightness.
        d.Data.Skip(12).Take(4).Should().Equal(0f, 1f, 0f, 0.7f);
        d.Positions[0].Should().Be(new Vector3(100, 300, -200));
    }
}

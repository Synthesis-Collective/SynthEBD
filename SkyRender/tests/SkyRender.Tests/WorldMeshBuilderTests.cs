using System;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using SkyRender.World;
using Xunit;

namespace SkyRender.Tests;

/// <summary>The GL-free terrain and water geometry: height decoding, the drawn-ground height
/// lookup, quadrant meshes with their layer weights, and water grids.</summary>
public class WorldMeshBuilderTests
{
    private const int Stride = 18;

    private static TerrainCell FlatCell(int gx = 0, int gy = 0, float z = 0f)
        => new(gx, gy, Enumerable.Repeat(z, TerrainCell.Side * TerrainCell.Side).ToArray());

    private static Vector3 Pos(WorldMeshData d, int v) => new(d.Vertices[v * Stride], d.Vertices[v * Stride + 1], d.Vertices[v * Stride + 2]);
    private static Vector3 Normal(WorldMeshData d, int v) => new(d.Vertices[v * Stride + 3], d.Vertices[v * Stride + 4], d.Vertices[v * Stride + 5]);
    private static Vector2 Uv(WorldMeshData d, int v) => new(d.Vertices[v * Stride + 6], d.Vertices[v * Stride + 7]);
    private static Vector4 Color(WorldMeshData d, int v) => new(d.Vertices[v * Stride + 8], d.Vertices[v * Stride + 9], d.Vertices[v * Stride + 10], d.Vertices[v * Stride + 11]);

    [Fact]
    public void Decode_AccumulatesColumnZeroDownTheRowsThenAlongEachRow()
    {
        // All gradients 1, offset 1: h[x, y] = (1 + (y + 1) + x) * 8.
        var g = Enumerable.Repeat((sbyte)1, 33 * 33).ToArray();
        var h = LandHeightDecoder.Decode(1f, g);
        for (int y = 0; y < 33; y++)
            for (int x = 0; x < 33; x++)
                h[y * 33 + x].Should().Be((2 + y + x) * 8f);
    }

    [Fact]
    public void Decode_MatchesAReferenceDecodeOnRandomGradients()
    {
        var rng = new Random(7);
        var g = new sbyte[33 * 33];
        for (int i = 0; i < g.Length; i++) g[i] = (sbyte)rng.Next(-128, 128);
        var h = LandHeightDecoder.Decode(-312.5f, g);

        // Reference: the [x, y] formulation hosts already use.
        float rowStart = -312.5f;
        for (int y = 0; y < 33; y++)
        {
            rowStart += g[y * 33];
            float acc = rowStart;
            h[y * 33].Should().Be(acc * 8f);
            for (int x = 1; x < 33; x++) { acc += g[y * 33 + x]; h[y * 33 + x].Should().Be(acc * 8f); }
        }
    }

    [Fact]
    public void Decode_RejectsAWrongSizedGrid()
    {
        var act = () => LandHeightDecoder.Decode(0f, new sbyte[10]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HeightField_InterpolatesOnTheDrawnTriangles()
    {
        var cell = FlatCell();
        cell.Heights[0 * 33 + 1] = 128f;   // vertex (1, 0)
        var field = new TerrainHeightField(new[] { cell });
        // Lower triangle (u >= v): (0,0)-(1,0)-(1,1).
        field.HeightAt(0.75f * 128f, 0.25f * 128f).Should().BeApproximately(64f, 1e-4f);
        // Upper triangle (u < v): (0,0)-(1,1)-(0,1) does not touch vertex (1,0).
        field.HeightAt(0.25f * 128f, 0.75f * 128f).Should().BeApproximately(0f, 1e-4f);
        field.HeightAt(-10f, 5f).Should().BeNull();
        field.HeightAt(5000f, 5f).Should().BeNull();
    }

    [Fact]
    public void Quadrant_CoversItsSixteenBySixteenSquaresWithFlatTriangles()
    {
        var cell = FlatCell(2, -3, z: 100f);
        var d = WorldMeshBuilder.BuildTerrainQuadrant(cell, TerrainQuadrantIndex.TopRight, Array.Empty<TerrainLayer>());
        d.Indices.Length.Should().Be(16 * 16 * 6);
        d.Positions.Length.Should().Be(16 * 16 * 6);

        // First vertex: grid (16, 16) of cell (2, -3), Skyrim (x, y, z) -> Y-up (x, z, -y).
        float sx = 2 * 4096f + 16 * 128f, sy = -3 * 4096f + 16 * 128f;
        Pos(d, 0).Should().Be(new Vector3(sx, 100f, -sy));
        Uv(d, 0).Should().Be(new Vector2(sx / 256f, sy / 256f));
        Normal(d, 0).X.Should().BeApproximately(0f, 1e-6f);
        Normal(d, 0).Y.Should().BeApproximately(1f, 1e-6f);
        Color(d, 0).Should().Be(Vector4.One);
    }

    [Fact]
    public void Quadrant_TrianglesFaceUpInYUpSpace()
    {
        var d = WorldMeshBuilder.BuildTerrainQuadrant(FlatCell(), TerrainQuadrantIndex.BottomLeft, Array.Empty<TerrainLayer>());
        float firstSign = 0f;
        for (int t = 0; t < d.Indices.Length; t += 3)
        {
            var a = d.Positions[t]; var b = d.Positions[t + 1]; var c = d.Positions[t + 2];
            // Shaded normals point up. The geometric winding is mirrored by the Y-up swap (which
            // is why terrain draws double-sided), but it must be the same for every triangle.
            Normal(d, t).Y.Should().BeApproximately(1f, 1e-6f);
            float sign = MathF.Sign(Vector3.Cross(b - a, c - a).Y);
            sign.Should().NotBe(0f);
            if (t == 0) firstSign = sign;
            sign.Should().Be(firstSign);
        }
    }

    [Fact]
    public void Quadrant_WeightsReadTheOpacityGridXFastestAtTheRightCorners()
    {
        var op = new float[17 * 17];
        op[0 * 17 + 1] = 0.5f;   // quadrant-local vertex (1, 0)
        op[1 * 17 + 1] = 0.25f;  // (1, 1)
        var layer = new TerrainLayer(new TerrainTexture("a.dds"), op);
        var d = WorldMeshBuilder.BuildTerrainQuadrant(FlatCell(), TerrainQuadrantIndex.BottomRight, new[] { layer });

        // Square (0, 0) of the quadrant is cell square (16, 0). Its first triangle is
        // (16,0)-(17,0)-(17,1): corners 00, 10, 11.
        d.LayerWeights![0 * 8].Should().Be(0f);
        d.LayerWeights[1 * 8].Should().Be(0.5f);
        d.LayerWeights[2 * 8].Should().Be(0.25f);
        Pos(d, 1).X.Should().Be(17 * 128f);
        // Second triangle (16,0)-(17,1)-(16,1): corners 00, 11, 01.
        d.LayerWeights[3 * 8].Should().Be(0f);
        d.LayerWeights[4 * 8].Should().Be(0.25f);
        d.LayerWeights[5 * 8].Should().Be(0f);
        // Unused layer slots stay zero.
        d.LayerWeights[1 * 8 + 1].Should().Be(0f);
    }

    [Fact]
    public void Quadrant_ASquareWithNoPaintedCornerTakesNothingFromTheLayer()
    {
        var op = Enumerable.Repeat(0.003f, 17 * 17).ToArray();
        var d = WorldMeshBuilder.BuildTerrainQuadrant(FlatCell(), TerrainQuadrantIndex.BottomLeft,
            new[] { new TerrainLayer(new TerrainTexture("a.dds"), op) });
        d.LayerWeights!.Should().OnlyContain(w => w == 0f);
    }

    [Fact]
    public void Quadrant_RejectsMoreThanEightLayers()
    {
        var layers = Enumerable.Range(0, 9).Select(_ => new TerrainLayer(new TerrainTexture("a.dds"), new float[289])).ToArray();
        var act = () => WorldMeshBuilder.BuildTerrainQuadrant(FlatCell(), TerrainQuadrantIndex.BottomLeft, layers);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeNormals_ReadsSignedBytesAndNormalises()
    {
        var b = new sbyte[33 * 33 * 3];
        for (int i = 0; i < 33 * 33; i++) { b[i * 3] = 0; b[i * 3 + 1] = -127; b[i * 3 + 2] = 0; }
        b[0] = 3; b[1] = 0; b[2] = 4;
        var n = TerrainCell.DecodeNormals(b);
        n[0].X.Should().BeApproximately(0.6f, 1e-6f);
        n[0].Z.Should().BeApproximately(0.8f, 1e-6f);
        n[1].Y.Should().BeApproximately(-1f, 1e-6f);
    }

    [Fact]
    public void DecodeColors_ScalesBytesToUnit()
    {
        var b = new byte[33 * 33 * 3];
        b[3] = 255; b[4] = 51; b[5] = 0;
        var c = TerrainCell.DecodeColors(b);
        c[1].Should().Be(new Vector3(1f, 0.2f, 0f));
        c[0].Should().Be(Vector3.Zero);
    }

    [Fact]
    public void Quadrant_UsesTheCellsVertexNormalsAndColoursWhenPresent()
    {
        var heights = Enumerable.Repeat(0f, 33 * 33).ToArray();
        var normals = Enumerable.Repeat(Vector3.UnitZ, 33 * 33).ToArray();
        var colors = Enumerable.Repeat(Vector3.One, 33 * 33).ToArray();
        // Grid vertex (1, 0) leans east and is tinted.
        normals[0 * 33 + 1] = Vector3.Normalize(new Vector3(1f, 0f, 1f));
        colors[0 * 33 + 1] = new Vector3(0.5f, 0.25f, 0.125f);
        var cell = new TerrainCell(0, 0, heights) { Normals = normals, Colors = colors };
        var d = WorldMeshBuilder.BuildTerrainQuadrant(cell, TerrainQuadrantIndex.BottomLeft, Array.Empty<TerrainLayer>());

        // Vertex 1 of the first triangle is grid (1, 0): Skyrim (x, y, z) normal -> Y-up (x, z, -y).
        var n = Normal(d, 1);
        n.X.Should().BeApproximately(MathF.Sqrt(0.5f), 1e-6f);
        n.Y.Should().BeApproximately(MathF.Sqrt(0.5f), 1e-6f);
        Color(d, 1).Should().Be(new Vector4(0.5f, 0.25f, 0.125f, 1f));
        Color(d, 0).Should().Be(Vector4.One);
        // Neighbouring vertices keep their own (up) normal: smooth, not per face.
        Normal(d, 0).Y.Should().BeApproximately(1f, 1e-6f);
        // The tangent is perpendicular to the normal.
        var t = new Vector3(d.Vertices[1 * 18 + 12], d.Vertices[1 * 18 + 13], d.Vertices[1 * 18 + 14]);
        Vector3.Dot(t, n).Should().BeApproximately(0f, 1e-5f);
    }

    [Fact]
    public void Water_ShadesShallowToDeepByDepthToTheGround()
    {
        var ground = new TerrainHeightField(new[] { FlatCell(z: -100f) });
        var shallow = new Vector3(0.2f, 0.6f, 0.6f);
        var deep = new Vector3(0f, 0.1f, 0.2f);
        var mat = new WaterMaterial(shallow, deep, Vector3.One, 0.5f, 0.07f);

        // Surface 96 units above flat ground: t = 96 / 192 = 0.5 everywhere over the cell.
        var d = WorldMeshBuilder.BuildWater(new WaterSurface(-4f, 0f, 0f, 4096f, 4096f, mat), ground);
        int side = 49;
        d.Positions.Length.Should().Be(side * side);
        d.Indices.Length.Should().Be(48 * 48 * 6);
        var c = Color(d, 10);
        new Vector3(c.X, c.Y, c.Z).Should().Be(Vector3.Lerp(shallow, deep, 0.5f));
        c.W.Should().Be(1f);
        d.SubmergedVertexCount.Should().Be(side * side - (side + side - 1) + (side + side - 1));

        // No ground: deep.
        var off = WorldMeshBuilder.BuildWater(new WaterSurface(0f, 0f, 0f, 4096f, 4096f, mat), (TerrainHeightField?)null);
        var co = Color(off, 0);
        new Vector3(co.X, co.Y, co.Z).Should().Be(deep);
    }

    [Fact]
    public void Water_ShoreAlphaRampsToOpaqueWithDepth()
    {
        var ground = new TerrainHeightField(new[] { FlatCell(z: 0f) });
        var mat = new WaterMaterial(Vector3.One, Vector3.Zero, Vector3.One, 0f, 0f) { ShoreAlpha = 0.25f };
        var d = WorldMeshBuilder.BuildWater(new WaterSurface(48f, 0f, 0f, 4096f, 4096f, mat), ground);
        // t = 48 / 192 = 0.25 -> alpha 0.25 + 0.75 * 0.25.
        Color(d, 0).W.Should().BeApproximately(0.4375f, 1e-6f);
    }

    [Fact]
    public void Water_TrianglesFaceUpAndUvFollowsTheNoiseScale()
    {
        var mat = new WaterMaterial(Vector3.One, Vector3.Zero, Vector3.One, 0f, 0f) { NoiseUvScale = 512f };
        var d = WorldMeshBuilder.BuildWater(new WaterSurface(0f, 1024f, 2048f, 5120f, 6144f, mat), (TerrainHeightField?)null);
        var a = d.Positions[d.Indices[0]]; var b = d.Positions[d.Indices[1]]; var c = d.Positions[d.Indices[2]];
        Vector3.Cross(b - a, c - a).Y.Should().BeGreaterThan(0f);
        Uv(d, 0).Should().Be(new Vector2(1024f / 512f, 2048f / 512f));
        Normal(d, 0).Should().Be(new Vector3(0f, 1f, 0f));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using SkyRender.World;
using Xunit;

namespace SkyRender.Tests;

/// <summary>Distant LOD, GL-free: the settings and tree file readers, block selection and the cut
/// under full-detail cells.</summary>
public class DistantLodTests
{
    private static readonly LodSettings Tamriel = new(-96, -96, 256, 4, 32);
    private const float Cell = TerrainCell.CellSize;

    [Fact]
    public void Settings_ParseTheSixteenByteLayout()
    {
        // Vanilla tamriel.lod: a0ff a0ff 0001 0000 0400 0000 2000 0000.
        var bytes = new byte[] { 0xa0, 0xff, 0xa0, 0xff, 0x00, 0x01, 0, 0, 4, 0, 0, 0, 0x20, 0, 0, 0 };
        LodSettings.Parse(bytes).Should().Be(Tamriel);
        FluentActions.Invoking(() => LodSettings.Parse(new byte[12])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BlockFor_AlignsToTheSouthWestCell()
    {
        Tamriel.BlockFor(4, -12, 4).Should().Be((4, -12));
        Tamriel.BlockFor(5, -11, 4).Should().Be((4, -12));
        Tamriel.BlockFor(-1, -1, 8).Should().Be((-8, -8));
        Tamriel.BlockFor(-96, -96, 32).Should().Be((-96, -96));
        Tamriel.BlockFor(17, 31, 32).Should().Be((0, 0));
    }

    [Fact]
    public void MeshPath_UsesTheGameFileNames()
    {
        new LodBlock(LodKind.Terrain, 4, 4, -12).MeshPath("Tamriel").Should().Be(@"meshes\terrain\Tamriel\Tamriel.4.4.-12.btr");
        new LodBlock(LodKind.Objects, 8, 0, -16).MeshPath("Tamriel").Should().Be(@"meshes\terrain\Tamriel\objects\Tamriel.8.0.-16.bto");
        new LodBlock(LodKind.Trees, 4, 0, 0).MeshPath("Tamriel").Should().Be(@"meshes\terrain\Tamriel\trees\Tamriel.4.0.0.btt");
        LodPaths.TreeAtlas("Tamriel").Should().Be(@"textures\terrain\Tamriel\trees\Tamrieltreelod.dds");
    }

    [Fact]
    public void Select_TerrainBlocksTileTheGridWithoutOverlap()
    {
        var eye = new Vector2(17224f, -47204f);
        var blocks = LodSelector.Select(LodKind.Terrain, Tamriel, new LodDistances(), eye);
        // Every cell of the 256-cell grid is covered exactly once.
        var cover = new Dictionary<(int, int), int>();
        foreach (var b in blocks)
            for (int x = b.X; x < b.X + b.Level; x++)
                for (int y = b.Y; y < b.Y + b.Level; y++)
                    cover[(x, y)] = cover.GetValueOrDefault((x, y)) + 1;
        cover.Count.Should().Be(256 * 256);
        cover.Values.Should().OnlyContain(n => n == 1);
        // The camera's own block is level 4; levels coarsen with distance.
        blocks.Single(b => b.X <= 4 && 4 < b.X + b.Level && b.Y <= -12 && -12 < b.Y + b.Level).Level.Should().Be(4);
        blocks.Select(b => b.Level).Distinct().Should().BeEquivalentTo(new[] { 4, 8, 16, 32 });
    }

    [Fact]
    public void Select_EachLevelIsWithinItsReachAndCouldNotSplit()
    {
        var d = new LodDistances();
        var eye = new Vector2(1000f, 2000f);
        foreach (var b in LodSelector.Select(LodKind.Terrain, Tamriel, d, eye))
        {
            int index = b.Level switch { 4 => 0, 8 => 1, 16 => 2, _ => 3 };
            if (index > 0) b.DistanceTo(eye).Should().BeGreaterThanOrEqualTo(d.Reach(LodKind.Terrain, index - 1));
        }
        // Level 4 terrain reaches fBlockLevel0Distance x fSplitDistanceMult.
        d.Reach(LodKind.Terrain, 0).Should().Be(90000f);
    }

    [Fact]
    public void Select_ObjectsStopAtLevel16AndTreesUseOneLevel()
    {
        var d = new LodDistances();
        var eye = new Vector2(0f, 0f);
        var objects = LodSelector.Select(LodKind.Objects, Tamriel, d, eye);
        objects.Should().NotBeEmpty();
        objects.Select(b => b.Level).Max().Should().Be(16);
        objects.Should().OnlyContain(b => b.DistanceTo(eye) < d.MaximumDistance);
        var trees = LodSelector.Select(LodKind.Trees, Tamriel, d, eye);
        trees.Should().OnlyContain(b => b.Level == 4 && b.DistanceTo(eye) < d.TreeLoadDistance);
        trees.Should().Contain(b => b.X == 0 && b.Y == 0);
    }

    [Fact]
    public void Select_MaxDistanceDropsFartherBlocks()
    {
        var eye = new Vector2(0f, 0f);
        LodSelector.Select(LodKind.Terrain, Tamriel, new LodDistances(), eye, maxDistance: 50000f)
            .Should().OnlyContain(b => b.DistanceTo(eye) < 50000f);
    }

    [Fact]
    public void LoadedCells_InteriorExcludesTheOuterEdge()
    {
        var loaded = LoadedCells.Square(0, 0, 1);   // cells -1..1
        loaded.IsInterior(0f, 0f).Should().BeTrue();            // a corner shared by four loaded cells
        loaded.IsInterior(Cell, 100f).Should().BeTrue();         // an inner cell edge
        loaded.IsInterior(-Cell, 100f).Should().BeFalse();       // the west outer edge
        loaded.IsInterior(2 * Cell, 0f).Should().BeFalse();      // the outer edge
        loaded.IsInterior(-Cell + 1f, 0f).Should().BeTrue();
        loaded.IsInterior(-Cell - 1f, 0f).Should().BeFalse();
        loaded.IsInterior(2 * Cell + 1f, 0f).Should().BeFalse();
        loaded.Covers(-Cell, -Cell, 2 * Cell, 2 * Cell).Should().BeTrue();
        loaded.Covers(-Cell, -Cell, 2 * Cell + 10f, 0f).Should().BeFalse();
    }

    // A flat terrain LOD block of 4x4 cells, one vertex every half cell, at height 100.
    private static LodShape FlatTerrain(float scale = 4f)
    {
        const int side = 9;
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                pos.Add(new Vector3(x * Cell / 2, y * Cell / 2, 100f));
                uv.Add(new Vector2(x / 8f, 1f - y / 8f));
            }
        var idx = new List<int>();
        for (int y = 0; y < side - 1; y++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = y * side + x;
                idx.AddRange(new[] { a, a + 1, a + side + 1, a, a + side + 1, a + side });
            }
        return new LodShape { Name = "land", Positions = pos.ToArray(), Uvs = uv.ToArray(), Indices = idx.ToArray(), Scale = scale,
            ShaderFlags1 = 0x80401000u, DiffusePath = "d.dds", NormalPath = "d_n.dds" };
    }

    private static IEnumerable<Vector3> SkyrimPositions(WorldMeshData d)
        => d.Positions.Select(p => new Vector3(p.X, -p.Z, p.Y));

    [Fact]
    public void Terrain_PlacedAtItsBlockAndUncutWithNoLoadedCells()
    {
        var block = new LodBlock(LodKind.Terrain, 4, 4, -12);
        var part = LodMeshBuilder.BuildTerrain(block, FlatTerrain(), null)!;
        part.Cut.Should().Be(0);
        part.Sunk.Should().Be(0);
        part.ModelSpaceNormals.Should().BeTrue();
        part.Data.Indices.Length.Should().Be(8 * 8 * 6);
        var sk = SkyrimPositions(part.Data).ToList();
        sk.Min(p => p.X).Should().BeApproximately(4 * Cell, 0.01f);
        sk.Min(p => p.Y).Should().BeApproximately(-12 * Cell, 0.01f);
        sk.Max(p => p.X).Should().BeApproximately(8 * Cell, 0.01f);
        sk.Should().OnlyContain(p => Math.Abs(p.Z - 100f) < 0.01f);
    }

    [Fact]
    public void Terrain_CutUnderLoadedCellsAndSunkAtTheSeam()
    {
        var block = new LodBlock(LodKind.Terrain, 4, 0, 0);
        // Cells (1,1) and (2,1) loaded: a 2x1-cell hole.
        var loaded = new LoadedCells(new[] { (1, 1), (2, 1) });
        var part = LodMeshBuilder.BuildTerrain(block, FlatTerrain(scale: 4f), loaded)!;
        // 2 cells x 4 squares x 2 triangles each are covered.
        part.Cut.Should().Be(16);
        part.Data.Indices.Length.Should().Be((128 - 16) * 3);
        // Strictly interior vertices of the loaded area: (1.5, 1.5) and (2, 1.5) and (2.5, 1.5) in cells.
        part.Sunk.Should().Be(3);
        var sk = SkyrimPositions(part.Data).ToList();
        // No kept vertex inside the loaded area stays at LOD height: the seam slopes 230 model units
        // (x the shape's scale 4) under the full terrain.
        sk.Where(p => p.X > Cell + 1 && p.X < 3 * Cell - 1 && p.Y > Cell + 1 && p.Y < 2 * Cell - 1)
          .Should().OnlyContain(p => Math.Abs(p.Z - (100f - 230f * 4f)) < 0.01f);
        // Outer-edge vertices keep their height.
        sk.Where(p => Math.Abs(p.X - Cell) < 0.5f && Math.Abs(p.Y - Cell) < 0.5f)
          .Should().OnlyContain(p => Math.Abs(p.Z - 100f) < 0.01f);
        // No kept triangle lies wholly inside the loaded area.
        for (int t = 0; t < part.Data.Indices.Length; t += 3)
        {
            var c = (sk[part.Data.Indices[t]] + sk[part.Data.Indices[t + 1]] + sk[part.Data.Indices[t + 2]]) / 3f;
            bool insideHole = c.X > Cell && c.X < 3 * Cell && c.Y > Cell && c.Y < 2 * Cell;
            insideHole.Should().BeFalse();
        }
    }

    [Fact]
    public void Terrain_FullyLoadedBlockLeavesNothing()
    {
        var block = new LodBlock(LodKind.Terrain, 4, 0, 0);
        var loaded = LoadedCells.Square(1, 1, 2);   // cells -1..3 cover the block 0..3
        LodMeshBuilder.BuildTerrain(block, FlatTerrain(), loaded).Should().BeNull();
    }

    // One triangle per cell of a level-4 block at (bx, by), in segment order k = 4 * cellX + cellY.
    // Object LOD vertices are in world coordinates.
    private static LodShape SegmentedObjects(int bx = 0, int by = 0)
    {
        var pos = new List<Vector3>();
        var idx = new List<int>();
        var segs = new List<(int, int)>();
        for (int k = 0; k < 16; k++)
        {
            float x = (bx + k / 4 + 0.5f) * Cell, y = (by + k % 4 + 0.5f) * Cell;
            int b = pos.Count;
            pos.Add(new Vector3(x, y, 0f)); pos.Add(new Vector3(x + 10, y, 0f)); pos.Add(new Vector3(x, y + 10, 0f));
            idx.AddRange(new[] { b, b + 1, b + 2 });
            segs.Add((k, 1));
        }
        return new LodShape { Name = "obj", Positions = pos.ToArray(), Uvs = new Vector2[pos.Count], Indices = idx.ToArray(), Scale = 4f, Segments = segs };
    }

    [Fact]
    public void Objects_DropTheSegmentsOfLoadedCells()
    {
        var block = new LodBlock(LodKind.Objects, 4, 8, -12);
        var loaded = new LoadedCells(new[] { (8, -12), (9, -10) });   // block cells (0,0) and (1,2): segments 0 and 6
        var part = LodMeshBuilder.BuildObjects(block, SegmentedObjects(8, -12), loaded)!;
        part.Cut.Should().Be(2);
        part.Data.Indices.Length.Should().Be(14 * 3);
        var cells = SkyrimPositions(part.Data).Select(p => ((int)MathF.Floor(p.X / Cell), (int)MathF.Floor(p.Y / Cell))).ToHashSet();
        cells.Should().NotContain((8, -12)).And.NotContain((9, -10)).And.Contain((9, -11));
    }

    [Fact]
    public void Objects_AreAlreadyInWorldCoordinates()
    {
        // Unlike terrain LOD, an object LOD block's vertices are not offset by the block origin.
        var part = LodMeshBuilder.BuildObjects(new LodBlock(LodKind.Objects, 4, 8, -12), SegmentedObjects(8, -12), null)!;
        var sk = SkyrimPositions(part.Data).ToList();
        sk.Min(p => p.X).Should().BeApproximately(8.5f * Cell, 0.01f);
        sk.Min(p => p.Y).Should().BeApproximately(-11.5f * Cell, 0.01f);
    }

    [Fact]
    public void Objects_WithoutSegmentsDropByTriangleCentre()
    {
        var block = new LodBlock(LodKind.Objects, 8, 0, 0);
        var shape = SegmentedObjects();
        var unsegmented = new LodShape { Name = "obj", Positions = shape.Positions, Uvs = shape.Uvs, Indices = shape.Indices, Scale = 8f };
        var part = LodMeshBuilder.BuildObjects(block, unsegmented, new LoadedCells(new[] { (0, 0), (3, 3) }))!;
        part.Cut.Should().Be(2);
    }

    [Fact]
    public void Water_LivesInTheTerrainBlocksAndUsesTheirReach()
    {
        LodKinds.All.HasFlag(LodKinds.Water).Should().BeTrue();
        new LodBlock(LodKind.Water, 4, 4, -12).MeshPath("Tamriel").Should().Be(@"meshes\terrain\Tamriel\Tamriel.4.4.-12.btr");
        var d = new LodDistances();
        for (int i = 0; i < 4; i++) d.Reach(LodKind.Water, i).Should().Be(d.Reach(LodKind.Terrain, i));
        var eye = new Vector2(17224f, -47204f);
        LodSelector.Select(LodKind.Water, Tamriel, d, eye).Should().Equal(
            LodSelector.Select(LodKind.Terrain, Tamriel, d, eye).Select(b => b with { Kind = LodKind.Water }));
    }

    // Level-4 LOD water as the vanilla files store it: one flat quad per cell with water, local to the
    // block's south-west corner, in a BSSubIndexTriShape whose segment k is cell (k / 4, k % 4).
    private static LodShape WaterQuads(params (int Cx, int Cy, float Z)[] cells)
    {
        var pos = new List<Vector3>();
        var idx = new List<int>();
        var segs = new List<(int, int)>();
        var byCell = cells.ToDictionary(c => (c.Cx, c.Cy), c => c.Z);
        for (int k = 0; k < 16; k++)
        {
            int first = idx.Count / 3;
            if (byCell.TryGetValue((k / 4, k % 4), out float z))
            {
                float x0 = k / 4 * Cell, y0 = k % 4 * Cell;
                int b = pos.Count;
                pos.Add(new Vector3(x0, y0, z)); pos.Add(new Vector3(x0 + Cell, y0, z));
                pos.Add(new Vector3(x0, y0 + Cell, z)); pos.Add(new Vector3(x0 + Cell, y0 + Cell, z));
                idx.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            }
            segs.Add((first, idx.Count / 3 - first));
        }
        return new LodShape { Name = "", Positions = pos.ToArray(), Uvs = new Vector2[pos.Count], Indices = idx.ToArray(),
            Scale = 4f, Segments = segs, IsWater = true };
    }

    [Fact]
    public void Water_PlacedAtItsBlockAtEachQuadsOwnHeight()
    {
        var block = new LodBlock(LodKind.Terrain, 4, 4, -12);
        var part = LodMeshBuilder.BuildWater(block, WaterQuads((0, 0, -250f), (2, 3, -6070f)), null)!;
        part.Kind.Should().Be(LodKind.Water);
        part.Cut.Should().Be(0);
        part.Data.Indices.Length.Should().Be(4 * 3);
        var sk = SkyrimPositions(part.Data).ToList();
        sk.Min(p => p.X).Should().BeApproximately(4 * Cell, 0.01f);
        sk.Min(p => p.Y).Should().BeApproximately(-12 * Cell, 0.01f);
        sk.Select(p => p.Z).Distinct().Should().BeEquivalentTo(new[] { -250f, -6070f });
        // UVs are world XY (the GL side divides by the water type's noise scale); the normal is up.
        int o = 0;
        part.Data.Vertices[o + 6].Should().BeApproximately(sk[0].X, 0.01f);
        part.Data.Vertices[o + 7].Should().BeApproximately(sk[0].Y, 0.01f);
        new Vector3(part.Data.Vertices[o + 3], part.Data.Vertices[o + 4], part.Data.Vertices[o + 5]).Y.Should().BeApproximately(1f, 1e-5f);
        // Every kept triangle faces up (Y-up: the cross product of its edges points +Y).
        var yup = part.Data.Positions;
        for (int t = 0; t < part.Data.Indices.Length; t += 3)
        {
            var a = yup[part.Data.Indices[t]]; var b = yup[part.Data.Indices[t + 1]]; var c = yup[part.Data.Indices[t + 2]];
            Vector3.Cross(b - a, c - a).Y.Should().BeGreaterThan(0f);
        }
    }

    [Fact]
    public void Water_DropsTheSegmentsOfLoadedCells()
    {
        var block = new LodBlock(LodKind.Terrain, 4, 8, -12);
        var shape = WaterQuads((0, 0, 0f), (1, 2, 0f), (3, 3, 0f));
        var part = LodMeshBuilder.BuildWater(block, shape, new LoadedCells(new[] { (9, -10) }))!;   // block cell (1, 2)
        part.Cut.Should().Be(2);
        var cells = SkyrimPositions(part.Data).Select(p => ((int)MathF.Floor(p.X / Cell + 1e-4f), (int)MathF.Floor(p.Y / Cell + 1e-4f))).ToHashSet();
        cells.Should().Contain((8, -12)).And.Contain((11, -9)).And.NotContain((9, -10));
        LodMeshBuilder.BuildWater(block, WaterQuads((1, 2, 0f)), new LoadedCells(new[] { (9, -10) })).Should().BeNull();
    }

    [Fact]
    public void Objects_KeepVertexColoursButNotTheirAlphaOnLodShapes()
    {
        var shape = SegmentedObjects();
        var colors = Enumerable.Repeat(new Vector4(0.5f, 0.6f, 0.7f, 0.25f), shape.Positions.Length).ToArray();
        LodShape With(uint flags2) => new()
        {
            Name = "objHD", Positions = shape.Positions, Uvs = shape.Uvs, Indices = shape.Indices, Scale = 4f,
            Segments = shape.Segments, ShaderFlags2 = flags2, Colors = colors,
        };
        var block = new LodBlock(LodKind.Objects, 4, 0, 0);
        // SLSF2_Vertex_Colors | SLSF2_HD_LOD_Objects: rgb kept, alpha 1 (the engine skips vertex alpha on LOD objects).
        var hd = LodMeshBuilder.BuildObjects(block, With(0x80000021u), null)!;
        hd.HasVertexColors.Should().BeTrue();
        hd.Data.Vertices[8].Should().BeApproximately(0.5f, 1e-6f);
        hd.Data.Vertices[10].Should().BeApproximately(0.7f, 1e-6f);
        hd.Data.Vertices[11].Should().Be(1f);
        // A shape without LOD flags (DynDOLOD grass LOD: SLSF2_Vertex_Colors only) keeps the alpha.
        LodMeshBuilder.BuildObjects(block, With(0x21u), null)!.Data.Vertices[11].Should().BeApproximately(0.25f, 1e-6f);
        // Without SLSF2_Vertex_Colors the colours are white.
        var plain = LodMeshBuilder.BuildObjects(block, With(0x5u), null)!;
        plain.HasVertexColors.Should().BeFalse();
        plain.Data.Vertices[8].Should().Be(1f);
    }

    private static byte[] TreeList(params TreeLodType[] types)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(types.Length);
        foreach (var t in types)
        {
            w.Write(t.Index); w.Write(t.Width); w.Write(t.Height);
            w.Write(t.UvMin.X); w.Write(t.UvMin.Y); w.Write(t.UvMax.X); w.Write(t.UvMax.Y); w.Write(0);
        }
        return ms.ToArray();
    }

    private static byte[] TreeBlock(int type, params TreeLodInstance[] trees)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(1); w.Write(type); w.Write(trees.Length);
        foreach (var t in trees)
        {
            w.Write(t.Position.X); w.Write(t.Position.Y); w.Write(t.Position.Z);
            w.Write(t.Rotation); w.Write(t.Scale); w.Write(t.RefFormId); w.Write(0); w.Write(0);
        }
        return ms.ToArray();
    }

    [Fact]
    public void TreeFiles_RoundTripTheXEditLayouts()
    {
        var type = new TreeLodType(9, 566.4f, 1521.5f, new Vector2(0.8f, 0.0f), new Vector2(0.9f, 0.25f));
        var list = TreeLodFiles.ParseList(TreeList(type));
        list.Should().ContainKey(9).WhoseValue.Should().Be(type);
        var inst = new TreeLodInstance(new Vector3(3513f, 1330f, -6002f), 3.258f, 0.72f, 0x000C8E76);
        var groups = TreeLodFiles.ParseBlock(TreeBlock(9, inst, inst));
        groups.Should().ContainSingle();
        groups[0].TypeIndex.Should().Be(9);
        groups[0].Instances.Should().HaveCount(2).And.OnlyContain(i => i == inst);
        TreeLodFiles.ParseBlock(Array.Empty<byte>()).Should().BeEmpty();
    }

    [Fact]
    public void Trees_AreTwoCrossedPlanesStandingOnTheInstance()
    {
        var type = new TreeLodType(0, 400f, 1000f, new Vector2(0.25f, 0f), new Vector2(0.5f, 0.5f));
        var types = new Dictionary<int, TreeLodType> { [0] = type };
        var inst = new TreeLodInstance(new Vector3(100f, 200f, 50f), 0f, 2f, 1);
        var block = new LodBlock(LodKind.Trees, 4, 0, 0);
        var part = LodMeshBuilder.BuildTrees(block, new[] { (0, new List<TreeLodInstance> { inst }) }, types, null, "atlas.dds")!;
        part.AlphaTest.Should().BeTrue();
        part.DoubleSided.Should().BeTrue();
        part.Data.Indices.Length.Should().Be(12);
        var sk = SkyrimPositions(part.Data).ToList();
        sk.Min(p => p.Z).Should().BeApproximately(50f, 0.01f);
        sk.Max(p => p.Z).Should().BeApproximately(50f + 2000f, 0.01f);
        sk.Min(p => p.X).Should().BeApproximately(100f - 400f, 0.01f);   // width 400 x scale 2, centred
        sk.Max(p => p.Y).Should().BeApproximately(200f + 400f, 0.01f);
        // A tree in a loaded cell is left to the full-detail cell.
        LodMeshBuilder.BuildTrees(block, new[] { (0, new List<TreeLodInstance> { inst }) }, types,
            new LoadedCells(new[] { (0, 0) }), "atlas.dds").Should().BeNull();
    }
}

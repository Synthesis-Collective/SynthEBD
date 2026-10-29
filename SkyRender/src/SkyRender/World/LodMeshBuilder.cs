using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>One LOD mesh ready to upload: geometry in the renderer's Y-up space and what to draw it with.</summary>
public sealed class LodMeshPart
{
    public required LodBlock Block { get; init; }
    public LodKind Kind => Block.Kind;
    public required WorldMeshData Data { get; init; }
    public string? DiffusePath { get; init; }
    public string? NormalPath { get; init; }
    /// <summary>The normal texture is in model space (terrain LOD) rather than tangent space.</summary>
    public bool ModelSpaceNormals { get; init; }
    public bool AlphaTest { get; init; }
    public float AlphaThreshold { get; init; }
    public bool DoubleSided { get; init; }
    public bool HasSpecular { get; init; }
    /// <summary>The vertex colours are the shape's own (<c>SLSF2_Vertex_Colors</c>) and multiply the
    /// diffuse; otherwise they are white.</summary>
    public bool HasVertexColors { get; init; }
    /// <summary>Triangles (or, for trees, instances) left out because full-detail cells cover them.</summary>
    public int Cut { get; init; }
    /// <summary>Terrain only: vertices lowered under the loaded cells (see <see cref="LodMeshBuilder.TerrainSink"/>).</summary>
    public int Sunk { get; init; }
}

/// <summary>
/// Builds LOD geometry, GL-free. Every builder takes the host's <see cref="LoadedCells"/> (null: none)
/// and removes what lies under them, as the engine hides LOD where it draws the full cells:
/// <list type="bullet">
/// <item>Terrain: the engine's LOD landscape vertex shader lowers every vertex strictly inside the
/// loaded area by 230 model units (Community Shaders <c>LodLandscape.hlsli</c>,
/// <c>AdjustLodLandscapeVertexPositionMS</c>), so the full terrain covers the LOD and the seam slopes
/// under it. This builder does the same, and also drops each triangle the loaded cells wholly cover,
/// which can never show.</item>
/// <item>Objects: level-4 object LOD is a BSSubIndexTriShape whose segment k holds the objects of the
/// block's cell (k / 4, k % 4); the segments of loaded cells are dropped. (That mapping is read off
/// the vanilla files: 99.5% of the triangles of every vanilla level-4 block lie within one cell of
/// the cell their segment names, the rest being large objects that cross cells.) Higher levels have
/// one segment; there, a triangle is dropped when the cell its centre is in is loaded.</item>
/// <item>Trees: an instance standing in a loaded cell is dropped (the cell draws the full tree).</item>
/// <item>Water: level-4 LOD water is a BSSubIndexTriShape whose segment k holds the quads of cell
/// (k / 4, k % 4), as for objects (every triangle of all 1444 vanilla level-4 water shapes lies in
/// the cell its segment names); the loaded cells' segments are dropped, and the cells draw their
/// own water. Higher levels have no segments; a triangle is dropped when its centre's cell is loaded.</item>
/// </list>
/// </summary>
public static class LodMeshBuilder
{
    private const int Stride = 18;

    /// <summary>How far the engine lowers LOD terrain under the loaded cells, in model units.</summary>
    public const float TerrainSink = 230f;

    /// <summary>A terrain LOD shape, placed at its block and cut under the loaded cells. Null when
    /// nothing is left.</summary>
    public static LodMeshPart? BuildTerrain(LodBlock block, LodShape shape, LoadedCells? loaded)
    {
        var origin = new Vector3(block.OriginX, block.OriginY, 0f);
        int n = shape.Positions.Length;
        var world = new Vector3[n];
        for (int i = 0; i < n; i++) world[i] = shape.Positions[i] + origin;
        var normals = shape.Normals ?? SmoothNormals(world, shape.Indices);

        float sink = TerrainSink * shape.Scale;
        var sunk = new bool[n];
        int sunkCount = 0;
        if (loaded is { Count: > 0 })
            for (int i = 0; i < n; i++)
                if (loaded.IsInterior(world[i].X, world[i].Y)) { sunk[i] = true; sunkCount++; }

        var keep = new List<int>(shape.Indices.Length);
        int cut = 0;
        for (int t = 0; t + 2 < shape.Indices.Length; t += 3)
        {
            int a = shape.Indices[t], b = shape.Indices[t + 1], c = shape.Indices[t + 2];
            if (loaded is { Count: > 0 } && Covered(loaded, world[a], world[b], world[c]))
            { cut++; continue; }
            keep.Add(a); keep.Add(b); keep.Add(c);
        }
        if (keep.Count == 0) return null;

        var placed = new Vector3[n];
        for (int i = 0; i < n; i++) placed[i] = sunk[i] ? world[i] - new Vector3(0f, 0f, sink) : world[i];
        var data = Pack($"lod-terrain:{block.Level}.{block.X}.{block.Y}:{shape.Name}", placed, normals, shape.Uvs,
            null, null, keep);
        return new LodMeshPart
        {
            Block = block, Data = data,
            DiffusePath = shape.DiffusePath, NormalPath = shape.NormalPath,
            ModelSpaceNormals = shape.ModelSpaceNormals,
            DoubleSided = shape.DoubleSided,
            HasSpecular = false,   // the engine's LOD landscape shader sets glossiness 0
            Cut = cut, Sunk = sunkCount,
        };
    }

    /// <summary>An object LOD shape, placed at its block, without the objects of loaded cells. Null
    /// when nothing is left.</summary>
    public static LodMeshPart? BuildObjects(LodBlock block, LodShape shape, LoadedCells? loaded)
    {
        // Object LOD vertices are already in world coordinates (unlike terrain LOD, whose vertices are
        // local to the block): a vanilla tamriel.4.8.-12.bto spans x 28751..51057, y -51083..-30658.
        var world = shape.Positions;
        int n = world.Length;
        int triCount = shape.Indices.Length / 3;

        var dropTri = CutByCell(block, world, shape.Indices, shape.Segments, loaded, out int cut);

        var keep = new List<int>(shape.Indices.Length);
        for (int t = 0; t < triCount; t++)
        {
            if (dropTri[t]) continue;
            keep.Add(shape.Indices[t * 3]); keep.Add(shape.Indices[t * 3 + 1]); keep.Add(shape.Indices[t * 3 + 2]);
        }
        if (keep.Count == 0) return null;

        var normals = shape.Normals ?? SmoothNormals(world, shape.Indices);
        // SLSF2_Vertex_Colors: the colour multiplies the diffuse (Community Shaders Lighting.hlsl,
        // color.xyz *= vertexColor). Vanilla's HD object LOD (objHD / objsnowHD, the mountains) carries
        // them, as does DynDOLOD's object and grass LOD. The alpha is not applied to LOD objects
        // (Lighting.hlsl skips alpha *= vertex alpha for LODOBJECTS and LODOBJECTSHD), so it is
        // written as 1 for shapes with SLSF2_LOD_Objects or SLSF2_HD_LOD_Objects.
        var colors = shape.HasVertexColors ? shape.Colors : null;
        if (colors is not null && (shape.ShaderFlags2 & 0x80000004u) != 0)
        {
            colors = (System.Numerics.Vector4[])colors.Clone();
            for (int i = 0; i < colors.Length; i++) colors[i].W = 1f;
        }
        var data = Pack($"lod-objects:{block.Level}.{block.X}.{block.Y}:{shape.Name}", world, normals, shape.Uvs,
            shape.Tangents, shape.Bitangents, keep, colors);
        return new LodMeshPart
        {
            Block = block, Data = data,
            DiffusePath = shape.DiffusePath, NormalPath = shape.NormalPath,
            ModelSpaceNormals = shape.ModelSpaceNormals,
            AlphaTest = shape.AlphaTest, AlphaThreshold = shape.AlphaThreshold,
            DoubleSided = shape.DoubleSided,
            HasSpecular = shape.HasSpecular,
            HasVertexColors = colors is not null,
            Cut = cut,
        };
    }

    /// <summary>
    /// A terrain block's LOD water quads (<see cref="LodShape.IsWater"/>), placed at the block (their
    /// vertices are local to its south-west corner, like the terrain's) and cut under the loaded cells.
    /// The quads keep their own heights: the generator writes each cell's water height (the cell's,
    /// else the worldspace default), so one block can hold several. Each vertex's UV is its world XY
    /// (divide by the water type's noise scale for the wave normal map, as
    /// <see cref="GlWorldFactory.CreateDistantLod(DistantLodData, Func{string, int?}, int, WaterMaterial?)"/> does);
    /// the normal is up. Null when nothing is left.
    /// </summary>
    public static LodMeshPart? BuildWater(LodBlock block, LodShape shape, LoadedCells? loaded)
    {
        var origin = new Vector3(block.OriginX, block.OriginY, 0f);
        int n = shape.Positions.Length;
        var world = new Vector3[n];
        for (int i = 0; i < n; i++) world[i] = shape.Positions[i] + origin;
        int triCount = shape.Indices.Length / 3;
        var dropTri = CutByCell(block, world, shape.Indices, shape.Segments, loaded, out int cut);
        var keep = new List<int>(shape.Indices.Length);
        for (int t = 0; t < triCount; t++)
        {
            if (dropTri[t]) continue;
            int a = shape.Indices[t * 3], b = shape.Indices[t * 3 + 1], c = shape.Indices[t * 3 + 2];
            // Wound so the face points up, whichever way the file winds it.
            if (Vector3.Cross(world[b] - world[a], world[c] - world[a]).Z < 0f) (b, c) = (c, b);
            keep.Add(a); keep.Add(b); keep.Add(c);
        }
        if (keep.Count == 0) return null;
        var normals = new Vector3[n];
        var tangents = new Vector3[n];
        var bitangents = new Vector3[n];
        var uvs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            normals[i] = Vector3.UnitZ; tangents[i] = Vector3.UnitX; bitangents[i] = Vector3.UnitY;
            uvs[i] = new Vector2(world[i].X, world[i].Y);
        }
        var data = Pack($"lod-water:{block.Level}.{block.X}.{block.Y}:{shape.Name}", world, normals, uvs,
            tangents, bitangents, keep);
        return new LodMeshPart
        {
            Block = block with { Kind = LodKind.Water }, Data = data,
            DoubleSided = true,
            Cut = cut,
        };
    }

    // Which triangles lie under loaded cells: by segment in a level-4 BSSubIndexTriShape (segment k =
    // the block's cell (k / 4, k % 4)), otherwise by the cell of the triangle's centre.
    private static bool[] CutByCell(LodBlock block, Vector3[] world, int[] indices,
        IReadOnlyList<(int FirstTriangle, int TriangleCount)> segments, LoadedCells? loaded, out int cut)
    {
        int triCount = indices.Length / 3;
        var dropTri = new bool[triCount];
        cut = 0;
        if (loaded is not { Count: > 0 } || !loaded.AnyIn(block.X, block.Y, block.X + block.Level - 1, block.Y + block.Level - 1))
            return dropTri;
        bool bySegment = block.Level == 4 && segments.Count > 0 && segments.Count <= 16;
        if (bySegment)
        {
            for (int k = 0; k < segments.Count; k++)
            {
                if (!loaded.Contains(block.X + k / 4, block.Y + k % 4)) continue;
                var (first, count) = segments[k];
                for (int t = Math.Max(0, first); t < Math.Min(triCount, first + count); t++)
                    if (!dropTri[t]) { dropTri[t] = true; cut++; }
            }
        }
        else
        {
            for (int t = 0; t < triCount; t++)
            {
                var c = (world[indices[t * 3]] + world[indices[t * 3 + 1]] + world[indices[t * 3 + 2]]) / 3f;
                if (loaded.Contains((int)MathF.Floor(c.X / TerrainCell.CellSize), (int)MathF.Floor(c.Y / TerrainCell.CellSize)))
                { dropTri[t] = true; cut++; }
            }
        }
        return dropTri;
    }

    /// <summary>Alpha threshold for tree billboards: 128/255, the alpha threshold xEdit's LOD
    /// generator prepares the atlas against (iDefaultAlphaThreshold).</summary>
    public const float TreeAlphaThreshold = 128f / 255f;

    /// <summary>
    /// A tree LOD block's instances as billboards: two double-sided planes crossing at 90°, standing
    /// on the instance position, turned about Z by its rotation, <see cref="TreeLodType.Width"/> ×
    /// scale wide and <see cref="TreeLodType.Height"/> × scale tall, textured from the type's atlas
    /// rectangle. That shape is the DynDOLOD manual's description of the engine's tree LOD ("2 double
    /// sided planes intersecting each other at a 90 degree angle", upright, fixed centre), and the
    /// engine's distant-tree vertex shader rotates a shared model by the per-instance angle about Z.
    /// Types missing from the list are skipped. Null when nothing is left.
    /// </summary>
    public static LodMeshPart? BuildTrees(LodBlock block, IReadOnlyList<(int TypeIndex, List<TreeLodInstance> Instances)> groups,
        IReadOnlyDictionary<int, TreeLodType> types, LoadedCells? loaded, string atlasPath)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();
        int cut = 0;
        foreach (var (typeIndex, instances) in groups)
        {
            if (!types.TryGetValue(typeIndex, out var type)) continue;
            foreach (var inst in instances)
            {
                if (loaded is { Count: > 0 } && loaded.Contains((int)MathF.Floor(inst.Position.X / TerrainCell.CellSize),
                                                               (int)MathF.Floor(inst.Position.Y / TerrainCell.CellSize)))
                { cut++; continue; }
                float w = type.Width * inst.Scale, h = type.Height * inst.Scale;
                if (w <= 0f || h <= 0f) continue;
                float c = MathF.Cos(inst.Rotation), s = MathF.Sin(inst.Rotation);
                Plane(new Vector3(c, s, 0f));
                Plane(new Vector3(-s, c, 0f));

                void Plane(Vector3 along)
                {
                    var half = along * (w * 0.5f);
                    var up = new Vector3(0f, 0f, h);
                    var normal = new Vector3(-along.Y, along.X, 0f);
                    int b = pos.Count;
                    pos.Add(inst.Position - half); uv.Add(new Vector2(type.UvMin.X, type.UvMax.Y));
                    pos.Add(inst.Position + half); uv.Add(new Vector2(type.UvMax.X, type.UvMax.Y));
                    pos.Add(inst.Position + half + up); uv.Add(new Vector2(type.UvMax.X, type.UvMin.Y));
                    pos.Add(inst.Position - half + up); uv.Add(new Vector2(type.UvMin.X, type.UvMin.Y));
                    for (int k = 0; k < 4; k++) nrm.Add(normal);
                    idx.Add(b); idx.Add(b + 1); idx.Add(b + 2);
                    idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
                }
            }
        }
        if (idx.Count == 0) return null;
        var data = Pack($"lod-trees:{block.Level}.{block.X}.{block.Y}", pos.ToArray(), nrm.ToArray(), uv.ToArray(),
            null, null, idx);
        return new LodMeshPart
        {
            Block = block, Data = data,
            DiffusePath = atlasPath,
            AlphaTest = true, AlphaThreshold = TreeAlphaThreshold,
            DoubleSided = true,
            Cut = cut,
        };
    }

    // True when the loaded cells cover the triangle's footprint.
    private static bool Covered(LoadedCells loaded, Vector3 a, Vector3 b, Vector3 c)
        => loaded.Covers(MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Y, MathF.Min(b.Y, c.Y)),
                         MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));

    /// <summary>Area-weighted vertex normals (Z-up) for shapes that store none.</summary>
    public static Vector3[] SmoothNormals(Vector3[] positions, int[] indices)
    {
        var n = new Vector3[positions.Length];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            var f = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            n[a] += f; n[b] += f; n[c] += f;
        }
        for (int i = 0; i < n.Length; i++)
            n[i] = n[i].LengthSquared() > 1e-12f ? Vector3.Normalize(n[i]) : Vector3.UnitZ;
        return n;
    }

    // Interleaves Z-up data into the renderer's Y-up vertex layout, keeping only the vertices the
    // kept triangles use.
    private static WorldMeshData Pack(string name, Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        Vector3[]? tangents, Vector3[]? bitangents, List<int> keptIndices, Vector4[]? colors = null)
    {
        var remap = new int[positions.Length];
        Array.Fill(remap, -1);
        var order = new List<int>();
        var indices = new int[keptIndices.Count];
        for (int i = 0; i < keptIndices.Count; i++)
        {
            int v = keptIndices[i];
            if (remap[v] < 0) { remap[v] = order.Count; order.Add(v); }
            indices[i] = remap[v];
        }
        var vertices = new float[order.Count * Stride];
        var yup = new Vector3[order.Count];
        for (int k = 0; k < order.Count; k++)
        {
            int v = order[k];
            var p = WorldMeshBuilder.ToYUp(positions[v]);
            var nn = WorldMeshBuilder.ToYUp(normals[v]);
            var uv = v < uvs.Length ? uvs[v] : Vector2.Zero;
            var t = tangents is not null ? WorldMeshBuilder.ToYUp(tangents[v]) : Vector3.Zero;
            var b = bitangents is not null ? WorldMeshBuilder.ToYUp(bitangents[v]) : Vector3.Zero;
            yup[k] = p;
            int o = k * Stride;
            vertices[o + 0] = p.X; vertices[o + 1] = p.Y; vertices[o + 2] = p.Z;
            vertices[o + 3] = nn.X; vertices[o + 4] = nn.Y; vertices[o + 5] = nn.Z;
            vertices[o + 6] = uv.X; vertices[o + 7] = uv.Y;
            var col = colors is not null && v < colors.Length ? colors[v] : Vector4.One;
            vertices[o + 8] = col.X; vertices[o + 9] = col.Y; vertices[o + 10] = col.Z; vertices[o + 11] = col.W;
            vertices[o + 12] = t.X; vertices[o + 13] = t.Y; vertices[o + 14] = t.Z;
            vertices[o + 15] = b.X; vertices[o + 16] = b.Y; vertices[o + 17] = b.Z;
        }
        return new WorldMeshData { Name = name, Vertices = vertices, Indices = indices, Positions = yup };
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>
/// CPU geometry for one terrain quadrant or water surface, in the renderer's Y-up space and in the
/// interleaved layout <see cref="GlMesh.Upload"/> takes (position 3, normal 3, uv 2, colour 4,
/// tangent 3, bitangent 3). GL-free, so it can be built off the render thread and tested.
/// </summary>
public sealed class WorldMeshData
{
    public required string Name { get; init; }
    public required float[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Y-up positions, one per vertex (for picking, bounds and the alpha sort).</summary>
    public required Vector3[] Positions { get; init; }
    /// <summary>Terrain only: <see cref="TerrainQuadrant.MaxLayers"/> layer weights per vertex.</summary>
    public float[]? LayerWeights { get; init; }
    /// <summary>Terrain only: the layers the weights refer to, in blend order.</summary>
    public IReadOnlyList<TerrainLayer> Layers { get; init; } = Array.Empty<TerrainLayer>();
    /// <summary>Water only: grid vertices whose ground lies below the surface.</summary>
    public int SubmergedVertexCount { get; init; }
}

/// <summary>Options for <see cref="WorldMeshBuilder.BuildTerrainQuadrant"/>.</summary>
public sealed record TerrainBuildOptions
{
    /// <summary>World units per texture repeat (UV = world XY / this). The default, 12 repeats per
    /// quadrant (2048 / 12), matched the game's ground-texture scale in a side-by-side in-game
    /// comparison; it is not a documented engine constant.</summary>
    public float TextureRepeat { get; init; } = 2048f / 12f;

    /// <summary>Opacity at or below which a layer counts as unpainted. A grid square whose four
    /// corners are all unpainted takes no contribution from that layer.</summary>
    public float PaintedThreshold { get; init; } = 0.004f;

    /// <summary>How the layers combine (used by <see cref="GlWorldFactory.CreateTerrain(TerrainCell, Func{string, int?}, int, TerrainBuildOptions?)"/>).
    /// <see cref="TerrainBlendMode.Over"/> matched the game in a side-by-side in-game comparison.</summary>
    public TerrainBlendMode Blend { get; init; } = TerrainBlendMode.Over;
}

/// <summary>Builds terrain and water geometry from <see cref="TerrainCell"/> / <see cref="WaterSurface"/> data.</summary>
public static class WorldMeshBuilder
{
    private const int Stride = 18;

    /// <summary>Skyrim Z-up to the renderer's Y-up: (x, z, −y), the same mapping as NIF geometry.</summary>
    public static Vector3 ToYUp(Vector3 z) => new(z.X, z.Z, -z.Y);

    /// <summary>
    /// One quadrant (16×16 grid squares) as triangles, three vertices each: shaded with the cell's
    /// vertex normals when it has them, else with each triangle's face normal; coloured with the
    /// cell's vertex colours when it has them, else white; with the layer weights of <paramref name="layers"/> (at most
    /// <see cref="TerrainQuadrant.MaxLayers"/>, in blend order) per vertex.
    /// </summary>
    public static WorldMeshData BuildTerrainQuadrant(TerrainCell cell, TerrainQuadrantIndex quadrant,
        IReadOnlyList<TerrainLayer> layers, TerrainBuildOptions? options = null)
    {
        options ??= new TerrainBuildOptions();
        if (layers.Count > TerrainQuadrant.MaxLayers)
            throw new ArgumentException($"at most {TerrainQuadrant.MaxLayers} layers per quadrant", nameof(layers));
        int q = (int)quadrant;
        int qx0 = (q & 1) * 16, qy0 = q >= 2 ? 16 : 0;
        const int squares = 16 * 16, vertsPerSquare = 6;
        int vertexCount = squares * vertsPerSquare;

        var vertices = new float[vertexCount * Stride];
        var weights = new float[vertexCount * TerrainQuadrant.MaxLayers];
        var positions = new Vector3[vertexCount];
        var indices = new int[vertexCount];
        int v = 0;

        // Layer opacity at quadrant-local grid vertex (lx, ly), 17x17 x-fastest.
        static float Op(TerrainLayer l, int lx, int ly)
        {
            int i = ly * TerrainCell.QuadrantSide + lx;
            return i >= 0 && i < l.Opacity.Length ? l.Opacity[i] : 0f;
        }

        var squareWeights = new float[TerrainQuadrant.MaxLayers * 4];
        for (int ly = 0; ly < 16; ly++)
            for (int lx = 0; lx < 16; lx++)
            {
                // Corner weights per layer: 00, 10, 11, 01. A square no corner of which is painted
                // takes nothing from the layer, as an unpainted square of a painted sheet would.
                Array.Clear(squareWeights);
                for (int li = 0; li < layers.Count; li++)
                {
                    float o00 = Op(layers[li], lx, ly), o10 = Op(layers[li], lx + 1, ly);
                    float o11 = Op(layers[li], lx + 1, ly + 1), o01 = Op(layers[li], lx, ly + 1);
                    float t = options.PaintedThreshold;
                    if (o00 <= t && o10 <= t && o11 <= t && o01 <= t) continue;
                    squareWeights[li * 4 + 0] = o00; squareWeights[li * 4 + 1] = o10;
                    squareWeights[li * 4 + 2] = o11; squareWeights[li * 4 + 3] = o01;
                }

                int x = qx0 + lx, y = qy0 + ly;
                var p00 = cell.VertexPosition(x, y);
                var p10 = cell.VertexPosition(x + 1, y);
                var p11 = cell.VertexPosition(x + 1, y + 1);
                var p01 = cell.VertexPosition(x, y + 1);
                // Two triangles: (x,y)-(x+1,y)-(x+1,y+1) and (x,y)-(x+1,y+1)-(x,y+1).
                AddTriangle(p00, p10, p11, (x, y), (x + 1, y), (x + 1, y + 1), 0, 1, 2);
                AddTriangle(p00, p11, p01, (x, y), (x + 1, y + 1), (x, y + 1), 0, 2, 3);
            }

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c, (int X, int Y) ga, (int X, int Y) gb, (int X, int Y) gc,
            int ca, int cb, int cc)
        {
            if (cell.Normals is { } normals)
            {
                Emit(a, ToYUp(normals[ga.Y * TerrainCell.Side + ga.X]), ga, ca);
                Emit(b, ToYUp(normals[gb.Y * TerrainCell.Side + gb.X]), gb, cb);
                Emit(c, ToYUp(normals[gc.Y * TerrainCell.Side + gc.X]), gc, cc);
                return;
            }
            var n = Vector3.Cross(b - a, c - a);
            n = n.LengthSquared() > 1e-10f ? Vector3.Normalize(n) : Vector3.UnitZ;
            var ny = ToYUp(n);
            Emit(a, ny, ga, ca); Emit(b, ny, gb, cb); Emit(c, ny, gc, cc);
        }

        void Emit(Vector3 p, Vector3 normalYUp, (int X, int Y) grid, int corner)
        {
            var py = ToYUp(p);
            positions[v] = py;
            indices[v] = v;
            int o = v * Stride;
            vertices[o + 0] = py.X; vertices[o + 1] = py.Y; vertices[o + 2] = py.Z;
            vertices[o + 3] = normalYUp.X; vertices[o + 4] = normalYUp.Y; vertices[o + 5] = normalYUp.Z;
            vertices[o + 6] = p.X / options.TextureRepeat; vertices[o + 7] = p.Y / options.TextureRepeat;
            var col = cell.Colors is { } colors ? colors[grid.Y * TerrainCell.Side + grid.X] : Vector3.One;
            vertices[o + 8] = col.X; vertices[o + 9] = col.Y; vertices[o + 10] = col.Z; vertices[o + 11] = 1f;
            // Tangent frame for tangent-space normal maps: UV runs along world +X / +Y, so the
            // tangent is +X and the bitangent +Y, each made perpendicular to the normal.
            var t = Orthogonal(ToYUp(Vector3.UnitX), normalYUp);
            var b = Orthogonal(ToYUp(Vector3.UnitY), normalYUp);
            vertices[o + 12] = t.X; vertices[o + 13] = t.Y; vertices[o + 14] = t.Z;
            vertices[o + 15] = b.X; vertices[o + 16] = b.Y; vertices[o + 17] = b.Z;
            for (int li = 0; li < layers.Count; li++)
                weights[v * TerrainQuadrant.MaxLayers + li] = squareWeights[li * 4 + corner];
            v++;
        }

        static Vector3 Orthogonal(Vector3 axis, Vector3 n)
        {
            var v = axis - n * Vector3.Dot(axis, n);
            return v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : axis;
        }

        return new WorldMeshData
        {
            Name = $"terrain:{cell.GridX},{cell.GridY}:{quadrant}",
            Vertices = vertices,
            Indices = indices,
            Positions = positions,
            LayerWeights = weights,
            Layers = layers,
        };
    }

    /// <summary>
    /// A water surface as a grid of <see cref="WaterMaterial.GridResolution"/> squares per side.
    /// Each vertex's colour is the body colour (shallow to deep by depth to <paramref name="ground"/>;
    /// no ground under a vertex counts as deep) and its alpha the shore ramp. The view-dependent
    /// sky reflection is added in the shader from the live camera.
    /// </summary>
    public static WorldMeshData BuildWater(WaterSurface surface, TerrainHeightField? ground)
        => BuildWater(surface, ground is null ? null : ground.HeightAt);

    /// <inheritdoc cref="BuildWater(WaterSurface, TerrainHeightField?)"/>
    /// <param name="groundHeightAt">Ground height at a world XY, or null where there is no ground.</param>
    public static WorldMeshData BuildWater(WaterSurface surface, Func<float, float, float?>? groundHeightAt)
    {
        var m = surface.Material;
        int grid = Math.Max(1, m.GridResolution);
        int side = grid + 1;
        float band = m.ShallowBand > 0f ? m.ShallowBand : 192f;
        float shore = Math.Clamp(m.ShoreAlpha, 0f, 1f);
        float uvScale = m.NoiseUvScale > 1e-3f ? m.NoiseUvScale : 1024f;

        var vertices = new float[side * side * Stride];
        var positions = new Vector3[side * side];
        var normal = ToYUp(Vector3.UnitZ);
        var tangent = ToYUp(Vector3.UnitX);
        var bitangent = ToYUp(Vector3.UnitY);
        int submerged = 0;
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
            {
                int i = gy * side + gx;
                float x = surface.X0 + (surface.X1 - surface.X0) * gx / grid;
                float y = surface.Y0 + (surface.Y1 - surface.Y0) * gy / grid;
                float? groundZ = groundHeightAt?.Invoke(x, y);
                float depth = groundZ is { } gz ? surface.Height - gz : band;
                if (depth > 0f) submerged++;
                float t = Math.Clamp(depth / band, 0f, 1f);
                var body = Vector3.Lerp(m.ShallowColor, m.DeepColor, t);

                var p = ToYUp(new Vector3(x, y, surface.Height));
                positions[i] = p;
                int o = i * Stride;
                vertices[o + 0] = p.X; vertices[o + 1] = p.Y; vertices[o + 2] = p.Z;
                vertices[o + 3] = normal.X; vertices[o + 4] = normal.Y; vertices[o + 5] = normal.Z;
                vertices[o + 6] = x / uvScale; vertices[o + 7] = y / uvScale;
                vertices[o + 8] = body.X; vertices[o + 9] = body.Y; vertices[o + 10] = body.Z;
                vertices[o + 11] = shore + (1f - shore) * t;
                vertices[o + 12] = tangent.X; vertices[o + 13] = tangent.Y; vertices[o + 14] = tangent.Z;
                vertices[o + 15] = bitangent.X; vertices[o + 16] = bitangent.Y; vertices[o + 17] = bitangent.Z;
            }

        var indices = new int[grid * grid * 6];
        int k = 0;
        for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
            {
                // Wound so the geometric normal faces up (+Y): advancing gy walks -Z in Y-up space.
                int a = gy * side + gx, b = a + 1, c = a + side, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }

        return new WorldMeshData
        {
            Name = "water:" + surface.Label,
            Vertices = vertices,
            Indices = indices,
            Positions = positions,
            SubmergedVertexCount = submerged,
        };
    }
}

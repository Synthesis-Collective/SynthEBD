using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>
/// One exterior cell of landscape (a LAND record), as plain data: a host reads the record and
/// fills this in; SkyRender builds and draws it. Positions are Skyrim world units, Z up, the same
/// space placed references use.
/// <para>A cell is 4096 units square with a 33×33 vertex grid (128 units apart); vertex 32 of one
/// cell is vertex 0 of its neighbour, so adjacent cells share their edge heights. Grid arrays are
/// x-fastest: index <c>y * 33 + x</c>, x increasing east, y increasing north.</para>
/// </summary>
public sealed class TerrainCell
{
    /// <summary>Vertices per side of a cell's grid.</summary>
    public const int Side = 33;
    /// <summary>World units between adjacent grid vertices.</summary>
    public const float VertexSpacing = 128f;
    /// <summary>World units per cell side.</summary>
    public const float CellSize = 4096f;
    /// <summary>Vertices per side of one quadrant's grid (a quadrant is 16×16 squares).</summary>
    public const int QuadrantSide = 17;

    public TerrainCell(int gridX, int gridY, float[] heights)
    {
        if (heights == null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != Side * Side)
            throw new ArgumentException($"a cell's height grid is {Side}×{Side} = {Side * Side} values, got {heights.Length}", nameof(heights));
        GridX = gridX;
        GridY = gridY;
        Heights = heights;
    }

    /// <summary>Cell coordinates: the cell's south-west corner is at (GridX × 4096, GridY × 4096).</summary>
    public int GridX { get; }
    public int GridY { get; }

    /// <summary>World-space Z of each grid vertex, 33×33, x-fastest (see <see cref="LandHeightDecoder"/>).</summary>
    public float[] Heights { get; }

    /// <summary>Per-vertex normals (unit length, Skyrim Z-up), 33×33 x-fastest, from the record's
    /// VNML (see <see cref="DecodeNormals"/>). Null: each triangle is shaded with its face normal.</summary>
    public Vector3[]? Normals { get; init; }

    /// <summary>Per-vertex colours (0..1), 33×33 x-fastest, from the record's VCLR (see
    /// <see cref="DecodeColors"/>). The shader divides each by its brightest channel, as the engine
    /// does, so they tint without darkening. Null: white.</summary>
    public Vector3[]? Colors { get; init; }

    /// <summary>Decodes VNML: three signed bytes (x, y, z) per vertex, 33×33 x-fastest.</summary>
    public static Vector3[] DecodeNormals(ReadOnlySpan<sbyte> xyz)
    {
        if (xyz.Length != Side * Side * 3)
            throw new ArgumentException($"VNML carries {Side * Side * 3} bytes, got {xyz.Length}", nameof(xyz));
        var n = new Vector3[Side * Side];
        for (int i = 0; i < n.Length; i++)
        {
            var v = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            n[i] = v.LengthSquared() > 0f ? Vector3.Normalize(v) : Vector3.UnitZ;
        }
        return n;
    }

    /// <summary>Decodes VCLR: three unsigned bytes (r, g, b) per vertex, 33×33 x-fastest, to 0..1.</summary>
    public static Vector3[] DecodeColors(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != Side * Side * 3)
            throw new ArgumentException($"VCLR carries {Side * Side * 3} bytes, got {rgb.Length}", nameof(rgb));
        var c = new Vector3[Side * Side];
        for (int i = 0; i < c.Length; i++)
            c[i] = new Vector3(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]) / 255f;
        return c;
    }

    /// <summary>The four quadrants, indexed by <see cref="TerrainQuadrantIndex"/>. A null entry, or
    /// one with no base texture, draws with the renderer's white texture.</summary>
    public TerrainQuadrant?[] Quadrants { get; } = new TerrainQuadrant?[4];

    /// <summary>Height of grid vertex (x, y).</summary>
    public float HeightAt(int x, int y) => Heights[y * Side + x];

    /// <summary>World position (Skyrim Z-up) of grid vertex (x, y).</summary>
    public Vector3 VertexPosition(int x, int y)
        => new(GridX * CellSize + x * VertexSpacing, GridY * CellSize + y * VertexSpacing, HeightAt(x, y));
}

/// <summary>How a terrain quadrant's alpha layers combine with its base texture.</summary>
public enum TerrainBlendMode
{
    /// <summary>Each layer over the result so far, weighted by its opacity times the layer
    /// texture's alpha: what separately drawn, alpha-blended layer sheets produce.</summary>
    OverWithTextureAlpha = 0,
    /// <summary>Each layer over the result so far, weighted by its opacity alone.</summary>
    Over = 1,
    /// <summary>Base weight 1 and each layer weighted by its opacity, then divided by the sum of
    /// the weights (the engine's shader normalises per-vertex weights this way).</summary>
    NormalizedSum = 2,
}

/// <summary>Quadrant order within a cell, matching the LAND record's quadrant numbers.</summary>
public enum TerrainQuadrantIndex
{
    BottomLeft = 0,
    BottomRight = 1,
    TopLeft = 2,
    TopRight = 3,
}

/// <summary>What one quadrant is painted with: a base texture and alpha layers in record order.</summary>
public sealed class TerrainQuadrant
{
    /// <summary>Maximum alpha layers drawn per quadrant; later layers are ignored.</summary>
    public const int MaxLayers = 8;

    /// <summary>The base texture, or null to draw the base white. The host picks the fallback: Skyrim draws its
    /// default landscape texture (<c>textures\landscape\dirt02.dds</c>) for a quadrant with no base layer or a
    /// base layer whose texture reference is zero.</summary>
    public TerrainTexture? Base { get; init; }
    public IReadOnlyList<TerrainLayer> Layers { get; init; } = Array.Empty<TerrainLayer>();
}

/// <summary>A landscape texture set: game-relative paths (e.g. <c>textures\landscape\dirt02.dds</c>).</summary>
public sealed record TerrainTexture(string DiffusePath, string? NormalPath = null)
{
    /// <summary>The grasses that grow where this texture is painted (LTEX GNAM), in record order.
    /// Read by <see cref="GrassPlacer"/>; the terrain mesh ignores them.</summary>
    public IReadOnlyList<GrassType>? Grasses { get; init; }
}

/// <summary>
/// One alpha layer over a quadrant: its texture and its 17×17 per-vertex opacity (0..1),
/// x-fastest within the quadrant (index <c>y * 17 + x</c>), as stored in the LAND record.
/// </summary>
public sealed record TerrainLayer(TerrainTexture Texture, float[] Opacity);

/// <summary>Decodes a LAND record's VHGT height data.</summary>
public static class LandHeightDecoder
{
    /// <summary>World units per stored gradient unit.</summary>
    public const float Unit = 8f;

    /// <summary>
    /// Decodes VHGT: <paramref name="offset"/> plus 33×33 signed gradients (x-fastest). Column 0
    /// accumulates down the rows starting from the offset, and each row then accumulates along x;
    /// every step is <see cref="Unit"/> world units. Returns world-space heights, x-fastest.
    /// </summary>
    public static float[] Decode(float offset, ReadOnlySpan<sbyte> gradients)
    {
        const int side = TerrainCell.Side;
        if (gradients.Length != side * side)
            throw new ArgumentException($"VHGT carries {side}×{side} gradients, got {gradients.Length}", nameof(gradients));
        var h = new float[side * side];
        float rowStart = offset;
        for (int y = 0; y < side; y++)
        {
            rowStart += gradients[y * side];
            float acc = rowStart;
            h[y * side] = acc * Unit;
            for (int x = 1; x < side; x++)
            {
                acc += gradients[y * side + x];
                h[y * side + x] = acc * Unit;
            }
        }
        return h;
    }
}

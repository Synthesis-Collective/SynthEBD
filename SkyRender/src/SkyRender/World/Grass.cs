using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>A grass type's flags (GRAS DATA, byte 28).</summary>
[Flags]
public enum GrassFlags : byte
{
    None = 0,
    /// <summary>Lit per vertex with the mesh's own normals, instead of with the instance's up axis.</summary>
    VertexLighting = 0x01,
    /// <summary>The height variation scales all three axes; otherwise only the height (model Z).</summary>
    UniformScaling = 0x02,
    /// <summary>Each instance is tilted onto the ground's slope; otherwise it stands upright.</summary>
    FitToSlope = 0x04,
}

/// <summary>How a grass type's distance from water is applied (GRAS DATA, bytes 8-11), in record order.</summary>
public enum GrassWaterRule
{
    AboveAtLeast = 0,
    AboveAtMost = 1,
    BelowAtLeast = 2,
    BelowAtMost = 3,
    EitherAtLeast = 4,
    EitherAtMost = 5,
    EitherAtMostAbove = 6,
    EitherAtMostBelow = 7,
}

/// <summary>
/// One grass type (a GRAS record), as plain data. A landscape texture lists the grasses that grow on
/// it (LTEX GNAM, <see cref="TerrainTexture.Grasses"/>).
/// </summary>
public sealed record GrassType
{
    /// <summary>A stable identifier for the type (the record's FormID, for instance). It seeds the
    /// placement, so the same data places the same grass on every run.</summary>
    public required uint Id { get; init; }
    /// <summary>The model, game-relative (e.g. <c>meshes\landscape\grass\...nif</c>).</summary>
    public required string ModelPath { get; init; }
    /// <summary>Chance, in percent, that a candidate position on fully painted ground gets an instance.</summary>
    public byte Density { get; init; }
    /// <summary>Ground slope range, in degrees, the grass grows on.</summary>
    public byte MinSlope { get; init; }
    public byte MaxSlope { get; init; } = 90;
    /// <summary>Distance from the water surface, in world units, that <see cref="WaterRule"/> tests.</summary>
    public ushort UnitsFromWater { get; init; }
    public GrassWaterRule WaterRule { get; init; }
    /// <summary>Spacing of the candidate positions, in world units (each is jittered by up to half of it).</summary>
    public float PositionRange { get; init; }
    /// <summary>Scale variation: each instance is scaled by 1 + a value in [-HeightRange, HeightRange].</summary>
    public float HeightRange { get; init; }
    /// <summary>Brightness variation, 0..1 (see <see cref="GrassPlacer"/>).</summary>
    public float ColorRange { get; init; }
    /// <summary>The wind sway period. A still frame has no wind, so it is carried but not drawn.</summary>
    public float WavePeriod { get; init; }
    public GrassFlags Flags { get; init; }
}

/// <summary>The engine's grass settings (Skyrim.ini / SkyrimPrefs.ini <c>[Grass]</c>).</summary>
public sealed record GrassSettings
{
    /// <summary><c>iMinGrassSize</c>: the smallest candidate spacing, in world units, whatever a type's
    /// position range. Engine default 20.</summary>
    public int MinGrassSize { get; init; } = 20;
    /// <summary><c>iMaxGrassTypesPerTexure</c> (sic): how many of a texture's grasses are used, in
    /// list order. Engine default 2.</summary>
    public int MaxGrassTypesPerTexture { get; init; } = 2;
    /// <summary><c>fGrassStartFadeDistance</c>: grass starts to fade at this camera distance. 7000 is
    /// the launcher's highest setting.</summary>
    public float StartFadeDistance { get; init; } = 7000f;
    /// <summary><c>fGrassFadeRange</c>: the distance over which it fades out. Engine default 1000.</summary>
    public float FadeRange { get; init; } = 1000f;
    /// <summary><c>fTexturePctThreshold</c>: a texture's share of the ground at or below which it
    /// grows no grass there. Engine default 0.</summary>
    public float TexturePercentThreshold { get; init; }
    /// <summary>Mixed into every seed, so a host can draw a different (equally valid) placement.</summary>
    public uint Seed { get; init; }

    /// <summary>The camera distance beyond which no grass shows: start + range.</summary>
    public float Reach => StartFadeDistance + Math.Max(0f, FadeRange);
}

/// <summary>
/// One placed grass instance, in Skyrim world space (Z up). The model's vertex <c>p</c> lands at
/// <c>Position + Rotation · (p ∘ (1 + Variation · mask))</c>, where the mask is (1, 1, 1) with
/// <see cref="GrassFlags.UniformScaling"/> and (0, 0, 1) without, as the engine's grass vertex shader
/// applies it.
/// </summary>
public readonly record struct GrassInstance(Vector3 Position, Matrix4x4 Rotation, float Variation, float Shade)
{
    /// <summary>The instance's up axis (Rotation · Z): the normal flat-lit grass is lit with.</summary>
    public Vector3 Up => new(Rotation.M13, Rotation.M23, Rotation.M33);
}

/// <summary>The instances of one grass type, and how many candidate positions were tested for it.</summary>
public sealed class GrassTypePlacement
{
    internal GrassTypePlacement(GrassType type) => Type = type;
    public GrassType Type { get; }
    public List<GrassInstance> Instances { get; } = new();
    public int Candidates { get; internal set; }
}

/// <summary>What <see cref="GrassPlacer.Place"/> produced: the instances per grass type.</summary>
public sealed class GrassPlacement
{
    internal readonly Dictionary<GrassType, GrassTypePlacement> ByTypeMap = new();
    public IReadOnlyCollection<GrassTypePlacement> Types => ByTypeMap.Values;
    public GrassSettings Settings { get; internal set; } = new();
    public int CellsPlaced { get; internal set; }
    public int CellsOutOfReach { get; internal set; }
    public int PatchesPlaced { get; internal set; }
    public int InstanceCount
    {
        get { int n = 0; foreach (var t in ByTypeMap.Values) n += t.Instances.Count; return n; }
    }
}

/// <summary>
/// Per-instance data for the instanced grass draw (<see cref="GlWorldFactory.UploadGrassInstances"/>),
/// GL-free: <see cref="FloatsPerInstance"/> floats per instance, in the renderer's Y-up space and for
/// model vertices in the Y-up model space <see cref="NifMeshBuilder"/> emits. Floats 0-11 are the
/// three rows of a 3 × 4 transform (rotation × scale, then translation in each row's w), so a model
/// vertex p lands at <c>(row0 · (p, 1), row1 · (p, 1), row2 · (p, 1))</c>; floats 12-14 are the
/// instance's up axis (the flat-lighting normal) and float 15 its brightness.
/// </summary>
public sealed class GrassInstanceData
{
    public const int FloatsPerInstance = 16;

    public required float[] Data { get; init; }
    public int Count => Data.Length / FloatsPerInstance;
    /// <summary>The instance origins, Y-up (bounds and diagnostics).</summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>Builds the buffer for <paramref name="type"/>'s instances.</summary>
    public static GrassInstanceData Build(GrassType type, IReadOnlyList<GrassInstance> instances)
    {
        bool uniform = (type.Flags & GrassFlags.UniformScaling) != 0;
        var data = new float[instances.Count * FloatsPerInstance];
        var positions = new Vector3[instances.Count];
        for (int i = 0; i < instances.Count; i++)
        {
            var g = instances[i];
            var r = g.Rotation;
            float sz = 1f + g.Variation, sxy = uniform ? sz : 1f;
            // Z-up world = pos + R · D · p_z, with p_z = (p.x, -p.z, p.y) the Y-up model vertex in Z-up
            // and D = diag(sxy, sxy, sz); Y-up world = (w.x, w.z, -w.y). Columns of R · D · (that
            // swap) in Z-up: (R1 sxy, R3 sz, -R2 sxy) with Rk the k-th column of R.
            var c0 = new Vector3(r.M11, r.M21, r.M31) * sxy;
            var c1 = new Vector3(r.M12, r.M22, r.M32) * sxy;
            var c2 = new Vector3(r.M13, r.M23, r.M33) * sz;
            var m0 = c0; var m1 = c2; var m2 = -c1;   // the model's Y-up columns, still in Z-up world
            // Rows of the Y-up result: Z-up row x, row z, minus row y.
            int o = i * FloatsPerInstance;
            Row(data, o, m0.X, m1.X, m2.X, g.Position.X);
            Row(data, o + 4, m0.Z, m1.Z, m2.Z, g.Position.Z);
            Row(data, o + 8, -m0.Y, -m1.Y, -m2.Y, -g.Position.Y);
            var up = WorldMeshBuilder.ToYUp(Vector3.Normalize(g.Up));
            data[o + 12] = up.X; data[o + 13] = up.Y; data[o + 14] = up.Z;
            data[o + 15] = g.Shade;
            positions[i] = WorldMeshBuilder.ToYUp(g.Position);
        }
        return new GrassInstanceData { Data = data, Positions = positions };
    }

    private static void Row(float[] d, int o, float a, float b, float c, float t)
    {
        d[o] = a; d[o + 1] = b; d[o + 2] = c; d[o + 3] = t;
    }

    /// <summary>Where instance <paramref name="i"/> puts the Y-up model vertex <paramref name="p"/> (Y-up
    /// world): the same arithmetic the vertex shader does.</summary>
    public Vector3 Transform(int i, Vector3 p)
    {
        int o = i * FloatsPerInstance;
        return new Vector3(
            Data[o] * p.X + Data[o + 1] * p.Y + Data[o + 2] * p.Z + Data[o + 3],
            Data[o + 4] * p.X + Data[o + 5] * p.Y + Data[o + 6] * p.Z + Data[o + 7],
            Data[o + 8] * p.X + Data[o + 9] * p.Y + Data[o + 10] * p.Z + Data[o + 11]);
    }
}

/// <summary>
/// Places grass on terrain cells the way the engine's grass manager does (RENDERING_PIPELINE.md,
/// "Grass"), GL-free. For each quadrant, each texture painted on it (the base and every alpha layer),
/// and each of that texture's first <see cref="GrassSettings.MaxGrassTypesPerTexture"/> grasses, the
/// quadrant is covered by patches of 2 × 2 grid squares. A patch tests a jittered grid of candidate
/// positions, side <c>min(256 / PositionRange, 256 / MinGrassSize)</c>; a candidate is kept with the
/// grass's density times the texture's share of the ground there (bilinear over the patch's 3 × 3
/// grid vertices), then tested against the ground's slope and the water rule, and given a
/// brightness, a turn about Z and a scale variation.
/// <para>The random numbers are this placer's own: the engine draws from a shared generator whose
/// state depends on load order and timing, so no source gives positions that match the game. A seed
/// made of the cell, quadrant, texture, grass and patch makes the placement repeatable.</para>
/// </summary>
public static class GrassPlacer
{
    /// <summary>World units per patch side (2 grid squares).</summary>
    public const float PatchSize = 2f * TerrainCell.VertexSpacing;
    /// <summary>Patches per quadrant side.</summary>
    public const int PatchesPerQuadrant = (TerrainCell.QuadrantSide - 1) / 2;

    /// <summary>
    /// Places the grass of <paramref name="cells"/>. <paramref name="waterHeight"/> gives a cell's water
    /// height (null: no water rule applies there). With <paramref name="camera"/> (Skyrim world space),
    /// cells and patches wholly beyond <see cref="GrassSettings.Reach"/> are skipped: the engine fades
    /// grass out by then, so they would draw nothing.
    /// </summary>
    public static GrassPlacement Place(IEnumerable<TerrainCell> cells, GrassSettings? settings = null,
        Func<TerrainCell, float?>? waterHeight = null, Vector3? camera = null)
    {
        settings ??= new GrassSettings();
        var result = new GrassPlacement { Settings = settings };
        foreach (var cell in cells)
        {
            if (camera is { } cam && HorizontalDistance(cam, cell.GridX * TerrainCell.CellSize, cell.GridY * TerrainCell.CellSize,
                    TerrainCell.CellSize) > settings.Reach)
            {
                result.CellsOutOfReach++;
                continue;
            }
            result.CellsPlaced++;
            float? water = waterHeight?.Invoke(cell);
            for (int q = 0; q < 4; q++)
                if (cell.Quadrants[q] is { } quad)
                    PlaceQuadrant(cell, q, quad, settings, water, camera, result);
        }
        return result;
    }

    /// <summary>The share of the ground each texture of a quadrant has at each of its 17 × 17 grid
    /// vertices: index 0 is the base, index i + 1 alpha layer i. Layers combine as the terrain draws
    /// them by default (<see cref="TerrainBlendMode.Over"/>): each layer covers what is under it by its
    /// opacity, so layer i keeps <c>o_i × Π_{j > i}(1 − o_j)</c> and the base <c>Π_j(1 − o_j)</c>.</summary>
    public static float[][] TextureShares(TerrainQuadrant quad)
    {
        const int n = TerrainCell.QuadrantSide * TerrainCell.QuadrantSide;
        int layers = Math.Min(quad.Layers.Count, TerrainQuadrant.MaxLayers);
        var shares = new float[layers + 1][];
        for (int t = 0; t <= layers; t++) shares[t] = new float[n];
        for (int v = 0; v < n; v++)
        {
            float above = 1f;   // what the layers above let through
            for (int i = layers - 1; i >= 0; i--)
            {
                var op = quad.Layers[i].Opacity;
                float o = v < op.Length ? Math.Clamp(op[v], 0f, 1f) : 0f;
                shares[i + 1][v] = o * above;
                above *= 1f - o;
            }
            shares[0][v] = above;
        }
        return shares;
    }

    private static void PlaceQuadrant(TerrainCell cell, int q, TerrainQuadrant quad, GrassSettings settings, float? water,
        Vector3? camera, GrassPlacement result)
    {
        var textures = new List<TerrainTexture?> { quad.Base };
        for (int i = 0; i < Math.Min(quad.Layers.Count, TerrainQuadrant.MaxLayers); i++) textures.Add(quad.Layers[i].Texture);
        float[][]? shares = null;
        int qx0 = (q & 1) * 16, qy0 = q >= 2 ? 16 : 0;
        for (int t = 0; t < textures.Count; t++)
        {
            if (textures[t]?.Grasses is not { Count: > 0 } grasses) continue;
            shares ??= TextureShares(quad);
            var share = shares[t];
            int used = 0;
            foreach (var grass in grasses)
            {
                if (used >= Math.Max(0, settings.MaxGrassTypesPerTexture)) break;
                if (grass is null) continue;
                used++;
                if (grass.Density == 0 || !(grass.PositionRange > 0f) || settings.MinGrassSize <= 0) continue;
                int side = (int)Math.Min(PatchSize / grass.PositionRange, PatchSize / settings.MinGrassSize);
                if (side < 1) continue;
                if (!result.ByTypeMap.TryGetValue(grass, out var placed))
                    result.ByTypeMap[grass] = placed = new GrassTypePlacement(grass);
                for (int py = 0; py < PatchesPerQuadrant; py++)
                    for (int px = 0; px < PatchesPerQuadrant; px++)
                    {
                        // The patch's 3 x 3 grid vertices, x-fastest: the grass's density times the texture's share.
                        var density = new float[9];
                        bool any = false;
                        for (int j = 0; j < 3; j++)
                            for (int i = 0; i < 3; i++)
                            {
                                float s = share[(py * 2 + j) * TerrainCell.QuadrantSide + px * 2 + i];
                                float d = s > settings.TexturePercentThreshold ? s * grass.Density * 0.01f : 0f;
                                density[j * 3 + i] = d;
                                any |= d > 0f;
                            }
                        if (!any) continue;
                        float startX = cell.GridX * TerrainCell.CellSize + (qx0 + px * 2) * TerrainCell.VertexSpacing;
                        float startY = cell.GridY * TerrainCell.CellSize + (qy0 + py * 2) * TerrainCell.VertexSpacing;
                        if (camera is { } cam && HorizontalDistance(cam, startX, startY, PatchSize) > settings.Reach) continue;
                        uint seed = Hash(settings.Seed, (uint)cell.GridX, (uint)cell.GridY, (uint)q, (uint)t, grass.Id,
                                         (uint)(py * PatchesPerQuadrant + px));
                        PlacePatch(cell, grass, side, startX, startY, density, water, new Rng(seed), placed);
                        result.PatchesPlaced++;
                    }
            }
        }
    }

    private static void PlacePatch(TerrainCell cell, GrassType grass, int side, float startX, float startY, float[] density,
        float? water, Rng rng, GrassTypePlacement placed)
    {
        float spacing = PatchSize / (2f * side);
        // The slope range as limits on the ground normal's Z: the engine looks the cosine up in a
        // 512-entry table, the angle quantised to 512 steps a turn.
        float minNz = SlopeCos(grass.MaxSlope), maxNz = SlopeCos(grass.MinSlope);
        for (int x = 0; x < side; x++)
        {
            float column = x + 0.5f;
            for (int y = 0; y < side; y++)
            {
                float row = y + 0.5f;
                placed.Candidates++;
                // Density at the candidate: bilinear over the patch's 3 x 3 grid vertices.
                float dx = column * 2f / side, dy = row * 2f / side;
                int ix = (int)dx, iy = (int)dy;
                float fx = dx - ix, fy = dy - iy;
                int k = ix + 3 * iy;
                float lower = density[k] * (1f - fx) + density[k + 1] * fx;
                float upper = density[k + 3] * (1f - fx) + density[k + 4] * fx;
                float here = (lower * (1f - fy) + upper * fy) * 32768f;
                uint chance = here > 0f ? (uint)Math.Min(here, 32768f) : 0u;
                if (chance == 0 || (rng.Next() & 0x7FFFu) >= chance) continue;

                float wx = startX + column * spacing * 2f + rng.Signed() * spacing;
                float wy = startY + row * spacing * 2f + rng.Signed() * spacing;
                if (!GroundAt(cell, wx, wy, out float height, out var normal, out var colour)) continue;
                if (water is { } wh && !AcceptWater(grass.WaterRule, grass.UnitsFromWater, wh, height)) continue;
                if (normal.Z < minNz || normal.Z > maxNz) continue;

                float shade = Shade(colour, rng.Signed(), grass.ColorRange);
                float cosine = rng.Signed();
                float variation = rng.Signed() * grass.HeightRange;
                var rotation = Orientation(cosine, (grass.Flags & GrassFlags.FitToSlope) != 0 ? normal : null);
                placed.Instances.Add(new GrassInstance(new Vector3(wx, wy, height), rotation, variation, shade));
            }
        }
    }

    /// <summary>Whether ground at <paramref name="height"/> passes a grass type's water rule (water
    /// surface at <paramref name="water"/>, distance <paramref name="units"/>).</summary>
    public static bool AcceptWater(GrassWaterRule rule, float units, float water, float height)
    {
        float lower = water - units, upper = water + units;
        return rule switch
        {
            GrassWaterRule.AboveAtLeast => height >= lower,
            GrassWaterRule.AboveAtMost => height >= water && height <= upper,
            GrassWaterRule.BelowAtLeast => height <= lower,
            GrassWaterRule.BelowAtMost => height <= water && height >= lower,
            GrassWaterRule.EitherAtLeast => height >= upper || height <= lower,
            GrassWaterRule.EitherAtMost => height >= lower && height <= upper,
            _ => true,
        };
    }

    /// <summary>An instance's brightness: the ground colour's luminance (0.31, 0.37, 0.32) times
    /// <c>(1 − ColorRange) + r · ColorRange / 4</c> for r in [−1, 1], kept below 1.</summary>
    public static float Shade(Vector3 groundColour, float random, float colorRange)
    {
        float lum = groundColour.X * 0.31f + groundColour.Y * 0.37f + groundColour.Z * 0.32f;
        float shade = lum * (random * 0.5f * (colorRange * 0.5f) + (1f - colorRange));
        return shade >= 1f ? 0.99f : Math.Max(0f, shade);
    }

    /// <summary>The instance rotation: a turn about Z whose cosine is <paramref name="cosine"/> (the sine
    /// non-negative), and with <paramref name="slopeNormal"/> that turn applied inside a frame whose Z is
    /// the ground normal.</summary>
    public static Matrix4x4 Orientation(float cosine, Vector3? slopeNormal)
    {
        float c = Math.Clamp(cosine, -1f, 1f), s = MathF.Sqrt(Math.Max(0f, 1f - c * c));
        // Row-major, column-vector convention: world = R · p (M11 M12 M13 is the first row).
        var turn = new Matrix4x4(c, -s, 0, 0, s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        if (slopeNormal is not { } n) return turn;
        var t = SlopeTangent(n);
        var b = Vector3.Cross(t, n);
        // Frame columns: (t x n, t, n).
        var frame = new Matrix4x4(b.X, t.X, n.X, 0, b.Y, t.Y, n.Y, 0, b.Z, t.Z, n.Z, 0, 0, 0, 0, 1);
        return MulColumnConvention(frame, turn);
    }

    /// <summary>A unit tangent to the ground normal: perpendicular to it within the plane of its two
    /// largest components.</summary>
    public static Vector3 SlopeTangent(Vector3 n)
    {
        Vector3 t;
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ax <= ay && ax <= az) t = new Vector3(0f, -n.Z, n.Y);
        else if (ay <= ax && ay <= az) t = new Vector3(-n.Z, 0f, n.X);
        else t = new Vector3(-n.Y, n.X, 0f);
        float len = t.Length();
        return len > 1e-6f ? t / len : Vector3.Zero;
    }

    /// <summary>The engine's slope test value for an angle: the cosine of the angle quantised to 512
    /// steps a turn.</summary>
    public static float SlopeCos(byte degrees)
    {
        float angle = Math.Min(degrees, (byte)90);
        int index = (int)(angle * 0.017453292f * 81.487328f) & 511;
        return MathF.Cos(index / 81.487328f);
    }

    /// <summary>The drawn ground at world (x, y) inside <paramref name="cell"/>: the height on the
    /// terrain mesh's triangles (each square split along its (x,y)-(x+1,y+1) diagonal), that triangle's
    /// face normal, and the vertex colour interpolated bilinearly (white without VCLR).</summary>
    public static bool GroundAt(TerrainCell cell, float x, float y, out float height, out Vector3 normal, out Vector3 colour)
    {
        float fx = (x - cell.GridX * TerrainCell.CellSize) / TerrainCell.VertexSpacing;
        float fy = (y - cell.GridY * TerrainCell.CellSize) / TerrainCell.VertexSpacing;
        height = 0f; normal = Vector3.UnitZ; colour = Vector3.One;
        if (fx < 0f || fy < 0f || fx > 32f || fy > 32f) return false;
        int ix = Math.Clamp((int)MathF.Floor(fx), 0, 31), iy = Math.Clamp((int)MathF.Floor(fy), 0, 31);
        float u = fx - ix, v = fy - iy;
        float h00 = cell.HeightAt(ix, iy), h10 = cell.HeightAt(ix + 1, iy);
        float h11 = cell.HeightAt(ix + 1, iy + 1), h01 = cell.HeightAt(ix, iy + 1);
        const float s = TerrainCell.VertexSpacing;
        if (u >= v)
        {
            height = h00 + (h10 - h00) * u + (h11 - h10) * v;
            normal = Vector3.Normalize(new Vector3(-(h10 - h00) / s, -(h11 - h10) / s, 1f));
        }
        else
        {
            height = h00 + (h11 - h01) * u + (h01 - h00) * v;
            normal = Vector3.Normalize(new Vector3(-(h11 - h01) / s, -(h01 - h00) / s, 1f));
        }
        if (cell.Colors is { } c)
        {
            int i00 = iy * TerrainCell.Side + ix;
            var lo = Vector3.Lerp(c[i00], c[i00 + 1], u);
            var hi = Vector3.Lerp(c[i00 + TerrainCell.Side], c[i00 + TerrainCell.Side + 1], u);
            colour = Vector3.Lerp(lo, hi, v);
        }
        return true;
    }

    private static float HorizontalDistance(Vector3 p, float x0, float y0, float size)
    {
        float dx = Math.Max(0f, Math.Max(x0 - p.X, p.X - (x0 + size)));
        float dy = Math.Max(0f, Math.Max(y0 - p.Y, p.Y - (y0 + size)));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // a · b, the ordinary matrix product (Matrix4x4.Multiply is row-by-column), so that
    // (a · b) · p = a · (b · p) in the column-vector convention used here.
    private static Matrix4x4 MulColumnConvention(Matrix4x4 a, Matrix4x4 b) => Matrix4x4.Multiply(a, b);

    private static uint Hash(params uint[] parts)
    {
        uint h = 0x811C9DC5u;
        foreach (var p in parts)
        {
            h ^= p;
            h *= 0x01000193u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
        }
        return h;
    }

    /// <summary>A small, repeatable generator (xorshift32 over a mixed seed).</summary>
    private struct Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s;
        }
        /// <summary>Uniform in [−1, 1).</summary>
        public float Signed() => (Next() >> 8) * (2f / 16777216f) - 1f;
    }
}

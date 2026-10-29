using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>The three kinds of distant LOD a worldspace ships.</summary>
public enum LodKind
{
    /// <summary>Terrain LOD: <c>.btr</c> meshes with a diffuse and a model-space normal texture per block.</summary>
    Terrain = 0,
    /// <summary>Object LOD: <c>.bto</c> meshes textured from the worldspace's object atlas.</summary>
    Objects = 1,
    /// <summary>Tree LOD: <c>.btt</c> instance lists drawn as billboards from the tree atlas.</summary>
    Trees = 2,
}

/// <summary>A set of <see cref="LodKind"/>s.</summary>
[Flags]
public enum LodKinds
{
    None = 0,
    Terrain = 1,
    Objects = 2,
    Trees = 4,
    All = Terrain | Objects | Trees,
}

/// <summary>
/// A worldspace's LOD grid, from <c>lodsettings\&lt;worldspace&gt;.lod</c>: 16 bytes, the south-west
/// cell (two int16), the stride in cells (int32) and the lowest and highest LOD level (int32 each).
/// Layout per xEdit's <c>TwbLodSettings.LoadFromData</c> (Skyrim branch); vanilla Tamriel reads
/// (-96, -96), stride 256, levels 4..32.
/// </summary>
public sealed record LodSettings(int SouthWestCellX, int SouthWestCellY, int Stride, int MinLevel, int MaxLevel)
{
    /// <summary>The settings file's game-relative path.</summary>
    public static string PathFor(string worldspace) => $@"lodsettings\{worldspace}.lod";

    /// <summary>Reads a <c>.lod</c> file's 16 bytes.</summary>
    public static LodSettings Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != 16)
            throw new ArgumentException($"a lodsettings file is 16 bytes, got {data.Length}", nameof(data));
        int sw = BitConverter.ToInt16(data[..2]), sh = BitConverter.ToInt16(data[2..4]);
        int stride = BitConverter.ToInt32(data[4..8]);
        int min = BitConverter.ToInt32(data[8..12]), max = BitConverter.ToInt32(data[12..16]);
        if (stride <= 0 || min <= 0 || max < min)
            throw new ArgumentException($"lodsettings values out of range: stride {stride}, levels {min}..{max}", nameof(data));
        return new LodSettings(sw, sh, stride, min, max);
    }

    /// <summary>The block of <paramref name="level"/> that holds cell (x, y): its south-west cell.
    /// Blocks are aligned to the south-west cell, <paramref name="level"/> cells a side.</summary>
    public (int X, int Y) BlockFor(int cellX, int cellY, int level)
        => (SouthWestCellX + FloorDiv(cellX - SouthWestCellX, level) * level,
            SouthWestCellY + FloorDiv(cellY - SouthWestCellY, level) * level);

    internal static int FloorDiv(int a, int b) => (int)MathF.Floor(a / (float)b);
}

/// <summary>
/// The engine's LOD distances, <c>[TerrainManager]</c> in SkyrimPrefs.ini. Per the DynDOLOD manual:
/// <see cref="Level0Distance"/> is the reach of LOD level 4, <see cref="Level1Distance"/> of level 8
/// and <see cref="MaximumDistance"/> of level 16 for object LOD; terrain LOD uses the same distances
/// multiplied by <see cref="SplitDistanceMult"/>; tree LOD has one level, drawn to
/// <see cref="TreeLoadDistance"/>. The defaults are the values of a launcher-written SkyrimPrefs.ini.
/// </summary>
public sealed record LodDistances
{
    /// <summary><c>fBlockLevel0Distance</c>: LOD level 4.</summary>
    public float Level0Distance { get; init; } = 60000f;
    /// <summary><c>fBlockLevel1Distance</c>: LOD level 8.</summary>
    public float Level1Distance { get; init; } = 90000f;
    /// <summary><c>fBlockMaximumDistance</c>: LOD level 16, the last object level.</summary>
    public float MaximumDistance { get; init; } = 250000f;
    /// <summary><c>fSplitDistanceMult</c>: terrain LOD's multiplier on the three distances.</summary>
    public float SplitDistanceMult { get; init; } = 1.5f;
    /// <summary><c>fTreeLoadDistance</c>: how far tree LOD is drawn.</summary>
    public float TreeLoadDistance { get; init; } = 75000f;

    /// <summary>How far a block of <paramref name="level"/> (relative to the worldspace's lowest
    /// level) is drawn. Terrain's top level has no limit; object LOD has no level past 16.</summary>
    public float Reach(LodKind kind, int levelIndex) => kind switch
    {
        LodKind.Trees => levelIndex == 0 ? TreeLoadDistance : 0f,
        LodKind.Terrain => levelIndex switch
        {
            0 => Level0Distance * SplitDistanceMult,
            1 => Level1Distance * SplitDistanceMult,
            2 => MaximumDistance * SplitDistanceMult,
            _ => float.PositiveInfinity,
        },
        _ => levelIndex switch
        {
            0 => Level0Distance,
            1 => Level1Distance,
            2 => MaximumDistance,
            _ => 0f,
        },
    };
}

/// <summary>One LOD block: <see cref="Level"/> cells a side, south-west cell (<see cref="X"/>, <see cref="Y"/>).</summary>
public readonly record struct LodBlock(LodKind Kind, int Level, int X, int Y)
{
    /// <summary>World X of the block's west edge.</summary>
    public float OriginX => X * TerrainCell.CellSize;
    /// <summary>World Y of the block's south edge.</summary>
    public float OriginY => Y * TerrainCell.CellSize;
    /// <summary>World units per block side.</summary>
    public float Size => Level * TerrainCell.CellSize;

    /// <summary>The block's game-relative file: <c>meshes\terrain\&lt;w&gt;\&lt;w&gt;.L.X.Y.btr</c>, the
    /// same under <c>objects\</c> with <c>.bto</c> and under <c>trees\</c> with <c>.btt</c>.</summary>
    public string MeshPath(string worldspace) => Kind switch
    {
        LodKind.Terrain => $@"meshes\terrain\{worldspace}\{worldspace}.{Level}.{X}.{Y}.btr",
        LodKind.Objects => $@"meshes\terrain\{worldspace}\objects\{worldspace}.{Level}.{X}.{Y}.bto",
        _ => $@"meshes\terrain\{worldspace}\trees\{worldspace}.{Level}.{X}.{Y}.btt",
    };

    /// <summary>Horizontal distance from (x, y) to the nearest point of the block (0 inside it).</summary>
    public float DistanceTo(Vector2 xy)
    {
        float dx = MathF.Max(0f, MathF.Max(OriginX - xy.X, xy.X - (OriginX + Size)));
        float dy = MathF.Max(0f, MathF.Max(OriginY - xy.Y, xy.Y - (OriginY + Size)));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"{Kind} {Level}.{X}.{Y}";
}

/// <summary>Game-relative paths of a worldspace's LOD files (xEdit's names; lookups are case-insensitive).</summary>
public static class LodPaths
{
    /// <summary>The tree type list, <c>meshes\terrain\&lt;w&gt;\trees\&lt;w&gt;.lst</c>.</summary>
    public static string TreeList(string worldspace) => $@"meshes\terrain\{worldspace}\trees\{worldspace}.lst";
    /// <summary>The tree billboard atlas, <c>textures\terrain\&lt;w&gt;\trees\&lt;w&gt;treelod.dds</c>.</summary>
    public static string TreeAtlas(string worldspace) => $@"textures\terrain\{worldspace}\trees\{worldspace}treelod.dds";
    /// <summary>The noise texture the engine's LOD landscape shader modulates terrain LOD with
    /// (<c>textures\terrain\noise.dds</c>, the one terrain noise texture the base game ships).</summary>
    public const string TerrainNoise = @"textures\terrain\noise.dds";
}

/// <summary>
/// The exterior cells a host draws at full detail. LOD under them is cut away (<see cref="LodMeshBuilder"/>).
/// </summary>
public sealed class LoadedCells
{
    private readonly HashSet<(int, int)> _cells;

    public LoadedCells(IEnumerable<(int X, int Y)> cells) => _cells = new HashSet<(int, int)>(cells);

    /// <summary>A square of (2r+1)² cells around (cx, cy), the engine's uGridsToLoad = 2r+1 grid.</summary>
    public static LoadedCells Square(int cx, int cy, int radius)
    {
        var s = new List<(int, int)>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                s.Add((cx + dx, cy + dy));
        return new LoadedCells(s);
    }

    public int Count => _cells.Count;
    public IEnumerable<(int X, int Y)> Cells { get { foreach (var c in _cells) yield return c; } }
    public bool Contains(int x, int y) => _cells.Contains((x, y));

    private const float Eps = 1e-3f;
    // A vertex within a quarter unit of a cell edge is on it.
    private const float EdgeEps = 0.25f / TerrainCell.CellSize;

    /// <summary>True when every cell touching world point (x, y) is loaded: the point is strictly
    /// inside the loaded area (a point on its outer edge is not).</summary>
    public bool IsInterior(float x, float y)
    {
        Span<int> xs = stackalloc int[2], ys = stackalloc int[2];
        int nx = Touching(x, xs), ny = Touching(y, ys);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if (!_cells.Contains((xs[i], ys[j]))) return false;
        return true;
    }

    /// <summary>True when every cell the rectangle [x0, x1] × [y0, y1] overlaps is loaded, so the
    /// full-detail terrain covers all of it.</summary>
    public bool Covers(float x0, float y0, float x1, float y1)
    {
        int cx0 = (int)MathF.Floor(x0 / TerrainCell.CellSize + Eps), cx1 = (int)MathF.Floor(x1 / TerrainCell.CellSize - Eps);
        int cy0 = (int)MathF.Floor(y0 / TerrainCell.CellSize + Eps), cy1 = (int)MathF.Floor(y1 / TerrainCell.CellSize - Eps);
        if (cx1 < cx0) cx1 = cx0;
        if (cy1 < cy0) cy1 = cy0;
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
                if (!_cells.Contains((cx, cy))) return false;
        return true;
    }

    /// <summary>True when any loaded cell lies in the rectangle (cell units, inclusive).</summary>
    public bool AnyIn(int cx0, int cy0, int cx1, int cy1)
    {
        foreach (var (x, y) in _cells)
            if (x >= cx0 && x <= cx1 && y >= cy0 && y <= cy1) return true;
        return false;
    }

    // The cell indices whose closed square contains coordinate v: one, or two on a cell edge.
    private static int Touching(float v, Span<int> into)
    {
        float f = v / TerrainCell.CellSize;
        float r = MathF.Round(f);
        if (MathF.Abs(f - r) < EdgeEps) { into[0] = (int)r - 1; into[1] = (int)r; return 2; }
        into[0] = (int)MathF.Floor(f);
        return 1;
    }
}

/// <summary>
/// Picks the LOD blocks the engine would draw around a camera. The blocks form a quadtree over the
/// worldspace's LOD grid (<see cref="LodSettings"/>): starting from the top level, a block splits into
/// its four children while the camera is within the children's reach (<see cref="LodDistances.Reach"/>),
/// and is drawn when the camera is within its own. The distance is horizontal, to the block's nearest
/// point. The split rule follows the per-level distances the DynDOLOD manual documents; the exact
/// metric the engine measures is not documented, so this one is a model, checked by in-game comparison.
/// </summary>
public static class LodSelector
{
    /// <summary>The blocks of one LOD kind to draw for a camera at <paramref name="eye"/> (world XY).
    /// Object LOD stops at level 16 and tree LOD has only the lowest level; terrain uses every level.</summary>
    public static List<LodBlock> Select(LodKind kind, LodSettings settings, LodDistances distances, Vector2 eye,
        float maxDistance = float.PositiveInfinity)
    {
        int min = settings.MinLevel;
        int top = kind switch
        {
            LodKind.Trees => min,
            LodKind.Objects => Math.Min(settings.MaxLevel, Math.Max(min, 16)),
            _ => settings.MaxLevel,
        };
        var result = new List<LodBlock>();
        int roots = Math.Max(1, settings.Stride / top);
        for (int i = 0; i < roots; i++)
            for (int j = 0; j < roots; j++)
                Visit(new LodBlock(kind, top, settings.SouthWestCellX + i * top, settings.SouthWestCellY + j * top), false);
        return result;

        // A split block's four children are all drawn (at their level or finer), so the levels
        // tile the area without holes; only a top-level block can be out of reach altogether.
        void Visit(LodBlock b, bool parentSplit)
        {
            float d = b.DistanceTo(eye);
            if (d >= maxDistance) return;
            int index = LevelIndex(b.Level, min);
            if (b.Level > min && d < distances.Reach(kind, index - 1))
            {
                int h = b.Level / 2;
                Visit(b with { Level = h }, true);
                Visit(b with { Level = h, X = b.X + h }, true);
                Visit(b with { Level = h, Y = b.Y + h }, true);
                Visit(b with { Level = h, X = b.X + h, Y = b.Y + h }, true);
                return;
            }
            if (parentSplit || d < distances.Reach(kind, index)) result.Add(b);
        }
    }

    // 0 for the lowest level, 1 for twice it, ...
    private static int LevelIndex(int level, int min)
    {
        int i = 0;
        for (int l = min; l < level; l *= 2) i++;
        return i;
    }
}

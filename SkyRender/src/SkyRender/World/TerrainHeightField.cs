using System;
using System.Collections.Generic;

namespace SkyRender.World;

/// <summary>
/// Ground height at any world XY, from a set of <see cref="TerrainCell"/>s, interpolated on the
/// same triangles the terrain mesh draws: each grid square splits along its (x,y)-(x+1,y+1)
/// diagonal. Used for water depth, and available to hosts that need the drawn ground height.
/// </summary>
public sealed class TerrainHeightField
{
    private readonly Dictionary<(int, int), TerrainCell> _cells = new();

    public TerrainHeightField() { }

    public TerrainHeightField(IEnumerable<TerrainCell> cells)
    {
        foreach (var c in cells) Add(c);
    }

    /// <summary>Adds or replaces the cell at its grid coordinates.</summary>
    public void Add(TerrainCell cell) => _cells[(cell.GridX, cell.GridY)] = cell;

    public int Count => _cells.Count;

    /// <summary>The drawn ground height at (x, y), or null where no cell is loaded.</summary>
    public float? HeightAt(float x, float y)
    {
        int gx = (int)MathF.Floor(x / TerrainCell.CellSize), gy = (int)MathF.Floor(y / TerrainCell.CellSize);
        if (!_cells.TryGetValue((gx, gy), out var cell)) return null;
        float fx = (x - gx * TerrainCell.CellSize) / TerrainCell.VertexSpacing;
        float fy = (y - gy * TerrainCell.CellSize) / TerrainCell.VertexSpacing;
        int ix = Math.Clamp((int)MathF.Floor(fx), 0, 31), iy = Math.Clamp((int)MathF.Floor(fy), 0, 31);
        float u = fx - ix, v = fy - iy;
        float h00 = cell.HeightAt(ix, iy), h10 = cell.HeightAt(ix + 1, iy);
        float h11 = cell.HeightAt(ix + 1, iy + 1), h01 = cell.HeightAt(ix, iy + 1);
        // Lower triangle (x,y)-(x+1,y)-(x+1,y+1) when u >= v, upper one otherwise.
        return u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v
                      : h00 + (h11 - h01) * u + (h01 - h00) * v;
    }
}

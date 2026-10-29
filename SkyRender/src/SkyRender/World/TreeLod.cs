using System;
using System.Collections.Generic;
using System.Numerics;

namespace SkyRender.World;

/// <summary>
/// One tree type of a worldspace's tree LOD list (<c>.lst</c>): the billboard's size in world units
/// and its rectangle in the tree atlas (UV 0..1, min = top-left).
/// </summary>
public readonly record struct TreeLodType(int Index, float Width, float Height, Vector2 UvMin, Vector2 UvMax);

/// <summary>One tree LOD instance (<c>.btt</c>): world position of the tree's base, rotation about Z
/// (radians), scale, and the placed reference it stands for.</summary>
public readonly record struct TreeLodInstance(Vector3 Position, float Rotation, float Scale, uint RefFormId);

/// <summary>
/// Readers for the tree LOD files. Layouts per xEdit's <c>wbLOD.pas</c>:
/// <list type="bullet">
/// <item><c>.lst</c>: int32 count, then per type <c>TwbLodTES5TreeType</c> (32 bytes): int32 index,
/// float width, float height, float UV min x, y, float UV max x, y, int32 unknown.</item>
/// <item><c>.btt</c>: int32 type count, then per type int32 list index, int32 instance count and that
/// many <c>TwbLodTES5TreeRef</c> (32 bytes): float x, y, z, float rotation, float scale, uint32
/// reference FormID, two int32 unknown.</item>
/// </list>
/// Width is the tree's object-bounds X extent and height its Z extent (xEdit's LoadBillboard), so a
/// billboard stands on the instance position and rises by height × scale.
/// </summary>
public static class TreeLodFiles
{
    private const int TypeSize = 32, RefSize = 32;

    /// <summary>Reads a <c>.lst</c> file: the types by their index.</summary>
    public static Dictionary<int, TreeLodType> ParseList(ReadOnlySpan<byte> data)
    {
        var types = new Dictionary<int, TreeLodType>();
        if (data.Length < 4) return types;
        int count = BitConverter.ToInt32(data[..4]);
        if (count < 0 || 4 + (long)count * TypeSize > data.Length)
            throw new ArgumentException($"tree LOD list claims {count} types in {data.Length} bytes", nameof(data));
        for (int i = 0; i < count; i++)
        {
            var e = data.Slice(4 + i * TypeSize, TypeSize);
            int index = BitConverter.ToInt32(e[..4]);
            types[index] = new TreeLodType(index,
                BitConverter.ToSingle(e[4..8]), BitConverter.ToSingle(e[8..12]),
                new Vector2(BitConverter.ToSingle(e[12..16]), BitConverter.ToSingle(e[16..20])),
                new Vector2(BitConverter.ToSingle(e[20..24]), BitConverter.ToSingle(e[24..28])));
        }
        return types;
    }

    /// <summary>Reads a <c>.btt</c> block: (list index, instances) per tree type. An empty file is an
    /// empty block (xEdit writes those to clear a block of trees).</summary>
    public static List<(int TypeIndex, List<TreeLodInstance> Instances)> ParseBlock(ReadOnlySpan<byte> data)
    {
        var result = new List<(int, List<TreeLodInstance>)>();
        if (data.Length < 4) return result;
        int types = BitConverter.ToInt32(data[..4]);
        int p = 4;
        for (int t = 0; t < types; t++)
        {
            if (p + 8 > data.Length) throw new ArgumentException("tree LOD block ends inside a type header", nameof(data));
            int index = BitConverter.ToInt32(data.Slice(p, 4));
            int n = BitConverter.ToInt32(data.Slice(p + 4, 4));
            p += 8;
            if (n < 0 || p + (long)n * RefSize > data.Length)
                throw new ArgumentException($"tree LOD block claims {n} instances past its end", nameof(data));
            var list = new List<TreeLodInstance>(n);
            for (int i = 0; i < n; i++, p += RefSize)
            {
                var r = data.Slice(p, RefSize);
                list.Add(new TreeLodInstance(
                    new Vector3(BitConverter.ToSingle(r[..4]), BitConverter.ToSingle(r[4..8]), BitConverter.ToSingle(r[8..12])),
                    BitConverter.ToSingle(r[12..16]), BitConverter.ToSingle(r[16..20]), BitConverter.ToUInt32(r[20..24])));
            }
            result.Add((index, list));
        }
        return result;
    }
}

using System.Buffers.Binary;
using System.IO;

namespace SkyRender;

/// <summary>
/// Proves from compressed block data alone that a DDS has at least one texel whose alpha is
/// non-zero, which settles <see cref="AssetCache.IsFullyTransparent"/> as false
/// without decoding the image. One-sided by design: true is a proof, false only means "not
/// proven here", and the caller falls back to the full decode — so the verdict can never differ
/// from the decode-based one. Ordinary opaque diffuse is proven by its first block.
///
/// <para>Per-format rules (lower bounds, independent of the decoder's rounding, because every
/// interpolated value is a weighted average of its two endpoints):</para>
/// <list type="bullet">
/// <item>BC1: four-colour blocks (c0 &gt; c1) are opaque; in three-colour blocks only index 3 is
/// transparent.</item>
/// <item>BC2: any non-zero explicit 4-bit alpha.</item>
/// <item>BC3: an endpoint index whose endpoint is non-zero; an interpolated index when both
/// endpoints are non-zero; index 7 in six-value mode (255).</item>
/// <item>BC7 (ARB_texture_compression_bptc): modes 0-3 decode alpha as 1.0; modes 4/5 when both
/// endpoints of the channel the rotation maps to alpha are non-zero; mode 6 when both alpha
/// endpoints are non-zero; mode 7 when all four are. The reserved mode decodes to zero.</item>
/// </list>
/// Uncompressed, BC4/BC5, typeless, cube, array and volume textures are never proven here.
/// </summary>
public static class DdsAlphaProbe
{
    private enum Format { Bc1, Bc2, Bc3, Bc7 }

    /// <summary>True only when some texel of the first surface's top mip provably has alpha &gt; 0.
    /// Never throws; any read or parse problem returns false (not proven).</summary>
    public static bool ProvesNonZeroAlpha(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return ProvesNonZeroAlpha(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    public static bool ProvesNonZeroAlpha(Stream stream)
    {
        Span<byte> header = stackalloc byte[148];
        if (stream.ReadAtLeast(header[..128], 128, throwOnEndOfStream: false) < 128) return false;
        if (U(header, 0) != 0x20534444 || U(header, 4) != 124 || U(header, 76) != 32) return false;
        uint width = U(header, 16), height = U(header, 12);
        if (width == 0 || height == 0 || width > 65536 || height > 65536) return false;
        // Cube maps, volumes and arrays: leave to the full decode.
        if ((U(header, 112) & 0x20FE00) != 0 || (U(header, 8) & 0x800000) != 0) return false;
        if ((U(header, 80) & 4) == 0) return false;   // not FourCC-compressed

        uint fourcc = U(header, 84);
        Format? format;
        if (fourcc == 0x30315844) // DX10
        {
            if (stream.ReadAtLeast(header[128..], 20, throwOnEndOfStream: false) < 20) return false;
            if (U(header, 132) != 3 || U(header, 140) != 1 || (U(header, 136) & 4) != 0) return false;
            format = U(header, 128) switch
            {
                71 or 72 => Format.Bc1, 74 or 75 => Format.Bc2,
                77 or 78 => Format.Bc3, 98 or 99 => Format.Bc7, _ => null,
            };
        }
        else format = fourcc switch
        {
            0x31545844 => Format.Bc1, 0x33545844 => Format.Bc2, 0x35545844 => Format.Bc3, _ => null,
        };
        if (format == null) return false;

        int blockBytes = format == Format.Bc1 ? 8 : 16;
        long blocks = ((width + 3L) / 4) * ((height + 3L) / 4);
        Span<byte> block = stackalloc byte[16];
        for (long i = 0; i < blocks; i++)
        {
            if (stream.ReadAtLeast(block[..blockBytes], blockBytes, throwOnEndOfStream: false) < blockBytes)
                return false;
            if (BlockProves(format.Value, block[..blockBytes])) return true;
        }
        return false;
    }

    private static bool BlockProves(Format format, ReadOnlySpan<byte> b) => format switch
    {
        Format.Bc1 => Bc1Proves(b),
        Format.Bc2 => BinaryPrimitives.ReadUInt64LittleEndian(b) != 0,
        Format.Bc3 => Bc3AlphaProves(b),
        _ => Bc7Proves(b),
    };

    internal static bool Bc1Proves(ReadOnlySpan<byte> b)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        if (c0 > c1) return true;                       // four-colour mode: no transparency
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        return indices != 0xFFFFFFFF;                   // any index other than 3 is opaque
    }

    internal static bool Bc3AlphaProves(ReadOnlySpan<byte> b)
    {
        int a0 = b[0], a1 = b[1];
        bool bothNonZero = a0 > 0 && a1 > 0;
        ulong indices = 0;
        for (int i = 0; i < 6; i++) indices |= (ulong)b[2 + i] << (8 * i);
        for (int t = 0; t < 16; t++)
        {
            int idx = (int)((indices >> (3 * t)) & 7);
            bool nonZero = idx switch
            {
                0 => a0 > 0,
                1 => a1 > 0,
                6 when a0 <= a1 => false,               // explicit 0
                7 when a0 <= a1 => true,                // explicit 255
                _ => bothNonZero,                       // interpolated: >= min(a0, a1)
            };
            if (nonZero) return true;
        }
        return false;
    }

    internal static bool Bc7Proves(ReadOnlySpan<byte> b)
    {
        var r = new BitReader(b);
        int mode = 0;
        while (mode < 8 && r.Read(1) == 0) mode++;
        switch (mode)
        {
            case 0: case 1: case 2: case 3:
                return true;                            // no alpha bits: alpha decodes to 1.0
            case 4:
            case 5:
            {
                int rotation = (int)r.Read(2);
                if (mode == 4) r.Read(1);               // index selection
                int colorBits = mode == 4 ? 5 : 7, alphaBits = mode == 4 ? 6 : 8;
                Span<uint> ch = stackalloc uint[8];     // R0 R1 G0 G1 B0 B1 A0 A1
                for (int i = 0; i < 6; i++) ch[i] = r.Read(colorBits);
                ch[6] = r.Read(alphaBits); ch[7] = r.Read(alphaBits);
                // Rotation 1/2/3 swaps alpha with R/G/B after interpolation.
                int first = rotation switch { 1 => 0, 2 => 2, 3 => 4, _ => 6 };
                return ch[first] != 0 && ch[first + 1] != 0;
            }
            case 6:
            {
                for (int i = 0; i < 6; i++) r.Read(7);  // RGB endpoints
                uint a0 = r.Read(7), a1 = r.Read(7), p0 = r.Read(1), p1 = r.Read(1);
                return ((a0 << 1) | p0) != 0 && ((a1 << 1) | p1) != 0;
            }
            case 7:
            {
                r.Read(6);                              // partition
                for (int i = 0; i < 12; i++) r.Read(5); // RGB endpoints, two subsets
                Span<uint> a = stackalloc uint[4];
                for (int i = 0; i < 4; i++) a[i] = r.Read(5);
                for (int i = 0; i < 4; i++) a[i] = (a[i] << 1) | r.Read(1);
                return a[0] != 0 && a[1] != 0 && a[2] != 0 && a[3] != 0;
            }
            default:
                return false;                           // reserved mode decodes to zero
        }
    }

    /// <summary>LSB-first reader over a 16-byte BC7 block.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _bit;
        public BitReader(ReadOnlySpan<byte> bytes) { _bytes = bytes; _bit = 0; }

        public uint Read(int count)
        {
            uint value = 0;
            for (int i = 0; i < count; i++, _bit++)
                value |= (uint)((_bytes[_bit >> 3] >> (_bit & 7)) & 1) << i;
            return value;
        }
    }

    private static uint U(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
}

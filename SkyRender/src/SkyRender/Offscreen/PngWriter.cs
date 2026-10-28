using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace SkyRender.Offscreen;

/// <summary>
/// Minimal PNG encoder for the offscreen renderer's readback: 8-bit truecolour (RGB, alpha dropped,
/// since readback alpha is stamped opaque), one Paeth filter per scanline, zlib-compressed IDAT.
/// A single fixed filter is within a few percent of adaptive filtering's size on smooth rendered
/// images for a fraction of the CPU. Stateless and thread-safe.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes top-left-origin RGBA8 pixels as an RGB PNG.</summary>
    public static byte[] EncodeRgb(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (rgba.Length < width * height * 4) throw new ArgumentException("Pixel buffer is too small.", nameof(rgba));

        int stride = width * 3;
        var filtered = new byte[(stride + 1) * height];
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int src = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                cur[x * 3] = rgba[src + x * 4];
                cur[x * 3 + 1] = rgba[src + x * 4 + 1];
                cur[x * 3 + 2] = rgba[src + x * 4 + 2];
            }

            int dst = y * (stride + 1);
            filtered[dst] = 4; // filter type: Paeth
            for (int i = 0; i < stride; i++)
            {
                int a = i >= 3 ? cur[i - 3] : 0;
                int b = prev[i];
                int c = i >= 3 ? prev[i - 3] : 0;
                filtered[dst + 1 + i] = (byte)(cur[i] - Paeth(a, b, c));
            }
            (prev, cur) = (cur, prev);
        }

        byte[] idat;
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(filtered, 0, filtered.Length);
            idat = compressed.ToArray();
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method: adaptive (per-scanline filter byte)
        header[12] = 0; // interlace: none

        using var png = new MemoryStream(idat.Length + 64);
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);

        var typeBytes = new byte[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc(0xFFFFFFFFu, typeBytes);
        crc = Crc(crc, data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using SkyRender.Offscreen;
using Xunit;

namespace SkyRender.Tests;

/// <summary>Round-trips <see cref="PngWriter"/> through an independent decode (inflate + undo the
/// Paeth filter) so the encoder is checked without trusting another PNG library.</summary>
public class PngWriterTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(64, 48)]
    public void EncodeRgb_RoundTripsEveryPixel(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var rng = new Random(width * 1000 + height);
        rng.NextBytes(rgba);

        var png = PngWriter.EncodeRgb(rgba, width, height);
        var rgb = Decode(png, out int w, out int h);

        w.Should().Be(width);
        h.Should().Be(height);
        for (int i = 0; i < width * height; i++)
        {
            rgb[i * 3].Should().Be(rgba[i * 4]);
            rgb[i * 3 + 1].Should().Be(rgba[i * 4 + 1]);
            rgb[i * 3 + 2].Should().Be(rgba[i * 4 + 2]);
        }
    }

    [Fact]
    public void EncodeRgb_ChunksCarryValidCrcs()
    {
        var png = PngWriter.EncodeRgb(new byte[2 * 2 * 4], 2, 2);
        int pos = 8;
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            uint stored = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            Crc32(png.AsSpan(pos + 4, 4 + len)).Should().Be(stored);
            pos += 12 + len;
        }
        pos.Should().Be(png.Length);
    }

    private static byte[] Decode(byte[] png, out int width, out int height)
    {
        png.AsSpan(0, 8).ToArray().Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        width = height = 0;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                data[8].Should().Be(8);
                data[9].Should().Be(2);
            }
            else if (type == "IDAT") idat.Write(data);
            pos += 12 + len;
        }

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var f = raw.ToArray();

        int stride = width * 3;
        var outp = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int src = y * (stride + 1);
            f[src].Should().Be(4);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= 3 ? outp[y * stride + i - 3] : 0;
                int b = y > 0 ? outp[(y - 1) * stride + i] : 0;
                int c = i >= 3 && y > 0 ? outp[(y - 1) * stride + i - 3] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                int pred = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                outp[y * stride + i] = (byte)(f[src + 1 + i] + pred);
            }
        }
        return outp;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte d in data)
        {
            crc ^= d;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}

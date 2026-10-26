using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CharacterViewer.Rendering;
using Pfim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="DdsAlphaProbe"/> may only ever claim "some texel has non-zero alpha" when the
/// decode-based collision-proxy verdict would be "not fully transparent". Constructed blocks pin
/// each per-format rule in both directions; the opt-in corpus test checks the claim against Pfim
/// on real files.
/// </summary>
public class DdsAlphaProbeTests
{
    private readonly ITestOutputHelper _out;
    public DdsAlphaProbeTests(ITestOutputHelper output) => _out = output;

    private const uint Dxt1 = 0x31545844, Dxt3 = 0x33545844, Dxt5 = 0x35545844, Dx10 = 0x30315844;

    /// <summary>A minimal single-mip DDS. <paramref name="dxgi"/> non-zero writes a DX10 header.</summary>
    private static byte[] Dds(uint fourcc, int width, int height, byte[] blocks, uint dxgi = 0)
    {
        int headerBytes = dxgi != 0 ? 148 : 128;
        var bytes = new byte[headerBytes + blocks.Length];
        void W(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        W(0, 0x20534444); W(4, 124); W(8, 0x1007); W(12, (uint)height); W(16, (uint)width);
        W(28, 1); W(76, 32); W(80, 4); W(84, fourcc); W(108, 0x1000);
        if (dxgi != 0) { W(128, dxgi); W(132, 3); W(140, 1); }
        blocks.CopyTo(bytes, headerBytes);
        return bytes;
    }

    private static bool Proves(byte[] file) => DdsAlphaProbe.ProvesNonZeroAlpha(new MemoryStream(file));

    private static byte[] Bc1(ushort c0, ushort c1, uint indices)
    {
        var b = new byte[8];
        BitConverter.GetBytes(c0).CopyTo(b, 0); BitConverter.GetBytes(c1).CopyTo(b, 2);
        BitConverter.GetBytes(indices).CopyTo(b, 4);
        return b;
    }

    private static byte[] Bc3(byte a0, byte a1, int index, byte[]? color = null)
    {
        var b = new byte[16];
        b[0] = a0; b[1] = a1;
        ulong idx = 0;
        for (int t = 0; t < 16; t++) idx |= (ulong)index << (3 * t);
        for (int i = 0; i < 6; i++) b[2 + i] = (byte)(idx >> (8 * i));
        (color ?? Bc1(0, 0, 0)).CopyTo(b, 8);
        return b;
    }

    /// <summary>LSB-first BC7 block writer.</summary>
    private sealed class Bc7Writer
    {
        private readonly byte[] _b = new byte[16];
        private int _bit;
        public Bc7Writer Mode(int mode) { Put(mode, 0); Put(1, 1); return this; }   // mode m: m zero bits, then 1
        public Bc7Writer Put(int count, int value)
        {
            for (int i = 0; i < count; i++, _bit++)
                if (((value >> i) & 1) != 0) _b[_bit >> 3] |= (byte)(1 << (_bit & 7));
            return this;
        }
        public Bc7Writer Repeat(int times, int count, int value)
        {
            for (int i = 0; i < times; i++) Put(count, value);
            return this;
        }
        public byte[] Bytes => _b;
    }

    [Fact]
    public void Bc1_FourColourIsOpaque_ThreeColourOnlyIndex3IsTransparent()
    {
        Assert.True(Proves(Dds(Dxt1, 4, 4, Bc1(0xF800, 0x001F, 0xFFFFFFFF))));
        Assert.False(Proves(Dds(Dxt1, 4, 4, Bc1(0x0000, 0x0000, 0xFFFFFFFF))));
        Assert.True(Proves(Dds(Dxt1, 4, 4, Bc1(0x0000, 0x0000, 0xFFFFFFFC))));
    }

    [Fact]
    public void Bc2_AnyNonZeroExplicitAlpha()
    {
        var zero = new byte[16];
        Assert.False(Proves(Dds(Dxt3, 4, 4, zero)));
        var one = new byte[16]; one[7] = 0x10;
        Assert.True(Proves(Dds(Dxt3, 4, 4, one)));
    }

    [Fact]
    public void Bc3_EndpointAndInterpolatedRules()
    {
        Assert.False(Proves(Dds(Dxt5, 4, 4, Bc3(0, 0, 0))));
        Assert.True(Proves(Dds(Dxt5, 4, 4, Bc3(5, 0, 0))));
        Assert.True(Proves(Dds(Dxt5, 4, 4, Bc3(0, 0, 7))));    // six-value mode: index 7 = 255
        Assert.False(Proves(Dds(Dxt5, 4, 4, Bc3(0, 0, 6))));   // six-value mode: index 6 = 0
        Assert.False(Proves(Dds(Dxt5, 4, 4, Bc3(0, 9, 2))));   // interpolated with a zero endpoint
        Assert.True(Proves(Dds(Dxt5, 4, 4, Bc3(200, 100, 3)))); // interpolated, both non-zero
    }

    [Fact]
    public void Bc7_ModeRules()
    {
        Assert.True(Proves(Dds(Dx10, 4, 4, new Bc7Writer().Mode(1).Bytes, dxgi: 98)));
        Assert.False(Proves(Dds(Dx10, 4, 4, new byte[16], dxgi: 98)));   // reserved mode

        // Mode 6: RGB 7-bit x6, then A0 A1 7-bit, then P0 P1.
        var m6Zero = new Bc7Writer().Mode(6).Repeat(6, 7, 127).Put(7, 0).Put(7, 0).Put(1, 0).Put(1, 0);
        Assert.False(Proves(Dds(Dx10, 4, 4, m6Zero.Bytes, dxgi: 98)));
        var m6PBitOnly = new Bc7Writer().Mode(6).Repeat(6, 7, 0).Put(7, 0).Put(7, 0).Put(1, 1).Put(1, 1);
        Assert.True(Proves(Dds(Dx10, 4, 4, m6PBitOnly.Bytes, dxgi: 98)));
        var m6OneZero = new Bc7Writer().Mode(6).Repeat(6, 7, 0).Put(7, 127).Put(7, 0).Put(1, 1).Put(1, 0);
        Assert.False(Proves(Dds(Dx10, 4, 4, m6OneZero.Bytes, dxgi: 98)));

        // Mode 5: rotation 2 bits, RGB 7-bit x6, A 8-bit x2. Rotation 1 makes R the alpha.
        var m5Alpha = new Bc7Writer().Mode(5).Put(2, 0).Repeat(6, 7, 0).Put(8, 255).Put(8, 1);
        Assert.True(Proves(Dds(Dx10, 4, 4, m5Alpha.Bytes, dxgi: 98)));
        var m5RotatedToZeroRed = new Bc7Writer().Mode(5).Put(2, 1).Repeat(6, 7, 0).Put(8, 255).Put(8, 255);
        Assert.False(Proves(Dds(Dx10, 4, 4, m5RotatedToZeroRed.Bytes, dxgi: 98)));
        var m5RotatedToRed = new Bc7Writer().Mode(5).Put(2, 1).Put(7, 3).Put(7, 3).Repeat(4, 7, 0).Put(8, 0).Put(8, 0);
        Assert.True(Proves(Dds(Dx10, 4, 4, m5RotatedToRed.Bytes, dxgi: 98)));

        // Mode 4: rotation, index selection, RGB 5-bit x6, A 6-bit x2.
        var m4 = new Bc7Writer().Mode(4).Put(2, 0).Put(1, 0).Repeat(6, 5, 0).Put(6, 1).Put(6, 63);
        Assert.True(Proves(Dds(Dx10, 4, 4, m4.Bytes, dxgi: 98)));

        // Mode 7: partition 6, RGB 5-bit x12, A 5-bit x4, P x4: all four alpha endpoints needed.
        var m7All = new Bc7Writer().Mode(7).Put(6, 0).Repeat(12, 5, 0).Repeat(4, 5, 0).Repeat(4, 1, 1);
        Assert.True(Proves(Dds(Dx10, 4, 4, m7All.Bytes, dxgi: 98)));
        var m7OneZero = new Bc7Writer().Mode(7).Put(6, 0).Repeat(12, 5, 0).Repeat(4, 5, 0).Put(1, 1).Put(1, 1).Put(1, 1).Put(1, 0);
        Assert.False(Proves(Dds(Dx10, 4, 4, m7OneZero.Bytes, dxgi: 98)));
    }

    [Fact]
    public void ScansPastTransparentBlocks_AndRefusesUnsupportedLayouts()
    {
        var blocks = Bc1(0, 0, 0xFFFFFFFF).Concat(Bc1(0xF800, 0x001F, 0)).ToArray();
        Assert.True(Proves(Dds(Dxt1, 8, 4, blocks)));
        // Truncated payload: an opaque first block still proves (the decode would fail and also
        // answer "not transparent"); a scan that reaches the missing block proves nothing.
        Assert.True(Proves(Dds(Dxt1, 8, 4, Bc1(0xF800, 0x001F, 0))));
        Assert.False(Proves(Dds(Dxt1, 8, 4, Bc1(0, 0, 0xFFFFFFFF))));

        var cube = Dds(Dxt1, 4, 4, Bc1(0xF800, 0x001F, 0));
        BitConverter.GetBytes(0xFE00u).CopyTo(cube, 112);
        Assert.False(Proves(cube));
        Assert.False(Proves(Dds(0x31495441, 4, 4, new byte[8])));          // BC4 (ATI1): not handled
    }

    /// <summary>Every proof the probe makes on constructed BC1/BC3 files agrees with Pfim.</summary>
    [Fact]
    public void Proofs_AgreeWithPfimDecode_OnConstructedFiles()
    {
        var files = new List<byte[]>
        {
            Dds(Dxt1, 4, 4, Bc1(0xF800, 0x001F, 0xFFFFFFFF)), Dds(Dxt1, 4, 4, Bc1(0, 0, 0xFFFFFFFC)),
            Dds(Dxt1, 4, 4, Bc1(0, 0, 0xFFFFFFFF)), Dds(Dxt5, 4, 4, Bc3(0, 0, 0)),
            Dds(Dxt5, 4, 4, Bc3(5, 0, 0)), Dds(Dxt5, 4, 4, Bc3(0, 0, 7)), Dds(Dxt5, 4, 4, Bc3(200, 100, 3)),
        };
        foreach (var file in files)
            if (Proves(file)) Assert.False(PfimFullyTransparent(new MemoryStream(file)));
    }

    /// <summary>Opt-in corpus check: set CVR_ALPHA_PROBE_FILES to a text file of DDS paths
    /// (one per line). Fails if the probe ever proves a texture the decode calls fully
    /// transparent; prints how many were proven vs left to the decode.</summary>
    [Fact]
    public void Proofs_AgreeWithPfimDecode_OnCorpus()
    {
        string? list = Environment.GetEnvironmentVariable("CVR_ALPHA_PROBE_FILES");
        if (string.IsNullOrWhiteSpace(list) || !File.Exists(list))
        {
            _out.WriteLine("SKIPPED: set CVR_ALPHA_PROBE_FILES to a list of DDS paths.");
            return;
        }
        int proven = 0, unproven = 0, transparent = 0;
        var violations = new List<string>();
        var unprovenByFormat = new Dictionary<string, List<string>>();
        foreach (string path in File.ReadLines(list).Select(l => l.Trim()).Where(File.Exists))
        {
            bool probe = DdsAlphaProbe.ProvesNonZeroAlpha(path);
            bool decodedTransparent;
            using (var s = File.OpenRead(path)) decodedTransparent = PfimFullyTransparent(s);
            if (decodedTransparent) transparent++;
            if (probe) { proven++; if (decodedTransparent) violations.Add(path); }
            else
            {
                unproven++;
                string key = DescribeFormat(path) + (decodedTransparent ? " [fully transparent]" : "");
                if (!unprovenByFormat.TryGetValue(key, out var group)) unprovenByFormat[key] = group = new();
                group.Add(path);
            }
        }
        _out.WriteLine($"proven={proven} unproven(decode fallback)={unproven} decodedFullyTransparent={transparent}");
        foreach (var (key, group) in unprovenByFormat.OrderByDescending(g => g.Value.Count))
            _out.WriteLine($"  unproven {key}: {group.Count}  e.g. {group[0]}");
        Assert.True(violations.Count == 0, "Probe contradicted the decode:\n" + string.Join("\n", violations));
        Assert.True(proven + unproven > 0, "Corpus list matched no files.");
    }

    /// <summary>Header summary for the corpus report: FourCC or DX10 DXGI format, plus
    /// cube/uncompressed flags.</summary>
    private static string DescribeFormat(string path)
    {
        try
        {
            var h = new byte[148];
            using (var s = File.OpenRead(path)) s.ReadAtLeast(h, 128, throwOnEndOfStream: false);
            uint U(int o) => BitConverter.ToUInt32(h, o);
            string flags = (U(112) & 0xFE00) != 0 ? " cube" : "";
            if ((U(80) & 4) == 0) return $"uncompressed rgbBits={U(88)} alphaMask={U(104):X8}{flags}";
            uint fourcc = U(84);
            if (fourcc == Dx10) return $"DX10 dxgi={U(128)}{flags}";
            return "fourcc=" + System.Text.Encoding.ASCII.GetString(h, 84, 4) + flags;
        }
        catch { return "unreadable"; }
    }

    /// <summary>The pre-probe verdict: Pfim decode; Rgba32 alpha all zero. Formats the cache
    /// converts with alpha forced to 255, unsupported formats and failed decodes are false.</summary>
    private static bool PfimFullyTransparent(Stream stream)
    {
        try
        {
            using var image = Pfimage.FromStream(stream);
            if (image.Format != ImageFormat.Rgba32) return false;
            int rowBytes = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
                for (int x = 3; x < rowBytes; x += 4)
                    if (image.Data[y * image.Stride + x] != 0) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

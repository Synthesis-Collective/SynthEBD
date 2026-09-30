using System.IO;

namespace CharacterViewer.TextureProbe;

internal static class Fixtures
{
    // Independently constructed constant-color blocks; each mip has a different
    // color so a route that regenerates mips or drops level zero fails sampling.
    internal static byte[] Dds(int dxgi, int width = 4, int height = 4, bool legacy = false, bool transparent = false)
    {
        var payload = new List<byte>();
        int count = 0;
        for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
        {
            byte[] block = Block(dxgi, count, transparent);
            for (int n = 0; n < ((w + 3) / 4) * ((h + 3) / 4); n++) payload.AddRange(block);
            count++;
            if (w == 1 && h == 1) break;
        }
        var bytes = new byte[(legacy ? 128 : 148) + payload.Count];
        Put(bytes, 0, 0x20534444); Put(bytes, 4, 124); Put(bytes, 8, 0xA1007);
        Put(bytes, 12, height); Put(bytes, 16, width); Put(bytes, 28, count);
        Put(bytes, 76, 32); Put(bytes, 80, 4); Put(bytes, 108, 0x401008);
        Put(bytes, 84, legacy ? dxgi switch { 71 => 0x31545844, 74 => 0x33545844, 77 => 0x35545844, 80 => 0x31495441, 83 => 0x32495441, _ => throw new ArgumentException() } : 0x30315844);
        if (!legacy) { Put(bytes, 128, dxgi); Put(bytes, 132, 3); Put(bytes, 140, 1); }
        payload.CopyTo(bytes, legacy ? 128 : 148);
        return bytes;
    }
    internal static void Put(byte[] bytes, int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    internal static byte[] Expected(int dxgi, int level, bool transparent = false) => dxgi switch
    {
        80 => new byte[] { 192, 192, 192, 255 },
        83 => new byte[] { 192, 64, 0, 255 },
        98 or 99 => new byte[] { 200, 60, 120, 254 },
        _ => new byte[] { (byte)(level % 3 == 0 ? 255 : 0), (byte)(level % 3 == 1 ? 255 : 0), (byte)(level % 3 == 2 ? 255 : 0), (byte)(transparent ? 0 : dxgi is 77 or 78 ? 128 : 255) }
    };
    private static byte[] Block(int format, int level, bool transparent)
    {
        if (format is 80 or 81) return new byte[] { 192, 64, 0, 0, 0, 0, 0, 0 };
        if (format is 83 or 84) return new byte[] { 192, 64, 0, 0, 0, 0, 0, 0, 64, 32, 0, 0, 0, 0, 0, 0 };
        var b = new byte[format is 71 or 72 ? 8 : 16];
        if (format is 98 or 99)
        {
            int bit = 0;
            void Bits(int value, int length) { for (int n = 0; n < length; n++, bit++) if ((value & (1 << n)) != 0) b[bit / 8] |= (byte)(1 << (bit % 8)); }
            Bits(64, 7); foreach (int value in new[] { 100, 100, 30, 30, 60, 60, 127, 127 }) Bits(value, 7);
            // p-bits and indices are zero: mode 6 constant endpoints.
            return b;
        }
        int offset = b.Length - 8;
        ushort color = (ushort)(level % 3 == 0 ? 0xF800 : level % 3 == 1 ? 0x07E0 : 0x001F);
        BitConverter.GetBytes(color).CopyTo(b, offset);
        if (format is 74 or 75) Array.Fill(b, transparent ? (byte)0 : (byte)255, 0, 8);
        if (format is 77 or 78) { b[0] = transparent ? (byte)0 : (byte)128; b[1] = 255; }
        return b;
    }
}

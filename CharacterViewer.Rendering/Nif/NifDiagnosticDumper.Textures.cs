using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Pfim;

namespace CharacterViewer.Rendering;

internal static partial class NifDiagnosticDumper
{
    // ════════════════════════════════════════════════════════════════════
    //  Texture pass
    //
    //  For every unique texture path the block walk recorded:
    //    1. Resolve via GameAssetResolver (this transparently extracts BSA-
    //       packed DDS to a temp cache and populates ResolvedDiskPath).
    //    2. Parse the DDS header (first 148 bytes) to surface width, height,
    //       format, and mip count — same fields NifSkope shows in its file
    //       properties view.
    //
    //  Pixel data is never read: only the 128-byte DDS_HEADER plus the
    //  optional 20-byte DDS_HEADER_DXT10 extension when the FourCC is "DX10".
    // ════════════════════════════════════════════════════════════════════

    private static void DumpTextures(
        StringBuilder sb,
        HashSet<string> texturePaths,
        GameAssetResolver assetResolver,
        ICharacterViewerLogger logger)
    {
        sb.AppendLine();
        sb.AppendLine($"=== Textures ({texturePaths.Count}) ===");

        if (texturePaths.Count == 0)
        {
            sb.AppendLine("  <no textures referenced>");
            return;
        }

        foreach (var path in SortedSet(texturePaths))
        {
            DumpOneTexture(sb, path, assetResolver);
            if (sb.Length > 32768) FlushFlush(logger, sb);
        }
    }

    private static void DumpOneTexture(StringBuilder sb, string gamePath, GameAssetResolver assetResolver)
    {
        sb.Append("  ").AppendLine(gamePath);

        AssetSource? src = null;
        try { src = assetResolver.ResolveAssetSource(gamePath); }
        catch (Exception ex) { sb.AppendLine($"    resolve: <error: {ex.Message}>"); return; }

        if (src == null)
        {
            sb.AppendLine("    resolve: <null>");
            return;
        }

        switch (src.Kind)
        {
            case AssetOriginKind.NotFound:
                sb.AppendLine("    origin: NotFound");
                return;
            case AssetOriginKind.Loose:
                sb.AppendLine("    origin: Loose");
                break;
            case AssetOriginKind.Bsa:
                sb.Append("    origin: BSA");
                if (!string.IsNullOrEmpty(src.BsaPath))
                {
                    sb.Append(" (").Append(Path.GetFileName(src.BsaPath));
                    if (!string.IsNullOrEmpty(src.InternalBsaPath)) sb.Append(" → ").Append(src.InternalBsaPath);
                    sb.Append(')');
                }
                sb.AppendLine();
                break;
        }

        if (string.IsNullOrEmpty(src.ResolvedDiskPath))
        {
            sb.AppendLine("    disk:   <unresolved>");
            return;
        }

        sb.Append("    disk:   ").AppendLine(src.ResolvedDiskPath);

        long fileBytes = -1;
        try { fileBytes = new FileInfo(src.ResolvedDiskPath).Length; } catch { }
        if (fileBytes >= 0) sb.Append("    bytes:  ").AppendLine(fileBytes.ToString("N0"));

        DdsInfo? info = TryReadDdsHeader(src.ResolvedDiskPath);
        if (info == null)
        {
            sb.AppendLine("    dds:    <not a DDS or read failed>");
            return;
        }

        sb.Append("    size:   ").Append(info.Width).Append(" × ").AppendLine(info.Height.ToString());
        sb.Append("    format: ").AppendLine(info.Format ?? "?");
        sb.Append("    mips:   ").AppendLine(info.MipCount.ToString());
        if (info.LinearSize > 0) sb.Append("    pitch:  ").AppendLine(info.LinearSize.ToString("N0"));

        // Pfim-decode a sparse sample of pixels and report colour/alpha statistics.
        // Reveals whether (e.g.) a FaceTint texture has an alpha mask we're
        // ignoring, or whether two diffuses we expect to match actually differ
        // significantly in mean tone.
        TryDumpPixelStats(sb, src.ResolvedDiskPath);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Pixel statistics (mean RGB, mean alpha, alpha histogram)
    // ════════════════════════════════════════════════════════════════════

    private static void TryDumpPixelStats(StringBuilder sb, string diskPath)
    {
        try
        {
            using var image = Pfimage.FromFile(diskPath);
            int w = image.Width, h = image.Height;
            int stride = image.Stride;
            byte[] data = image.Data;

            // Sample every Nth pixel along each axis so 8192² textures don't
            // make the dump take seconds. Target ~10k samples regardless of
            // input size — that's enough for stable means and an alpha
            // histogram without paying full O(w·h).
            int target = 10_000;
            int totalPx = w * h;
            int step = Math.Max(1, (int)Math.Sqrt(totalPx / (double)target));

            int bpp; // bytes per pixel in image.Data
            bool hasAlpha;
            int rOff, gOff, bOff, aOff;
            switch (image.Format)
            {
                // Pfim's BGRA32 layout (the most common Skyrim DDS output).
                case ImageFormat.Rgba32:
                    bpp = 4; rOff = 2; gOff = 1; bOff = 0; aOff = 3; hasAlpha = true; break;
                case ImageFormat.Rgb24:
                    bpp = 3; rOff = 2; gOff = 1; bOff = 0; aOff = -1; hasAlpha = false; break;
                case ImageFormat.Rgb8:
                    bpp = 1; rOff = 0; gOff = 0; bOff = 0; aOff = -1; hasAlpha = false; break;
                default:
                    sb.Append("    pixels: <unsupported pfim format ").Append(image.Format).AppendLine(">");
                    return;
            }

            long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            int n = 0;
            // alpha histogram: 4 buckets — [0,64), [64,128), [128,192), [192,256)
            Span<int> aHist = stackalloc int[4];

            for (int y = 0; y < h; y += step)
            {
                int rowBase = y * stride;
                for (int x = 0; x < w; x += step)
                {
                    int idx = rowBase + x * bpp;
                    if (idx + bpp > data.Length) break;
                    byte r = data[idx + rOff];
                    byte g = data[idx + gOff];
                    byte b = data[idx + bOff];
                    sumR += r; sumG += g; sumB += b;
                    if (hasAlpha)
                    {
                        byte a = data[idx + aOff];
                        sumA += a;
                        aHist[a >> 6]++;
                    }
                    n++;
                }
            }

            if (n == 0) return;

            sb.Append("    samples:").Append(n.ToString()).Append(" (every ").Append(step).AppendLine("th pixel)");
            sb.Append("    mean RGB:")
              .Append("(").Append((sumR / (double)n).ToString("F1"))
              .Append(", ").Append((sumG / (double)n).ToString("F1"))
              .Append(", ").Append((sumB / (double)n).ToString("F1"))
              .AppendLine(")");
            if (hasAlpha)
            {
                double meanA = sumA / (double)n;
                sb.Append("    mean A: ").Append(meanA.ToString("F1"))
                  .Append("   alpha hist: [0..63]=").Append(Pct(aHist[0], n))
                  .Append("  [64..127]=").Append(Pct(aHist[1], n))
                  .Append("  [128..191]=").Append(Pct(aHist[2], n))
                  .Append("  [192..255]=").Append(Pct(aHist[3], n))
                  .AppendLine();
            }
            else
            {
                sb.AppendLine("    mean A: <no alpha channel>");
            }
        }
        catch (Exception ex)
        {
            sb.Append("    pixels: <decode failed: ").Append(ex.Message).AppendLine(">");
        }
    }

    private static string Pct(int count, int total)
    {
        return ((100.0 * count) / total).ToString("F1") + "%";
    }

    // ════════════════════════════════════════════════════════════════════
    //  DDS header parser
    //
    //  Reference: Microsoft DDS specification (docs.microsoft.com/en-us/windows/win32/direct3ddds/dds-header)
    //  and NifSkope src/gl/gltexloaders.cpp:1784-1917 (same field offsets).
    // ════════════════════════════════════════════════════════════════════

    private sealed class DdsInfo
    {
        public int Width;
        public int Height;
        public int MipCount;
        public int LinearSize;
        public string? Format;
    }

    private static DdsInfo? TryReadDdsHeader(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Buffer enough for DDS_HEADER (124B + 4B magic = 128B) and
            // optional DDS_HEADER_DXT10 (+20B). 148 bytes covers both.
            Span<byte> buf = stackalloc byte[148];
            int read = fs.Read(buf);
            if (read < 128) return null;

            // Magic 'DDS '
            if (buf[0] != (byte)'D' || buf[1] != (byte)'D' || buf[2] != (byte)'S' || buf[3] != (byte)' ')
                return null;

            // dwSize at offset 4 should be 124 — sanity check
            uint headerSize = ReadU32(buf, 4);
            if (headerSize != 124) return null;

            int dwHeight = (int)ReadU32(buf, 12);
            int dwWidth  = (int)ReadU32(buf, 16);
            int dwLinear = (int)ReadU32(buf, 20);
            int dwMips   = (int)ReadU32(buf, 28);

            // DDS_PIXELFORMAT starts at offset 76 (32 bytes)
            uint pfFlags = ReadU32(buf, 80);
            uint pfFourCC = ReadU32(buf, 84);
            uint pfRgbBits = ReadU32(buf, 88);
            uint pfRMask = ReadU32(buf, 92);
            uint pfGMask = ReadU32(buf, 96);
            uint pfBMask = ReadU32(buf, 100);
            uint pfAMask = ReadU32(buf, 104);

            string format = DescribeFormat(pfFlags, pfFourCC, pfRgbBits, pfRMask, pfGMask, pfBMask, pfAMask, buf, read);

            return new DdsInfo
            {
                Width = dwWidth,
                Height = dwHeight,
                MipCount = Math.Max(1, dwMips),
                LinearSize = dwLinear,
                Format = format,
            };
        }
        catch { return null; }
    }

    private static string DescribeFormat(uint flags, uint fourCC, uint rgbBits,
                                         uint rMask, uint gMask, uint bMask, uint aMask,
                                         Span<byte> buf, int bufLen)
    {
        const uint DDPF_FOURCC      = 0x4;
        const uint DDPF_RGB         = 0x40;
        const uint DDPF_LUMINANCE   = 0x20000;
        const uint DDPF_ALPHAPIXELS = 0x1;

        if ((flags & DDPF_FOURCC) != 0)
        {
            string fcc = FourCCToString(fourCC);
            if (fcc == "DX10" && bufLen >= 148)
            {
                // DDS_HEADER_DXT10 starts at offset 128, dxgiFormat is the first uint32.
                uint dxgi = ReadU32(buf, 128);
                return $"{DxgiFormatName(dxgi)} (DX10/dxgi={dxgi})";
            }
            return $"{fcc} (FourCC)";
        }
        if ((flags & DDPF_RGB) != 0)
        {
            bool hasAlpha = (flags & DDPF_ALPHAPIXELS) != 0;
            return $"RGB{(hasAlpha ? "A" : "")} {rgbBits}bpp R=0x{rMask:X} G=0x{gMask:X} B=0x{bMask:X} A=0x{aMask:X}";
        }
        if ((flags & DDPF_LUMINANCE) != 0)
        {
            return $"Luminance {rgbBits}bpp";
        }
        return $"flags=0x{flags:X8}";
    }

    private static string FourCCToString(uint cc)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)(cc & 0xFF);
        chars[1] = (char)((cc >> 8) & 0xFF);
        chars[2] = (char)((cc >> 16) & 0xFF);
        chars[3] = (char)((cc >> 24) & 0xFF);
        for (int i = 0; i < 4; i++)
            if (chars[i] < ' ' || chars[i] > '~') chars[i] = '?';
        return new string(chars);
    }

    private static uint ReadU32(Span<byte> b, int offset)
    {
        return (uint)b[offset]
             | ((uint)b[offset + 1] << 8)
             | ((uint)b[offset + 2] << 16)
             | ((uint)b[offset + 3] << 24);
    }

    // Subset of DXGI_FORMAT enum (the values that actually appear in game DDS files).
    // Full list at https://learn.microsoft.com/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format.
    private static string DxgiFormatName(uint v) => v switch
    {
        2  => "R32G32B32A32_FLOAT",
        10 => "R16G16B16A16_FLOAT",
        24 => "R10G10B10A2_UNORM",
        28 => "R8G8B8A8_UNORM",
        29 => "R8G8B8A8_UNORM_SRGB",
        61 => "R8_UNORM",
        62 => "R8_UINT",
        65 => "A8_UNORM",
        70 => "BC1_TYPELESS",
        71 => "BC1_UNORM",
        72 => "BC1_UNORM_SRGB",
        73 => "BC2_TYPELESS",
        74 => "BC2_UNORM",
        75 => "BC2_UNORM_SRGB",
        76 => "BC3_TYPELESS",
        77 => "BC3_UNORM",
        78 => "BC3_UNORM_SRGB",
        79 => "BC4_TYPELESS",
        80 => "BC4_UNORM",
        81 => "BC4_SNORM",
        82 => "BC5_TYPELESS",
        83 => "BC5_UNORM",
        84 => "BC5_SNORM",
        87 => "B8G8R8A8_UNORM",
        91 => "B8G8R8A8_UNORM_SRGB",
        94 => "BC6H_TYPELESS",
        95 => "BC6H_UF16",
        96 => "BC6H_SF16",
        97 => "BC7_TYPELESS",
        98 => "BC7_UNORM",
        99 => "BC7_UNORM_SRGB",
        _  => $"DXGI_{v}",
    };
}

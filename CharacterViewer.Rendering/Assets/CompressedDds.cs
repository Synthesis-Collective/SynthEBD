using System.Buffers.Binary;
using System.IO;

namespace CharacterViewer.Rendering;

public enum DdsBlockFormat { Bc1, Bc2, Bc3, Bc4, Bc5, Bc7 }
public enum DdsReadStatus { Eligible, Fallback, Invalid }
public readonly record struct DdsMip(int Width, int Height, int Offset, int Length);

/// <summary>A validated ordinary 2D DDS payload. Owns its blocks; no decoded pixels.</summary>
public sealed class CompressedDds
{
    internal byte[] Blocks { get; }
    public DdsBlockFormat Format { get; }
    public bool IsSrgb { get; }
    public uint AlphaMode { get; }
    public IReadOnlyList<DdsMip> Mips { get; }
    public int Width => Mips[0].Width;
    public int Height => Mips[0].Height;
    public long PayloadBytes => Blocks.LongLength;
    public bool HasFullChain => Mips[^1].Width == 1 && Mips[^1].Height == 1;
    /// <summary>Elapsed file open/read time; OS cache state is not controlled.</summary>
    public double ReadMs { get; private set; }
    /// <summary>Header validation and allocation time excluding file open/read.</summary>
    public double ParseMs { get; private set; }

    private CompressedDds(byte[] blocks, DdsBlockFormat format, bool srgb, uint alphaMode, DdsMip[] mips)
    {
        Blocks = blocks; Format = format; IsSrgb = srgb; AlphaMode = alphaMode;
        Mips = Array.AsReadOnly(mips);
    }

    /// <summary>Reads and validates headers and every declared mip before allocating blocks.
    /// Unsupported layouts/semantics use the decoded route; malformed supported files are errors.</summary>
    public static DdsReadStatus Read(string path, out CompressedDds? texture, out string reason, int maximumFinalMipDimension = 1)
        => ReadCore(path, true, maximumFinalMipDimension, out texture, out reason);

    internal static DdsReadStatus Inspect(string path, int maximumFinalMipDimension, out string reason)
        => ReadCore(path, false, maximumFinalMipDimension, out _, out reason);

    private static DdsReadStatus ReadCore(string path, bool readBlocks, int maximumFinalMipDimension, out CompressedDds? texture, out string reason)
    {
        if (maximumFinalMipDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFinalMipDimension));
        texture = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        double readMs = 0;
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[148];
            stream.ReadExactly(header[..128]);
            readMs += elapsed.Elapsed.TotalMilliseconds;
            if (U(header, 0) != 0x20534444 || U(header, 4) != 124 || U(header, 76) != 32)
                throw new InvalidDataException("Invalid DDS magic or header size");
            uint width = U(header, 16), height = U(header, 12), count = U(header, 28);
            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                throw new InvalidDataException("Invalid DDS dimensions");
            if ((U(header, 112) & 0x20FE00) != 0 || U(header, 24) > 1 || (U(header, 8) & 0x800000) != 0)
            { reason = "cube-or-volume"; return DdsReadStatus.Fallback; }
            if ((U(header, 80) & 4) == 0)
            { reason = "uncompressed-pixel-format"; return DdsReadStatus.Fallback; }
            uint fourcc = U(header, 84), dxgi = 0, alpha = 0;
            bool srgb = false;
            DdsBlockFormat? format;
            if (fourcc == 0x30315844) // DX10
            {
                double start = elapsed.Elapsed.TotalMilliseconds;
                stream.ReadExactly(header[128..]);
                readMs += elapsed.Elapsed.TotalMilliseconds - start;
                if (U(header, 140) == 0) throw new InvalidDataException("DDS array size is zero");
                if (U(header, 132) != 3 || U(header, 140) != 1 || (U(header, 136) & 4) != 0)
                { reason = "dx10-array-cube-or-non-2d"; return DdsReadStatus.Fallback; }
                dxgi = U(header, 128); alpha = U(header, 144);
                if (alpha > 4) throw new InvalidDataException("Invalid DDS alpha mode");
                if (alpha == 2)
                { reason = "premultiplied-alpha"; return DdsReadStatus.Fallback; }
                srgb = dxgi is 72 or 75 or 78 or 99;
                format = dxgi switch
                {
                    71 or 72 => DdsBlockFormat.Bc1, 74 or 75 => DdsBlockFormat.Bc2,
                    77 or 78 => DdsBlockFormat.Bc3, 80 => DdsBlockFormat.Bc4,
                    83 => DdsBlockFormat.Bc5, 98 or 99 => DdsBlockFormat.Bc7, _ => null
                };
            }
            else format = fourcc switch
            {
                0x31545844 => DdsBlockFormat.Bc1, 0x33545844 => DdsBlockFormat.Bc2,
                0x35545844 => DdsBlockFormat.Bc3,
                0x31495441 or 0x55344342 => DdsBlockFormat.Bc4, // ATI1 / BC4U
                0x32495441 or 0x55354342 => DdsBlockFormat.Bc5, // ATI2 / BC5U
                _ => null
            };
            if (format == null)
            {
                reason = dxgi is 81 or 84 || fourcc is 0x53344342 or 0x53354342
                    ? "signed-rgtc-decoder-contract" : $"unsupported-format-fourcc-{fourcc:X8}-dxgi-{dxgi}";
                return DdsReadStatus.Fallback;
            }
            int fullCount = 1;
            for (uint size = Math.Max(width, height); size > 1; size >>= 1) fullCount++;
            count = Math.Max(1, count);
            if (count > fullCount) throw new InvalidDataException("DDS mip count exceeds dimensions");
            var mips = new DdsMip[count];
            int w = (int)width, h = (int)height, total = 0;
            int blockBytes = format is DdsBlockFormat.Bc1 or DdsBlockFormat.Bc4 ? 8 : 16;
            for (int level = 0; level < count; level++)
            {
                int size = checked((int)(((w + 3L) / 4) * ((h + 3L) / 4) * blockBytes));
                mips[level] = new(w, h, total, size);
                total = checked(total + size);
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            }
            if (total > stream.Length - stream.Position)
                throw new InvalidDataException("Truncated DDS mip payload");
            reason = "eligible";
            if (Math.Max(mips[^1].Width, mips[^1].Height) > maximumFinalMipDimension)
            { reason = "final-mip-exceeds-limit"; return DdsReadStatus.Fallback; }
            if (!readBlocks) return DdsReadStatus.Eligible;
            var blocks = new byte[total];
            double readStart = elapsed.Elapsed.TotalMilliseconds;
            stream.ReadExactly(blocks);
            readMs += elapsed.Elapsed.TotalMilliseconds - readStart;
            texture = new(blocks, format.Value, srgb, alpha, mips);
            texture.ReadMs = readMs;
            texture.ParseMs = elapsed.Elapsed.TotalMilliseconds - readMs;
            reason = "eligible";
            return DdsReadStatus.Eligible;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException or ArgumentException)
        {
            reason = ex.Message;
            return DdsReadStatus.Invalid;
        }
    }

    private static uint U(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
}

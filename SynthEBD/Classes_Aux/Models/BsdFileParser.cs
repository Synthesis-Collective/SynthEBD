// Define CATALOG_VERBOSE_LOGGING to re-enable per-file parse-success logs
// (useful when diagnosing slider-catalog issues; normally spammy -- e.g. UUNP emits
// ~400 success lines per load). Failure logs are always on.
//#define CATALOG_VERBOSE_LOGGING

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SynthEBD;

/// <summary>
/// Parsed slider diff data from a single entry in an OSD file.
/// Maps vertex indices to position deltas for one slider.
/// </summary>
public class OsdSliderData
{
    public required string Name { get; init; }
    public required Dictionary<ushort, System.Numerics.Vector3> VertexDeltas { get; init; }
}

/// <summary>
/// Represents one parsed OSD file. The ShapeName is derived from the file name
/// (e.g. "CBBE Body.osd" → "CBBE Body") since the OSD format itself does not
/// store a shape name.
/// </summary>
public class OsdFile
{
    public required string ShapeName { get; init; }
    public required List<OsdSliderData> Sliders { get; init; }
}

/// <summary>
/// Parses BodySlide's OSD (Outfit Studio Data) and BSD (BodySlide Data) binary files
/// containing per-slider sparse vertex deltas. Formats verified against BodySlide source
/// (DiffData.cpp → OSDataFile::Read / BSDataFile::Read).
///
/// OSD layout (multi-slider, one file per shape -- CBBE/BHUNP/3BA):
///   uint32    magic   "OSD\0"
///   uint32    version
///   uint32    dataCount
///   for each entry:
///     uint8             nameLength
///     char[nameLength]  name (length-prefixed, NOT null-terminated)
///     uint16            diffCount
///     for each diff (packed, 14 bytes):
///       uint16  vertexIndex
///       float   deltaX / deltaY / deltaZ
///
/// BSD layout (single-slider, one file per slider -- UUNP-style; no magic):
///   uint32    diffCount
///   for each diff (packed, 16 bytes):
///     uint32  vertexIndex
///     float   deltaX / deltaY / deltaZ
///   (slider name is the file name without extension)
/// </summary>
public class BsdFileParser
{
    private readonly ICharacterViewerLogger _logger;

    /// <summary>
    /// OSD file magic. BodySlide writes this as the C/C++ multichar literal 'OSD\0',
    /// whose numeric value on x86 is 0x4F534400 (with 'O' in the high byte). When that
    /// uint32 is serialized little-endian to disk, the on-disk bytes are
    /// 00 44 53 4F — and BinaryReader.ReadUInt32 reads them back as 0x4F534400.
    /// (The intuitive "ASCII bytes 'O','S','D','\0' read LE → 0x0044534F" form is wrong
    /// for this format; that was the bug that silently rejected every .osd file.)
    /// </summary>
    private const uint OsdMagic = 0x4F534400;

    private readonly CharacterViewerLogGate _logGate;

    public BsdFileParser(ICharacterViewerLogger logger, CharacterViewerLogGate logGate)
    {
        _logger = logger;
        _logGate = logGate;
    }

    private void LogVerbose(string message)
    {
        if (_logGate != null && _logGate.Verbose) _logger?.LogMessage(message);
    }

    /// <summary>
    /// Parses an OSD file from disk. ShapeName is derived from the file name.
    /// Returns null if the file cannot be read or has an invalid header.
    /// </summary>
    public OsdFile? ParseOsdFile(string osdFilePath)
    {
        if (!File.Exists(osdFilePath))
        {
            _logger.LogError("CharacterViewer: OSD file not found: '" + osdFilePath + "'");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(osdFilePath);
            string shapeName = Path.GetFileNameWithoutExtension(osdFilePath);
            return ParseOsdFile(stream, shapeName, osdFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to parse OSD '" + osdFilePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses an OSD file from a stream (e.g. after BSA extraction).
    /// </summary>
    public OsdFile? ParseOsdFile(Stream stream, string shapeName, string displayPath = "")
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            // Magic header
            uint magic = reader.ReadUInt32();
            if (magic != OsdMagic)
            {
                _logger.LogError("CharacterViewer: Invalid OSD magic in '" + displayPath +
                    "' (expected 0x" + OsdMagic.ToString("X8") + ", got 0x" + magic.ToString("X8") + ")");
                return null;
            }

            // Version
            uint version = reader.ReadUInt32();

            // Data count (number of slider entries)
            uint dataCount = reader.ReadUInt32();

            var sliders = new List<OsdSliderData>((int)dataCount);
            int totalDeltas = 0;

            for (uint i = 0; i < dataCount; i++)
            {
                // Length-prefixed name
                byte nameLength = reader.ReadByte();
                byte[] nameBytes = reader.ReadBytes(nameLength);
                string name = Encoding.ASCII.GetString(nameBytes);

                // Diff count
                ushort diffCount = reader.ReadUInt16();

                var deltas = new Dictionary<ushort, System.Numerics.Vector3>(diffCount);

                // Read packed diff structs (14 bytes each: uint16 + 3×float)
                for (int j = 0; j < diffCount; j++)
                {
                    ushort vertIndex = reader.ReadUInt16();
                    float dx = reader.ReadSingle();
                    float dy = reader.ReadSingle();
                    float dz = reader.ReadSingle();

                    // Clamp near-zero values (matches BodySlide's clampEpsilon)
                    const float epsilon = 1e-10f;
                    if (Math.Abs(dx) < epsilon) dx = 0;
                    if (Math.Abs(dy) < epsilon) dy = 0;
                    if (Math.Abs(dz) < epsilon) dz = 0;

                    if (dx != 0 || dy != 0 || dz != 0)
                    {
                        deltas[vertIndex] = new System.Numerics.Vector3(dx, dy, dz);
                    }
                }

                sliders.Add(new OsdSliderData
                {
                    Name = name,
                    VertexDeltas = deltas
                });

                totalDeltas += deltas.Count;
            }

#if CATALOG_VERBOSE_LOGGING
            LogVerbose("CharacterViewer: Parsed OSD '" + displayPath +
                "' -> shape '" + shapeName + "', " + sliders.Count + " sliders, " + totalDeltas + " total deltas (v" + version + ")");
#else
            _ = totalDeltas; _ = version;
#endif

            return new OsdFile
            {
                ShapeName = shapeName,
                Sliders = sliders
            };
        }
        catch (EndOfStreamException)
        {
            _logger.LogError("CharacterViewer: Unexpected end of OSD file '" + displayPath + "'");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to parse OSD '" + displayPath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses a UUNP-style .bsd file: one file per slider, with a different on-disk layout
    /// than .osd. BSD has no magic header -- it's just a uint32 delta-count followed by
    /// packed 16-byte records (uint32 vertexIndex + 3× float). The slider name is the
    /// file name without extension. Returns a single-slider <see cref="OsdFile"/> so the
    /// rest of the pipeline can treat BSD and OSD uniformly. Returns null on error.
    /// </summary>
    public OsdFile? ParseBsdFile(string bsdFilePath)
    {
        if (!File.Exists(bsdFilePath))
        {
            _logger.LogError("CharacterViewer: BSD file not found: '" + bsdFilePath + "'");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(bsdFilePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            uint diffCount = reader.ReadUInt32();
            var deltas = new Dictionary<ushort, System.Numerics.Vector3>((int)diffCount);

            for (uint j = 0; j < diffCount; j++)
            {
                uint vertIndex = reader.ReadUInt32();
                float dx = reader.ReadSingle();
                float dy = reader.ReadSingle();
                float dz = reader.ReadSingle();

                const float epsilon = 1e-10f;
                if (Math.Abs(dx) < epsilon) dx = 0;
                if (Math.Abs(dy) < epsilon) dy = 0;
                if (Math.Abs(dz) < epsilon) dz = 0;

                if ((dx != 0 || dy != 0 || dz != 0) && vertIndex <= ushort.MaxValue)
                {
                    deltas[(ushort)vertIndex] = new System.Numerics.Vector3(dx, dy, dz);
                }
            }

            string sliderName = Path.GetFileNameWithoutExtension(bsdFilePath);
            var slider = new OsdSliderData { Name = sliderName, VertexDeltas = deltas };

#if CATALOG_VERBOSE_LOGGING
            LogVerbose("CharacterViewer: Parsed BSD '" + bsdFilePath +
                "' -> slider '" + sliderName + "', " + deltas.Count + " deltas");
#endif

            return new OsdFile
            {
                ShapeName = sliderName,
                Sliders = new List<OsdSliderData> { slider }
            };
        }
        catch (EndOfStreamException)
        {
            _logger.LogError("CharacterViewer: Unexpected end of BSD file '" + bsdFilePath + "'");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to parse BSD '" + bsdFilePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Scans a directory for all .osd and .bsd files and parses them. .osd files are multi-
    /// slider with an "OSD\0" magic (CBBE/BHUNP/3BA/etc.). .bsd files are single-slider with
    /// no magic and a different record layout (UUNP-style per-slider files). Results are
    /// de-duplicated by <see cref="OsdFile.ShapeName"/> (first hit wins) so a single shape
    /// parsed through two paths only appears once. Returns an empty list if the directory
    /// does not exist.
    ///
    /// <paramref name="recursive"/> defaults to true (legacy behavior used by morph-preview
    /// fallback). Pass false for the body-type registry slider-catalog scan -- UUNP ships
    /// outfit variants in nested folders (e.g. Unified UNP/NeverNude/NNBra*.bsd) that
    /// pollute the body's slider set if recursed into.
    /// </summary>
    public List<OsdFile> ParseAllOsdInDirectory(string directoryPath, bool recursive = true)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<OsdFile>();

        if (!Directory.Exists(directoryPath))
        {
            LogVerbose("CharacterViewer: OSD directory not found: '" + directoryPath + "'");
            return results;
        }

        var seenShapeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.osd", searchOption))
        {
            var osd = ParseOsdFile(filePath);
            if (osd != null && seenShapeNames.Add(osd.ShapeName))
            {
                results.Add(osd);
            }
        }

        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.bsd", searchOption))
        {
            var bsd = ParseBsdFile(filePath);
            if (bsd != null && seenShapeNames.Add(bsd.ShapeName))
            {
                results.Add(bsd);
            }
        }

        return results;
    }
}

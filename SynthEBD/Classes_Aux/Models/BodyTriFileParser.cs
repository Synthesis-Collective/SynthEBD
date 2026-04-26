using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace SynthEBD;

/// <summary>
/// One named morph inside a body .tri file. VertexDeltas is sparse -- only
/// non-zero vertices are stored -- and keyed by the NIF's own vertex indices,
/// so the .tri is topology-matched to its sibling .nif 1:1.
/// </summary>
public class BodyTriMorph
{
    public required string Name { get; init; }
    public required Dictionary<ushort, Vector3> VertexDeltas { get; init; }
}

/// <summary>
/// One shape block inside a body .tri file. BodySlide writes one block per
/// NIF shape (e.g. femalebody, femalehands) with that shape's full morph set.
/// </summary>
public class BodyTriShape
{
    public required string ShapeName { get; init; }
    public required List<BodyTriMorph> Morphs { get; init; }
}

/// <summary>
/// Parsed contents of a BodySlide body .tri file.
/// </summary>
public class BodyTriFile
{
    public required string FilePath { get; init; }
    public required List<BodyTriShape> Shapes { get; init; }
}

/// <summary>
/// Parses BodySlide's body-morph .tri files (NifTools "TRIP" format -- distinct
/// from FaceGen's FRTRI003 head-morph format handled by
/// <see cref="TriFileParser"/>). Format verified against BodySlide source
/// (src/files/TriFile.cpp, TriFile::Read/Write).
///
/// On-disk layout:
///   char[4]   magic  bytes 'P','I','R','T' on disk (uint32 0x54524950)
///                    BodySlide writes this as a C multichar literal `'TRIP'`,
///                    whose x86 uint32 value is 0x54524950. Serialized
///                    little-endian, the low byte ('P') lands at offset 0, so
///                    an ASCII decode of the first 4 bytes spells "PIRT", not
///                    "TRIP". (Same gotcha that bit BsdFileParser's OSD magic.)
///   POSITION MORPHS SECTION:
///     uint16  shapeCount
///     for each shape:
///       uint8           shapeNameLen
///       char[n]         shapeName                  (no null terminator)
///       uint16          morphCount
///       for each morph:
///         uint8         morphNameLen
///         char[n]       morphName                  (= slider name, no LCP stripping needed)
///         float         multiplier                 (per-morph delta scale factor)
///         uint16        vertexDeltaCount           (sparse -- only affected verts)
///         for each delta (8 bytes):
///           uint16      vertexIndex
///           int16       dx, dy, dz                 (multiply by multiplier → float offset)
///   UV MORPHS SECTION (optional, EOF if absent):
///     uint16  shapeCountUV
///     ... same shape as position, but 2D deltas (6 bytes) ...
///
/// We only parse the position section -- UV morphs aren't used for vertex
/// deformation in the CharacterViewer.
///
/// Unlike the OSD/BSD path, morph names in .tri files are direct slider names
/// ("AreolaSize", not "3BA AreolaSize"), so no longest-common-prefix stripping
/// is required.
/// </summary>
public class BodyTriFileParser
{
    private readonly ICharacterViewerLogger _logger;

    public BodyTriFileParser(ICharacterViewerLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a body .tri file. Returns null on missing file, wrong magic, or
    /// any parse error. Failures are logged at error level; success is silent.
    /// </summary>
    public BodyTriFile? Parse(string triFilePath)
    {
        if (!File.Exists(triFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(triFilePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            // Magic: on-disk bytes spell "PIRT" (uint32 0x54524950 written LE).
            // The authoring-side literal is `'TRIP'`, but the low byte lands at
            // offset 0 on disk, so the ASCII decode is reversed. See class doc
            // for the full story.
            byte[] magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != (byte)'P' || magic[1] != (byte)'I'
                || magic[2] != (byte)'R' || magic[3] != (byte)'T')
            {
                _logger.LogError("CharacterViewer: Invalid body .tri magic in '" + triFilePath +
                    "' (expected 'PIRT' on disk, got '" + Encoding.ASCII.GetString(magic) + "')");
                return null;
            }

            var shapes = new List<BodyTriShape>();

            ushort shapeCount = reader.ReadUInt16();
            for (int s = 0; s < shapeCount; s++)
            {
                string shapeName = ReadLengthPrefixedName(reader);
                ushort morphCount = reader.ReadUInt16();

                var morphs = new List<BodyTriMorph>(morphCount);
                for (int m = 0; m < morphCount; m++)
                {
                    string morphName = ReadLengthPrefixedName(reader);
                    float multiplier = reader.ReadSingle();
                    ushort deltaCount = reader.ReadUInt16();

                    var deltas = new Dictionary<ushort, Vector3>(deltaCount);
                    for (int d = 0; d < deltaCount; d++)
                    {
                        ushort vertIndex = reader.ReadUInt16();
                        short dxRaw = reader.ReadInt16();
                        short dyRaw = reader.ReadInt16();
                        short dzRaw = reader.ReadInt16();

                        float dx = dxRaw * multiplier;
                        float dy = dyRaw * multiplier;
                        float dz = dzRaw * multiplier;

                        // Match BodySlide's near-zero clamp (prevents denormals
                        // and keeps zero-check consistency with OSD path).
                        const float epsilon = 1e-10f;
                        if (Math.Abs(dx) < epsilon) dx = 0;
                        if (Math.Abs(dy) < epsilon) dy = 0;
                        if (Math.Abs(dz) < epsilon) dz = 0;

                        if (dx != 0 || dy != 0 || dz != 0)
                        {
                            deltas[vertIndex] = new Vector3(dx, dy, dz);
                        }
                    }

                    morphs.Add(new BodyTriMorph
                    {
                        Name = morphName,
                        VertexDeltas = deltas
                    });
                }

                shapes.Add(new BodyTriShape
                {
                    ShapeName = shapeName,
                    Morphs = morphs
                });
            }

            // UV morphs section intentionally skipped -- not needed for vertex
            // position deformation. If the stream has trailing UV data, we just
            // let it sit.

            return new BodyTriFile
            {
                FilePath = triFilePath,
                Shapes = shapes
            };
        }
        catch (EndOfStreamException)
        {
            _logger.LogError("CharacterViewer: Unexpected end of body .tri file '" + triFilePath + "'");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to parse body .tri '" + triFilePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads a uint8 length prefix followed by that many ASCII bytes. No null
    /// terminator is stored -- the length byte is authoritative.
    /// </summary>
    private static string ReadLengthPrefixedName(BinaryReader reader)
    {
        byte len = reader.ReadByte();
        if (len == 0) return string.Empty;
        byte[] bytes = reader.ReadBytes(len);
        return Encoding.ASCII.GetString(bytes);
    }
}

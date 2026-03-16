using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SynthEBD;

/// <summary>
/// Pure-C# binary parser for FaceGen .tri files (magic "FRTRI003").
///
/// TRI files store a polygonal mesh (vertices, triangles, quads) plus named
/// morph targets. For Skyrim head parts, the "CharGen" variant TRI files
/// (e.g., EyesFemaleChargen.tri, FemaleHeadCharGen.tri) contain difference
/// morphs whose names correspond to the chargen slider system. Each morph
/// stores a per-vertex (dx, dy, dz) delta compressed as scaled shorts.
///
/// ─── Binary Layout (FRTRI003) ───────────────────────────────────────────
///
///   Header:
///     char[8]   magic          "FRTRI003"
///     int32     vertexCount    V
///     int32     triCount       T
///     int32     quadCount      Q
///     int32     labelledVerts  LV
///     int32     labelledSurfs  LS
///     int32     texCoordCount  X  (0 = per-vertex UVs, X>0 = per-facet)
///     int32     extFlags       bit0 = has texture coords, bit1 = 16-bit labels
///     int32     diffMorphCount Md  (difference morphs — what we need)
///     int32     statMorphCount Ms  (stat morphs)
///     int32     statVertCount  K   (total stat morph vertices)
///     byte[16]  reserved
///
///   Geometry:
///     float3 × (V+K)   vertices
///     int3   × T        triangles
///     int4   × Q        quads
///     (labels, texture coords — skipped)
///
///   Difference morphs (Md entries):
///     For each morph:
///       int32 N, char[N]  label (null-terminated string)
///       float             scale
///       short3 × V        per-vertex delta (multiply by scale)
///
///   Stat morphs (Ms entries) — not needed for chargen, skipped.
///
/// ─── Usage in FaceGenPatcher ────────────────────────────────────────────
///
///   1. Locate the CharGen .tri file for the replacement head part
///   2. Parse it with TriFileParser.Load()
///   3. Read the NPC's FaceMorph slider values from Mutagen
///   4. Call ApplyMorphs() to compute per-vertex offsets
///   5. Add those offsets to the cloned shape's vertices
///
/// This produces CK-equivalent per-vertex morphing — not just a rigid
/// translation, but the actual deformation that conforms the new mesh
/// to the NPC's specific face shape (eye depth, spacing, tilt, etc.).
///
/// ─── Reference ──────────────────────────────────────────────────────────
///
///   FaceGen SDK: https://facegen.com/dl/sdk/doc/manual/fileformats.html
///   PyNifly trihandler.py (BadDogSkyrim)
///   Outfit Studio / BodySlide (ousnius)
/// </summary>
public class TriFileParser
{
    // ═══════════════════════════════════════════════════════════════════════
    //  PUBLIC DATA STRUCTURES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A single named morph target from a .tri file. Contains the morph name
    /// and the pre-scaled per-vertex deltas (already multiplied by the scale
    /// factor stored in the file).
    /// </summary>
    public class TriMorph
    {
        /// <summary>
        /// Morph name as stored in the .tri file. For chargen morphs these
        /// correspond to slider names: "NoseLength", "BrowHeight", etc.
        /// For preset morphs: "NoseType0", "NoseType1", "BrowType0", etc.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Per-vertex delta vectors. Length == VertexCount from the header.
        /// Each entry is (dx, dy, dz) representing the vertex displacement
        /// when this morph is applied at full weight (1.0).
        ///
        /// These are already pre-multiplied by the scale factor from the file,
        /// so the consumer just multiplies by the slider weight and adds to
        /// the base vertex position.
        /// </summary>
        public (float X, float Y, float Z)[] Deltas { get; set; }
    }

    /// <summary>
    /// Parsed contents of a .tri file, containing the base mesh geometry
    /// and all difference morphs.
    /// </summary>
    public class TriFileData
    {
        /// <summary>File path this was loaded from (for diagnostics).</summary>
        public string FilePath { get; set; }

        /// <summary>Number of base mesh vertices (V from header).</summary>
        public int VertexCount { get; set; }

        /// <summary>Number of triangles (T from header).</summary>
        public int TriangleCount { get; set; }

        /// <summary>Number of quads (Q from header).</summary>
        public int QuadCount { get; set; }

        /// <summary>
        /// Base mesh vertex positions. Length == VertexCount.
        /// These are the undeformed "default" positions before any morph.
        /// </summary>
        public (float X, float Y, float Z)[] Vertices { get; set; }

        /// <summary>
        /// All difference morphs in the file, keyed by name (case-insensitive).
        /// For CharGen TRIs these contain the slider morphs.
        /// </summary>
        public Dictionary<string, TriMorph> Morphs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ordered list of morph names as they appear in the file.
        /// Useful for debugging and ordered iteration.
        /// </summary>
        public List<string> MorphNames { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONSTANTS
    // ═══════════════════════════════════════════════════════════════════════

    private const string MAGIC_FRTRI003 = "FRTRI003";

    // ═══════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses a .tri file from the given path. Returns null if the file
    /// doesn't exist, has an unrecognized magic number, or fails to parse.
    /// </summary>
    /// <param name="filePath">Absolute path to the .tri file.</param>
    /// <param name="errorMessage">
    ///   Set to a diagnostic string on failure, null on success.
    /// </param>
    /// <returns>Parsed data, or null on failure.</returns>
    public static TriFileData Load(string filePath, out string errorMessage)
    {
        errorMessage = null;

        if (!File.Exists(filePath))
        {
            errorMessage = "TRI file not found: " + filePath;
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            return Parse(reader, filePath, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = "Exception parsing TRI file: " + filePath + " — " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Parses a .tri file from a byte array (useful for BSA-extracted data).
    /// </summary>
    public static TriFileData LoadFromBytes(byte[] data, string diagnosticName, out string errorMessage)
    {
        errorMessage = null;

        if (data == null || data.Length == 0)
        {
            errorMessage = "TRI data is null or empty: " + diagnosticName;
            return null;
        }

        try
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            return Parse(reader, diagnosticName, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = "Exception parsing TRI data: " + diagnosticName + " — " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Applies a set of named morph weights to the TRI's base vertices and
    /// returns the resulting per-vertex offsets (deltas from the base mesh).
    ///
    /// The formula for each vertex:
    ///   offset[i] = Σ (weight[j] × morph[j].Deltas[i])
    ///
    /// Only morphs present in both <paramref name="morphWeights"/> and the
    /// TRI file are applied. Missing morphs are silently skipped.
    /// </summary>
    /// <param name="triData">Parsed .tri file data.</param>
    /// <param name="morphWeights">
    ///   Map of morph name → weight (slider value). Names are matched
    ///   case-insensitively. Weight range is typically [-1.0, 1.0] for
    ///   chargen sliders; preset morphs use 1.0 when active.
    /// </param>
    /// <returns>
    ///   Per-vertex offset array of length triData.VertexCount.
    ///   Each entry is the cumulative (dx, dy, dz) to add to the base vertex.
    /// </returns>
    public static (float X, float Y, float Z)[] ComputeMorphOffsets(
        TriFileData triData,
        Dictionary<string, float> morphWeights)
    {
        int vertCount = triData.VertexCount;
        var offsets = new (float X, float Y, float Z)[vertCount];

        foreach (var (morphName, weight) in morphWeights)
        {
            // Skip zero weights — no contribution.
            if (Math.Abs(weight) < 1e-7f)
                continue;

            if (!triData.Morphs.TryGetValue(morphName, out var morph))
                continue;

            // Accumulate weighted deltas.
            for (int i = 0; i < vertCount; i++)
            {
                var d = morph.Deltas[i];
                offsets[i].X += d.X * weight;
                offsets[i].Y += d.Y * weight;
                offsets[i].Z += d.Z * weight;
            }
        }

        return offsets;
    }

    /// <summary>
    /// Convenience method: computes morph offsets and returns the final
    /// morphed vertex positions (base + offsets).
    /// </summary>
    public static (float X, float Y, float Z)[] ComputeMorphedVertices(
        TriFileData triData,
        Dictionary<string, float> morphWeights)
    {
        var offsets = ComputeMorphOffsets(triData, morphWeights);
        var result = new (float X, float Y, float Z)[triData.VertexCount];

        for (int i = 0; i < triData.VertexCount; i++)
        {
            result[i] = (
                triData.Vertices[i].X + offsets[i].X,
                triData.Vertices[i].Y + offsets[i].Y,
                triData.Vertices[i].Z + offsets[i].Z
            );
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BINARY PARSER
    // ═══════════════════════════════════════════════════════════════════════

    private static TriFileData Parse(BinaryReader reader, string diagnosticName, out string errorMessage)
    {
        errorMessage = null;

        // ── Magic number ──
        byte[] magicBytes = reader.ReadBytes(8);
        string magic = Encoding.ASCII.GetString(magicBytes);

        if (magic != MAGIC_FRTRI003)
        {
            errorMessage = "Unrecognized TRI magic: \"" + magic + "\" in " + diagnosticName +
                           " (expected \"" + MAGIC_FRTRI003 + "\")";
            return null;
        }

        // ── Header fields ──
        int vertexCount    = reader.ReadInt32();  // V
        int triCount       = reader.ReadInt32();  // T
        int quadCount      = reader.ReadInt32();  // Q
        int labelledVerts  = reader.ReadInt32();  // LV
        int labelledSurfs  = reader.ReadInt32();  // LS
        int texCoordCount  = reader.ReadInt32();  // X
        int extFlags       = reader.ReadInt32();  // extension info
        int diffMorphCount = reader.ReadInt32();  // Md
        int statMorphCount = reader.ReadInt32();  // Ms
        int statVertCount  = reader.ReadInt32();  // K
        byte[] reserved    = reader.ReadBytes(16);

        bool hasTexCoords   = (extFlags & 0x01) != 0;
        bool has16BitLabels = (extFlags & 0x02) != 0;

        // Sanity checks
        if (vertexCount < 0 || vertexCount > 100_000)
        {
            errorMessage = "TRI vertex count out of range: " + vertexCount + " in " + diagnosticName;
            return null;
        }

        var result = new TriFileData
        {
            FilePath = diagnosticName,
            VertexCount = vertexCount,
            TriangleCount = triCount,
            QuadCount = quadCount,
        };

        // ── Vertices: (V + K) × float3 ──
        //
        // The vertex array stores V base mesh vertices followed by K extra
        // vertices for stat morphs. We read ALL of them — the base vertices
        // go into result.Vertices, and the extras are kept temporarily for
        // stat morph delta computation below.

        int totalVerts = vertexCount + statVertCount;
        result.Vertices = new (float, float, float)[vertexCount];
        var allVertices = new (float X, float Y, float Z)[totalVerts];

        for (int i = 0; i < totalVerts; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            allVertices[i] = (x, y, z);
            if (i < vertexCount)
            {
                result.Vertices[i] = (x, y, z);
            }
        }

        // ── Triangles: T × int3 ──
        for (int i = 0; i < triCount; i++)
        {
            reader.ReadInt32(); // v0
            reader.ReadInt32(); // v1
            reader.ReadInt32(); // v2
        }

        // ── Quads: Q × int4 ──
        for (int i = 0; i < quadCount; i++)
        {
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
        }

        // ── Labelled vertices: LV entries ──
        for (int i = 0; i < labelledVerts; i++)
        {
            reader.ReadInt32(); // vertex index
            SkipString(reader); // label string
        }

        // ── Labelled surface points: LS entries ──
        for (int i = 0; i < labelledSurfs; i++)
        {
            reader.ReadInt32();   // tri index
            reader.ReadSingle();  // u
            reader.ReadSingle();  // v
            reader.ReadSingle();  // w
            SkipString(reader);   // label
        }

        // ── Texture coordinates (optional) ──
        if (hasTexCoords)
        {
            if (texCoordCount == 0)
            {
                // Per-vertex UVs: V × float2
                for (int i = 0; i < vertexCount; i++)
                {
                    reader.ReadSingle(); // u
                    reader.ReadSingle(); // v
                }
            }
            else
            {
                // Per-facet UVs: X × float2, then T × int3 + Q × int4 indices
                for (int i = 0; i < texCoordCount; i++)
                {
                    reader.ReadSingle();
                    reader.ReadSingle();
                }
                for (int i = 0; i < triCount; i++)
                {
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt32();
                }
                for (int i = 0; i < quadCount; i++)
                {
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt32();
                }
            }
        }

        // ── Difference morphs: Md entries ──
        //
        // This is the critical section for chargen morphing. Each morph has:
        //   - A null-terminated label (the morph/slider name)
        //   - A float scale factor
        //   - V × (short, short, short) compressed deltas
        //
        // The actual delta for vertex i is: (short_x, short_y, short_z) × scale

        for (int m = 0; m < diffMorphCount; m++)
        {
            string morphName = ReadString(reader);
            float scale = reader.ReadSingle();

            var deltas = new (float, float, float)[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                short dx = reader.ReadInt16();
                short dy = reader.ReadInt16();
                short dz = reader.ReadInt16();

                deltas[i] = (dx * scale, dy * scale, dz * scale);
            }

            var morph = new TriMorph
            {
                Name = morphName,
                Deltas = deltas,
            };

            // Use TryAdd to handle potential duplicate morph names gracefully
            // (first occurrence wins).
            result.Morphs.TryAdd(morphName, morph);
            result.MorphNames.Add(morphName);
        }

        // ── Stat morphs: Ms entries ──
        //
        // Stat morphs define absolute vertex positions for a subset of
        // vertices. Used for preset part selection: NoseType0, NoseType1,
        // BrowType0, EyeType0, MouthType0, etc.
        //
        // Layout per stat morph:
        //   label (int N, char[N])
        //   int32 L         — number of affected base vertices
        //   int32 × L       — affected vertex indices (into base array 0..V-1)
        //
        // The target positions for these vertices are in the extra vertex
        // region: allVertices[V + statVertOffset .. V + statVertOffset + L - 1].
        //
        // To unify with ComputeMorphOffsets(), we convert each stat morph
        // to an equivalent difference morph:
        //   delta[affectedIdx] = extraVertex - baseVertex
        //   delta[otherIdx]    = (0, 0, 0)
        //
        // When applied at weight 1.0, this produces the same result as
        // replacing the affected vertices with their stat morph targets.

        int statVertOffset = 0;

        for (int m = 0; m < statMorphCount; m++)
        {
            string morphName = ReadString(reader);
            int affectedCount = reader.ReadInt32();

            var affectedIndices = new int[affectedCount];
            for (int i = 0; i < affectedCount; i++)
            {
                affectedIndices[i] = reader.ReadInt32();
            }

            // Convert to difference deltas.
            var deltas = new (float X, float Y, float Z)[vertexCount];

            for (int i = 0; i < affectedCount; i++)
            {
                int baseIdx = affectedIndices[i];
                int extraIdx = vertexCount + statVertOffset + i;

                if (baseIdx >= 0 && baseIdx < vertexCount && extraIdx < totalVerts)
                {
                    var baseV = allVertices[baseIdx];
                    var statV = allVertices[extraIdx];
                    deltas[baseIdx] = (
                        statV.X - baseV.X,
                        statV.Y - baseV.Y,
                        statV.Z - baseV.Z
                    );
                }
            }

            var morph = new TriMorph
            {
                Name = morphName,
                Deltas = deltas,
            };

            result.Morphs.TryAdd(morphName, morph);
            result.MorphNames.Add(morphName);

            statVertOffset += affectedCount;
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STRING HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads a length-prefixed string: int32 N, followed by N bytes.
    /// The FaceGen spec says the string is null-terminated, but N includes
    /// the null terminator in most implementations.
    /// </summary>
    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0) return string.Empty;

        byte[] bytes = reader.ReadBytes(length);

        // Strip trailing null terminator(s) if present.
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = length;

        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    /// <summary>
    /// Skips past a length-prefixed string without allocating.
    /// </summary>
    private static void SkipString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length > 0)
        {
            reader.BaseStream.Seek(length, SeekOrigin.Current);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  CHARGEN SLIDER ↔ MORPH NAME MAPPING
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Maps between Mutagen's NPC FaceMorph float array (19 chargen slider
/// values) and the morph names used in CharGen .tri files.
///
/// Each slider maps to TWO morph targets — one per direction:
///   Slider "EyesUpVsDown" at -0.7 → "EyesMoveDown" at weight 0.7
///   Slider "EyesForwardVsBack" at +0.2 → "EyesForward" at weight 0.2
///
/// Presets use 1-based indexing: NAMA Eyes=4 → "EyesType5" in the TRI.
/// </summary>
public static class ChargenSliderMap
{
    private static readonly (string Positive, string Negative)[] SliderMorphNames = new[]
    {
        ("NoseLong",       "NoseShort"),        //  0 - NoseLongVsShort
        ("NoseUp",         "NoseDown"),          //  1 - NoseUpVsDown
        ("JawUp",          "JawDown"),            //  2 - JawUpVsDown
        ("JawNarrow",      "JawWide"),            //  3 - JawNarrowVsWide
        ("JawForward",     "JawBack"),            //  4 - JawForwardVsBack
        ("CheeksUp",       "CheeksDown"),         //  5 - CheeksUpVsDown
        ("CheeksForward",  "CheeksBack"),         //  6 - CheeksForwardVsBack
        ("EyesMoveUp",     "EyesMoveDown"),       //  7 - EyesUpVsDown
        ("EyesMoveIn",     "EyesMoveOut"),        //  8 - EyesInVsOut
        ("BrowUp",         "BrowDown"),            //  9 - BrowsUpVsDown
        ("BrowIn",         "BrowOut"),             // 10 - BrowsInVsOut
        ("BrowForward",    "BrowBack"),            // 11 - BrowsForwardVsBack
        ("LipsUp",         "LipsDown"),            // 12 - LipsUpVsDown
        ("LipsIn",         "LipsOut"),             // 13 - LipsInVsOut
        ("ChinNarrow",     "ChinWide"),            // 14 - ChinNarrowVsWide
        ("ChinUp",         "ChinDown"),            // 15 - ChinUpVsDown
        ("ChinUnderbite",  "ChinOverbite"),        // 16 - ChinUnderbiteVsOverbite
        ("EyesForward",    "EyesBack"),            // 17 - EyesForwardVsBack
        ("NoseNarrow",     "NoseWide"),            // 18 - Unknown (Nose Width)
    };

    private static readonly string[] PresetMorphPrefixes = new string[]
    {
        "NoseType",   // NAMA[0]
        "BrowType",   // NAMA[1]
        "EyesType",   // NAMA[2] — plural 's'
        "MouthType",  // NAMA[3]
    };

    public static Dictionary<string, float> BuildMorphWeights(
        IReadOnlyList<float> faceMorphSliders,
        uint? nosePreset = null,
        uint? browPreset = null,
        uint? eyePreset = null,
        uint? mouthPreset = null)
    {
        var weights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        if (faceMorphSliders != null)
        {
            int count = Math.Min(faceMorphSliders.Count, SliderMorphNames.Length);
            for (int i = 0; i < count; i++)
            {
                float value = faceMorphSliders[i];
                if (Math.Abs(value) < 1e-7f) continue;

                var (positiveName, negativeName) = SliderMorphNames[i];
                if (value > 0) weights[positiveName] = value;
                else weights[negativeName] = Math.Abs(value);
            }
        }

        AddPresetMorph(weights, PresetMorphPrefixes[0], nosePreset);
        AddPresetMorph(weights, PresetMorphPrefixes[1], browPreset);
        AddPresetMorph(weights, PresetMorphPrefixes[2], eyePreset);
        AddPresetMorph(weights, PresetMorphPrefixes[3], mouthPreset);

        return weights;
    }

    public static string GetSliderMorphName(int sliderIndex)
    {
        if (sliderIndex < 0 || sliderIndex >= SliderMorphNames.Length) return null;
        var (pos, neg) = SliderMorphNames[sliderIndex];
        return pos + "/" + neg;
    }

    public static int SliderCount => SliderMorphNames.Length;

    private static void AddPresetMorph(Dictionary<string, float> weights, string prefix, uint? presetIndex)
    {
        if (!presetIndex.HasValue || presetIndex.Value == uint.MaxValue) return;
        uint triIndex = presetIndex.Value + 1; // NAMA 0-based → TRI 1-based
        weights[prefix + triIndex] = 1.0f;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using nifly;
using NiHeader = nifly.NiHeader;
using NiObject = nifly.NiObject;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace CharacterViewer.Rendering;

/// <summary>
/// Converts NIF file mesh data (via niflysharp) into HelixToolkit-compatible geometry
/// for 3D rendering in the character viewer.
/// </summary>
public class NifMeshBuilder
{
    private readonly ICharacterViewerLogger _logger;
    private readonly CharacterViewerLogGate _logGate;
    // Optional: only used by NifDiagnosticDumper for the FULL_LOGGING texture
    // pass (resolves game-relative paths to disk so the dumper can read DDS
    // headers). Null in hosts that don't supply a resolver.
    private readonly GameAssetResolver? _assetResolver;

    public NifMeshBuilder(ICharacterViewerLogger logger, CharacterViewerLogGate logGate,
        GameAssetResolver? assetResolver = null)
    {
        _logger = logger;
        _logGate = logGate;
        _assetResolver = assetResolver;
    }

    private void LogVerbose(string message)
    {
        if (_logGate != null && _logGate.Verbose) _logger?.LogMessage(message);
    }

    // --- Neck-gap diagnostic instrumentation ---
    // When true, TryApplyCpuSkinning logs the translation/scale delta between the
    // skeleton NIF's bone world transform and the mesh NIF's bone world transform,
    // for bones on the neck/shoulder seam. Toggle off once diagnosis is complete.
    private const bool _logBoneDeltas = true;

    // Bones most likely to influence the head-body seam and adjacent areas.
    // Using the common Skyrim bone naming convention (with trailing "[Xxx]" tags).
    private static readonly HashSet<string> _diagnosticBonesOfInterest = new(StringComparer.OrdinalIgnoreCase)
    {
        "NPC Spine2 [Spn2]",
        "NPC Neck [Neck]",
        "NPC Head [Head]",
        "NPC L Clavicle [LClv]",
        "NPC R Clavicle [RClv]",
        "NPC L UpperArm [LUar]",
        "NPC R UpperArm [RUar]",
    };
    /// <summary>
    /// Result of building a single NIF shape into renderable data.
    /// </summary>
    public class BuiltMesh
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required int[] Indices { get; init; }
        public required Vector2[] TextureCoordinates { get; init; }
        public required Vector3[] Tangents { get; init; }
        public required Vector3[] Bitangents { get; init; }
        public required string ShapeName { get; init; }

        /// <summary>
        /// Texture paths extracted from the shape's BSShaderTextureSet, indexed by slot.
        /// Slot 0 = diffuse, 1 = normal, 2 = glow/skin tint, 7 = specular, etc.
        /// </summary>
        public Dictionary<int, string> TexturePaths { get; init; } = new();

        /// <summary>
        /// True if this shape's BSLightingShaderProperty has the SLSF1_Model_Space_Normals flag set.
        /// MSN textures encode normals in the mesh's model coordinate space rather than tangent space.
        /// </summary>
        public bool IsModelSpaceNormals { get; init; }

        /// <summary>
        /// True if this shape uses the Greyscale-to-Palette hair tint shader.
        /// The diffuse texture is a greyscale mask that should be multiplied by the tint color.
        /// </summary>
        public bool IsHairTintShader { get; init; }

        /// <summary>
        /// Hair tint color from BSLightingShaderProperty.hairTintColor (RGB, 0–1 range).
        /// </summary>
        public (float R, float G, float B)? HairTintColor { get; init; }

        /// <summary>
        /// Bind-pose (unskinned) vertex positions in Y-up space. Null for unskinned shapes.
        /// </summary>
        public Vector3[]? BindPosePositions { get; init; }

        /// <summary>
        /// Bind-pose (unskinned) vertex normals in Y-up space. Null for unskinned shapes.
        /// </summary>
        public Vector3[]? BindPoseNormals { get; init; }

        /// <summary>
        /// Bind-pose vertex positions from the weight-0 companion NIF (<c>_0.nif</c>),
        /// stashed verbatim before any blend. Used by <see cref="VM_CharacterViewer.ApplyMorphSet"/>
        /// to lerp against <see cref="Weight1BindPosePositions"/> at the current NpcWeight,
        /// so changing weight via ApplyBodySlide produces the same engine-equivalent base
        /// mesh that loading a weight-specific NPC would. Null when no <c>_0.nif</c>
        /// companion exists for this shape, or for non-body parts.
        /// Mutable (vs. init-only) because it's attached after construction in the
        /// load path — the BuiltMesh comes out of BuildFromFile without _0 awareness,
        /// then LoadAllMeshParts pairs it with the freshly-built _0 mesh and stashes
        /// the latter's bind pose here. Travels through scene install via the BuiltMesh
        /// reference so it survives ClearScene mid-load.
        /// </summary>
        public Vector3[]? Weight0BindPosePositions { get; set; }

        /// <summary>
        /// Bind-pose vertex positions from the weight-1 companion NIF (<c>_1.nif</c>),
        /// snapshotted verbatim before <c>BlendWeightMorph</c> blends them into
        /// <see cref="BindPosePositions"/>. Same purpose as <see cref="Weight0BindPosePositions"/>:
        /// gives <see cref="VM_CharacterViewer.ApplyMorphSet"/> the un-blended _1
        /// endpoint to lerp against. Null when no _0/_1 pair was loaded.
        /// </summary>
        public Vector3[]? Weight1BindPosePositions { get; set; }

        /// <summary>
        /// Pre-computed skinning data for CPU-side bone-weight skinning.
        /// Null for unskinned shapes. Used to re-skin after BodySlide deformation.
        /// </summary>
        public SkinningInfo? Skinning { get; init; }

        /// <summary>
        /// Names of bones this shape's vertices are weighted to that resolved
        /// from NO source - present in neither the skeleton NIF nor the mesh's
        /// own NIF - so their vertices would collapse to the origin. Null/empty
        /// in the normal case. Bones that are absent from the skeleton but
        /// embedded in the mesh NIF do NOT appear here (they render via the
        /// mesh-NIF fallback like the base body does); those are reported
        /// separately on <see cref="BonesAbsentFromSkeleton"/>. The mesh-override
        /// channel reads this to SKIP a genuinely unrenderable shape (no crash,
        /// no bind-pose collapse) and surface it as a missing asset. The normal
        /// load path ignores it (existing behavior unchanged).
        /// </summary>
        public IReadOnlyList<string>? UnresolvedSkinBones { get; init; }

        /// <summary>
        /// Names of weighted bones present in the mesh's own NIF but absent from
        /// the resolved skeleton, so they rendered via the mesh-NIF fallback.
        /// Null/empty in the normal case (skeleton provides every bone). A
        /// non-empty list means the loaded skeleton is missing bones the mesh
        /// expects: the shape still renders, but on a frame the base meshes
        /// (which DO get those bones from the skeleton) don't share, so it can be
        /// misaligned. The mesh-override channel surfaces this as a
        /// skeleton-compatibility warning (e.g. an auxiliary armature needs a
        /// skeleton mod that the load order is missing). The normal load path
        /// ignores it.
        /// </summary>
        public IReadOnlyList<string>? BonesAbsentFromSkeleton { get; init; }

        /// <summary>
        /// True if this shape is the primary head mesh in a FaceGen NIF.
        /// </summary>
        public bool IsPrimaryHeadShape { get; init; }

        /// <summary>
        /// Dismember-partition IDs (SSE SBP_* values) read from this shape's
        /// <see cref="BSDismemberSkinInstance"/>, or null when it has none (a
        /// plain <c>NiSkinInstance</c> shape such as eyes/brows/mouth). Lets the
        /// slot-occupancy resolver tag a FaceGen head NIF's sub-shapes by their
        /// real biped slot instead of the coarse "Head" group slot — so a hood
        /// occupying the hair slot (31) can hide baked-in hair (partition 131)
        /// the same way body armor hides the base body. See
        /// <c>VM_CharacterViewer.BipedSlotsForBaseShape</c>.
        /// </summary>
        public IReadOnlyList<ushort>? DismemberPartitions { get; init; }

        /// <summary>
        /// True if this shape's NiAlphaProperty has the alpha test flag set (bit 9).
        /// </summary>
        public bool HasAlphaTest { get; init; }

        /// <summary>
        /// True if this shape's NiAlphaProperty has the alpha blend flag set (bit 0).
        /// </summary>
        public bool HasAlphaBlend { get; init; }

        /// <summary>
        /// True if this shape's BSLightingShaderProperty has SLSF2_ZBuffer_Write
        /// (shaderFlags2 bit 0) — i.e. the NIF wants this shape to write depth.
        /// Drives the alpha-blend pass's per-shape depth masking: solid blended
        /// geometry (e.g. an SMP beard) carries this flag and must occlude what's
        /// behind it, while overlay decals (brows, eyelashes, face marks) leave it
        /// clear so they composite without writing depth. Mirrors NifSkope's
        /// <c>depthWrite = hasSF2(SLSF2_ZBuffer_Write)</c>. Defaults true (the
        /// common case for opaque/cutout shapes that have no reason to skip depth).
        /// </summary>
        public bool ZBufferWrite { get; init; } = true;

        /// <summary>
        /// Material alpha (BSLightingShaderProperty.alpha, 0–1). &lt; 1 marks a
        /// genuinely translucent shape, which — like NifSkope's
        /// <c>translucent = (alpha &lt; 1.0)</c> — keeps depth-write off even when
        /// ZBuffer_Write is set. Defaults 1.0 (fully opaque material).
        /// </summary>
        public float MaterialAlpha { get; init; } = 1f;

        /// <summary>
        /// Alpha test threshold from NiAlphaProperty (0–1 range).
        /// </summary>
        public float AlphaThreshold { get; init; }

        /// <summary>
        /// SrcBlend factor from NiAlphaProperty.flags bits 1-4 (Bethesda
        /// enum: 0=ONE, 1=ZERO, 2=SRC_COLOR, ..., 6=SRC_ALPHA, 7=INV_SRC_ALPHA, ...).
        /// Default 6 (SRC_ALPHA) when no alpha property is present.
        /// Mapped to OpenTK BlendingFactor at draw time.
        /// </summary>
        public int SrcBlendIndex { get; init; } = 6;

        /// <summary>
        /// DstBlend factor from NiAlphaProperty.flags bits 5-8 (same Bethesda
        /// enum as SrcBlend). Default 7 (INV_SRC_ALPHA) when no alpha property
        /// is present. UBE-style wet-eye outer cornea shapes ship with this
        /// set to 0 (ONE), producing additive blend so a near-black cornea
        /// adds nothing to the iris underneath while bright catchlight pixels
        /// add brightness.
        /// </summary>
        public int DstBlendIndex { get; init; } = 7;

        /// <summary>
        /// True if this shape has SLSF2_Double_Sided (shaderFlags2 bit 4).
        /// </summary>
        public bool IsDoubleSided { get; init; }

        /// <summary>
        /// True if SLSF1_Greyscale_To_Palette_Color flag (bit 4) is set in shaderFlags1.
        /// When set, the shader samples only the red channel of the diffuse texture
        /// and applies: baseColor.rrr * tint_color * greyscaleToPaletteScale.
        /// When NOT set (but IsHairTintShader is true), the full RGB diffuse is
        /// multiplied: baseColor.rgb *= tint_color (simple tint).
        /// </summary>
        public bool HasGreyscaleToPaletteFlag { get; init; }

        // --- Shader material properties from BSLightingShaderProperty ---
        public float Glossiness { get; init; } = 80f;
        public float SpecularStrength { get; init; } = 1f;
        public Vector3 SpecularColor { get; init; } = Vector3.One;
        public float SubsurfaceRolloff { get; init; }
        public float GreyscaleToPaletteScale { get; init; } = 1f;
        public float RimlightPower { get; init; } = 2f;
        public bool HasVertexColors { get; init; }
        public Vector3 EmissiveColor { get; init; }
        public float EmissiveMultiple { get; init; }
        public Vector2 UvScale { get; init; } = Vector2.One;
        public Vector2 UvOffset { get; init; }
        public float EnvironmentMapScale { get; init; } = 1f;
        public float EyeCubemapScale { get; init; } = 1f;

        /// <summary>
        /// Per-vertex colors (RGBA, 0–1 range). Null if the shape has no vertex colors.
        /// </summary>
        public Vector4[]? VertexColors { get; init; }

        /// <summary>Shader flags for detecting specular, soft lighting, hair soft lighting, etc.</summary>
        public uint ShaderFlags1 { get; init; }
        public uint ShaderFlags2 { get; init; }
        public uint ShaderType { get; init; }

        /// <summary>NIF-side <c>BSLightingShaderProperty.skinTintAlpha</c>.
        /// Always 0.0 in the vanilla / modder-authored sample we surveyed,
        /// but read so the SkinTintAlpha-weighted operator in the debug
        /// face-tint path can use it.</summary>
        public float SkinTintAlpha { get; init; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BSLightingShaderProperty flag constants
    //  Bit positions per Nifskope/Bethesda spec (glproperty.h:450-517).
    //  NPC Portrait Creator has 9 incorrect bit mappings in ParseShaderFlags;
    //  these are the authoritative values.
    // ═══════════════════════════════════════════════════════════════════════

    // --- SLSF1 (shaderFlags1) ---
    private const uint SLSF1_Specular              = 1u << 0;
    private const uint SLSF1_GreyscaleToPalette    = 1u << 4;
    private const uint SLSF1_EnvironmentMapping    = 1u << 7;
    private const uint SLSF1_FacegenDetailMap      = 1u << 10;
    private const uint SLSF1_ModelSpaceNormals     = 1u << 12;
    private const uint SLSF1_EyeEnvironmentMapping = 1u << 17;
    private const uint SLSF1_HairSoftLighting      = 1u << 18;
    private const uint SLSF1_OwnEmit               = 1u << 22;

    // --- SLSF2 (shaderFlags2) ---
    private const uint SLSF2_ZBufferWrite           = 1u << 0;
    private const uint SLSF2_DoubleSided            = 1u << 4;
    private const uint SLSF2_VertexColors           = 1u << 5;
    private const uint SLSF2_GlowMap                = 1u << 6;
    private const uint SLSF2_SoftLighting           = 1u << 25;
    private const uint SLSF2_RimLighting            = 1u << 26;
    private const uint SLSF2_BackLighting           = 1u << 27;

    /// <summary>
    /// Pure C# representation of a nifly MatTransform, cached for fast per-vertex
    /// skinning without SWIG interop overhead in the inner loop.
    /// Stores a 3x3 rotation matrix + translation + uniform scale.
    /// </summary>
    internal struct CachedSkinTransform
    {
        public float R00, R01, R02;
        public float R10, R11, R12;
        public float R20, R21, R22;
        public float Tx, Ty, Tz;
        public float Scale;

        /// <summary>
        /// Applies this transform to a position: result = rotation * (pos * scale) + translation.
        /// </summary>
        public void Apply(float x, float y, float z, out float rx, out float ry, out float rz)
        {
            float sx = x * Scale, sy = y * Scale, sz = z * Scale;
            rx = R00 * sx + R01 * sy + R02 * sz + Tx;
            ry = R10 * sx + R11 * sy + R12 * sz + Ty;
            rz = R20 * sx + R21 * sy + R22 * sz + Tz;
        }

        /// <summary>
        /// Applies only the rotation to a direction vector (no scale, no translation).
        /// </summary>
        public void ApplyRotation(float x, float y, float z, out float rx, out float ry, out float rz)
        {
            rx = R00 * x + R01 * y + R02 * z;
            ry = R10 * x + R11 * y + R12 * z;
            rz = R20 * x + R21 * y + R22 * z;
        }
    }

    /// <summary>
    /// Stores pre-computed skinning data for a mesh, enabling re-skinning after
    /// BodySlide deformation without re-reading the NIF.
    /// </summary>
    public class SkinningInfo
    {
        /// <summary>Pre-computed skinning matrices (boneWorld * inverseBindPose) per bone, in NIF Z-up space.</summary>
        internal CachedSkinTransform[] BoneTransforms { get; init; } = Array.Empty<CachedSkinTransform>();

        /// <summary>Per-vertex bone indices, 4 per vertex, stored flat [v0_b0, v0_b1, v0_b2, v0_b3, v1_b0, ...].
        /// Public so cross-assembly consumers (the bone-transition criterion in SynthEBD) can read them.</summary>
        public int[] VertBoneIndices { get; init; } = Array.Empty<int>();

        /// <summary>Per-vertex bone weights, 4 per vertex, stored flat (same layout as VertBoneIndices).
        /// Public for the same reason as <see cref="VertBoneIndices"/>.</summary>
        public float[] VertBoneWeights { get; init; } = Array.Empty<float>();

        /// <summary>Number of vertices this skinning data applies to.</summary>
        public int VertexCount { get; init; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PARSED-NIF LRU CACHE
    //
    //  Skyrim NPCs commonly share the same body NIFs (e.g. femalebody_1.nif),
    //  so users flipping between NPCs in the Consistency / Specific-NPC editors
    //  would re-parse the same file over and over. This cache keys on the NIF
    //  path + mtime and the skeleton path + mtime (skinning transforms depend
    //  on both). Cache hits return freshly-cloned BuiltMesh instances because
    //  BlendWeightMorph in VM_CharacterViewer mutates Positions/Normals/etc. in
    //  place — returning the canonical snapshot directly would corrupt future
    //  hits. The clone allocates ~1MB of vertex data per body mesh, which is
    //  still ~100× cheaper than a real NIF parse + skinning pass.
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class NifCacheEntry
    {
        public string NifPath { get; init; } = "";
        public long NifMTimeTicks { get; init; }
        public string? SkeletonPath { get; init; }
        public long SkeletonMTimeTicks { get; init; }
        /// <summary>The host-supplied body-part label this entry was built
        /// under — affects which shapes survive the dismember-partition
        /// filter in BuildAllShapes, so it must be part of the cache key.</summary>
        public string? BipedBodyPart { get; init; }
        public required List<BuiltMesh> Meshes { get; init; }
        /// <summary>Estimated bytes held by <see cref="Meshes"/> (vertex/index
        /// arrays + skinning), stamped at insert for the byte-budget accounting so
        /// eviction doesn't have to re-walk the arrays.</summary>
        public long Bytes { get; init; }
    }

    // 96 (was 16, then 32). The offscreen prewarm pipeline warms this cache from
    // worker threads for several NPCs concurrently while the render thread also
    // parses its current NPC, so the live working set is roughly
    // (MaxParallelPortraitRenders + 1) NPCs, each touching up to ~9 entries
    // (body/hands/feet × _0/_1 + head + hair + tail). At 32 that working set
    // overflowed and prewarmed entries were evicted before their render consumed
    // them — measured ~1% build-cache hits, so the parse never moved off the
    // render thread. 96 (~7-10 NPCs at 4-way) keeps prewarmed entries resident
    // until consumed. Entries are vertex-data clones (~1-2 MB each) → ~100-190 MB
    // worst case; in practice far less since body/hands/feet NIFs are shared across
    // NPCs. Must exceed (maxParallelRenders + 1) × ~9 to avoid re-introducing the
    // thrash; revisit if MaxParallelPortraitRenders is raised well above 4.
    private const int CacheMaxEntries = 96;
    // RAM-aware byte ceiling layered ON TOP of the entry-count cap above. The count
    // cap stays the primary mechanism because it is tuned to the parallel-prewarm
    // working set (see note above); the byte budget is a safety bound so a set of
    // pathologically large meshes can't balloon RAM. Its floor (256 MB) sits above
    // the ~190 MB worst-case footprint of 96 normal entries, so on any machine the
    // byte ceiling only trips for unusually large meshes and never evicts below the
    // working set the count cap maintains. Its ceiling is a share of total RAM (not
    // a fixed cap) so it scales with the host and tracks free RAM like the other
    // in-RAM caches (see SystemMemoryBudget).
    private const long CacheMinBudgetBytes = 256L * 1024 * 1024;        // 256 MB floor
    private const double CacheMaxFractionOfTotal = 0.4;                 // ceiling: 40% of RAM
    private const double CacheFreeRamFraction = 0.25;
    private const int CacheRepollEveryAdds = 16;
    private readonly LinkedList<NifCacheEntry> _cache = new();
    private readonly object _cacheLock = new();
    private long _cacheBytes;
    private long _cacheBudgetBytes;
    private int _cacheAddsSinceRepoll;

    // --- Opt-in parsed-NIF cache diagnostics (drop a LogNifCacheDiag.txt next to
    // the exe). Appends one line per lookup outcome to RenderLogs/NifCacheDiag.log
    // so we can tell a true cache hit from a miss, and on a miss whether an entry
    // for the same NIF path exists with a DIFFERENT key (mtime / skeleton /
    // body-part — a key mismatch) vs no entry at all (first-load or eviction).
    // Zero overhead when the trigger file is absent (single static bool check).
    private static readonly bool _cacheDiag =
        System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "LogNifCacheDiag.txt"));
    private static readonly string _cacheDiagPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "RenderLogs", "NifCacheDiag.log");
    private static readonly object _cacheDiagLock = new();
    private static long _diagHits, _diagMisses;

    // Per-thread time + count spent in an actual NIF parse (cache miss: NifFile.Load
    // + skeleton materialize + BuildAllShapes/CPU skinning). ThreadStatic so the now-
    // inline offscreen build phase attributes its OWN parse cost, separate from
    // prewarm-worker parses on other threads — lets the profiler split `build` into
    // parse (should trend to ~0 once prewarm warms everything) vs the per-render
    // floor of resolve + clone + weight-morph.
    [ThreadStatic] private static long _threadParseTicks;
    [ThreadStatic] private static int _threadParseCount;
    public double ThreadParseMs => _threadParseTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    public int ThreadParseCount => _threadParseCount;

    // Parse split: of the ThreadParse total, how much is native NifFile.Load (file
    // parse) vs BuildAllShapes (C#-side per-vertex SWIG marshaling + CPU skinning).
    // ThreadStatic for the same reason as above — each worker/render thread sees only
    // its own work, so a host can snapshot deltas around a single parse (e.g. PrewarmNpc)
    // and attribute the split per-NPC. Answers "is the FaceGen head cost in the native
    // Load or the managed marshaling" without per-block instrumentation overhead.
    [ThreadStatic] private static long _threadLoadTicks;
    [ThreadStatic] private static long _threadBuildTicks;
    public double ThreadLoadMs => _threadLoadTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    public double ThreadBuildMs => _threadBuildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    // Process-wide aggregate of the same split (all threads), so the NifCacheDiag log
    // can print a load-vs-build summary even when per-render CSV timing is off. Gated
    // entirely behind the _cacheDiag trigger file — zero cost otherwise.
    private static long _procLoadTicks, _procBuildTicks, _procGeomTicks, _procSkinTicks;
    private static int _procParseCount;

    // Within BuildAllShapes, split build cost into the geometry SWIG marshaling + per-
    // element copy loops (verts/normals/uvs/colors/tangents/bitangents/triangles — what a
    // native bulk-copy helper would eliminate) vs the CPU skinning / shape-transform pass
    // (which it would not). De-risks the native-helper decision: a high geom share means
    // the helper directly targets the cost. ThreadStatic, accumulated across all shapes of
    // a parse; BuildFromFile snapshots the deltas around BuildAllShapes.
    [ThreadStatic] private static long _threadGeomTicks;
    [ThreadStatic] private static long _threadSkinTicks;

    // Roll the per-parse load/build split into the process-wide aggregate and, when
    // the cache-diag trigger is present, periodically log the running load-vs-build
    // share. Off the hot path when _cacheDiag is false (single bool check).
    private static void AccumulateParseSplit(long loadTicks, long buildTicks, long geomTicks, long skinTicks)
    {
        if (!_cacheDiag) return;
        long l = System.Threading.Interlocked.Add(ref _procLoadTicks, loadTicks);
        long b = System.Threading.Interlocked.Add(ref _procBuildTicks, buildTicks);
        long g = System.Threading.Interlocked.Add(ref _procGeomTicks, geomTicks);
        long s = System.Threading.Interlocked.Add(ref _procSkinTicks, skinTicks);
        int n = System.Threading.Interlocked.Increment(ref _procParseCount);
        if (n % 25 == 0)
        {
            double freq = System.Diagnostics.Stopwatch.Frequency;
            double loadMs = l * 1000.0 / freq, buildMs = b * 1000.0 / freq;
            double geomMs = g * 1000.0 / freq, skinMs = s * 1000.0 / freq;
            double total = loadMs + buildMs;
            // geom% / skin% are shares of build (the rest of build = shader/texture/alpha reads).
            CacheDiagLog($"--- parse split: parses={n} loadMs={loadMs:F0} buildMs={buildMs:F0}" +
                $" (load={(total <= 0 ? 0 : 100 * loadMs / total):F0}% build={(total <= 0 ? 0 : 100 * buildMs / total):F0}%)" +
                $" | of build: geomMs={geomMs:F0} skinMs={skinMs:F0}" +
                $" (geom={(buildMs <= 0 ? 0 : 100 * geomMs / buildMs):F0}% skin={(buildMs <= 0 ? 0 : 100 * skinMs / buildMs):F0}%," +
                $" avg geom={geomMs / n:F2} skin={skinMs / n:F2} ms/parse) ---");
        }
    }

    private static void CacheDiagLog(string line)
    {
        try
        {
            lock (_cacheDiagLock)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_cacheDiagPath)!);
                System.IO.File.AppendAllText(_cacheDiagPath, line + "\n");
            }
        }
        catch { /* diagnostics must never disrupt a render */ }
    }

    /// <summary>
    /// Drops every cached parse result. Call when the mod environment is reloaded
    /// so the next BuildFromFile re-reads from disk rather than returning a result
    /// parsed from a now-different file.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            _cacheBytes = 0;
        }
    }

    /// <summary>Estimates the resident bytes of a built-mesh snapshot for the
    /// cache's byte-budget accounting: the per-vertex arrays (positions, normals,
    /// tangents, bitangents, UVs, the optional bind-pose / weight-companion arrays)
    /// plus indices and flat skinning data. Approximate by design (it omits small
    /// scalar fields and dictionary overhead) since it only drives a safety
    /// ceiling, not exact bookkeeping. Vector3 = 12 bytes, Vector2 = 8 bytes.</summary>
    private static long EstimateMeshListBytes(List<BuiltMesh> meshes)
    {
        static long Vec3(System.Numerics.Vector3[]? a) => (long)(a?.Length ?? 0) * 12;

        long bytes = 0;
        foreach (var m in meshes)
        {
            bytes += Vec3(m.Positions) + Vec3(m.Normals) + Vec3(m.Tangents) + Vec3(m.Bitangents);
            bytes += (long)(m.Indices?.Length ?? 0) * sizeof(int);
            bytes += (long)(m.TextureCoordinates?.Length ?? 0) * 8; // Vector2
            bytes += Vec3(m.BindPosePositions) + Vec3(m.BindPoseNormals);
            bytes += Vec3(m.Weight0BindPosePositions) + Vec3(m.Weight1BindPosePositions);
            if (m.Skinning != null)
            {
                bytes += (long)(m.Skinning.VertBoneIndices?.Length ?? 0) * sizeof(int);
                bytes += (long)(m.Skinning.VertBoneWeights?.Length ?? 0) * sizeof(float);
            }
        }
        return bytes;
    }

    /// <summary>
    /// Loads all renderable shapes from a NIF file and converts them to HelixToolkit geometry.
    /// </summary>
    /// <param name="nifPath">Absolute path to the mesh NIF.</param>
    /// <param name="skeletonNif">Optional already-open skeleton NIF for CPU skinning. Caller owns the lifetime.</param>
    /// <param name="skeletonPath">Optional absolute path to the skeleton NIF, used as part of the cache key.
    /// When <paramref name="skeletonNif"/> is non-null, this must also be supplied for caching to apply —
    /// otherwise the cache is bypassed (different skeletons produce different skinning transforms).</param>
    /// <param name="bipedBodyPart">Optional host-supplied body-part label
    /// ("Body", "Hands", "Feet", "Head", "Hair", "Tail"). When set to one of
    /// the labels with a known biped-slot mapping ("Body"=32, "Hands"=33,
    /// "Feet"=37), shapes whose dismember partitions don't include that slot
    /// are skipped. Mirrors the engine's behavior of treating each ARMA's
    /// WorldModel NIF as the source for one biped slot only — vanilla child
    /// meshes (ChildFeet.nif) bundle placeholder head / mouth / eye shapes
    /// that the engine ignores via the partition-vs-slot rule, and without
    /// this filter those placeholders render at world Y≈120 with the loaded
    /// body part's TXST overrides (body-textures-on-face for Dorthe).
    /// Default null disables the filter — passing null preserves the
    /// pre-existing "render every shape in the NIF" behavior for callers
    /// that don't have body-part context (BuildFromNif, dev paths).</param>
    /// <param name="skeletonProvider">Optional lazy skeleton loader. When
    /// <paramref name="skeletonNif"/> is null and this is supplied, the skeleton
    /// NIF is loaded by invoking this ONLY on a cache miss (i.e. when a real parse
    /// happens). On a cache hit the method returns before touching it, so a caller
    /// whose body parts are all already cached (e.g. a fully-prewarmed offscreen
    /// render) never pays the skeleton parse. The cache key still uses
    /// <paramref name="skeletonPath"/>, which the caller supplies eagerly, so a
    /// deferred load doesn't change hit/miss behavior.</param>
    public List<BuiltMesh> BuildFromFile(string nifPath, NifFile? skeletonNif = null, string? skeletonPath = null, string? bipedBodyPart = null, System.Threading.CancellationToken ct = default, Func<NifFile?>? skeletonProvider = null)
    {
        long nifMTime = TryGetMTime(nifPath);
        long skelMTime = skeletonPath != null ? TryGetMTime(skeletonPath) : 0;

        // Cache is safe when the skeleton identity is known (path supplied) or
        // when no skeleton is in play. A caller that passes a NifFile without a
        // path cannot validate the skeleton against the cache entry, so we skip
        // caching entirely in that case.
        bool cacheable = skeletonNif == null || skeletonPath != null;

        // While FULL_LOGGING is on we always re-load the NIF so each preview
        // attempt produces a fresh dump — otherwise the cache short-circuit
        // would silence subsequent loads of the same file. Reverts to the
        // normal fast path when the const is flipped back to false.
        bool fullLogging = NifDiagnosticDumper.FULL_LOGGING && _logGate?.Verbose == true;
        if (cacheable && !fullLogging)
        {
            var cached = TryGetFromCache(nifPath, skeletonPath, nifMTime, skelMTime, bipedBodyPart);
            if (cached != null) return cached;
        }

        // Cache miss → a real parse. Time it (ThreadStatic) so the profiler can see
        // how much of `build` is parse vs the per-render resolve/clone/morph floor.
        long parseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var results = new List<BuiltMesh>();
        using var nif = new NifFile();
        int loadResult = nif.Load(nifPath);
        long loadTicks = System.Diagnostics.Stopwatch.GetTimestamp() - parseStart;
        if (loadResult != 0)
        {
            _threadParseTicks += loadTicks;
            _threadLoadTicks += loadTicks;
            _threadParseCount++;
            AccumulateParseSplit(loadTicks, 0, 0, 0);
            return results;
        }

        NifDiagnosticDumper.DumpIfEnabled(nif, nifPath, _logGate, _logger, _assetResolver);

        // The skeleton is actually needed now. When the caller deferred it
        // (skeletonProvider), materialize it here. On the all-cache-hit path this
        // method already returned above and the skeleton NIF was never loaded —
        // that's the point: a fully-prewarmed render skips the skeleton re-parse.
        if (skeletonNif == null && skeletonProvider != null)
            skeletonNif = skeletonProvider();

        long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        long geomBefore = _threadGeomTicks, skinBefore = _threadSkinTicks;
        results = BuildAllShapes(nif, skeletonNif, bipedBodyPart, ct);
        long buildTicks = System.Diagnostics.Stopwatch.GetTimestamp() - buildStart;
        _threadParseTicks += loadTicks + buildTicks;
        _threadLoadTicks += loadTicks;
        _threadBuildTicks += buildTicks;
        _threadParseCount++;
        AccumulateParseSplit(loadTicks, buildTicks,
            _threadGeomTicks - geomBefore, _threadSkinTicks - skinBefore);

        if (cacheable && results.Count > 0)
        {
            // Store a deep-cloned snapshot so future in-place mutations of the
            // returned list (BlendWeightMorph) don't corrupt subsequent hits.
            var snapshot = CloneBuiltMeshList(results);
            long snapshotBytes = EstimateMeshListBytes(snapshot);
            lock (_cacheLock)
            {
                _cache.AddFirst(new NifCacheEntry
                {
                    NifPath = nifPath,
                    NifMTimeTicks = nifMTime,
                    SkeletonPath = skeletonPath,
                    SkeletonMTimeTicks = skelMTime,
                    BipedBodyPart = bipedBodyPart,
                    Meshes = snapshot,
                    Bytes = snapshotBytes,
                });
                _cacheBytes += snapshotBytes;

                // Periodically re-evaluate the byte budget against current free RAM.
                if (_cacheBudgetBytes == 0 || ++_cacheAddsSinceRepoll >= CacheRepollEveryAdds)
                {
                    _cacheAddsSinceRepoll = 0;
                    _cacheBudgetBytes = SystemMemoryBudget.Compute(
                        _cacheBytes, CacheFreeRamFraction, CacheMinBudgetBytes, CacheMaxFractionOfTotal);
                }

                // Evict by the count cap, plus the byte ceiling as a safety bound,
                // but protect the shared body-part parses (femalebody / hands / feet
                // / hair, reused across all/most NPCs) from being displaced by
                // one-shot entries. Outfits are diverse and per-NPC FaceGen heads are
                // unique, so attire (null body part) and Head are evicted first; only
                // if every remaining entry is a shared part do we evict the oldest of
                // those (prevents starvation). The analogous diverse-outfit vs
                // shared-skin TEXTURE split is handled by the resident GL texture
                // cache's segmented LRU, so it isn't re-implemented here. The
                // _cache.Count > 1 guard keeps a single oversized entry from looping.
                while (_cache.Count > CacheMaxEntries ||
                       (_cacheBytes > _cacheBudgetBytes && _cache.Count > 1))
                {
                    var victim = OldestEvictable();
                    _cacheBytes -= victim.Value.Bytes;
                    _cache.Remove(victim);
                }
            }
        }

        return results;
    }

    /// <summary>Oldest cache node, preferring a non-shared (Head / attire) role so
    /// shared body-part parses survive one-shot churn; falls back to the oldest
    /// overall when every entry is a shared part. Called under <c>_cacheLock</c>.</summary>
    private LinkedListNode<NifCacheEntry> OldestEvictable()
    {
        for (var node = _cache.Last; node != null; node = node.Previous)
            if (!IsSharedBodyPart(node.Value.BipedBodyPart)) return node;
        return _cache.Last!;
    }

    /// <summary>Body parts whose NIFs are shared across NPCs (same mesh reused), so
    /// they're worth protecting in the parse cache. Head is per-NPC FaceGen and
    /// attire (null body part) is diverse, so both are one-shot and evicted first.</summary>
    private static bool IsSharedBodyPart(string? bipedBodyPart) => bipedBodyPart switch
    {
        "Body" or "Hands" or "Feet" or "Hair" or "Tail" => true,
        _ => false,
    };

    private List<BuiltMesh>? TryGetFromCache(string nifPath, string? skeletonPath,
        long nifMTime, long skelMTime, string? bipedBodyPart)
    {
        string? samePathDiff = null; // first same-path-but-different-key reason (diag)
        lock (_cacheLock)
        {
            for (var node = _cache.First; node != null; node = node.Next)
            {
                var e = node.Value;
                if (!string.Equals(e.NifPath, nifPath, StringComparison.OrdinalIgnoreCase)) continue;
                bool keyMatch =
                    string.Equals(e.BipedBodyPart, bipedBodyPart, StringComparison.Ordinal)
                    && e.NifMTimeTicks == nifMTime
                    && string.Equals(e.SkeletonPath, skeletonPath, StringComparison.OrdinalIgnoreCase)
                    && e.SkeletonMTimeTicks == skelMTime;
                if (keyMatch)
                {
                    // LRU touch
                    _cache.Remove(node);
                    _cache.AddFirst(node);
                    if (_cacheDiag) System.Threading.Interlocked.Increment(ref _diagHits);
                    return CloneBuiltMeshList(e.Meshes);
                }
                if (_cacheDiag && samePathDiff == null)
                {
                    samePathDiff =
                        (e.NifMTimeTicks != nifMTime ? $"nifMtime({e.NifMTimeTicks}!={nifMTime}) " : "") +
                        (!string.Equals(e.BipedBodyPart, bipedBodyPart, StringComparison.Ordinal)
                            ? $"bodyPart('{e.BipedBodyPart}'!='{bipedBodyPart}') " : "") +
                        (!string.Equals(e.SkeletonPath, skeletonPath, StringComparison.OrdinalIgnoreCase)
                            ? $"skelPath('{e.SkeletonPath}'!='{skeletonPath}') " : "") +
                        (e.SkeletonMTimeTicks != skelMTime ? $"skelMtime({e.SkeletonMTimeTicks}!={skelMTime}) " : "");
                }
            }
        }

        if (_cacheDiag)
        {
            System.Threading.Interlocked.Increment(ref _diagMisses);
            string fn = System.IO.Path.GetFileName(nifPath);
            CacheDiagLog(samePathDiff != null
                ? $"MISS [{fn}] part={bipedBodyPart} SAME-PATH-DIFF: {samePathDiff}"
                : $"MISS [{fn}] part={bipedBodyPart} no same-path entry (first-load or evicted)");
            long h = System.Threading.Interlocked.Read(ref _diagHits);
            long m = System.Threading.Interlocked.Read(ref _diagMisses);
            if ((h + m) % 50 == 0)
                CacheDiagLog($"--- totals: hits={h} misses={m} ({(h + m == 0 ? 0 : 100 * h / (h + m))}% hit) ---");
        }
        return null;
    }

    private static long TryGetMTime(string path)
    {
        try { return System.IO.File.GetLastWriteTimeUtc(path).Ticks; }
        catch { return 0; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SURVEY (read-only NIF metadata for diagnostic / coverage tools)
    //
    //  Pulls the per-shape signals our heuristics depend on (FaceGen-node
    //  presence, dismember partitions, NiAVObject flags, vertex/triangle
    //  counts, local-Z height, shader type, baked slot-0 diffuse, primary-
    //  head election outcome) without going through the build pipeline. No
    //  GL upload, no skinning, no caching — every call re-reads the file.
    //  Used by the host's mesh-survey runner to aggregate data across many
    //  NPCs / mods so heuristic changes can be validated empirically rather
    //  than per-NPC.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>One row of <see cref="SurveyNif"/> output — the metadata for
    /// one shape inside a NIF.</summary>
    public sealed record NifSurveyShape(
        string ShapeName,
        uint Flags,
        bool HasDismember,
        IReadOnlyList<ushort> Partitions,
        int VertexCount,
        int TriangleCount,
        float LocalZHeight,
        int ShaderType,
        string? BakedDiffusePath,
        bool WouldBePrimaryHead);

    /// <summary>Result of <see cref="SurveyNif"/>. <see cref="LoadOk"/> is
    /// false when the file is missing / malformed; <see cref="Error"/>
    /// carries a short reason. Successful loads always populate
    /// <see cref="Shapes"/> (possibly empty if the NIF has no
    /// <c>NiShape</c> blocks).</summary>
    public sealed record NifSurveyResult(
        bool LoadOk,
        bool HasFaceGenNode,
        string? PrimaryHeadShapeName,
        IReadOnlyList<NifSurveyShape> Shapes,
        string? Error);

    /// <summary>Aggregate metadata for a single FaceGen NIF — totals across
    /// every shape, plus the file size on disk. Returned by
    /// <see cref="AnalyzeFaceGen"/>. <see cref="LoadOk"/> is false when the
    /// NIF could not be parsed; the file-size field is still populated in
    /// that case (it doesn't require a successful parse) so a host UI can
    /// still report size when polycount is unavailable.</summary>
    public readonly record struct FaceGenStats(
        bool LoadOk,
        int TotalVertices,
        int TotalTriangles,
        int ShapeCount,
        long FileSizeBytes);

    /// <summary>Reads a NIF from disk and returns aggregate vertex / triangle /
    /// shape totals plus the file size. When <paramref name="measureGeometry"/>
    /// is false, skips the NIF parse entirely and returns just the file size —
    /// this is the fast path when a host only wants size reporting and not
    /// polycount.
    /// <para>This is a synchronous CPU-bound method. The underlying
    /// <c>NifFile.Load</c> is a native call that won't observe a
    /// <see cref="System.Threading.CancellationToken"/>; callers wanting
    /// cancellability should run it inside <c>Task.Run</c> and check the
    /// token before / after the call.</para></summary>
    public FaceGenStats AnalyzeFaceGen(string nifPath, bool measureGeometry)
    {
        long size = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(nifPath) && System.IO.File.Exists(nifPath))
                size = new System.IO.FileInfo(nifPath).Length;
        }
        catch { /* best-effort */ }

        if (!measureGeometry || size == 0)
            return new FaceGenStats(size > 0, 0, 0, 0, size);

        var survey = SurveyNif(nifPath);
        if (!survey.LoadOk)
            return new FaceGenStats(false, 0, 0, 0, size);

        int verts = 0;
        int tris = 0;
        for (int i = 0; i < survey.Shapes.Count; i++)
        {
            verts += survey.Shapes[i].VertexCount;
            tris += survey.Shapes[i].TriangleCount;
        }
        return new FaceGenStats(true, verts, tris, survey.Shapes.Count, size);
    }

    /// <summary>Reads a NIF from disk and returns per-shape diagnostic
    /// metadata. Mirrors the data <see cref="BuildAllShapes"/> /
    /// <see cref="FindAccessoryOffsetAndPrimaryHead"/> consult to make
    /// decisions, so a host-side survey can record what those decisions
    /// will be without having to render the result.</summary>
    public NifSurveyResult SurveyNif(string nifPath)
    {
        if (string.IsNullOrWhiteSpace(nifPath) || !System.IO.File.Exists(nifPath))
            return new NifSurveyResult(false, false, null,
                Array.Empty<NifSurveyShape>(), "file not found: " + (nifPath ?? "(null)"));

        using var nif = new NifFile();
        try
        {
            if (nif.Load(nifPath) != 0)
                return new NifSurveyResult(false, false, null,
                    Array.Empty<NifSurveyShape>(), "NifFile.Load returned non-zero");
        }
        catch (Exception ex)
        {
            return new NifSurveyResult(false, false, null,
                Array.Empty<NifSurveyShape>(), "load threw: " + ex.Message);
        }

        bool hasFaceGen = DetectFaceGenNif(nif);

        using var shapes = nif.GetShapes();
        var header = nif.GetHeader();

        // Primary-head election only meaningful inside a FaceGen NIF —
        // matches the gating in BuildAllShapes.
        string? primaryHeadName = null;
        if (hasFaceGen && shapes.Count > 0)
        {
            try
            {
                var (_, name) = FindAccessoryOffsetAndPrimaryHead(nif, shapes);
                primaryHeadName = name;
            }
            catch (Exception ex)
            {
                // Survey is best-effort — if the head pre-pass throws,
                // continue with primary-head undetermined.
                System.Diagnostics.Debug.WriteLine(
                    "[SurveyNif] FindAccessoryOffsetAndPrimaryHead threw: " + ex.Message);
            }
        }

        var entries = new List<NifSurveyShape>(shapes.Count);
        for (int i = 0; i < shapes.Count; i++)
        {
            try { entries.Add(BuildSurveyShape(nif, header, shapes[i], primaryHeadName)); }
            catch (Exception ex)
            {
                // Per-shape failure shouldn't abort the survey.
                entries.Add(new NifSurveyShape(
                    "?error: " + ex.Message, 0u, false, Array.Empty<ushort>(),
                    0, 0, 0f, -1, null, false));
            }
        }

        return new NifSurveyResult(true, hasFaceGen, primaryHeadName, entries, null);
    }

    private static NifSurveyShape BuildSurveyShape(NifFile nif, NiHeader header,
        NiShape shape, string? primaryHeadName)
    {
        string shapeName = shape.name?.get() ?? "?";
        uint flags = shape.flags;

        var partitions = ReadDismemberPartitions(header, shape);
        bool hasDismember = partitions != null;
        IReadOnlyList<ushort> partitionList = partitions ?? Array.Empty<ushort>();

        // Vertex count + local Z extent (the height heuristic the primary-
        // head election uses).
        int vCount = 0;
        float localZ = 0f;
        using (var verts = nif.GetVertsForShape(shape))
        {
            if (verts != null && verts.Count > 0)
            {
                vCount = verts.Count;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int j = 0; j < verts.Count; j++)
                {
                    float z = verts[j].z;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
                localZ = maxZ - minZ;
            }
        }

        // Triangle count
        int tCount = 0;
        using (var nifTris = new vectorTriangle())
        {
            if (shape.GetTriangles(nifTris)) tCount = nifTris.Count;
        }

        // Shader type (BSLightingShaderProperty.bslspShaderType, e.g. 5 =
        // BSLSP_SKINTINT for face shapes). -1 when no BSLSP attached.
        int shaderType = -1;
        var shaderRef = shape.ShaderPropertyRef();
        if (shaderRef != null && !shaderRef.IsEmpty())
        {
            try
            {
                NiObject shaderObj = header.GetBlockById(shaderRef.index);
                if (shaderObj is BSLightingShaderProperty bslsp)
                    shaderType = (int)bslsp.bslspShaderType;
            }
            catch { /* best-effort */ }
        }

        // Slot-0 diffuse texture path, as baked into the NIF (TXST overrides
        // applied later in the build pipeline aren't part of the NIF data
        // and so don't appear here — that's intentional, the survey is
        // about NIF authoring).
        string? diffuse = null;
        try
        {
            string slot0 = nif.GetTexturePathByIndex(shape, 0);
            if (!string.IsNullOrWhiteSpace(slot0)) diffuse = slot0;
        }
        catch { /* best-effort */ }

        bool wouldBePrimaryHead = primaryHeadName != null && shapeName == primaryHeadName;

        return new NifSurveyShape(
            shapeName, flags, hasDismember, partitionList,
            vCount, tCount, localZ, shaderType, diffuse, wouldBePrimaryHead);
    }

    private static List<BuiltMesh> CloneBuiltMeshList(List<BuiltMesh> source)
    {
        var copy = new List<BuiltMesh>(source.Count);
        for (int i = 0; i < source.Count; i++) copy.Add(CloneBuiltMesh(source[i]));
        return copy;
    }

    /// <summary>
    /// Shallow-clones shared read-only data (Indices, TexturePaths, Skinning,
    /// shader flags) and deep-clones the vertex arrays that BlendWeightMorph
    /// mutates in place.
    /// </summary>
    private static BuiltMesh CloneBuiltMesh(BuiltMesh b) => new()
    {
        Positions = (Vector3[])b.Positions.Clone(),
        Normals = (Vector3[])b.Normals.Clone(),
        Indices = b.Indices,
        TextureCoordinates = b.TextureCoordinates,
        Tangents = (Vector3[])b.Tangents.Clone(),
        Bitangents = (Vector3[])b.Bitangents.Clone(),
        ShapeName = b.ShapeName,
        TexturePaths = b.TexturePaths,
        IsModelSpaceNormals = b.IsModelSpaceNormals,
        IsHairTintShader = b.IsHairTintShader,
        HairTintColor = b.HairTintColor,
        BindPosePositions = b.BindPosePositions != null ? (Vector3[])b.BindPosePositions.Clone() : null,
        BindPoseNormals = b.BindPoseNormals != null ? (Vector3[])b.BindPoseNormals.Clone() : null,
        Weight0BindPosePositions = b.Weight0BindPosePositions != null ? (Vector3[])b.Weight0BindPosePositions.Clone() : null,
        Weight1BindPosePositions = b.Weight1BindPosePositions != null ? (Vector3[])b.Weight1BindPosePositions.Clone() : null,
        Skinning = b.Skinning,
        UnresolvedSkinBones = b.UnresolvedSkinBones,
        BonesAbsentFromSkeleton = b.BonesAbsentFromSkeleton,
        IsPrimaryHeadShape = b.IsPrimaryHeadShape,
        DismemberPartitions = b.DismemberPartitions,
        HasAlphaTest = b.HasAlphaTest,
        HasAlphaBlend = b.HasAlphaBlend,
        ZBufferWrite = b.ZBufferWrite,
        MaterialAlpha = b.MaterialAlpha,
        AlphaThreshold = b.AlphaThreshold,
        SrcBlendIndex = b.SrcBlendIndex,
        DstBlendIndex = b.DstBlendIndex,
        IsDoubleSided = b.IsDoubleSided,
        HasGreyscaleToPaletteFlag = b.HasGreyscaleToPaletteFlag,
        Glossiness = b.Glossiness,
        SpecularStrength = b.SpecularStrength,
        SpecularColor = b.SpecularColor,
        SubsurfaceRolloff = b.SubsurfaceRolloff,
        GreyscaleToPaletteScale = b.GreyscaleToPaletteScale,
        RimlightPower = b.RimlightPower,
        HasVertexColors = b.HasVertexColors,
        EmissiveColor = b.EmissiveColor,
        EmissiveMultiple = b.EmissiveMultiple,
        UvScale = b.UvScale,
        UvOffset = b.UvOffset,
        EnvironmentMapScale = b.EnvironmentMapScale,
        EyeCubemapScale = b.EyeCubemapScale,
        VertexColors = b.VertexColors,
        ShaderFlags1 = b.ShaderFlags1,
        ShaderFlags2 = b.ShaderFlags2,
        ShaderType = b.ShaderType,
        SkinTintAlpha = b.SkinTintAlpha,
    };

    /// <summary>
    /// Loads all renderable shapes from an already-open NifFile.
    /// The caller owns the NifFile lifetime.
    /// </summary>
    public List<BuiltMesh> BuildFromNif(NifFile nif, NifFile? skeletonNif = null)
    {
        // Path-less / body-part-less entry: no slot context is available, so
        // the dismember-partition filter is disabled (every shape builds).
        return BuildAllShapes(nif, skeletonNif, bipedBodyPart: null);
    }

    /// <summary>
    /// Shared implementation: finds the primary head shape (if any), computes accessory
    /// offset transforms, and builds all shapes with correct positioning.
    /// </summary>
    private List<BuiltMesh> BuildAllShapes(NifFile nif, NifFile? skeletonNif, string? bipedBodyPart,
        System.Threading.CancellationToken ct = default)
    {
        var results = new List<BuiltMesh>();
        using var shapes = nif.GetShapes();
        if (shapes.Count == 0) return results;

        // --- Pre-pass: Find the primary head shape for accessory positioning ---
        // FaceGen NIFs contain a main face mesh plus accessories (brow, eyes, mouth, scars)
        // that may have identity transforms with vertices near the origin. We detect the
        // primary head (tallest mesh among head-partition shapes) and use its global transform
        // to correctly position accessories that would otherwise appear at the feet.
        // Detection is gated on the presence of a BSFaceGenNiNodeSkinned block — body /
        // hands / feet NIFs sometimes bundle placeholder head shapes (ChildHead in
        // ChildFeet.nif) and we don't want those flagged primary-head; routing them
        // through ApplyTexturesToGlMesh's FaceTint-blend branch turns the rendered
        // face dark.
        bool isFaceGenNif = DetectFaceGenNif(nif);
        LogVerbose("CharacterViewer: NIF FaceGen detection: BSFaceGenNiNodeSkinned "
            + (isFaceGenNif ? "found → primary-head detection ENABLED"
                            : "absent → primary-head detection skipped"));
        var (accessoryOffset, primaryHeadName) = isFaceGenNif
            ? FindAccessoryOffsetAndPrimaryHead(nif, shapes)
            : (null, null);

        // --- Pre-pass: dismember-partition filter ---
        // Vanilla child body NIFs (ChildFeet.nif) bundle placeholder head /
        // mouth / eye shapes alongside the actual feet. The Skyrim engine
        // appears to ignore them at render time by treating each ARMA's
        // WorldModel NIF as the source for ONE biped slot — and skipping
        // shapes whose dismember partitions don't include that slot. NifSkope
        // does NOT do this (its source filters only on the AppCulled flag),
        // so a standalone NIF view shows the placeholders too — but the in-
        // game engine doesn't render them. Mirroring the engine here removes
        // the body-textures-on-face artifact for child NPCs.
        ushort? expectedBipedSlot = GetExpectedBipedSlot(bipedBodyPart);
        var skipped = expectedBipedSlot.HasValue ? new HashSet<int>() : null;
        if (expectedBipedSlot.HasValue)
        {
            var header = nif.GetHeader();
            for (int si = 0; si < shapes.Count; si++)
            {
                var shape = shapes[si];
                var partitions = ReadDismemberPartitions(header, shape);
                if (partitions == null) continue; // No dismember → no filter info, keep
                if (partitions.Count == 0) continue; // Empty list → keep
                if (!partitions.Contains(expectedBipedSlot.Value))
                {
                    skipped!.Add(si);
                    LogVerbose("CharacterViewer: [BipedFilter] Skipping '"
                        + (shape.name?.get() ?? "?")
                        + "' partitions=[" + string.Join(",", partitions)
                        + "] — slot " + expectedBipedSlot.Value
                        + " (" + bipedBodyPart + ") not present");
                }
            }
        }

        for (int si = 0; si < shapes.Count; si++)
        {
            // Per-shape build (parse + CPU skinning) is the heavy unit for a
            // high-poly head NIF — the dominant uncached per-NPC cost. Check
            // between shapes so a host cancel doesn't have to finish the whole
            // NIF. Default token (cached/preview paths) never cancels.
            ct.ThrowIfCancellationRequested();
            if (skipped != null && skipped.Contains(si)) continue;
            var shape = shapes[si];
            var built = BuildShape(nif, shape, accessoryOffset, skeletonNif, primaryHeadName);
            if (built != null)
                results.Add(built);
        }

        return results;
    }

    /// <summary>Returns the primary biped-slot ID for a body-part label, or
    /// null when no filtering should apply. Slots match the SSE biped-object
    /// enum: 32 = body / torso, 33 = hands, 37 = feet.
    ///
    /// <para>"Body" / "Hands" / "Feet" filter against vanilla child meshes
    /// that bundle placeholder shapes (ChildFeet.nif → ChildHead, EyesChild,
    /// MouthChild, BODY, Wrists). A 250-NPC mesh survey across the user's
    /// mod library showed every Body shape carries 32, every Hands shape
    /// carries 33, every Feet shape carries 37 — the filter has zero false
    /// positives in the corpus.</para>
    ///
    /// <para>"Head" / "Hair" / "Tail" return null. The same survey found
    /// 58 face accessories inside FaceGen NIFs (FemaleMouthHumanoidDefault,
    /// KWA_FemaleBrows, KWA_FemaleEyesHuman, eyelashes, eyeshadow…) authored
    /// with partition [32] — the body slot — despite being legitimate
    /// head-region geometry. 5 hair-region shapes and 2 tail-region shapes
    /// in their respective NIFs likewise carry [32]. Filtering these body
    /// parts by partition would cull legitimate accessories, so they're
    /// exempt. Dismember-partition values are not a reliable indicator of
    /// shape role across modder authoring conventions for the head / hair /
    /// tail regions.</para>
    ///
    /// <para>Mirrors the engine's behavior of treating each ARMA's
    /// WorldModel NIF as the source for one biped slot only. The precise
    /// engine mechanism is undocumented but the rule is consistent with
    /// observed in-game behavior on test NPCs (Dorthe / vanilla child).</para>
    ///
    /// <para>Public to let host-side diagnostic tools (e.g. the mesh-survey
    /// runner) compute the expected filter outcome without re-defining the
    /// slot mapping. See <c>RENDERING_PIPELINE.md → Dismember partitions
    /// and shape filtering</c> for the full survey breakdown.</para></summary>
    public static ushort? GetExpectedBipedSlot(string? bodyPart) => bodyPart switch
    {
        "Body" => 32,
        "Hands" => 33,
        "Feet" => 37,
        _ => null,
    };

    /// <summary>Reads the dismember-partition ID list for a shape. Returns
    /// null when the shape has no <see cref="BSDismemberSkinInstance"/> at
    /// all (an unskinned decoration, or a shape using plain
    /// <c>NiSkinInstance</c>) — callers treat that as "no filter info,
    /// keep the shape." Returns an empty list when the dismember instance
    /// exists but has no partitions, which is also treated as keep.</summary>
    private static IReadOnlyList<ushort>? ReadDismemberPartitions(NiHeader header, NiShape shape)
    {
        var skinRef = shape.SkinInstanceRef();
        if (skinRef == null || skinRef.IsEmpty()) return null;
        NiObject skinObj = header.GetBlockById(skinRef.index);
        if (skinObj is not BSDismemberSkinInstance dismember) return null;
        using var partitions = dismember.partitions;
        if (partitions == null) return Array.Empty<ushort>();
        using var items = partitions.items();
        var ids = new List<ushort>(items.Count);
        for (int pi = 0; pi < items.Count; pi++)
            ids.Add(items[pi].partID);
        return ids;
    }

    /// <summary>
    /// Head dismember partition IDs: SSE's SBP_30_HEAD / SBP_130_HEAD /
    /// SBP_230_HEAD plus the legacy Oblivion-era BP_HEAD = 1. Bethesda's
    /// vanilla child face meshes (MaleHeadChild, ChildHead) ship with the
    /// legacy partition list [1, 0] and never got migrated to the SSE
    /// numbering — NifSkope still labels partition 1 as "BP_HEAD" via the
    /// shared body-part enum, so this is the authoritative signal rather
    /// than a name heuristic.
    /// </summary>
    private static bool IsHeadDismemberPartition(ushort partId)
    {
        return partId == 30 || partId == 130 || partId == 230 || partId == 1;
    }

    /// <summary>Collapses a dismember-partition ID onto the canonical biped-slot
    /// numbering (30-61), folding the SSE duplicate 130-161 / 230-261 ranges back
    /// down (e.g. 131 → 31 hair, 130/230 → 30 head, 143 → 43 ears) and mapping the
    /// legacy Oblivion <c>BP_HEAD = 1</c> to 30. IDs outside those ranges are
    /// returned unchanged for the caller to filter.</summary>
    public static int PartitionToBipedSlot(ushort partId) => partId switch
    {
        1 => 30,                          // legacy BP_HEAD
        >= 230 and <= 261 => partId - 200,
        >= 130 and <= 161 => partId - 100,
        _ => partId,
    };

    /// <summary>True when this NIF contains a node named
    /// <c>BSFaceGenNiNodeSkinned</c>. SSE FaceGen NIFs (the per-NPC
    /// <c>…\FaceGenData\FaceGeom\&lt;plugin&gt;\&lt;formid&gt;.nif</c> files)
    /// always carry one as a child of the root <c>BSFadeNode</c>; vanilla
    /// body / hands / feet NIFs do not. Scoping primary-head detection to
    /// this signal prevents the placeholder <c>ChildHead</c> shape that
    /// Bethesda bundles into <c>ChildFeet.nif</c> (with the same
    /// <c>partitions=[1,0]</c> as the FaceGen face mesh) from being flagged
    /// primary head — which would route FaceTint-blend onto the body's
    /// slot-0 diffuse and turn the rendered face dark.
    /// <para>The block's <i>type</i> is <c>NiNode</c> (a generic node
    /// class); the FaceGen-specific marker lives in the block's
    /// <see cref="NiObjectNET.name"/> field, so we read that instead of
    /// <c>GetBlockTypeStringById</c>.</para></summary>
    private static bool DetectFaceGenNif(NifFile nif)
    {
        try
        {
            var header = nif.GetHeader();
            uint n = header.GetNumBlocks();
            for (uint i = 0; i < n; i++)
            {
                NiObject? blk = null;
                try { blk = header.GetBlockById(i); } catch { continue; }
                if (blk is NiObjectNET named && named.name != null)
                {
                    string? raw = null;
                    try { raw = named.name.get(); } catch { }
                    if (raw == "BSFaceGenNiNodeSkinned") return true;
                }
            }
        }
        catch { /* niflib errors fall through as "not FaceGen" */ }
        return false;
    }

    /// <summary>
    /// Walks the NIF scene graph from an object up to the root, composing transforms
    /// to get the object's transform in NIF root space (Z-up).
    /// Equivalent to NPC Portrait Creator's GetAVObjectTransformToGlobal.
    /// </summary>
    private MatTransform GetTransformToGlobal(NifFile nif, NiAVObject obj, string? shapeName = null)
    {
        // GetTransformToParent returns a non-owning wrapper (cMemoryOwn=false),
        // ComposeTransforms returns an owning copy (cMemoryOwn=true).
        // Intermediate objects are small and will be cleaned up by the GC finalizer.
        var result = obj.GetTransformToParent();
        var parent = nif.GetParentNode(obj);

        if (shapeName != null)
        {
            LogVerbose("CharacterViewer: [Transform Chain] '" + shapeName +
                "' local: T=(" + result.translation.x.ToString("F2") + ", " +
                result.translation.y.ToString("F2") + ", " + result.translation.z.ToString("F2") +
                ") S=" + result.scale.ToString("F3") +
                " rotIdentity=" + result.rotation.IsIdentity());
        }

        while (parent != null)
        {
            var parentXform = parent.GetTransformToParent();
            if (shapeName != null)
            {
                string parentName = (parent as NiAVObject)?.name?.get() ?? "?";
                LogVerbose("CharacterViewer: [Transform Chain] '" + shapeName +
                    "' parent '" + parentName +
                    "': T=(" + parentXform.translation.x.ToString("F2") + ", " +
                    parentXform.translation.y.ToString("F2") + ", " + parentXform.translation.z.ToString("F2") +
                    ") S=" + parentXform.scale.ToString("F3") +
                    " rotIdentity=" + parentXform.rotation.IsIdentity());
            }
            result = parentXform.ComposeTransforms(result);
            parent = nif.GetParentNode(parent);
        }

        if (shapeName != null)
        {
            LogVerbose("CharacterViewer: [Transform Chain] '" + shapeName +
                "' composed global: T=(" + result.translation.x.ToString("F2") + ", " +
                result.translation.y.ToString("F2") + ", " + result.translation.z.ToString("F2") +
                ") S=" + result.scale.ToString("F3"));
        }

        return result;
    }

    /// <summary>
    /// Pre-pass to find the primary head shape's global transform for accessory positioning.
    /// Also returns the primary head shape's name for tagging in BuiltMesh.
    /// Returns null transform if no head shapes are found (e.g. body NIFs).
    /// </summary>
    private (MatTransform? offset, string? primaryHeadName) FindAccessoryOffsetAndPrimaryHead(NifFile nif, vectorNiShape shapes)
    {
        NiHeader header = nif.GetHeader();

        // Collect head-partition candidate shapes with their local Z extent (height)
        NiShape? primaryHead = null;
        float primaryHeadHeight = -1f;

        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var skinRef = shape.SkinInstanceRef();
            if (skinRef == null || skinRef.IsEmpty()) continue;

            NiObject skinObj = header.GetBlockById(skinRef.index);
            if (skinObj is not BSDismemberSkinInstance dismember) continue;

            // Check if any partition is a head partition
            bool isHeadCandidate = false;
            var partIdList = new List<ushort>();
            using var partitions = dismember.partitions;
            if (partitions != null)
            {
                using var items = partitions.items();
                for (int pi = 0; pi < items.Count; pi++)
                {
                    partIdList.Add(items[pi].partID);
                    if (IsHeadDismemberPartition(items[pi].partID))
                    {
                        isHeadCandidate = true;
                    }
                }
            }

            string sName = shape.name?.get() ?? "?";
            LogVerbose("CharacterViewer: [Skinning] Shape '" + sName +
                "' partitions=[" + string.Join(",", partIdList) +
                "] isHeadCandidate=" + isHeadCandidate);

            if (!isHeadCandidate) continue;

            // Measure local Z extent to find the tallest (primary) head shape
            using var verts = nif.GetVertsForShape(shape);
            if (verts == null || verts.Count == 0) continue;

            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int vi = 0; vi < verts.Count; vi++)
            {
                float z = verts[vi].z;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            float height = maxZ - minZ;
            if (height > primaryHeadHeight)
            {
                primaryHeadHeight = height;
                primaryHead = shape;
            }
        }

        if (primaryHead == null) return (null, null);

        string headName = primaryHead.name?.get() ?? "?";
        LogVerbose("CharacterViewer: Primary head shape identified: '" +
            headName + "' (height=" + primaryHeadHeight.ToString("F1") + ")");

        var offset = GetTransformToGlobal(nif, primaryHead, headName + " [primary head]");
        LogVerbose("CharacterViewer: Accessory offset transform: T=(" +
            offset.translation.x.ToString("F2") + ", " +
            offset.translation.y.ToString("F2") + ", " + offset.translation.z.ToString("F2") +
            ") S=" + offset.scale.ToString("F3"));
        return (offset, headName);
    }

    private BuiltMesh? BuildShape(NifFile nif, NiShape shape, MatTransform? accessoryOffset,
        NifFile? skeletonNif = null, string? primaryHeadName = null)
    {
        // Skip non-renderable scaffold geometry. UBE head NIFs ship "*_Dummy" lens
        // shapes positioned over the eyes as decal anchors; without this skip they
        // upload as untextured opaque quads and occlude the iris.
        if ((shape.flags & 1u) != 0)
        {
            LogVerbose("CharacterViewer: Skipping hidden shape '" +
                (shape.name?.get() ?? "?") + "' (NiAVObject AppCulled flag set)");
            return null;
        }
        var preShaderRef = shape.ShaderPropertyRef();
        if (preShaderRef == null || preShaderRef.IsEmpty())
        {
            LogVerbose("CharacterViewer: Skipping shape '" +
                (shape.name?.get() ?? "?") + "' (no shader property attached)");
            return null;
        }

        // SMP hair physics-collision meshes (e.g. "UpperCollision" in
        // EvelynnHair_1.nif) carry a real BSLightingShaderProperty but bind
        // zero textures and have no NiAlphaProperty. Without this skip they
        // upload as opaque untextured geometry that lights up white where it
        // faces the camera and goes black on the back-side, producing the
        // "splotches over the chest/neck" symptom. Name-based filtering was
        // rejected because mod authors freely choose collision-shape names.
        string preDiffuse = nif.GetTexturePathByIndex(shape, 0);
        string preNormal = nif.GetTexturePathByIndex(shape, 1);
        if (string.IsNullOrWhiteSpace(preDiffuse)
            && string.IsNullOrWhiteSpace(preNormal)
            && !shape.HasAlphaProperty())
        {
            LogVerbose("CharacterViewer: Skipping shape '" +
                (shape.name?.get() ?? "?") +
                "' (no diffuse/normal and no alpha property - likely physics/collision mesh)");
            return null;
        }

        // Geometry marshaling + copy span starts here (split-timing; see _threadGeomTicks).
        long geomStart = System.Diagnostics.Stopwatch.GetTimestamp();
        long shapeSkinTicks = 0;

        // Extract vertices
        using var nifVerts = nif.GetVertsForShape(shape);
        if (nifVerts == null || nifVerts.Count == 0)
            return null;

        // Extract triangles
        using var nifTris = new vectorTriangle();
        if (!shape.GetTriangles(nifTris) || nifTris.Count == 0)
            return null;

        int vertCount = nifVerts.Count;

        // Extract normals (may be null)
        using var nifNormals = nif.GetNormalsForShape(shape);

        // Extract UVs (may be null)
        using var nifUvs = nif.GetUvsForShape(shape);

        // --- CPU Skinning or Shape Transform ---
        // When a skeleton NIF is provided, CPU-side bone-weight skinning positions
        // vertices using the real skeleton's bone transforms. This closes the neck gap
        // between head and body meshes. Without a skeleton, we fall back to the shape
        // transform heuristic (ComputeEffectiveTransform).
        SkinningInfo? skinning = null;
        Vector3[]? bindPosePositionsYUp = null;
        Vector3[]? bindPoseNormalsYUp = null;
        float[]? skinnedPosX = null, skinnedPosY = null, skinnedPosZ = null;
        float[]? skinnedNrmX = null, skinnedNrmY = null, skinnedNrmZ = null;

        MatTransform? shapeTransform = null;
        bool hasTransform = false;
        List<string>? unresolvedBones = null;
        List<string>? bonesAbsentFromSkeleton = null;

        // Skinning / shape-transform pass — timed separately from geometry marshaling so
        // the native-bulk-copy decision can tell apart what the helper fixes (geom copy)
        // from what it doesn't (this block). Mutually exclusive branches.
        long skinStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (skeletonNif != null && shape.HasSkinInstance())
        {
            skinning = TryApplyCpuSkinning(nif, shape, nifVerts, nifNormals, vertCount,
                skeletonNif,
                out skinnedPosX, out skinnedPosY, out skinnedPosZ,
                out skinnedNrmX, out skinnedNrmY, out skinnedNrmZ,
                out unresolvedBones, out bonesAbsentFromSkeleton);
        }

        if (skinning == null)
        {
            // Fallback: use shape transform heuristic for unskinned shapes or when no skeleton
            shapeTransform = ComputeEffectiveTransform(nif, shape, accessoryOffset);
            hasTransform = shapeTransform != null
                            && (shapeTransform.scale != 1.0f
                                || !shapeTransform.rotation.IsIdentity()
                                || !IsZeroTranslation(shapeTransform.translation));
        }
        shapeSkinTicks = System.Diagnostics.Stopwatch.GetTimestamp() - skinStart;

        // Build positions — skinned or transform-based, then convert Z-up → Y-up
        var positions = new Vector3[vertCount];
        if (skinning != null)
        {
            bindPosePositionsYUp = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                var v = nifVerts[i];
                bindPosePositionsYUp[i] = new Vector3(v.x, v.z, -v.y);
                positions[i] = new Vector3(skinnedPosX![i], skinnedPosZ![i], -skinnedPosY![i]);
            }
        }
        else
        {
            for (int i = 0; i < vertCount; i++)
            {
                var v = nifVerts[i];
                float px = v.x, py = v.y, pz = v.z;
                if (hasTransform)
                    ApplyTransform(shapeTransform!, px, py, pz, out px, out py, out pz);
                positions[i] = new Vector3(px, pz, -py);
            }
        }

        // Build normals
        var normals = new Vector3[vertCount];
        if (skinning != null && skinnedNrmX != null)
        {
            bindPoseNormalsYUp = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                if (nifNormals != null && nifNormals.Count == vertCount)
                {
                    var n = nifNormals[i];
                    bindPoseNormalsYUp[i] = new Vector3(n.x, n.z, -n.y);
                }
                normals[i] = new Vector3(skinnedNrmX[i], skinnedNrmZ![i], -skinnedNrmY![i]);
            }
        }
        else if (nifNormals != null && nifNormals.Count == vertCount)
        {
            bool hasRotation = hasTransform && !shapeTransform!.rotation.IsIdentity();
            for (int i = 0; i < vertCount; i++)
            {
                var n = nifNormals[i];
                float nx = n.x, ny = n.y, nz = n.z;
                if (hasRotation)
                    ApplyRotation(shapeTransform!.rotation, nx, ny, nz, out nx, out ny, out nz);
                normals[i] = new Vector3(nx, nz, -ny);
            }
        }

        // Build UVs
        var uvs = new Vector2[vertCount];
        if (nifUvs != null && nifUvs.Count == vertCount)
        {
            for (int i = 0; i < vertCount; i++)
            {
                var uv = nifUvs[i];
                uvs[i] = new Vector2(uv.u, uv.v);
            }
        }

        // Extract vertex colors from NIF (RGBA, 0–1 range)
        Vector4[]? vertexColors = null;
        bool hasVertexColors = false;
        try
        {
            using var nifColors = new vectorColor4();
            if (nif.GetColorsForShape(shape, nifColors) && nifColors.Count == vertCount)
            {
                hasVertexColors = true;
                vertexColors = new Vector4[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    var c = nifColors[i];
                    vertexColors[i] = new Vector4(c.r, c.g, c.b, c.a);
                }
            }
        }
        catch { /* Shape has no vertex colors */ }

        // Diagnostic: vertex-color statistics. Useful for diagnosing
        // face/body seam issues where vanilla NPCs have vertex colors on
        // their face mesh but mod replacers don't — if the vanilla colors
        // are systematically off-(1,1,1,1) they'd modulate the diffuse in
        // a way replacer faces miss. Logged per-shape under verbose mode.
        if (hasVertexColors && vertexColors != null && vertexColors.Length > 0)
        {
            float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            float minR = float.MaxValue, minG = float.MaxValue, minB = float.MaxValue, minA = float.MaxValue;
            float maxR = float.MinValue, maxG = float.MinValue, maxB = float.MinValue, maxA = float.MinValue;
            int nonWhiteCount = 0;
            foreach (var v in vertexColors)
            {
                sumR += v.X; sumG += v.Y; sumB += v.Z; sumA += v.W;
                if (v.X < minR) minR = v.X; if (v.X > maxR) maxR = v.X;
                if (v.Y < minG) minG = v.Y; if (v.Y > maxG) maxG = v.Y;
                if (v.Z < minB) minB = v.Z; if (v.Z > maxB) maxB = v.Z;
                if (v.W < minA) minA = v.W; if (v.W > maxA) maxA = v.W;
                if (v.X < 0.999f || v.Y < 0.999f || v.Z < 0.999f || v.W < 0.999f)
                    nonWhiteCount++;
            }
            int n = vertexColors.Length;
            string vcShapeName = shape.name?.get() ?? "?";
            LogVerbose("CharacterViewer: [VertexColor] '" + vcShapeName +
                "' n=" + n +
                " mean=(" + (sumR / n).ToString("F3") + "," + (sumG / n).ToString("F3") + "," + (sumB / n).ToString("F3") + "," + (sumA / n).ToString("F3") + ")" +
                " min=(" + minR.ToString("F3") + "," + minG.ToString("F3") + "," + minB.ToString("F3") + "," + minA.ToString("F3") + ")" +
                " max=(" + maxR.ToString("F3") + "," + maxG.ToString("F3") + "," + maxB.ToString("F3") + "," + maxA.ToString("F3") + ")" +
                " non-white=" + nonWhiteCount + "/" + n);
        }
        else
        {
            string vcShapeName = shape.name?.get() ?? "?";
            LogVerbose("CharacterViewer: [VertexColor] '" + vcShapeName +
                "' (no vertex color data; uploads (1,1,1,1) per vertex)");
        }

        // Build tangents and bitangents from NIF (Z-up → Y-up)
        var tangents = new Vector3[vertCount];
        var bitangents = new Vector3[vertCount];
        try
        {
            using var nifTangents = nif.GetTangentsForShape(shape);
            using var nifBitangents = nif.GetBitangentsForShape(shape);
            if (nifTangents != null && nifTangents.Count == vertCount &&
                nifBitangents != null && nifBitangents.Count == vertCount)
            {
                for (int i = 0; i < vertCount; i++)
                {
                    var t = nifTangents[i];
                    var b = nifBitangents[i];
                    // Z-up → Y-up conversion
                    tangents[i] = new Vector3(t.x, t.z, -t.y);
                    bitangents[i] = new Vector3(b.x, b.z, -b.y);
                }
            }
            else
            {
                // Compute tangents from positions/normals/UVs if NIF doesn't have them
                ComputeTangents(positions, normals, uvs, nifTris, tangents, bitangents);
            }
        }
        catch
        {
            ComputeTangents(positions, normals, uvs, nifTris, tangents, bitangents);
        }

        // Build triangle indices
        var indices = new int[nifTris.Count * 3];
        for (int i = 0; i < nifTris.Count; i++)
        {
            var tri = nifTris[i];
            indices[i * 3] = tri.p1;
            indices[i * 3 + 1] = tri.p2;
            indices[i * 3 + 2] = tri.p3;
        }

        // Close the geometry span. Geom = everything from GetVertsForShape through the
        // triangle-index loop MINUS the skin/transform pass nested inside it, i.e. the
        // pure geometry SWIG marshaling + per-element copy the native helper would remove.
        _threadSkinTicks += shapeSkinTicks;
        _threadGeomTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - geomStart) - shapeSkinTicks;

        // Extract texture paths from BSShaderTextureSet
        var texturePaths = new Dictionary<int, string>();
        for (uint slot = 0; slot < 9; slot++)
        {
            string texPath = nif.GetTexturePathByIndex(shape, slot);
            if (!string.IsNullOrWhiteSpace(texPath))
                texturePaths[(int)slot] = texPath;
        }

        // Extract shader flags and material properties
        bool isModelSpaceNormals = false;
        bool isHairTintShader = false;
        (float R, float G, float B)? hairTintColor = null;
        float glossiness = 80f;
        float specularStrength = 1f;
        float materialAlpha = 1f;
        Vector3 specularColor = Vector3.One;
        float subsurfaceRolloff = 0f;
        float greyscaleToPaletteScale = 1f;
        float rimlightPower = 2f;
        Vector3 emissiveColor = Vector3.Zero;
        float emissiveMultiple = 0f;
        Vector2 uvScale = Vector2.One;
        Vector2 uvOffset = Vector2.Zero;
        float environmentMapScale = 1f;
        float eyeCubemapScale = 1f;
        float skinTintAlpha = 0f;
        uint shaderFlags1 = 0, shaderFlags2 = 0, shaderType = 0;
        try
        {
            NiHeader header = nif.GetHeader();
            NiBlockRefNiShader shaderRef = shape.ShaderPropertyRef();
            if (shaderRef != null && !shaderRef.IsEmpty())
            {
                NiObject shaderObj = header.GetBlockById(shaderRef.index);
                if (shaderObj is BSLightingShaderProperty bslsp)
                {
                    shaderFlags1 = bslsp.shaderFlags1;
                    shaderFlags2 = bslsp.shaderFlags2;
                    shaderType = bslsp.bslspShaderType;
                    isModelSpaceNormals = (shaderFlags1 & SLSF1_ModelSpaceNormals) != 0;
                    glossiness = bslsp.glossiness;
                    specularStrength = bslsp.specularStrength;
                    try { skinTintAlpha = bslsp.skinTintAlpha; } catch { }
                    // Material alpha (< 1 = translucent); used alongside
                    // ZBuffer_Write to decide depth masking in the blend pass.
                    try { materialAlpha = bslsp.alpha; } catch { }

                    // Extract additional properties safely
                    try { subsurfaceRolloff = bslsp.subsurfaceRolloff; } catch { }
                    // niflysharp exposes this via the American spelling `grayscaleToPaletteScale`.
                    // For hair tint shapes the NIF typically stores a value >1 (often 2-4) that
                    // brightens the baked dark HCLR/hairTintColor back up to the actual in-game color.
                    // Without reading it here, hair renders far too dark.
                    try { greyscaleToPaletteScale = bslsp.grayscaleToPaletteScale; } catch { }
                    try { rimlightPower = bslsp.rimlightPower; } catch { }

                    // Specular color (RGB)
                    try
                    {
                        var sc = bslsp.specularColor;
                        if (sc != null)
                            specularColor = new Vector3(sc.x, sc.y, sc.z);
                    }
                    catch { }

                    // Emissive color and multiplier
                    try
                    {
                        var ec = bslsp.emissiveColor;
                        if (ec != null)
                            emissiveColor = new Vector3(ec.x, ec.y, ec.z);
                        emissiveMultiple = bslsp.emissiveMultiple;
                    }
                    catch { }

                    // UV scale and offset
                    try
                    {
                        var uvsRaw = bslsp.uvScale;
                        if (uvsRaw != null)
                            uvScale = new Vector2(uvsRaw.u, uvsRaw.v);
                        var uvoRaw = bslsp.uvOffset;
                        if (uvoRaw != null)
                            uvOffset = new Vector2(uvoRaw.u, uvoRaw.v);
                    }
                    catch { }

                    // Always-on diagnostic for non-default UV transforms.
                    // Hair scalp / buzz-cut shapes typically encode tiling
                    // values like (4, 4) or (8, 8) here so a fine noise
                    // texture reads as fine-detail hair. If a shape that
                    // should tile shows uvScale=(1.00, 1.00) in the log,
                    // the niflysharp accessor is returning the wrong value
                    // for that NIF and the texture renders as a single
                    // stretched copy (chunky / low-detail look).
                    if (uvScale.X != 1f || uvScale.Y != 1f ||
                        uvOffset.X != 0f || uvOffset.Y != 0f)
                    {
                        string sName = shape.name?.get() ?? "?";
                        string msg = $"[NifMeshBuilder] shape=[{sName}] uvScale=({uvScale.X:F2}, {uvScale.Y:F2}) uvOffset=({uvOffset.X:F2}, {uvOffset.Y:F2}) shaderType={shaderType}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        System.Diagnostics.Trace.WriteLine(msg);
                    }

                    // Environment map scale and eye cubemap scale
                    try { environmentMapScale = bslsp.environmentMapScale; } catch { }
                    try { eyeCubemapScale = bslsp.eyeCubemapScale; } catch { }

                    if (bslsp.bslspShaderType == (uint)BSLightingShaderPropertyShaderType.BSLSP_HAIRTINT)
                    {
                        isHairTintShader = true;
                        var tint = bslsp.hairTintColor;
                        if (tint != null)
                            hairTintColor = (tint.x, tint.y, tint.z);
                    }

                    // Decode relevant shader flags for readability
                    bool hasSpecular = (shaderFlags1 & (1u << 0)) != 0;
                    bool hasEnvMap = (shaderFlags1 & (1u << 7)) != 0;
                    bool hasFacegenDetail = (shaderFlags1 & (1u << 10)) != 0;
                    bool hasEyeEnvMap = (shaderFlags1 & (1u << 17)) != 0;
                    bool hasHairSoft = (shaderFlags1 & (1u << 18)) != 0;
                    bool hasOwnEmit = (shaderFlags1 & (1u << 22)) != 0;
                    bool hasDoubleSided = (shaderFlags2 & (1u << 4)) != 0;
                    bool hasVertColors = (shaderFlags2 & (1u << 5)) != 0;
                    bool hasSoftLight = (shaderFlags2 & (1u << 25)) != 0;
                    bool hasRimLight = (shaderFlags2 & (1u << 26)) != 0;
                    bool hasBackLight = (shaderFlags2 & (1u << 27)) != 0;

                    LogVerbose("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") + "'" +
                        " shaderType=" + bslsp.bslspShaderType +
                        " flags1=0x" + shaderFlags1.ToString("X8") +
                        " flags2=0x" + shaderFlags2.ToString("X8"));
                    LogVerbose("  Flags: MSN=" + isModelSpaceNormals +
                        " Specular=" + hasSpecular +
                        " EnvMap=" + hasEnvMap +
                        " FacegenDetail=" + hasFacegenDetail +
                        " EyeEnvMap=" + hasEyeEnvMap +
                        " HairSoft=" + hasHairSoft +
                        " OwnEmit=" + hasOwnEmit +
                        " DoubleSided=" + hasDoubleSided +
                        " VertColors=" + hasVertColors +
                        " SoftLight=" + hasSoftLight +
                        " RimLight=" + hasRimLight +
                        " BackLight=" + hasBackLight);
                    LogVerbose("  Material: gloss=" + glossiness.ToString("F0") +
                        " specStr=" + specularStrength.ToString("F2") +
                        " specColor=(" + specularColor.X.ToString("F2") + ", " + specularColor.Y.ToString("F2") + ", " + specularColor.Z.ToString("F2") + ")" +
                        " ssRolloff=" + subsurfaceRolloff.ToString("F2") +
                        " rimPow=" + rimlightPower.ToString("F2") +
                        " greyPalScale=" + greyscaleToPaletteScale.ToString("F2") +
                        " emissive=(" + emissiveColor.X.ToString("F2") + ", " + emissiveColor.Y.ToString("F2") + ", " + emissiveColor.Z.ToString("F2") + ")x" + emissiveMultiple.ToString("F2") +
                        " envScale=" + environmentMapScale.ToString("F2") +
                        " uvScale=(" + uvScale.X.ToString("F2") + ", " + uvScale.Y.ToString("F2") + ")" +
                        " uvOff=(" + uvOffset.X.ToString("F2") + ", " + uvOffset.Y.ToString("F2") + ")");
                    LogVerbose("  Textures: " + string.Join(", ",
                        texturePaths.OrderBy(kv => kv.Key).Select(kv => "[" + kv.Key + "]=" + System.IO.Path.GetFileName(kv.Value))));
                    if (isHairTintShader)
                        LogVerbose("  HairTint: " + (hairTintColor.HasValue
                            ? "(" + hairTintColor.Value.R.ToString("F3") + ", " + hairTintColor.Value.G.ToString("F3") + ", " + hairTintColor.Value.B.ToString("F3") + ")"
                            : "(no color)"));
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose("CharacterViewer: Could not read shader flags for shape '" +
                (shape.name?.get() ?? "?") + "': " + ex.Message);
        }

        // Read NiAlphaProperty for alpha test/blend (brow, hair, and other transparent shapes)
        bool hasAlphaTest = false;
        bool hasAlphaBlend = false;
        float alphaThreshold = 0f;
        // Default blend factors: SRC_ALPHA / INV_SRC_ALPHA (standard "over"
        // transparency). Used when there's no NiAlphaProperty or when the
        // shape isn't alpha-blended. The factors only matter for the alpha-
        // blend pass; opaque and alpha-test passes don't consult them.
        int srcBlendIndex = 6; // Bethesda enum: SRC_ALPHA
        int dstBlendIndex = 7; // Bethesda enum: INV_SRC_ALPHA
        try
        {
            if (shape.HasAlphaProperty())
            {
                NiHeader alphaHeader = nif.GetHeader();
                var alphaRef = shape.AlphaPropertyRef();
                if (alphaRef != null && !alphaRef.IsEmpty())
                {
                    NiObject alphaObj = alphaHeader.GetBlockById(alphaRef.index);
                    if (alphaObj is NiAlphaProperty alphaProp)
                    {
                        ushort flags = alphaProp.flags;
                        // Bit 0 of NiAlphaProperty flags = alpha blend enable
                        hasAlphaBlend = (flags & 1) != 0;
                        // Bit 9 of NiAlphaProperty flags = alpha test enable
                        hasAlphaTest = (flags & (1 << 9)) != 0;
                        // Bits 1-4 = SrcBlend, bits 5-8 = DstBlend (Bethesda
                        // enum values, mapped to GL factors at draw time).
                        // Honoring these per-mesh is what lets shapes with
                        // additive blend (e.g., UBE wet-eye outer cornea
                        // ships SrcBlend=SRC_ALPHA / DstBlend=ONE) render
                        // correctly without special-case shader hacks.
                        srcBlendIndex = (flags >> 1) & 0xF;
                        dstBlendIndex = (flags >> 5) & 0xF;
                        alphaThreshold = alphaProp.threshold / 255f;

                        // If alpha property exists but no flags set, default to alpha test
                        // (matches NPC Portrait Creator behavior)
                        if (!hasAlphaTest && !hasAlphaBlend)
                        {
                            hasAlphaTest = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose("CharacterViewer: Could not read NiAlphaProperty for shape '" +
                (shape.name?.get() ?? "?") + "': " + ex.Message);
        }

        // If normals are all zero (missing or unreadable), compute from geometry
        if (AreNormalsAllZero(normals))
        {
            string dbgName = shape.name?.get() ?? "?";
            LogVerbose("CharacterViewer: Normals all zero for shape '" +
                dbgName + "', computing from geometry");
            ComputeNormalsFromGeometry(positions, indices, normals);

            // TEMP DEBUG: dump position bbox, first 3 face normals, and final vert0 normal
            // to diagnose MSN body mesh lighting offset. Safe to remove once fixed.
            if (positions.Length > 0 && indices.Length >= 9)
            {
                float minX = positions[0].X, minY = positions[0].Y, minZ = positions[0].Z;
                float maxX = minX, maxY = minY, maxZ = minZ;
                for (int i = 1; i < positions.Length; i++)
                {
                    var p = positions[i];
                    if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                    if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                }
                LogVerbose(string.Format(
                    "CharacterViewer: [GEOM-NORMAL] '{0}' bbox X=[{1:F1},{2:F1}] Y=[{3:F1},{4:F1}] Z=[{5:F1},{6:F1}]",
                    dbgName, minX, maxX, minY, maxY, minZ, maxZ));

                for (int t = 0; t < 3; t++)
                {
                    int i0 = indices[t * 3], i1 = indices[t * 3 + 1], i2 = indices[t * 3 + 2];
                    var p0 = positions[i0]; var p1 = positions[i1]; var p2 = positions[i2];
                    var fn = Vector3.Cross(p1 - p0, p2 - p0);
                    float flen = fn.Length();
                    if (flen > 1e-6f) fn /= flen;
                    LogVerbose(string.Format(
                        "CharacterViewer: [GEOM-NORMAL] '{0}' tri{1} v0=({2:F1},{3:F1},{4:F1}) faceN=({5:F2},{6:F2},{7:F2})",
                        dbgName, t, p0.X, p0.Y, p0.Z, fn.X, fn.Y, fn.Z));
                }

                var n0 = normals[0];
                LogVerbose(string.Format(
                    "CharacterViewer: [GEOM-NORMAL] '{0}' vert0 finalN=({1:F3},{2:F3},{3:F3})",
                    dbgName, n0.X, n0.Y, n0.Z));
            }
        }

        string shapeName = shape.name?.get() ?? $"Shape_{positions.Length}v";

        // Capture this shape's dismember partitions so the slot-occupancy
        // resolver can tag baked-in head sub-shapes (e.g. hair = partition 131)
        // by their real biped slot rather than the coarse "Head" group slot.
        var dismemberPartitions = ReadDismemberPartitions(nif.GetHeader(), shape);

        bool isPrimaryHead = primaryHeadName != null && shapeName == primaryHeadName;
        bool isDoubleSided = (shaderFlags2 & SLSF2_DoubleSided) != 0;
        bool zBufferWrite = (shaderFlags2 & SLSF2_ZBufferWrite) != 0;
        LogVerbose("CharacterViewer: Built shape '" + shapeName +
            "': " + positions.Length + " verts, " + (indices.Length / 3) + " tris" +
            ", textures: [" + string.Join(", ", texturePaths.Keys) + "]" +
            ", MSN=" + isModelSpaceNormals +
            (hasAlphaTest ? ", alphaTest threshold=" + alphaThreshold.ToString("F2") : "") +
            (hasAlphaBlend ? ", alphaBlend" : "") +
            (hasAlphaBlend && !zBufferWrite ? ", noZWrite" : "") +
            (materialAlpha < 1f ? ", matAlpha=" + materialAlpha.ToString("F2") : "") +
            (isDoubleSided ? ", doubleSided" : "") +
            (isPrimaryHead ? ", PRIMARY_HEAD" : ""));

        // TEMP DEBUG: dump NIF-space and Y-up-converted normal of vertex 0
        // to diagnose a 90-degree normal orientation mismatch between the
        // body mesh and the armor meshes. Safe to remove once fixed.
        if (nifNormals != null && nifNormals.Count > 0 && normals.Length > 0)
        {
            var nifN0 = nifNormals[0];
            var yupN0 = normals[0];
            LogVerbose(string.Format(
                "CharacterViewer: [NORMAL-DEBUG] '{0}' vert0 nifN=({1:F3},{2:F3},{3:F3}) yupN=({4:F3},{5:F3},{6:F3}) skinned={7}",
                shapeName, nifN0.x, nifN0.y, nifN0.z,
                yupN0.X, yupN0.Y, yupN0.Z,
                skinning != null));
        }

        if (_logBoneDeltas)
        {
            LogShapeBoundsDiagnostic(nif, shape, skinning != null, positions, positions.Length);
        }

        return new BuiltMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            TextureCoordinates = uvs,
            Tangents = tangents,
            Bitangents = bitangents,
            ShapeName = shapeName,
            TexturePaths = texturePaths,
            IsModelSpaceNormals = isModelSpaceNormals,
            IsHairTintShader = isHairTintShader,
            HairTintColor = hairTintColor,
            BindPosePositions = bindPosePositionsYUp,
            BindPoseNormals = bindPoseNormalsYUp,
            Skinning = skinning,
            UnresolvedSkinBones = (unresolvedBones != null && unresolvedBones.Count > 0)
                ? unresolvedBones : null,
            BonesAbsentFromSkeleton = (bonesAbsentFromSkeleton != null && bonesAbsentFromSkeleton.Count > 0)
                ? bonesAbsentFromSkeleton : null,
            IsPrimaryHeadShape = isPrimaryHead,
            DismemberPartitions = dismemberPartitions,
            HasAlphaTest = hasAlphaTest,
            HasAlphaBlend = hasAlphaBlend,
            ZBufferWrite = zBufferWrite,
            MaterialAlpha = materialAlpha,
            AlphaThreshold = alphaThreshold,
            SrcBlendIndex = srcBlendIndex,
            DstBlendIndex = dstBlendIndex,
            IsDoubleSided = isDoubleSided,
            HasGreyscaleToPaletteFlag = (shaderFlags1 & SLSF1_GreyscaleToPalette) != 0,
            Glossiness = glossiness,
            SpecularStrength = specularStrength,
            SpecularColor = specularColor,
            SubsurfaceRolloff = subsurfaceRolloff,
            GreyscaleToPaletteScale = greyscaleToPaletteScale,
            RimlightPower = rimlightPower,
            HasVertexColors = hasVertexColors,
            VertexColors = vertexColors,
            EmissiveColor = emissiveColor,
            EmissiveMultiple = emissiveMultiple,
            UvScale = uvScale,
            UvOffset = uvOffset,
            EnvironmentMapScale = environmentMapScale,
            EyeCubemapScale = eyeCubemapScale,
            ShaderFlags1 = shaderFlags1,
            ShaderFlags2 = shaderFlags2,
            ShaderType = shaderType,
            SkinTintAlpha = skinTintAlpha,
        };
    }

    /// <summary>
    /// Computes the effective transform for a shape, applying the accessory positioning
    /// heuristic from NPC Portrait Creator. For shapes whose composed global transform
    /// has near-zero translation (accessories like brow, eyes, mouth in FaceGen NIFs),
    /// the primary head's global transform is used instead.
    /// </summary>
    private MatTransform ComputeEffectiveTransform(NifFile nif, NiShape shape, MatTransform? accessoryOffset)
    {
        string shapeName = shape.name?.get() ?? "?";

        // Log vertex bounds in local space (before any transform) for diagnostics
        using var diagVerts = nif.GetVertsForShape(shape);
        if (diagVerts != null && diagVerts.Count > 0)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            float sumX = 0, sumY = 0, sumZ = 0;
            for (int i = 0; i < diagVerts.Count; i++)
            {
                var v = diagVerts[i];
                sumX += v.x; sumY += v.y; sumZ += v.z;
                if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
                if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
            }
            int n = diagVerts.Count;
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' localVerts(" + n + "): centroid=(" +
                (sumX / n).ToString("F1") + ", " + (sumY / n).ToString("F1") + ", " + (sumZ / n).ToString("F1") +
                ") bounds=[(" + minX.ToString("F1") + "," + minY.ToString("F1") + "," + minZ.ToString("F1") +
                ")..(" + maxX.ToString("F1") + "," + maxY.ToString("F1") + "," + maxZ.ToString("F1") + ")]");
        }

        // Get the full composed transform from shape to NIF root
        var globalTransform = GetTransformToGlobal(nif, shape, shapeName);

        // If no accessory offset was found (not a head NIF), use the global transform as-is
        if (accessoryOffset == null)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' → using own global transform (no head NIF detected)");
            return globalTransform;
        }

        // Accessory heuristic: if this shape's global transform has near-zero translation,
        // it's likely a FaceGen accessory (brow, eyes, mouth) whose vertices are in local
        // bone space. Apply the primary head's transform to position it correctly.
        //
        // However, some shapes (like hair) have identity transforms but their vertices are
        // already pre-translated to world space. For these, applying the head offset would
        // double the translation. We detect this by checking if the vertex centroid is far
        // from the origin (>10 units), matching NPC Portrait Creator's PRETRANSLATED_THRESHOLD.
        const float ZERO_TRANSLATION_THRESHOLD = 0.1f;
        const float PRETRANSLATED_THRESHOLD = 10.0f;
        float translationLength = (float)Math.Sqrt(
            globalTransform.translation.x * globalTransform.translation.x +
            globalTransform.translation.y * globalTransform.translation.y +
            globalTransform.translation.z * globalTransform.translation.z);

        if (translationLength < ZERO_TRANSLATION_THRESHOLD)
        {
            // Check if vertices are already pre-translated to world space
            float centroidLength = 0f;
            if (diagVerts != null && diagVerts.Count > 0)
            {
                float sumX = 0, sumY = 0, sumZ = 0;
                for (int i = 0; i < diagVerts.Count; i++)
                {
                    var v = diagVerts[i];
                    sumX += v.x; sumY += v.y; sumZ += v.z;
                }
                int n = diagVerts.Count;
                float cx = sumX / n, cy = sumY / n, cz = sumZ / n;
                centroidLength = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
            }

            if (centroidLength > PRETRANSLATED_THRESHOLD)
            {
                LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                    "' → using identity (pre-translated vertices, centroidLen=" +
                    centroidLength.ToString("F1") + " > threshold=" + PRETRANSLATED_THRESHOLD.ToString("F1") + ")");
                return globalTransform;
            }

            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' → APPLYING accessory offset (translationLen=" + translationLength.ToString("F3") +
                ", centroidLen=" + centroidLength.ToString("F1") +
                " ≤ threshold=" + PRETRANSLATED_THRESHOLD.ToString("F1") + ")");
            return accessoryOffset;
        }

        LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
            "' → using own global transform (translationLen=" + translationLength.ToString("F2") + ")");
        return globalTransform;
    }

    /// <summary>
    /// Applies a nifly MatTransform (rotation * (point * scale) + translation) to a point.
    /// </summary>
    private static void ApplyTransform(MatTransform transform, float x, float y, float z,
        out float rx, out float ry, out float rz)
    {
        float sx = x * transform.scale;
        float sy = y * transform.scale;
        float sz = z * transform.scale;

        using var srcVec = new nifly.Vector3();
        srcVec.x = sx;
        srcVec.y = sy;
        srcVec.z = sz;
        using var rotated = transform.rotation.opMult(srcVec);

        rx = rotated.x + transform.translation.x;
        ry = rotated.y + transform.translation.y;
        rz = rotated.z + transform.translation.z;
    }

    /// <summary>
    /// Applies only the rotation component of a transform to a direction vector.
    /// </summary>
    private static void ApplyRotation(Matrix3 rotation, float x, float y, float z,
        out float rx, out float ry, out float rz)
    {
        using var srcVec = new nifly.Vector3();
        srcVec.x = x;
        srcVec.y = y;
        srcVec.z = z;
        using var rotated = rotation.opMult(srcVec);
        rx = rotated.x;
        ry = rotated.y;
        rz = rotated.z;
    }

    /// <summary>
    /// Extracts a nifly MatTransform's values into a pure C# CachedSkinTransform,
    /// avoiding SWIG interop calls during per-vertex skinning.
    /// </summary>
    private static CachedSkinTransform ExtractTransform(MatTransform t)
    {
        // Extract rotation matrix by multiplying basis vectors.
        // Matrix3.opMult(e_x) gives (rows[0][0], rows[1][0], rows[2][0]) = column 0.
        using var ex = new nifly.Vector3(); ex.x = 1;
        using var ey = new nifly.Vector3(); ey.y = 1;
        using var ez = new nifly.Vector3(); ez.z = 1;

        using var c0 = t.rotation.opMult(ex);
        using var c1 = t.rotation.opMult(ey);
        using var c2 = t.rotation.opMult(ez);

        return new CachedSkinTransform
        {
            // Reconstruct rows from columns
            R00 = c0.x, R01 = c1.x, R02 = c2.x,
            R10 = c0.y, R11 = c1.y, R12 = c2.y,
            R20 = c0.z, R21 = c1.z, R22 = c2.z,
            Tx = t.translation.x,
            Ty = t.translation.y,
            Tz = t.translation.z,
            Scale = t.scale,
        };
    }

    /// <summary>
    /// Diagnostic helper: logs the bind-pose mismatch between the skeleton NIF and the
    /// mesh NIF for a given bone. This is the delta that produces neck-gap / seam artefacts
    /// when a mesh's skinToBone (authored against its own bone positions) is paired with
    /// the skeleton's bone positions at runtime.
    ///
    /// For each bone of interest, logs:
    ///   - skeleton-NIF bone world translation (the runtime bone position we use)
    ///   - mesh-NIF bone world translation (the position skinToBone was authored for)
    ///   - translation delta and magnitude
    ///   - scale values from each source
    ///   - inverse-bind (skinToBone) translation, for cross-reference
    /// </summary>
    private void LogBoneDelta(NifFile meshNif, NifFile skeletonNif, string shapeName,
        string boneName, MatTransform inverseBind)
    {
        using var skelBoneWorld = new MatTransform();
        using var meshBoneWorld = new MatTransform();
        bool hasSkel = skeletonNif.GetNodeTransformToGlobal(boneName, skelBoneWorld);
        bool hasMesh = meshNif.GetNodeTransformToGlobal(boneName, meshBoneWorld);

        if (!hasSkel || !hasMesh)
        {
            LogVerbose("CharacterViewer: [BoneDelta] '" + shapeName + "' bone '" + boneName +
                "': skeleton=" + hasSkel + " mesh=" + hasMesh + " (cannot compute delta)");
            return;
        }

        float dx = skelBoneWorld.translation.x - meshBoneWorld.translation.x;
        float dy = skelBoneWorld.translation.y - meshBoneWorld.translation.y;
        float dz = skelBoneWorld.translation.z - meshBoneWorld.translation.z;
        float mag = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        LogVerbose("CharacterViewer: [BoneDelta] '" + shapeName + "' bone '" + boneName + "'");
        LogVerbose("  skel T=(" + skelBoneWorld.translation.x.ToString("F3") + ", " +
                                           skelBoneWorld.translation.y.ToString("F3") + ", " +
                                           skelBoneWorld.translation.z.ToString("F3") + ") S=" +
                                           skelBoneWorld.scale.ToString("F4"));
        LogVerbose("  mesh T=(" + meshBoneWorld.translation.x.ToString("F3") + ", " +
                                           meshBoneWorld.translation.y.ToString("F3") + ", " +
                                           meshBoneWorld.translation.z.ToString("F3") + ") S=" +
                                           meshBoneWorld.scale.ToString("F4"));
        LogVerbose("  Δ T=(" + dx.ToString("F3") + ", " + dy.ToString("F3") + ", " +
                                        dz.ToString("F3") + ") |Δ|=" + mag.ToString("F3"));
        LogVerbose("  skinToBone T=(" + inverseBind.translation.x.ToString("F3") + ", " +
                                                 inverseBind.translation.y.ToString("F3") + ", " +
                                                 inverseBind.translation.z.ToString("F3") + ") S=" +
                                                 inverseBind.scale.ToString("F4"));
    }

    /// <summary>
    /// Diagnostic helper: reports the Z-range of a skinned shape's vertices in NIF Z-up
    /// space, then breaks them into Y buckets (front-to-back bands) and reports min/max Z
    /// per bucket. This lets us compare the body's neck-hole rim (max-Z per Y bucket) to
    /// the head's neck-bottom ring (min-Z per Y bucket) at corresponding front/mid/back
    /// positions — the critical data for identifying a geometric seam.
    /// </summary>
    // Per-shape bounds diagnostic. Logs each shape's name, partition IDs, which
    // routing branch it went through (skinned vs unskinned fallback), vertex count,
    // and final-position bounds in NIF Z-up space. Helps detect misrouted or
    // mispositioned shapes across the multi-NIF character assembly.
    private void LogShapeBoundsDiagnostic(NifFile nif, NiShape shape, bool wasSkinned,
        Vector3[] positionsYUp, int vertCount)
    {
        if (vertCount == 0) return;
        string shapeName = shape.name?.get() ?? "?";

        // Dismember partition IDs (if any)
        string partStr = "none";
        var skinRef = shape.SkinInstanceRef();
        if (skinRef != null && !skinRef.IsEmpty())
        {
            NiHeader header = nif.GetHeader();
            NiObject skinObj = header.GetBlockById(skinRef.index);
            if (skinObj is BSDismemberSkinInstance dismember)
            {
                var parts = new List<ushort>();
                using var partitions = dismember.partitions;
                if (partitions != null)
                {
                    using var items = partitions.items();
                    for (int pi = 0; pi < items.Count; pi++)
                        parts.Add(items[pi].partID);
                }
                partStr = parts.Count == 0 ? "empty" : string.Join(",", parts);
            }
            else
            {
                partStr = "non-dismember";
            }
        }

        // Y-up → Z-up inverse: Z-up = (x_yUp, -z_yUp, y_yUp)
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < vertCount; i++)
        {
            var p = positionsYUp[i];
            float zx = p.X;
            float zy = -p.Z;
            float zz = p.Y;
            if (zx < minX) minX = zx; if (zx > maxX) maxX = zx;
            if (zy < minY) minY = zy; if (zy > maxY) maxY = zy;
            if (zz < minZ) minZ = zz; if (zz > maxZ) maxZ = zz;
        }

        LogVerbose("CharacterViewer: [ShapeDiag] '" + shapeName + "'" +
            " route=" + (wasSkinned ? "skinned" : "fallback") +
            " verts=" + vertCount +
            " parts=[" + partStr + "]" +
            " X=[" + minX.ToString("F2") + "," + maxX.ToString("F2") + "]" +
            " Y=[" + minY.ToString("F2") + "," + maxY.ToString("F2") + "]" +
            " Z=[" + minZ.ToString("F2") + "," + maxZ.ToString("F2") + "]");
    }

    /// <summary>
    /// Attempts CPU-side bone-weight skinning for a shape. Returns SkinningInfo on success,
    /// null if the shape is not properly skinned. On success, outputs skinned positions and
    /// normals as flat float arrays in NIF Z-up space.
    /// </summary>
    /// <param name="skeletonNif">The loaded skeleton NIF providing real bone world transforms.</param>
    private SkinningInfo? TryApplyCpuSkinning(NifFile nif, NiShape shape,
        vectorVector3 nifVerts, vectorVector3? nifNormals, int vertCount,
        NifFile skeletonNif,
        out float[]? outPosX, out float[]? outPosY, out float[]? outPosZ,
        out float[]? outNrmX, out float[]? outNrmY, out float[]? outNrmZ,
        out List<string>? unresolvedBones, out List<string>? bonesAbsentFromSkeleton)
    {
        outPosX = outPosY = outPosZ = null;
        outNrmX = outNrmY = outNrmZ = null;
        // Names of weighted bones that resolve from no source (skip the shape)
        // and bones present in the mesh NIF but absent from the skeleton (render
        // via fallback, but warn). Populated after the weight pass so we only
        // flag bones vertices actually use - see BuiltMesh.UnresolvedSkinBones /
        // BuiltMesh.BonesAbsentFromSkeleton.
        unresolvedBones = null;
        bonesAbsentFromSkeleton = null;

        string shapeName = shape.name?.get() ?? "?";
        NiHeader header = nif.GetHeader();

        // --- Step 1: Get bone list ---
        using var boneNames = new vectorstring();
        uint numBones = nif.GetShapeBoneList(shape, boneNames);
        if (numBones == 0)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' has skin instance but no bones");
            return null;
        }

        LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' skinning with " + numBones + " bones");

        // --- Step 2: Compute per-bone skinning matrices ---
        // skinMatrix[i] = boneWorldTransform * inverseBindPose
        // Bone world transforms come from the SKELETON NIF (not the shape's own NIF),
        // which provides the real bind-pose bone positions from the full skeleton hierarchy.
        var boneTransforms = new CachedSkinTransform[numBones];
        // Per-bone resolution tracking. A bone resolves from the skeleton NIF
        // or, failing that, the mesh's own NIF (actor body/armor NIFs embed
        // copies of the bones they're weighted to). After the weight pass these
        // feed two diagnostics, for bones vertices actually use:
        //   boneResolved     - got a transform from EITHER source. A bone that
        //                      resolves from neither keeps a zero transform and
        //                      would collapse its vertices to the origin.
        //   boneFromSkeleton - got it from the skeleton specifically. A bone
        //                      resolved only via the mesh-NIF fallback means the
        //                      skeleton is missing it: the mesh still renders, but
        //                      against a frame the other meshes don't share, so it
        //                      can be misaligned - the signal that an incompatible
        //                      or absent skeleton (e.g. a missing skeleton mod) is
        //                      loaded.
        var boneResolved = new bool[numBones];
        var boneFromSkeleton = new bool[numBones];
        int validBones = 0;
        int skeletonBones = 0;
        for (uint i = 0; i < numBones; i++)
        {
            using var inverseBind = new MatTransform();
            if (!nif.GetShapeBoneTransform(shape, i, inverseBind))
            {
                LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                    "' bone " + i + " — no inverse bind-pose, using identity");
                continue;
            }

            string boneName = boneNames[(int)i];
            using var boneWorld = new MatTransform();

            // Try the skeleton NIF first (real bone positions), fall back to the shape's NIF
            if (skeletonNif.GetNodeTransformToGlobal(boneName, boneWorld))
            {
                skeletonBones++;
                boneFromSkeleton[i] = true;
            }
            else if (!nif.GetNodeTransformToGlobal(boneName, boneWorld))
            {
                LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                    "' bone '" + boneName + "' — not found in skeleton or shape NIF, skipping");
                continue;
            }

            // --- Diagnostic: skeleton vs mesh-NIF bone world-transform comparison ---
            // Quantifies the bind-pose mismatch that produces the neck seam.
            // Logs translation delta, |delta|, scale ratio, and inverse-bind translation
            // for each bone of interest. Enabled by _logBoneDeltas.
            if (_logBoneDeltas && _diagnosticBonesOfInterest.Contains(boneName))
            {
                LogBoneDelta(nif, skeletonNif, shapeName, boneName, inverseBind);
            }

            using var skinMatrix = boneWorld.ComposeTransforms(inverseBind);
            boneTransforms[i] = ExtractTransform(skinMatrix);
            boneResolved[i] = true;
            validBones++;
        }

        LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
            "' bone transforms: " + validBones + " valid (" + skeletonBones + " from skeleton)");

        if (validBones == 0)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' no valid bone transforms, skipping skinning");
            return null;
        }

        // --- Step 3: Read per-vertex bone weights from NiSkinData ---
        var skinRef = shape.SkinInstanceRef();
        if (skinRef == null || skinRef.IsEmpty()) return null;

        NiObject skinObj = header.GetBlockById(skinRef.index);
        // BSDismemberSkinInstance extends NiSkinInstance — try both
        NiSkinInstance? skinInst = skinObj as NiSkinInstance;
        if (skinInst == null)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' skin instance is not NiSkinInstance");
            return null;
        }

        var dataRef = skinInst.dataRef;
        if (dataRef == null || dataRef.IsEmpty())
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' no NiSkinData reference");
            return null;
        }

        NiObject dataObj = header.GetBlockById(dataRef.index);
        if (dataObj is not NiSkinData skinData)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' block is not NiSkinData");
            return null;
        }

        // Build per-vertex arrays: 4 bone indices + 4 weights per vertex
        int[] vertBoneIndices = new int[vertCount * 4];
        float[] vertBoneWeights = new float[vertCount * 4];

        var bonesVec = skinData.bones;
        if (bonesVec == null)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName + "' NiSkinData.bones is null");
            return null;
        }

        int boneCount = Math.Min(bonesVec.Count, (int)numBones);
        int totalWeightEntries = 0;
        for (int boneIdx = 0; boneIdx < boneCount; boneIdx++)
        {
            var boneData = bonesVec[boneIdx];
            var vertWeights = boneData.vertexWeights;
            if (vertWeights == null) continue;

            for (int wi = 0; wi < vertWeights.Count; wi++)
            {
                var sw = vertWeights[wi];
                int vertIdx = sw.index;
                float weight = sw.weight;
                if (weight <= 0 || vertIdx >= vertCount) continue;

                // Find an empty slot for this vertex (up to 4 bones per vertex)
                int baseIdx = vertIdx * 4;
                for (int k = 0; k < 4; k++)
                {
                    if (vertBoneWeights[baseIdx + k] == 0f)
                    {
                        vertBoneIndices[baseIdx + k] = boneIdx;
                        vertBoneWeights[baseIdx + k] = weight;
                        totalWeightEntries++;
                        break;
                    }
                }
            }
        }

        LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
            "' read " + totalWeightEntries + " weight entries across " + boneCount + " bones");

        // --- Step 4: Apply skinning — blend bone transforms per vertex ---
        outPosX = new float[vertCount];
        outPosY = new float[vertCount];
        outPosZ = new float[vertCount];
        outNrmX = new float[vertCount];
        outNrmY = new float[vertCount];
        outNrmZ = new float[vertCount];

        bool hasNormals = nifNormals != null && nifNormals.Count == vertCount;

        for (int vi = 0; vi < vertCount; vi++)
        {
            var v = nifVerts[vi];
            float px = v.x, py = v.y, pz = v.z;

            float accPx = 0, accPy = 0, accPz = 0;
            float accNx = 0, accNy = 0, accNz = 0;
            float totalWeight = 0;

            int baseIdx = vi * 4;
            for (int k = 0; k < 4; k++)
            {
                float w = vertBoneWeights[baseIdx + k];
                if (w <= 0) continue;
                int bIdx = vertBoneIndices[baseIdx + k];

                boneTransforms[bIdx].Apply(px, py, pz, out float tx, out float ty, out float tz);
                accPx += w * tx;
                accPy += w * ty;
                accPz += w * tz;

                if (hasNormals)
                {
                    var n = nifNormals![vi];
                    boneTransforms[bIdx].ApplyRotation(n.x, n.y, n.z, out float tnx, out float tny, out float tnz);
                    accNx += w * tnx;
                    accNy += w * tny;
                    accNz += w * tnz;
                }

                totalWeight += w;
            }

            if (totalWeight > 0)
            {
                outPosX[vi] = accPx / totalWeight;
                outPosY[vi] = accPy / totalWeight;
                outPosZ[vi] = accPz / totalWeight;

                if (hasNormals)
                {
                    float nLen = (float)Math.Sqrt(accNx * accNx + accNy * accNy + accNz * accNz);
                    if (nLen > 0.0001f)
                    {
                        outNrmX[vi] = accNx / nLen;
                        outNrmY[vi] = accNy / nLen;
                        outNrmZ[vi] = accNz / nLen;
                    }
                    else
                    {
                        var n = nifNormals![vi];
                        outNrmX[vi] = n.x;
                        outNrmY[vi] = n.y;
                        outNrmZ[vi] = n.z;
                    }
                }
            }
            else
            {
                // No bone weights — keep bind-pose position
                outPosX[vi] = px;
                outPosY[vi] = py;
                outPosZ[vi] = pz;
                if (hasNormals)
                {
                    var n = nifNormals![vi];
                    outNrmX[vi] = n.x;
                    outNrmY[vi] = n.y;
                    outNrmZ[vi] = n.z;
                }
            }
        }

        // Note: No shapeToRoot transform is applied here. The skeleton's bone world
        // transforms already place vertices in NIF root space. Applying the shape's
        // local-to-root transform on top would double-count the offset (confirmed by
        // headparts which have identity shape transforms and are correctly positioned
        // by skinning alone).

        // Log a position sample to verify skinning is working
        if (vertCount > 0)
        {
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' sample vert[0]: bind=(" + nifVerts[0].x.ToString("F2") + "," +
                nifVerts[0].y.ToString("F2") + "," + nifVerts[0].z.ToString("F2") +
                ") → skinned=(" + outPosX[0].ToString("F2") + "," +
                outPosY[0].ToString("F2") + "," + outPosZ[0].ToString("F2") + ")");
        }

        // --- Step 5: classify weighted bones by where they resolved ---
        // Only bones a vertex actually references with weight > 0 matter; a bone
        // listed by the NIF but unused can't break the pose. Two buckets:
        //   unresolved     - in neither skeleton nor mesh NIF; the shape would
        //                    collapse toward the origin (mesh-override channel
        //                    treats this as "skip + warn").
        //   skeletonAbsent - resolved only via the mesh-NIF fallback (present in
        //                    the mesh, absent from the skeleton). The shape still
        //                    renders, but on a frame the base meshes don't share,
        //                    so it can be misaligned - the signal that an
        //                    incompatible / missing skeleton is loaded.
        HashSet<string>? unresolved = null;
        HashSet<string>? skeletonAbsent = null;
        for (int idx = 0; idx < vertBoneWeights.Length; idx++)
        {
            if (vertBoneWeights[idx] <= 0f) continue;
            int bIdx = vertBoneIndices[idx];
            if (bIdx < 0 || bIdx >= numBones) continue;
            if (!boneResolved[bIdx])
                (unresolved ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(boneNames[bIdx]);
            else if (!boneFromSkeleton[bIdx])
                (skeletonAbsent ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(boneNames[bIdx]);
        }
        if (unresolved != null)
        {
            unresolvedBones = new List<string>(unresolved);
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' references " + unresolved.Count + " bone(s) found in neither skeleton nor mesh NIF: [" +
                string.Join(", ", unresolved) + "]");
        }
        if (skeletonAbsent != null)
        {
            bonesAbsentFromSkeleton = new List<string>(skeletonAbsent);
            LogVerbose("CharacterViewer: [Skinning] '" + shapeName +
                "' references " + skeletonAbsent.Count + " bone(s) absent from the skeleton (rendered via mesh-NIF fallback): [" +
                string.Join(", ", skeletonAbsent) + "]");
        }

        return new SkinningInfo
        {
            BoneTransforms = boneTransforms,
            VertBoneIndices = vertBoneIndices,
            VertBoneWeights = vertBoneWeights,
            VertexCount = vertCount,
        };
    }

    /// <summary>
    /// Applies bone-weight skinning to Y-up positions and normals using pre-computed skinning data.
    /// Converts Y-up → Z-up, applies skinning in Z-up, converts back to Y-up.
    /// Can be called in-place (same collection for input and output).
    /// </summary>
    public static void ApplySkinning(
        Vector3[] inputPositions,
        Vector3[] inputNormals,
        SkinningInfo skinning,
        Vector3[] outputPositions,
        Vector3[] outputNormals)
    {
        int vertCount = skinning.VertexCount;
        if (vertCount == 0 || inputPositions.Length < vertCount) return;

        bool hasNormals = inputNormals != null && inputNormals.Length >= vertCount;

        for (int vi = 0; vi < vertCount; vi++)
        {
            var posYUp = inputPositions[vi];
            float px = posYUp.X, py = -posYUp.Z, pz = posYUp.Y;

            float accPx = 0, accPy = 0, accPz = 0;
            float accNx = 0, accNy = 0, accNz = 0;
            float totalWeight = 0;

            float nx = 0, ny = 0, nz = 0;
            if (hasNormals)
            {
                var nrmYUp = inputNormals![vi];
                nx = nrmYUp.X; ny = -nrmYUp.Z; nz = nrmYUp.Y;
            }

            int baseIdx = vi * 4;
            for (int k = 0; k < 4; k++)
            {
                float w = skinning.VertBoneWeights[baseIdx + k];
                if (w <= 0) continue;
                int bIdx = skinning.VertBoneIndices[baseIdx + k];

                skinning.BoneTransforms[bIdx].Apply(px, py, pz, out float tx, out float ty, out float tz);
                accPx += w * tx; accPy += w * ty; accPz += w * tz;

                if (hasNormals)
                {
                    skinning.BoneTransforms[bIdx].ApplyRotation(nx, ny, nz, out float tnx, out float tny, out float tnz);
                    accNx += w * tnx; accNy += w * tny; accNz += w * tnz;
                }
                totalWeight += w;
            }

            if (totalWeight > 0)
            {
                outputPositions[vi] = new Vector3(accPx / totalWeight, accPz / totalWeight, -accPy / totalWeight);
                if (hasNormals)
                {
                    float nLen = MathF.Sqrt(accNx * accNx + accNy * accNy + accNz * accNz);
                    outputNormals[vi] = nLen > 0.0001f
                        ? new Vector3(accNx / nLen, accNz / nLen, -accNy / nLen)
                        : inputNormals![vi];
                }
            }
            else
            {
                outputPositions[vi] = posYUp;
                if (hasNormals) outputNormals[vi] = inputNormals![vi];
            }
        }
    }

    private static bool IsZeroTranslation(nifly.Vector3 v)
    {
        const float eps = 0.0001f;
        return Math.Abs(v.x) < eps && Math.Abs(v.y) < eps && Math.Abs(v.z) < eps;
    }

    private static bool AreNormalsAllZero(Vector3[] normals)
    {
        const float eps = 0.0001f;
        foreach (var n in normals)
        {
            if (Math.Abs(n.X) > eps || Math.Abs(n.Y) > eps || Math.Abs(n.Z) > eps)
                return false;
        }
        return true;
    }

    private static void ComputeNormalsFromGeometry(Vector3[] positions, int[] indices, Vector3[] normals)
    {
        for (int i = 0; i < normals.Length; i++)
            normals[i] = Vector3.Zero;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var faceNormal = Vector3.Cross(edge1, edge2);
            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            normals[i] = len > 0.0001f ? normals[i] / len : new Vector3(0, 1, 0);
        }
    }

    /// <summary>
    /// Computes tangents and bitangents from position/normal/UV data using the Lengyel algorithm.
    /// </summary>
    private static void ComputeTangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        vectorTriangle tris, Vector3[] tangents, Vector3[] bitangents)
    {
        var tan1 = new Vector3[positions.Length];
        var tan2 = new Vector3[positions.Length];

        for (int i = 0; i < tris.Count; i++)
        {
            var tri = tris[i];
            int i0 = tri.p1, i1 = tri.p2, i2 = tri.p3;
            if (i0 >= positions.Length || i1 >= positions.Length || i2 >= positions.Length) continue;

            var v0 = positions[i0]; var v1 = positions[i1]; var v2 = positions[i2];
            var w0 = uvs[i0]; var w1 = uvs[i1]; var w2 = uvs[i2];

            float x1 = v1.X - v0.X, x2 = v2.X - v0.X;
            float y1 = v1.Y - v0.Y, y2 = v2.Y - v0.Y;
            float z1 = v1.Z - v0.Z, z2 = v2.Z - v0.Z;
            float s1 = w1.X - w0.X, s2 = w2.X - w0.X;
            float t1 = w1.Y - w0.Y, t2 = w2.Y - w0.Y;

            float denom = s1 * t2 - s2 * t1;
            if (MathF.Abs(denom) < 1e-10f) continue;
            float r = 1f / denom;

            var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
            var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

            tan1[i0] += sdir; tan1[i1] += sdir; tan1[i2] += sdir;
            tan2[i0] += tdir; tan2[i1] += tdir; tan2[i2] += tdir;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            var n = normals[i];
            var t = tan1[i];

            // Gram-Schmidt orthogonalize
            var tangent = t - n * Vector3.Dot(n, t);
            float len = tangent.Length();
            tangents[i] = len > 0.0001f ? tangent / len : Vector3.Zero;

            // Bitangent = cross(n, t) * handedness
            bitangents[i] = Vector3.Cross(n, tangents[i]);
            if (Vector3.Dot(bitangents[i], tan2[i]) < 0)
                bitangents[i] = -bitangents[i];
        }
    }
}

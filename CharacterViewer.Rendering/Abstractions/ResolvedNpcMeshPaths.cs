using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// Sex of the NPC whose mesh paths were resolved. Lives in the rendering tier
/// (alongside <see cref="ResolvedNpcMeshPaths"/>) rather than in the host so the
/// rendering project doesn't take a dependency on SynthEBD's <c>Gender</c> enum.
/// Values match SynthEBD's <c>Gender</c> ordinal positions so the
/// <see cref="SynthEbdNpcMeshDataSourceAdapter"/> can cast directly.
/// </summary>
public enum Sex
{
    Male,
    Female
}

/// <summary>
/// Resolved mesh + texture paths for one NPC, packaged in a Mutagen-free POCO so
/// the CharacterViewer rendering tier can consume it without taking a dependency
/// on the Skyrim record schema. SynthEBD's <see cref="NpcMeshResolver.NpcMeshPaths"/>
/// is converted to this form by <see cref="SynthEbdNpcMeshDataSourceAdapter"/>.
///
/// Once Phase B/C of the extraction completes this becomes the canonical type
/// returned by all resolution paths — the inner <c>NpcMeshResolver.NpcMeshPaths</c>
/// goes away and Skyrim-tier code produces <see cref="ResolvedNpcMeshPaths"/>
/// directly.
/// </summary>
public sealed class ResolvedNpcMeshPaths
{
    public string? BodyMeshPath { get; init; }
    public string? HandsMeshPath { get; init; }
    public string? FeetMeshPath { get; init; }
    public string? HeadMeshPath { get; init; }

    /// <summary>Mesh path for the worn armor's hair-slot ARMA (biped slot 31).
    /// Skyrim mods like High Poly NPC Overhaul use a "bald" FaceGen scalp
    /// paired with a wig in this slot — without rendering it the NPC appears
    /// hairless. Vanilla NPCs and most overhauls leave this null and bake
    /// hair shapes into the FaceGen NIF directly.</summary>
    public string? HairMeshPath { get; init; }

    /// <summary>Mesh path for the worn armor's tail-slot ARMA (biped slot 40).
    /// Required for Khajiit and Argonian races whose tails are armatures
    /// rather than baked-in shapes.</summary>
    public string? TailMeshPath { get; init; }

    public Sex Sex { get; init; }

    /// <summary>Race-specific skeleton NIF path (Data-relative); used for CPU-side
    /// bone-weight skinning to close the head-body neck seam.</summary>
    public string? SkeletonPath { get; init; }

    /// <summary>Body-part name → human-readable record traversal trace, displayed
    /// in viewer tooltips and used for resolution diagnostics.</summary>
    public Dictionary<string, string> ResolutionChains { get; init; } = new();

    /// <summary>Body-part name → texture-slot index → game-relative texture path.
    /// Sourced from ARMA.SkinTexture (TXST) records and overrides the NIF's
    /// embedded BSShaderTextureSet at runtime. Head textures are intentionally
    /// excluded — FaceGen NIF paths are ground truth for heads.</summary>
    public Dictionary<string, Dictionary<int, string>> TxstTextures { get; init; } = new();

    /// <summary>FaceTint DDS path (Data-relative) constructed from the NPC's
    /// FormKey; CPU-blended onto the head diffuse texture.</summary>
    public string? FaceTintPath { get; init; }

    /// <summary>QNAM TextureLighting color (RGB, 0..1) applied as a skin tint
    /// to body/hands/feet meshes that use the ST_SkinTint shader type. Null when
    /// the NPC has no TextureLighting set.</summary>
    public (float R, float G, float B)? TextureLightingColor { get; init; }

    /// <summary>NPC weight (0–100) from the NPC record, used by the morph
    /// interpolator. Defaults to 50 if the host can't determine it.</summary>
    public int NpcWeight { get; init; } = 50;

    /// <summary>NPC.Height multiplier from the record (1.0 default). Applied
    /// uniformly to the rendered model. The host should clamp zero/negative
    /// values from malformed records before populating this.</summary>
    public float NpcBaseHeight { get; init; } = 1f;

    /// <summary>HCLR record-resolved hair color (RGB, 0..1). In-game Skyrim
    /// uses this to override the default tint baked into the hair NIF's BSLSP.
    /// Null when the NPC has no HairColor FormLink set or the host can't
    /// resolve the HCLR record.</summary>
    public (float R, float G, float B)? HairColorRgb { get; init; }

    /// <summary>Shape names in the FaceGen NIF that are eyeball geometry,
    /// per the NPC's resolved HeadPart records: the EditorIDs of every
    /// effective head part of type Eyes plus its Extra Parts (FaceGen bakes
    /// one shape per geometry-bearing head part, named after its EditorID).
    /// Authoritative input to <c>GlMesh.IsEye</c> classification for shapes
    /// whose shader type is not BSLSP_EYE — custom eyes authored as
    /// BSLSP_ENVMAP with arbitrary shape names ("FoxGloveEyeMesh") otherwise
    /// evade the plural-"Eyes" name heuristic and receive eye-socket SSAO.
    /// Supply a case-insensitive set (membership is tested with the set's
    /// own comparer). Empty when the host has no head-part data; the name
    /// heuristic then remains the only fallback.</summary>
    public IReadOnlySet<string> EyeShapeNames { get; init; } = EmptyEyeShapeNames;

    private static readonly IReadOnlySet<string> EmptyEyeShapeNames = new HashSet<string>();

    /// <summary>Returns a copy with <see cref="HeadMeshPath"/> swapped — used by
    /// the HeadParts preview flow to retarget the viewer at a temp FaceGen NIF
    /// without re-resolving the rest of the record chain.</summary>
    public ResolvedNpcMeshPaths WithHeadMeshPath(string? headMeshPath) =>
        new()
        {
            BodyMeshPath = BodyMeshPath,
            HandsMeshPath = HandsMeshPath,
            FeetMeshPath = FeetMeshPath,
            HeadMeshPath = headMeshPath,
            HairMeshPath = HairMeshPath,
            TailMeshPath = TailMeshPath,
            Sex = Sex,
            SkeletonPath = SkeletonPath,
            ResolutionChains = ResolutionChains,
            TxstTextures = TxstTextures,
            FaceTintPath = FaceTintPath,
            TextureLightingColor = TextureLightingColor,
            NpcWeight = NpcWeight,
            NpcBaseHeight = NpcBaseHeight,
            HairColorRgb = HairColorRgb,
            EyeShapeNames = EyeShapeNames,
        };
}

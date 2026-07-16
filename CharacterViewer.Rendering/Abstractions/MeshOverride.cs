using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// Classifies a <see cref="MeshOverride"/> so the renderer can pick sensible
/// defaults (skin tint, draw order, slot-hiding precedence) without the host
/// having to spell each of them out. Skinning and tint are already generic
/// (the renderer CPU-skins from the NIF's own bone weights and tints by NIF
/// shader type - see RENDERING_PIPELINE.md "Override channels"); the kind only
/// nudges defaults:
/// <list type="bullet">
///   <item><see cref="Skin"/> - lets the shader decide tint (an auxiliary skin
///   mesh tints with the body QNAM like any slot-32 skin shape).</item>
///   <item><see cref="Armor"/> - never takes the body skin QNAM tint even if
///   the NIF is mis-authored with a skin shader type; hides the slots it
///   occupies.</item>
///   <item><see cref="Headgear"/> - like Armor, plus hides the hair slot by
///   default so a helmet replaces hair the way it does in game.</item>
///   <item><see cref="Hair"/> - a hair-slot replacement (e.g. a wig).</item>
///   <item><see cref="Other"/> - no special treatment.</item>
/// </list>
/// </summary>
public enum MeshOverrideKind
{
    Skin,
    Armor,
    Headgear,
    Hair,
    Other,
}

/// <summary>
/// Neutral mesh-replacement record the rendering tier accepts through
/// <see cref="VM_CharacterViewer.ApplyMeshOverrides"/>, mirroring the existing
/// texture-only <see cref="TextureOverride"/> channel. It synthesizes a new
/// renderable shape from a <c>.nif</c> the base NPC doesn't carry. The first
/// consumer is an auxiliary armature on a non-base biped slot (e.g. slot 52)
/// that some mods add to the actor at runtime by script: its mesh exists only in
/// the selected asset-pack subgroup, never in the static WornArmor the resolver
/// walks. The same channel later serves NPC Plugin Chooser 2's "Include Default
/// Outfit" / "Include headgear".
///
/// <para>The host (SynthEBD or NPC2) is Mutagen-aware and builds these specs;
/// the rendering tier stays Mutagen-free and only loads / skins / textures /
/// hides them.</para>
/// </summary>
public sealed record MeshOverride
{
    /// <summary>Stable identity for replace / bookkeeping (e.g. "Slot52",
    /// "Outfit:0x..:ARMA", "Headgear"). A re-applied override set replaces any
    /// previously-applied shapes; this key lets the host reason about which is
    /// which. Also used as the rendered shape's <see cref="GlMesh.BodyPart"/> so
    /// numeric-slot texture overrides (e.g. a slot-52 auxiliary mesh's
    /// <c>SkinTexture.*</c>) route to it via <see cref="VM_CharacterViewer.ApplyTextureOverrides"/>.</summary>
    public string Key { get; init; } = "";

    /// <summary>Game-relative <c>.nif</c> path (Data\meshes\...), resolved via
    /// <see cref="GameAssetResolver"/> like any other dependency mesh. For an
    /// auxiliary armature this is the selected subgroup's
    /// <c>WorldModel.&lt;sex&gt;.File</c>.</summary>
    public string MeshPath { get; init; } = "";

    /// <summary>Bitmask of biped-object slots this mesh occupies, in the same
    /// encoding the asset-pack destinations use (<c>(BipedObjectFlag)N</c>,
    /// i.e. <c>1 &lt;&lt; (slot - 30)</c>). A slot-52 auxiliary armature is
    /// <c>1 &lt;&lt; 22</c> = 4194304. Drives slot occupancy / hiding.</summary>
    public int BipedSlots { get; init; }

    /// <summary>Bitmask of slots whose existing occupants this override hides.
    /// Null means "same as <see cref="BipedSlots"/>" (an override hides exactly
    /// the slots it fills). Headgear sets this explicitly to also cover the hair
    /// slot. An auxiliary mesh on a free slot collides with nothing, so it hides
    /// nothing visible.</summary>
    public int? HidesSlots { get; init; }

    /// <summary>Effective hide mask - <see cref="HidesSlots"/> when set,
    /// otherwise <see cref="BipedSlots"/>.</summary>
    public int EffectiveHidesSlots => HidesSlots ?? BipedSlots;

    /// <summary>Tint-default / draw-order / hide-rule selector. Default
    /// <see cref="MeshOverrideKind.Skin"/> suits an auxiliary skin mesh.</summary>
    public MeshOverrideKind Kind { get; init; } = MeshOverrideKind.Skin;

    /// <summary>Mesh-wide TXST override bundled with the mesh (texture-slot index
    /// -> game-relative path), applied to every shape. A mesh override carries its
    /// own texture set because that's how the source provides it: a SynthEBD
    /// auxiliary subgroup ships mesh + its slot's <c>SkinTexture.*</c>, and an
    /// ArmorAddon ships <c>WorldModel</c> + <c>SkinTexture</c> (NAM0/NAM1)
    /// together. Per-object <c>AlternateTextures</c>, which target individual
    /// named shapes, go through <see cref="ShapeTextures"/> instead. Null = use
    /// the NIF's own embedded <c>BSShaderTextureSet</c>.</summary>
    public IReadOnlyDictionary<int, string>? Textures { get; init; }

    /// <summary>Per-shape TXST override from the plugin's <c>AlternateTextures</c>
    /// (MODS) list: NIF shape node name -> (texture-slot index -> game-relative
    /// path). This expresses what the flat <see cref="Textures"/> channel cannot —
    /// a different TextureSet per named shape within a single mesh, which is how an
    /// ArmorAddon's <c>WorldModel.AlternateTextures</c> retextures individual
    /// shapes (e.g. alternate-coloured variants of one shared cuirass NIF). Keys
    /// are matched against each shape's <c>BuiltMesh.ShapeName</c> (the NIF
    /// geometry node's own name). A per-shape entry wins over <see cref="Textures"/>
    /// for the same slot on that shape. Null = no per-shape overrides.
    /// <para>Legacy name-only channel. Ignored when <see cref="AlternateTextures"/>
    /// is supplied — that list carries the record's 3D Index too, so a shape the
    /// mesh's author RENAMED (BodySlide/Outfit Studio rebuilds do this routinely)
    /// can still be matched the way the engine matches it.</para></summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? ShapeTextures { get; init; }

    /// <summary>Per-shape TXST overrides from the plugin's <c>AlternateTextures</c>
    /// (MODS) list with BOTH identity fields of each entry (3D Name + 3D Index),
    /// letting the renderer fall back to index matching when the mesh's shapes were
    /// renamed. Supersedes <see cref="ShapeTextures"/> when non-null; entries apply
    /// in list order (later wins per slot on the same shape) and win over
    /// <see cref="Textures"/> for the same slot on their shape.</summary>
    public IReadOnlyList<AlternateTextureSpec>? AlternateTextures { get; init; }
}

/// <summary>
/// One <c>AlternateTextures</c> (MODS) entry of a plugin model record, carried
/// with both identity fields the record stores for its target shape:
/// <list type="bullet">
///   <item><see cref="ShapeName"/> — the "3D Name". Matches the NIF geometry
///   node's own name as authored, but goes stale when the mesh is rebuilt:
///   BodySlide/Outfit Studio output routinely renames shapes (its project
///   shape names differ from the shipped mesh's), and the CK/engine still
///   apply the entry — observed in the field as "variant renders in game and
///   CK but not in the preview" (untextured/black outfit pieces).</item>
///   <item><see cref="ShapeIndex"/> — the "3D Index". The engine-side key, but
///   only trustworthy as a shape ordinal for meshes whose block order matches
///   scene order (true of Outfit Studio/BodySlide output — exactly the files
///   whose names go stale). Block-sorting optimizers can make a shape's file
///   ordinal differ from its record index, so index is the FALLBACK, not the
///   primary key: those tools reorder but don't rename, and the name match
///   still lands first.</item>
/// </list>
/// See <c>VM_CharacterViewer.ApplyOneMeshOverride</c> for the matching rules.
/// </summary>
public sealed record AlternateTextureSpec
{
    /// <summary>The record's "3D Name" for the target shape; may no longer name
    /// any shape in a rebuilt mesh. Empty = unnamed (index-only matching).</summary>
    public string ShapeName { get; init; } = "";

    /// <summary>The record's "3D Index" for the target shape; compared against
    /// the shape's ordinal among the NIF's shape blocks
    /// (<c>BuiltMesh.ShapeOrdinal</c>). -1 = unknown (name-only matching).</summary>
    public int ShapeIndex { get; init; } = -1;

    /// <summary>Texture-slot index -> game-relative path (same encoding as
    /// <see cref="MeshOverride.Textures"/>).</summary>
    public IReadOnlyDictionary<int, string> Textures { get; init; } = new Dictionary<int, string>();
}

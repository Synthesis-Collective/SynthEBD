using System;
using System.Collections.Generic;

namespace SkyRender.Character.Offscreen;

/// <summary>
/// Offscreen render of one NPC, built by <see cref="NpcSceneBuilder"/>: the resolved NPC mesh
/// paths (already host-resolved into <see cref="ResolvedNpcMeshPaths"/>), optional texture / mesh
/// overrides, camera framing, the host scene hook and the character-tuning toggles, on top of the
/// generic <see cref="OffscreenRenderRequest"/> settings.
///
/// A host assembles these from its own <c>PortraitCreator</c> equivalent inputs to
/// drive the in-process mugshot renderer.
/// </summary>
public sealed class NpcRenderRequest : OffscreenRenderRequest
{
    /// <summary>The NPC's resolved body / hands / feet / head mesh paths,
    /// produced by the host's <see cref="INpcMeshDataSource"/> (or by
    /// A host's adapter that wraps NpcMeshResolver).</summary>
    public required ResolvedNpcMeshPaths MeshPaths { get; init; }

    /// <summary>Absolute path to a head NIF that should override
    /// <see cref="ResolvedNpcMeshPaths.HeadMeshPath"/>. Used by
    /// FaceGen-preview flows that bake head-part assignments into a
    /// temp NIF before rendering.</summary>
    public string? OverrideHeadMeshAbsolutePath { get; init; }

    /// <summary>Texture overrides applied after mesh load. Each
    /// <see cref="TextureOverride"/> targets a specific (body part, slot)
    /// pair on the loaded scene. Optional — null leaves textures at
    /// their NIF-embedded defaults.</summary>
    public IEnumerable<TextureOverride>? TextureOverrides { get; init; }

    /// <summary>Mesh overrides applied after mesh load — auxiliary shapes
    /// synthesized from <c>.nif</c> files the base NPC doesn't carry (the same
    /// neutral channel as <see cref="VM_CharacterViewer.ApplyMeshOverrides"/>:
    /// A host's auxiliary-armature subgroup mesh, a host's
    /// "Include Default Outfit" / "Include headgear"). Loaded, CPU-skinned to the
    /// scene skeleton, textured, slot-hidden (body armor hides the base body,
    /// headgear hides hair). Optional — null adds no extra shapes. Any
    /// unrenderable / skeleton-incompatible overrides are reported via
    /// <see cref="MeshOverrideWarningsOut"/>.</summary>
    public IEnumerable<MeshOverride>? MeshOverrides { get; init; }

    /// <summary>How the camera frames the character. See <see cref="CameraFraming"/>.</summary>
    public CameraFraming Camera { get; init; } = CameraFraming.PortraitDefault;

    /// <summary>a host's "normal map hack" toggle — enables a
    /// CPU-side normal-map fix-up for some non-vanilla mods that ship MSN
    /// textures the standard tangent-space shader can't read directly.
    /// Currently unused by this renderer (Phase D.2 will wire it).</summary>
    public bool EnableNormalMapHack { get; init; }

    /// <summary>a host's "use modded fallback textures" toggle.
    /// Currently unused (Phase D.2).</summary>
    public bool UseModdedFallbackTextures { get; init; } = true;

    /// <summary>Optional host hook invoked on the render thread against the
    /// per-request <see cref="VM_CharacterViewer"/> once the scene is fully
    /// built — after mesh load and texture / mesh overrides — and before the camera
    /// is configured and the frame drawn. Lets a host deform the body shapes
    /// (<see cref="VM_CharacterViewer.ApplyShapeDeformer"/>, which applies immediately
    /// here) and add overlays computed from the deformed geometry (an
    /// <see cref="IRenderOverlay"/> in <see cref="GlRenderer.Overlays"/>, built from
    /// <see cref="VM_CharacterViewer.GetShapePositions"/>). The VM is created per
    /// request, so nothing the hook sets carries into the next render. Must not
    /// touch WPF objects (it isn't on the dispatcher thread). An exception is
    /// logged and the frame still renders without the overlay.</summary>
    public Action<VM_CharacterViewer>? BeforeDraw { get; init; }

    /// <summary>
    /// Optional output collection. If non-null and <see cref="MeshOverrides"/>
    /// were supplied, the renderer appends every texture game-path that FIRST
    /// went missing while the mesh-override (attire) shapes were being built —
    /// i.e. the set-difference of <see cref="VM_CharacterViewer.MissingTexturePaths"/>
    /// taken across <see cref="VM_CharacterViewer.ApplyMeshOverrides"/>. This
    /// splits "the outfit/headgear references a texture that doesn't resolve"
    /// (an outfit-asset problem) from the base NPC's own missing textures, which
    /// continue to flow to <see cref="MissingTexturePathsOut"/> (copied out
    /// before overrides run). A missing texture already referenced by the base
    /// mesh stays attributed to the base list, not here.
    /// <para>Pass a <c>new List&lt;string&gt;()</c> to opt in; leave null to
    /// skip tracking. Independent of the other missing-* collections.</para>
    /// </summary>
    public List<string>? MissingOutfitTexturePathsOut { get; init; }

    /// <summary>
    /// Optional output collection. If non-null and <see cref="MeshOverrides"/>
    /// were supplied, the renderer appends the human-readable warning strings
    /// from <see cref="VM_CharacterViewer.MeshOverrideWarnings"/> — auxiliary
    /// override meshes that couldn't render (mesh not found / weighted to a bone
    /// in neither skeleton nor mesh NIF) or that rendered against an
    /// incompatible / absent skeleton (may be misaligned). Lets the host show
    /// the same missing-asset hint for offscreen renders that the live preview
    /// shows. Empty after the render means every supplied override rendered
    /// cleanly.
    /// <para>Pass a <c>new List&lt;string&gt;()</c> to opt in; leave null to
    /// skip tracking. Independent of <see cref="MissingMeshPathsOut"/> /
    /// <see cref="MissingTexturePathsOut"/>.</para>
    /// </summary>
    public List<string>? MeshOverrideWarningsOut { get; init; }

    /// <summary>
    /// Optional output collection: the structured counterpart of
    /// <see cref="MeshOverrideWarningsOut"/> (same entries, from
    /// <see cref="VM_CharacterViewer.MeshOverrideWarningDetails"/>). The
    /// <see cref="MeshOverrideWarningKind"/> lets the host route each warning —
    /// e.g. exclude <see cref="MeshOverrideWarningKind.StalePhysicsConfig"/>
    /// (the mod's own physics-XML link is broken; the render is still correct)
    /// from a persisted missing-asset list so it never re-stales a cached
    /// mugshot. Pass a fresh list to opt in; leave null to skip.
    /// </summary>
    public List<MeshOverrideWarning>? MeshOverrideWarningDetailsOut { get; init; }

    /// <summary>Strategy A: exclude hair from the shadow caster set so
    /// bangs don't cast a hard shadow onto the forehead. Default off.</summary>
    public bool ExcludeHairShadowCaster { get; init; } = false;

    /// <summary>Max view-space gap between a hair/beard fragment and the
    /// opaque surface behind it for screen-space AO to still apply to the
    /// hair (2.5.20+). Hair is excluded from the SSAO depth prepass, so its
    /// AO texel belongs to the surface behind the strands; past this gap it
    /// is background structure (collar edges, lip lines) and fades to
    /// unoccluded instead of ghosting through the beard. See
    /// GlRenderer.SsaoHairGap.</summary>
    public float SsaoHairGap { get; init; } = 0.8f;

    /// <summary>Eye catch-light toggle (2.5.13+). When true, eye shapes
    /// (those flagged <see cref="GlMesh.IsEye"/>) get an extra tight,
    /// bright Blinn-Phong specular spot from the key light layered on
    /// top of their regular specular. The single biggest "alive vs.
    /// dead" cue for eyes in portrait photography.</summary>
    public bool EnableEyeCatchlight { get; init; } = false;

    /// <summary>Skin-only saturation multiplier applied post-tint,
    /// pre-lighting. 1.0 is no-op (default). &gt;1 boosts chroma on
    /// shapes flagged as skin (BSLSP_FACE / BSLSP_SKINTINT); hair, eyes,
    /// and brows pass through unchanged. Compensates for downstream
    /// desaturation that washes race-distinguishing skin character
    /// toward neutral.</summary>
    public float SkinSaturationBoost { get; init; } = 1.0f;

    /// <summary>Hair finishing relief. When true, hair pixels skip the fresnel
    /// contour darkening and use a gentler exposure pull-down, so blonde hair
    /// is not crushed toward brown by the skin-tuned tone-map chain. Default
    /// false for back-compat; only meaningful with tone-mapping on.</summary>
    public bool TonemapHairRelief { get; init; } = false;

    /// <summary>Neutral-white-tint hair albedo compensation strength. Default 1.0
    /// (on) so a red wig with a white baked tint renders deep, not pink. Keyed on
    /// tint neutrality, so dark-tint and warm-blonde hair are exempt.</summary>
    public float HairAlbedoCompensate { get; init; } = 1.0f;
}

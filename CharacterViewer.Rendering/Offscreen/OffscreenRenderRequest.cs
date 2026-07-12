using System;
using System.Collections.Generic;
using System.Threading;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Single-shot offscreen render configuration. Carries everything the
/// offscreen renderer needs to produce one image: the resolved NPC mesh
/// paths (already host-resolved into <see cref="ResolvedNpcMeshPaths"/>),
/// optional texture / morph overrides, output dimensions, lighting,
/// camera framing, and background color.
///
/// NPC Plugin Chooser 2 will assemble these from its own
/// <c>PortraitCreator</c> equivalent inputs to drive the in-process
/// mugshot renderer (replacing the C++ NPC Portrait Creator subprocess).
/// </summary>
public sealed class OffscreenRenderRequest
{
    /// <summary>The NPC's resolved body / hands / feet / head mesh paths,
    /// produced by the host's <see cref="INpcMeshDataSource"/> (or by
    /// SynthEBD's adapter that wraps NpcMeshResolver).</summary>
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
    /// SynthEBD's auxiliary-armature subgroup mesh, NPC Plugin Chooser 2's
    /// "Include Default Outfit" / "Include headgear"). Loaded, CPU-skinned to the
    /// scene skeleton, textured, slot-hidden (body armor hides the base body,
    /// headgear hides hair). Optional — null adds no extra shapes. Any
    /// unrenderable / skeleton-incompatible overrides are reported via
    /// <see cref="MeshOverrideWarningsOut"/>.</summary>
    public IEnumerable<MeshOverride>? MeshOverrides { get; init; }

    /// <summary>BodySlide-style morph deformation to apply to body shapes.
    /// Optional — null leaves the body at its bind pose.</summary>
    public MorphSet? Morphs { get; init; }

    /// <summary>NPC weight (0–100) used to interpolate the morph deltas.
    /// Ignored when <see cref="Morphs"/> is null.</summary>
    public int MorphWeight { get; init; } = 50;

    /// <summary>Output framebuffer width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Output framebuffer height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Lighting layout (key/fill/rim positions + intensities).
    /// Null falls back to <see cref="CharacterViewerLightingPresets.DefaultLayout"/>.</summary>
    public CharacterViewerLightingLayout? Lighting { get; init; }

    /// <summary>Lighting color scheme (per-light RGB tints). Null falls back
    /// to <see cref="CharacterViewerLightingPresets.DefaultColorScheme"/>.</summary>
    public CharacterViewerLightingColorScheme? Colors { get; init; }

    /// <summary>Solid background color the framebuffer is cleared to before
    /// the character is rendered. Defaults to a neutral mid-gray that matches
    /// SynthEBD's main viewer background.</summary>
    public (byte R, byte G, byte B) BackgroundRgb { get; init; } = (105, 105, 105);

    /// <summary>How the camera frames the character. See <see cref="CameraFraming"/>.</summary>
    public CameraFraming Camera { get; init; } = CameraFraming.PortraitDefault;

    /// <summary>NPC Plugin Chooser 2's "normal map hack" toggle — enables a
    /// CPU-side normal-map fix-up for some non-vanilla mods that ship MSN
    /// textures the standard tangent-space shader can't read directly.
    /// Currently unused by this renderer (Phase D.2 will wire it).</summary>
    public bool EnableNormalMapHack { get; init; }

    /// <summary>NPC Plugin Chooser 2's "use modded fallback textures" toggle.
    /// Currently unused (Phase D.2).</summary>
    public bool UseModdedFallbackTextures { get; init; } = true;

    /// <summary>Optional cancellation token. The renderer aborts at the next
    /// await point if cancellation is requested.</summary>
    public CancellationToken Cancellation { get; init; }

    /// <summary>
    /// When true (default), a loose copy of an asset under the host's
    /// vanilla data folder takes precedence over any BSA copy of the same
    /// path — including BSAs scoped to a non-vanilla mod via
    /// <see cref="AdditionalScopes"/>. Mirrors the engine's actual rule
    /// that loose Data files override BSA-packed ones. Set false for
    /// strict-BSA mode (useful when previewing the original mod content
    /// without the user's installed loose-file overrides).
    /// <para>The FaceGen tree (paths containing <c>\FaceGenData\</c>) is
    /// excluded regardless of this flag — FaceGen NIFs and FaceTint DDS
    /// are NPC-keyed (FormID-named) and a vanilla loose copy must never
    /// preempt the mod's actual override or the original BSA content.
    /// Mod-folder loose FaceGen still applies.</para>
    /// </summary>
    public bool VanillaLooseOverridesBsa { get; init; } = true;

    /// <summary>
    /// When true, a loose copy of an asset in the host's vanilla data
    /// folder takes precedence over the same path in any mod folder
    /// listed in <see cref="AdditionalScopes"/> — letting the user's
    /// installed body / skin / texture replacers leak into mod-specific
    /// previews. Default false preserves normal mod priority. The
    /// FaceGen tree (<c>…\FaceGenData\…</c>) is excluded regardless of
    /// this flag because its NPC-specific assets are keyed by FormID and
    /// a vanilla loose copy would defeat the mod's actual face override.
    /// Only matches against the vanilla data folder's loose files —
    /// vanilla BSAs are NOT consulted for this fast-path.
    /// </summary>
    public bool VanillaLooseOverridesModLoose { get; init; } = false;

    /// <summary>
    /// Optional priority-ordered loose-file search paths consulted BEFORE
    /// <see cref="IDataFolderProvider.DataFolderPath"/> for this render only.
    /// Last entry wins (matches the "later mod folder beats earlier in the
    /// same conceptual mod" convention used by mod managers like MO2). Hosts
    /// use this to scope a render to a specific mod's loose overrides without
    /// mutating shared adapter state — clears automatically when the request
    /// goes out of scope.
    ///
    /// <para>Used by NPC Plugin Chooser 2's per-mod mugshot generation: each
    /// render targets a specific mod's <c>CorrespondingFolderPaths</c> so
    /// textures embedded inside NIFs (BSShaderTextureSet blocks) resolve
    /// against that mod's folders without bleeding into renders of other
    /// mods that ship the same relative path.</para>
    /// </summary>
    public IReadOnlyList<string>? AdditionalDataFolders { get; init; }

    /// <summary>
    /// Strict two-phase asset-resolution chain. When non-null, the resolver
    /// IGNORES <see cref="AdditionalDataFolders"/> /
    /// <see cref="IDataFolderProvider.DataFolderPath"/> /
    /// <see cref="IBsaArchiveProvider.TryLocateInBsa"/> broadcast and
    /// follows ONLY this chain in two phases:
    /// <list type="number">
    /// <item><b>Loose phase</b> — iterate scopes <i>last-to-first</i>; for
    /// each scope, check <c>Path.Combine(scope.FolderPath, &lt;subpath&gt;)</c>.
    /// First hit wins.</item>
    /// <item><b>Scoped-BSA phase</b> — if no loose hit, iterate scopes
    /// last-to-first again; for each scope, ask
    /// <see cref="IBsaArchiveProvider.TryLocateInScopedBsa"/> for any BSA
    /// at <c>scope.FolderPath</c> owned by one of <c>scope.ModKeyFileNames</c>
    /// that contains the file. First hit wins.</item>
    /// </list>
    ///
    /// <para>If neither phase matches, the asset is genuinely missing —
    /// no implicit fallback to vanilla data folder or BSA broadcast.
    /// Hosts that want vanilla as a fallback include it as the FIRST
    /// scope in the list (<c>scopes[0]</c> is checked LAST due to the
    /// last-to-first iteration).</para>
    ///
    /// <para>Used by NPC Plugin Chooser 2's per-mod mugshot generation
    /// to faithfully reproduce a single mod's contribution + vanilla
    /// fallback without cross-mod texture bleed. Mirrors the resolution
    /// order of NPC Portrait Creator's CLI (<c>--gamedata</c> +
    /// <c>--data</c> chain).</para>
    /// </summary>
    public IReadOnlyList<RenderScope>? AdditionalScopes { get; init; }

    /// <summary>
    /// Optional output collection. If non-null, the renderer appends each
    /// host-expected mesh game-path that the asset resolver could not
    /// locate during this render (loose scopes + scoped BSAs all missed).
    /// Empty after <see cref="IOffscreenRenderer.RenderToPngAsync"/>
    /// completes means every body / hands / feet / head / hair / tail /
    /// skeleton path that the host populated did resolve. Non-empty
    /// means the rendered PNG is missing one or more shapes — useful
    /// for surfacing an "incomplete render" UI hint over the resulting
    /// image (NPC Plugin Chooser 2's mugshot tile uses this to show a
    /// missing-mesh overlay when a mod ships a NIF path that doesn't
    /// resolve in the user's load order).
    /// <para>Pass a <c>new List&lt;string&gt;()</c> to opt in; leave
    /// null to skip tracking.</para>
    /// </summary>
    public List<string>? MissingMeshPathsOut { get; init; }

    /// <summary>
    /// Optional output collection. If non-null, the renderer appends each
    /// texture game-path the host's NIFs referenced that the texture
    /// manager couldn't decode (asset resolver missed, or the DDS load
    /// itself failed). The affected shapes are rendered as a wireframe
    /// placeholder rather than as flat-white billboards. Empty after
    /// <see cref="IOffscreenRenderer.RenderToPngAsync"/> completes means
    /// every texture the host expected did decode.
    /// <para>Pass a <c>new List&lt;string&gt;()</c> to opt in; leave
    /// null to skip tracking. Independent of
    /// <see cref="MissingMeshPathsOut"/> — a render can have one, both,
    /// or neither populated.</para>
    /// </summary>
    public List<string>? MissingTexturePathsOut { get; init; }

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
    /// Optional per-render timing sink. Pass a fresh <see cref="RenderTimings"/>
    /// to have the renderer record a wall-clock phase breakdown (setup / build /
    /// install / draw / readback / encode) for this render. Pure data — no
    /// logging — so it can be collected with verbose tracing OFF, which is the
    /// only way to get representative numbers (the verbose trace inflates the
    /// render it's measuring). Leave null to skip.
    /// </summary>
    public RenderTimings? TimingsOut { get; init; }

    /// <summary>
    /// Controls how the renderer handles alpha shapes whose diffuse texture
    /// couldn't be decoded. <c>true</c> (default): render as a wireframe
    /// placeholder in the missing-texture color so the missing-texture
    /// state is visible. <c>false</c>: cull the shape entirely (previous
    /// pre-2.5.6 behavior). Pairs with
    /// <see cref="MissingTexturePathsOut"/> — both populated independently
    /// so the host can show the overlay even when wireframe rendering is
    /// off.
    /// </summary>
    public bool RenderMissingTextureAsWireframe { get; init; } = true;

    /// <summary>
    /// Portrait-quality tone-mapping toggle (2.5.9+). When true, the final
    /// fragment-shader stage applies an ACES filmic tone-mapper, sRGB
    /// framebuffer encoding, and a mild saturation boost — pushing the
    /// output closer to a "looks-like-a-portrait" aesthetic and away from
    /// the flat linear "looks-like-a-render" look. Default false to keep
    /// hosts that don't opt in on the legacy pipeline; NPC Plugin
    /// Chooser 2 enables it via the Internal renderer settings panel.
    /// </summary>
    public bool EnableToneMapping { get; init; } = false;

    /// <summary>
    /// Shadow-map toggle (2.5.10+). When true, the renderer runs an extra
    /// depth-only pass from the key directional light's POV and samples
    /// the resulting shadow map with PCF in the main fragment shader,
    /// so the brow ridge casts onto the eye sockets, the nose onto the
    /// cheek, and hair onto the forehead. Default false to preserve
    /// legacy occlusion-free lighting for hosts that don't opt in.
    /// </summary>
    public bool EnableShadows { get; init; } = false;

    /// <summary>
    /// Screen-space ambient occlusion toggle (2.5.11+). When true, the
    /// renderer runs a depth pre-pass + SSAO post-process and samples
    /// the resulting AO texture per fragment in basic.frag, multiplying
    /// the result into the diffuse term so concave crevices (eye
    /// sockets, nostrils, lip line) read darker. Default false to
    /// preserve legacy occlusion-free lighting for hosts that don't
    /// opt in.
    /// </summary>
    public bool EnableAmbientOcclusion { get; init; } = false;

    /// <summary>SSAO sample radius in world units (2.5.12+). Larger =
    /// softer / broader AO; smaller = tight crevice-only AO. Defaults
    /// match the hardcoded value from 2.5.11 so existing pipelines
    /// produce identical output without setting this.</summary>
    public float SsaoRadius { get; init; } = 4.0f;

    /// <summary>SSAO depth-comparison bias in world units (2.5.12+).
    /// Higher reduces self-shadowing on flat surfaces; too high
    /// erases real AO.</summary>
    public float SsaoBias { get; init; } = 0.05f;

    /// <summary>SSAO power-curve exponent (2.5.12+). Higher = harder
    /// darkening in deep crevices, more subtle elsewhere.</summary>
    public float SsaoIntensity { get; init; } = 1.5f;

    /// <summary>Assumed SSAO occluder thickness in view-space units
    /// (2.5.20+). An occluder only darkens a fragment when its depth is
    /// within roughly this distance; geometry farther behind (e.g. a
    /// collar a few units behind a thin beard strand) is treated as a
    /// separate surface seen through a gap, not a local crevice wall.
    /// Also drives the bilateral SSAO blur's depth threshold. Defaults
    /// match GlRenderer's hardcoded 2.5.16 value.</summary>
    public float SsaoThickness { get; init; } = 1.5f;

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

    /// <summary>Subsurface scattering strength multiplier (2.5.14+). The
    /// renderer's SSS math now uses subsurfaceRolloff as the proper
    /// wrap parameter and adds a back-scatter / translucency term.
    /// This multiplier scales the visible SSS contribution; 0 disables
    /// it (matches pre-2.5.14 behavior at runtime - existing v5-stamped
    /// tiles validate against v5 hashes regardless), 1.0 is "honest"
    /// SSS at the source values, 1.5-2.0 boosts toward professional
    /// portrait reference. Default 0 for back-compat with hosts that
    /// don't opt in.</summary>
    public float SubsurfaceStrength { get; init; } = 0f;

    /// <summary>Skin-only saturation multiplier applied post-tint,
    /// pre-lighting. 1.0 is no-op (default). &gt;1 boosts chroma on
    /// shapes flagged as skin (BSLSP_FACE / BSLSP_SKINTINT); hair, eyes,
    /// and brows pass through unchanged. Compensates for downstream
    /// desaturation that washes race-distinguishing skin character
    /// toward neutral.</summary>
    public float SkinSaturationBoost { get; init; } = 1.0f;

    /// <summary>Vignette inner radius in NDC units (2.5.15+). Pixels
    /// within this circular zone of screen center are unaffected; the
    /// darkening smoothsteps from this radius out to the corner
    /// (sqrt(2)). Folded under the tone-mapping path in basic.frag so
    /// hosts opting out of <see cref="EnableToneMapping"/> get the
    /// legacy linear pipeline regardless of vignette settings. Default
    /// 0.7 mirrors the pre-2.5.15 hardcoded radius.</summary>
    public float VignetteRadius { get; init; } = 0.7f;

    /// <summary>Vignette darkening strength (2.5.15+). 0 = off (no
    /// darkening anywhere); 1.0 = corners go to black. Default 0 for
    /// back-compat with hosts that don't opt in - pre-2.5.15 hardcoded
    /// behavior is approximately Radius=0.7 / Intensity=0.3.</summary>
    public float VignetteIntensity { get; init; } = 0f;

    /// <summary>Tone-map exposure multiplier (2.5.19+). 1.0 = neutral
    /// (the legacy hardcoded look); &gt;1 brightens, &lt;1 darkens. Scales
    /// the linear color into the ACES curve. Folded under the tone-mapping
    /// path in basic.frag, so it only takes effect when
    /// <see cref="EnableToneMapping"/> is on. Default 1.0 for back-compat.</summary>
    public float Exposure { get; init; } = 1.0f;

    /// <summary>Hair finishing relief. When true, hair pixels skip the fresnel
    /// contour darkening and use a gentler exposure pull-down, so blonde hair
    /// is not crushed toward brown by the skin-tuned tone-map chain. Default
    /// false for back-compat; only meaningful with tone-mapping on.</summary>
    public bool TonemapHairRelief { get; init; } = false;

    /// <summary>Daylight boost: scales the directional lights by
    /// <see cref="DaylightBoostIntensity"/> + slight warmth (ambient
    /// untouched). Default false for back-compat.</summary>
    public bool DaylightBoost { get; init; } = false;

    /// <summary>Directional-light gain when <see cref="DaylightBoost"/> is on.
    /// 1.0 = warmth only; higher brightens. Default 1.5.</summary>
    public float DaylightBoostIntensity { get; init; } = 1.5f;

    /// <summary>Bloom: bright-pass + blur glow composited over the scene.
    /// Default false for back-compat; only meaningful with tone-mapping on.</summary>
    public bool EnableBloom { get; init; } = false;

    /// <summary>Bloom composite gain when <see cref="EnableBloom"/> is on.
    /// 0 = no glow; higher = stronger. Default 0.7.</summary>
    public float BloomIntensity { get; init; } = 0.7f;

    /// <summary>Optional thread-agnostic diagnostic sink for per-render
    /// decisions the renderer would otherwise emit silently — currently the
    /// MeshAware camera fitter's per-shape bbox / union / distance trace.
    /// Built by the host as a closure that writes to whatever destination
    /// is appropriate (a flow-scoped log file, an in-memory buffer, etc.)
    /// and survives the renderer's dedicated render thread, which does NOT
    /// inherit the host's AsyncLocal call context. Null = silent.</summary>
    public Action<string>? DiagnosticLog { get; init; }
}

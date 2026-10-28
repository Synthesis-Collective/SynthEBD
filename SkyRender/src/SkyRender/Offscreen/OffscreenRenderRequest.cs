using System;
using System.Collections.Generic;
using System.Threading;

namespace SkyRender.Offscreen;

/// <summary>
/// Single-shot offscreen render configuration: what the offscreen renderer itself needs to produce one
/// image (output size, background, lighting, asset-resolution scopes, the generic shading toggles,
/// cancellation, diagnostics and out-lists), plus <see cref="Scene"/> for builder-specific input.
/// What is drawn comes from the renderer's <see cref="IOffscreenSceneBuilder"/>; the NPC builder's
/// request is SkyRender.Character's <c>NpcRenderRequest</c>, which derives from this class.
/// </summary>
public class OffscreenRenderRequest
{
    /// <summary>Optional builder-specific scene description for a host-supplied
    /// <see cref="IOffscreenSceneBuilder"/> (the NPC builder ignores it and reads
    /// SkyRender.Character's <c>NpcRenderRequest</c> members instead). The renderer never reads it.</summary>
    public object? Scene { get; init; }

    /// <summary>Output framebuffer width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Output framebuffer height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Lighting layout (key/fill/rim positions + intensities).
    /// Null falls back to <see cref="LightingPresets.DefaultLayout"/>.</summary>
    public LightingLayout? Lighting { get; init; }

    /// <summary>Lighting color scheme (per-light RGB tints). Null falls back
    /// to <see cref="LightingPresets.DefaultColorScheme"/>.</summary>
    public LightingColorScheme? Colors { get; init; }

    /// <summary>Solid background color the framebuffer is cleared to before
    /// the character is rendered. Defaults to a neutral mid-gray.</summary>
    public (byte R, byte G, byte B) BackgroundRgb { get; init; } = (105, 105, 105);

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
    /// installed asset replacers leak into mod-specific
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
    /// <para>Useful e.g. for per-mod portrait generation, where each render
    /// targets one mod's folders so textures embedded inside NIFs (BSShaderTextureSet blocks) resolve
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
    /// <para>Useful for per-mod portrait generation,
    /// to faithfully reproduce a single mod's contribution + vanilla
    /// fallback without cross-mod texture bleed. Mirrors the resolution
    /// order of NPC Portrait Creator's CLI (<c>--gamedata</c> +
    /// <c>--data</c> chain).</para>
    /// </summary>
    public IReadOnlyList<RenderScope>? AdditionalScopes { get; init; }

    /// <summary>
    /// Engine-order resolution mode. When true, every asset in this
    /// render — base meshes, FaceGen and TXST textures, NIF-embedded texture
    /// paths, skeletons, and mesh overrides alike — resolves in the order the
    /// game will actually use once the host's generated output is in place:
    /// <list type="number">
    /// <item>the non-vanilla <see cref="AdditionalScopes"/> (per-scope
    /// loose-then-own-BSAs blocks, last-to-first) — these assets are destined
    /// to be copied into the output, which deploys loose and wins;</item>
    /// <item>the vanilla scope's loose folder (under a mod manager's VFS,
    /// every enabled mod's loose files);</item>
    /// <item>the broadcast archive tier
    /// (<see cref="IBsaArchiveProvider.TryLocateInBsa"/>), provider-ranked —
    /// A host restricts it to data-folder archives of enabled
    /// plugins and ranks by load order;</item>
    /// <item>scopes flagged <see cref="RenderScope.DeprioritizeBelowDataFolder"/>,
    /// as a last resort.</item>
    /// </list>
    /// Default false preserves the strict two-phase walk (miss = NotFound),
    /// which depicts ONE mod's contribution without cross-mod bleed — the
    /// right mode when no generated output will exist to justify the wider
    /// view. A per-override fallback flag (SkyRender.Character's mesh overrides)
    /// still widens individual overrides when this is off.
    /// </summary>
    public bool AllowLoadOrderFallback { get; init; }

    /// <summary>
    /// Optional output collection. If non-null, the renderer appends each
    /// host-expected mesh game-path that the asset resolver could not
    /// locate during this render (loose scopes + scoped BSAs all missed).
    /// Empty after <see cref="IOffscreenRenderer.RenderToPngAsync"/>
    /// completes means every body / hands / feet / head / hair / tail /
    /// skeleton path that the host populated did resolve. Non-empty
    /// means the rendered PNG is missing one or more shapes — useful
    /// for surfacing an "incomplete render" UI hint over the resulting
    /// image (e.g. to show a missing-mesh overlay when a mod ships a NIF path that doesn't
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
    /// Optional output collection. If non-null and
    /// <see cref="AllowLoadOrderFallback"/> is on, the renderer appends the
    /// game-relative path of every asset that engine-order resolution
    /// satisfied from the DATA FOLDER rather than the render's mod scopes —
    /// Tier 2 (data-folder loose) and Tier 3 (broadcast archives of enabled
    /// plugins) hits; see <see cref="AssetSource.ViaDataFolderFallback"/>.
    /// Each such asset is a runtime dependency of the depicted mod: the
    /// render (and the game) only shows it while whichever mod
    /// supplies it stays activated. Deduplicated case-insensitively and
    /// sorted; hits from a preceding <see cref="IOffscreenRenderer.PrewarmAsync"/>
    /// on the same request instance are included, since the render may
    /// consume the prewarmed result without re-resolving.
    /// <para><b>Referencer-scoped:</b> only assets whose REFERENCE originates
    /// in-scope are listed. When a NIF is itself resolved via the data-folder
    /// fallback (typically the user's global body/skin baseline standing in
    /// for a body the mod doesn't ship), that NIF appears here but the
    /// textures / physics XMLs it references do not — they are the baseline's
    /// own internals, and the parent NIF's line already carries the
    /// keep-activated dependency. Textures referenced by the mod's own
    /// in-scope NIFs (FaceGen, shipped attire) always report.</para>
    /// <para>Pass a <c>new List&lt;string&gt;()</c> to opt in; leave null to
    /// skip tracking. Independent of the missing-* collections.</para>
    /// </summary>
    public List<string>? DataFolderFallbackPathsOut { get; init; }

    /// <summary>Optional, for a request that is only prewarmed and never rendered (a
    /// host warming a tile ahead of time with a request of its own). When non-null,
    /// <see cref="IOffscreenRenderer.PrewarmAsync"/> appends the data-folder-fallback
    /// game paths recorded during the warm-up, sorted, so the host can carry them into
    /// the later render's own request — which may hit the warm caches without
    /// re-resolving those paths. Enables tracking by itself (no
    /// <see cref="DataFolderFallbackPathsOut"/> needed). Leave null for the ordinary
    /// prewarm-then-render flow on one request, where the render reports them.</summary>
    public List<string>? PrewarmFallbackPathsOut { get; init; }

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
    /// Portrait-quality tone-mapping toggle. When true, the final
    /// fragment-shader stage applies an ACES filmic tone-mapper, sRGB
    /// framebuffer encoding, and a mild saturation boost — pushing the
    /// output closer to a "looks-like-a-portrait" aesthetic and away from
    /// the flat linear "looks-like-a-render" look. Default false to keep
    /// hosts that don't opt in on the legacy pipeline.
    /// </summary>
    public bool EnableToneMapping { get; init; } = false;

    /// <summary>
    /// Shadow-map toggle. When true, the renderer runs an extra
    /// depth-only pass from the key directional light's POV and samples
    /// the resulting shadow map with PCF in the main fragment shader,
    /// so the brow ridge casts onto the eye sockets, the nose onto the
    /// cheek, and hair onto the forehead. Default false to preserve
    /// legacy occlusion-free lighting for hosts that don't opt in.
    /// </summary>
    public bool EnableShadows { get; init; } = false;

    /// <summary>Strategy B (default ON): soften the cast shadow with a
    /// constant bias + wide PCF kernel, removing the "brow ridge" and the
    /// over-dark neck under the jaw. Only relevant when EnableShadows.</summary>
    public bool SoftenShadowEdges { get; init; } = true;

    /// <summary>Strategy B PCF kernel step in shadow-map texels.</summary>
    public float ShadowPcfRadius { get; init; } = 1.5f;

    /// <summary>Strategy C: tighten the light's orthographic frustum so the
    /// shadow bias resolves to fewer world units. Default off.</summary>
    public bool TightShadowFrustum { get; init; } = false;

    /// <summary>Strategy C light-frustum scene radius (world units).</summary>
    public float ShadowFrustumRadius { get; init; } = 100f;

    /// <summary>
    /// Screen-space ambient occlusion toggle. When true, the
    /// renderer runs a depth pre-pass + SSAO post-process and samples
    /// the resulting AO texture per fragment in basic.frag, multiplying
    /// the result into the diffuse term so concave crevices (eye
    /// sockets, nostrils, lip line) read darker. Default false to
    /// preserve legacy occlusion-free lighting for hosts that don't
    /// opt in.
    /// </summary>
    public bool EnableAmbientOcclusion { get; init; } = false;

    /// <summary>SSAO sample radius in world units. Larger =
    /// softer / broader AO; smaller = tight crevice-only AO. Defaults
    /// match the hardcoded value from 2.5.11 so existing pipelines
    /// produce identical output without setting this.</summary>
    public float SsaoRadius { get; init; } = 4.0f;

    /// <summary>SSAO depth-comparison bias in world units.
    /// Higher reduces self-shadowing on flat surfaces; too high
    /// erases real AO.</summary>
    public float SsaoBias { get; init; } = 0.05f;

    /// <summary>SSAO power-curve exponent. Higher = harder
    /// darkening in deep crevices, more subtle elsewhere.</summary>
    public float SsaoIntensity { get; init; } = 1.5f;

    /// <summary>Assumed SSAO occluder thickness in view-space units
    ///. An occluder only darkens a fragment when its depth is
    /// within roughly this distance; geometry farther behind (e.g. a
    /// collar a few units behind a thin beard strand) is treated as a
    /// separate surface seen through a gap, not a local crevice wall.
    /// Also drives the bilateral SSAO blur's depth threshold. Defaults
    /// match GlRenderer's hardcoded 2.5.16 value.</summary>
    public float SsaoThickness { get; init; } = 1.5f;

    /// <summary>Subsurface scattering strength multiplier. The
    /// renderer's SSS math now uses subsurfaceRolloff as the proper
    /// wrap parameter and adds a back-scatter / translucency term.
    /// This multiplier scales the visible SSS contribution; 0 disables
    /// it (matches pre-2.5.14 behavior at runtime), 1.0 is "honest"
    /// SSS at the source values, 1.5-2.0 boosts toward professional
    /// portrait reference. Default 0 for back-compat with hosts that
    /// don't opt in.</summary>
    public float SubsurfaceStrength { get; init; } = 0f;

    /// <summary>Vignette inner radius in NDC units. Pixels
    /// within this circular zone of screen center are unaffected; the
    /// darkening smoothsteps from this radius out to the corner
    /// (sqrt(2)). Folded under the tone-mapping path in basic.frag so
    /// hosts opting out of <see cref="EnableToneMapping"/> get the
    /// legacy linear pipeline regardless of vignette settings. Default
    /// 0.7 mirrors the pre-2.5.15 hardcoded radius.</summary>
    public float VignetteRadius { get; init; } = 0.7f;

    /// <summary>Vignette darkening strength. 0 = off (no
    /// darkening anywhere); 1.0 = corners go to black. Default 0 for
    /// back-compat with hosts that don't opt in - pre-2.5.15 hardcoded
    /// behavior is approximately Radius=0.7 / Intensity=0.3.</summary>
    public float VignetteIntensity { get; init; } = 0f;

    /// <summary>Tone-map exposure multiplier. 1.0 = neutral
    /// (the legacy hardcoded look); &gt;1 brightens, &lt;1 darkens. Scales
    /// the linear color into the ACES curve. Folded under the tone-mapping
    /// path in basic.frag, so it only takes effect when
    /// <see cref="EnableToneMapping"/> is on. Default 1.0 for back-compat.</summary>
    public float Exposure { get; init; } = 1.0f;

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
    /// decisions the renderer would otherwise emit silently — e.g. a scene builder's camera-fit trace.
    /// Built by the host as a closure that writes to whatever destination
    /// is appropriate (a flow-scoped log file, an in-memory buffer, etc.)
    /// and survives the renderer's dedicated render thread, which does NOT
    /// inherit the host's AsyncLocal call context. Null = silent.</summary>
    public Action<string>? DiagnosticLog { get; init; }
}

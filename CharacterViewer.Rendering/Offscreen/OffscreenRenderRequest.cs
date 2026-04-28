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
    /// When true, the renderer drops every BSA-extracted file it touched
    /// during this render (and the resolver's cache entries for them) once
    /// the PNG bytes are returned. Suited to one-and-done batch flows like
    /// NPC Plugin Chooser 2's mugshot tile generation, where the user
    /// produces each per-NPC PNG once and never needs the extracted
    /// sources again — the temp cache would otherwise accumulate
    /// indefinitely. Default <c>false</c> preserves cross-render parse /
    /// pixel caching for hosts that re-render the same NPC many times
    /// (e.g. SynthEBD's BodySlide preset switcher).
    /// </summary>
    public bool ClearExtractionCacheAfterRender { get; init; } = false;

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
}

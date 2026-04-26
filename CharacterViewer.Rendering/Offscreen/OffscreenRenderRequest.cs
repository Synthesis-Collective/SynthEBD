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
}

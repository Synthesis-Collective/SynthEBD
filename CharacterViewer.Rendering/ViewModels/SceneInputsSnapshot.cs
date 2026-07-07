using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// Immutable, GL-free snapshot of the neutral scene inputs the live
/// <see cref="VM_CharacterViewer"/> last received (mesh paths, head override,
/// texture / mesh overrides, morph + weight, lighting, background, and the
/// asset-resolution scoping). Produced by
/// <see cref="VM_CharacterViewer.TryGetSceneInputsSnapshot"/> so a host can
/// re-express the same inputs as an
/// <see cref="Offscreen.OffscreenRenderRequest"/> without reaching into the
/// VM's GL state.
///
/// <para>Consumed by SynthEBD's software fallback preview: when the embedded
/// <c>GLWpfControl</c> can't start (no <c>WGL_NV_DX_interop</c> — Wine, VMs,
/// RDP), the live VM retains its inputs but never uploads a GL scene, and the
/// fallback controller renders them through the offscreen
/// <see cref="Offscreen.IOffscreenRenderer"/> instead. Everything a request
/// needs except the per-render output size, camera framing, and cancellation
/// token — those the caller supplies each frame.</para>
/// </summary>
public sealed record SceneInputsSnapshot(
    ResolvedNpcMeshPaths MeshPaths,
    string? OverrideHeadMeshAbsolutePath,
    IReadOnlyList<TextureOverride>? TextureOverrides,
    IReadOnlyList<MeshOverride>? MeshOverrides,
    MorphSet? Morphs,
    int MorphWeight,
    CharacterViewerLightingLayout? Lighting,
    CharacterViewerLightingColorScheme? Colors,
    (byte R, byte G, byte B) BackgroundRgb,
    IReadOnlyList<RenderScope>? AdditionalScopes,
    IReadOnlyList<string>? AdditionalDataFolders,
    bool VanillaLooseOverridesBsa,
    bool VanillaLooseOverridesModLoose);

using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// BSA enumeration + extraction surface used by <see cref="GameAssetResolver"/>
/// to satisfy lookups that miss the loose-files check. The interface deliberately
/// works in disk paths only — no Mutagen archive types leak into the viewer
/// subsystem — so other host apps can implement it against their own BSA stack.
///
/// SynthEBD adapts <see cref="BSAHandler"/> behind this interface
/// (see <see cref="SynthEbdBsaProviderAdapter"/>).
/// </summary>
public interface IBsaArchiveProvider
{
    /// <summary>Eager-opens all archives in load order so subsequent lookups hit
    /// the in-memory index instead of paying first-touch I/O.</summary>
    void EnsureAllArchivesOpened();

    /// <summary>Reports whether <paramref name="subpath"/> exists inside any
    /// open archive. <paramref name="containingBsaPath"/> receives the absolute
    /// path of the BSA that owns the file, useful for tooltips/diagnostics.</summary>
    bool TryLocateInBsa(string subpath, out string? containingBsaPath);

    /// <summary>
    /// Reports whether <paramref name="subpath"/> exists inside any BSA
    /// physically located under <paramref name="folderPath"/> (i.e.
    /// <c>bsaPath.StartsWith(folderPath)</c>) AND owned by one of
    /// <paramref name="modKeyFileNames"/> (BSAs are typically named after
    /// their owning plugin, e.g. <c>MyMod.bsa</c> / <c>MyMod - Textures.bsa</c>).
    /// Returns true on the first hit; <paramref name="containingBsaPath"/>
    /// receives the absolute path of the BSA.
    ///
    /// <para>Used by <see cref="GameAssetResolver"/> to implement
    /// <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/> /
    /// <see cref="VM_CharacterViewer.AdditionalScopes"/> — strict
    /// per-folder-per-mod BSA scoping so the active mod's archives win
    /// over vanilla even when both ship the same relative path (e.g.
    /// override FaceGen NIFs at <c>meshes\…\FaceGenData\FaceGeom\Skyrim.esm\…nif</c>).</para>
    /// </summary>
    bool TryLocateInScopedBsa(
        string subpath,
        string folderPath,
        IReadOnlyList<string> modKeyFileNames,
        out string? containingBsaPath);

    /// <summary>Extracts the named asset from its containing BSA to
    /// <paramref name="destPath"/>, creating parent directories as needed.</summary>
    bool TryExtractToDisk(string subpath, string destPath);
}

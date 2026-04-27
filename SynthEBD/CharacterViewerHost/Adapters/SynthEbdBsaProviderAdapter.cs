using System.Collections.Generic;
using Mutagen.Bethesda.Archives;

namespace SynthEBD;

/// <summary>
/// Adapts SynthEBD's <see cref="BSAHandler"/> to <see cref="IBsaArchiveProvider"/>
/// so the viewer's asset resolver never sees Mutagen archive types directly.
///
/// The interface deliberately works in disk paths only — the adapter does the
/// double-lookup (locate + re-locate-for-extract) but BSAHandler caches its
/// per-archive file index so the second call is O(1).
/// </summary>
public sealed class SynthEbdBsaProviderAdapter : IBsaArchiveProvider
{
    private readonly BSAHandler _inner;

    public SynthEbdBsaProviderAdapter(BSAHandler inner) => _inner = inner;

    public void EnsureAllArchivesOpened() => _inner.EnsureAllArchivesOpened();

    public bool TryLocateInBsa(string subpath, out string? containingBsaPath)
    {
        if (_inner.TryFindFileInAnyArchive(subpath, out _, out containingBsaPath))
        {
            return true;
        }
        containingBsaPath = null;
        return false;
    }

    public bool TryExtractToDisk(string containingBsaPath, string subpath, string destPath)
    {
        // Extract from the EXACT BSA the caller specified — never broadcast.
        // The renderer's scoped resolution can pick a non-vanilla archive
        // when multiple BSAs ship the same relative path (e.g. override
        // FaceGen NIFs); broadcasting here would extract whichever the
        // index returns first and silently substitute vanilla content.
        if (string.IsNullOrEmpty(containingBsaPath)) return false;
        if (!_inner.TryFindFileInArchive(containingBsaPath, subpath, out IArchiveFile archiveFile))
        {
            return false;
        }
        return _inner.TryExtractFileFromBSA(archiveFile, destPath);
    }

    /// <summary>
    /// Stub: SynthEBD does not currently set <c>OffscreenRenderRequest.AdditionalScopes</c>
    /// or <c>VM_CharacterViewer.AdditionalScopes</c>, so this method is never
    /// reached on the SynthEBD code path. NPC Plugin Chooser 2's adapter
    /// implements the full strict scoped lookup. If SynthEBD ever wants
    /// per-mod-folder BSA scoping, add the necessary
    /// <c>TryFindFileInArchiveAtFolder(modKey, folder, subpath, ...)</c>
    /// helper to <see cref="BSAHandler"/> and wire it through here.
    /// </summary>
    public bool TryLocateInScopedBsa(
        string subpath,
        string folderPath,
        IReadOnlyList<string> modKeyFileNames,
        out string? containingBsaPath)
    {
        containingBsaPath = null;
        return false;
    }
}

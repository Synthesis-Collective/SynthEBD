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

    public bool TryExtractToDisk(string subpath, string destPath)
    {
        if (!_inner.TryFindFileInAnyArchive(subpath, out IArchiveFile archiveFile, out _))
        {
            return false;
        }
        return _inner.TryExtractFileFromBSA(archiveFile, destPath);
    }
}

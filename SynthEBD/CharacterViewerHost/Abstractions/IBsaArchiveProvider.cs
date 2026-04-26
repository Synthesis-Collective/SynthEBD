namespace SynthEBD;

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

    /// <summary>Extracts the named asset from its containing BSA to
    /// <paramref name="destPath"/>, creating parent directories as needed.</summary>
    bool TryExtractToDisk(string subpath, string destPath);
}

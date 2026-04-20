using System;
using System.Collections.Generic;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Flips <see cref="BodyTypeRegistryEntry.IsInstalled"/> based on whether any of an entry's
/// <see cref="BodyTypeRegistryEntry.IdentityFingerprints"/> resolve to an existing file or
/// directory under the game's Data folder.
///
/// Runs cheaply (a dozen <see cref="File.Exists"/> / <see cref="Directory.Exists"/> calls per
/// entry) so no caching is needed -- safe to re-invoke whenever the user adds a body mod or
/// edits the registry.
/// </summary>
public class BodyTypeFingerprintScanner
{
    private readonly Logger _logger;

    public BodyTypeFingerprintScanner(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// For each entry in <paramref name="entries"/>, sets <see cref="BodyTypeRegistryEntry.IsInstalled"/>
    /// to true iff any of its fingerprints resolves under <paramref name="dataFolder"/>.
    /// Accepts forward or back slashes in fingerprint paths; empty / null paths are skipped.
    /// </summary>
    public void ScanInstalled(string dataFolder, IEnumerable<BodyTypeRegistryEntry> entries)
    {
        if (entries == null) return;

        if (string.IsNullOrWhiteSpace(dataFolder) || !Directory.Exists(dataFolder))
        {
            foreach (var entry in entries)
            {
                if (entry != null) entry.IsInstalled = false;
            }
            _logger.LogMessage("BodyTypeFingerprintScanner: data folder missing -- marking all registry entries uninstalled.");
            return;
        }

        foreach (var entry in entries)
        {
            if (entry == null) continue;
            entry.IsInstalled = HasAnyFingerprint(dataFolder, entry);
        }
    }

    private static bool HasAnyFingerprint(string dataFolder, BodyTypeRegistryEntry entry)
    {
        if (entry.IdentityFingerprints == null) return false;
        foreach (var rawPath in entry.IdentityFingerprints)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var normalized = rawPath.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar)
                                    .TrimStart(Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(dataFolder, normalized);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return true;
            }
        }
        return false;
    }
}

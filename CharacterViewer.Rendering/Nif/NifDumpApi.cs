using System;
using System.Text;
using nifly;

namespace CharacterViewer.Rendering;

/// <summary>
/// Public, gate-free entry point to the NifSkope-style diagnostic dump in
/// <see cref="NifDiagnosticDumper"/>. Intended for standalone tooling (the
/// CharacterViewer.NifDump CLI) rather than in-app verbose logging: it ignores
/// both <see cref="NifDiagnosticDumper.FULL_LOGGING"/> and
/// <see cref="CharacterViewerLogGate.Verbose"/> and returns the dump as a
/// string instead of routing it through a host logger.
/// </summary>
public static class NifDumpApi
{
    /// <summary>
    /// Loads a .nif from disk and returns the full diagnostic dump (header,
    /// block index, scene graph, per-block detail, referenced texture paths).
    /// Texture pixel stats are omitted (no <see cref="GameAssetResolver"/> in
    /// standalone use) — texture paths are still listed. Throws on I/O or
    /// parse failure so a CLI caller can surface the error.
    /// </summary>
    public static string DumpFileToString(string nifPath)
    {
        using var nif = new NifFile();
        if (nif.Load(nifPath) != 0)
            throw new InvalidOperationException($"nifly failed to parse '{nifPath}' (Load returned non-zero).");
        return DumpToString(nif, nifPath);
    }

    /// <summary>
    /// Returns the full diagnostic dump of an already-loaded <see cref="NifFile"/>.
    /// </summary>
    public static string DumpToString(NifFile nif, string nifPath)
    {
        ArgumentNullException.ThrowIfNull(nif);
        var collector = new StringCollectorLogger();
        NifDiagnosticDumper.Dump(nif, nifPath, collector, assetResolver: null);
        return collector.ToString();
    }

    /// <summary>Accumulates dump sections into a single string. The dumper
    /// flushes section-by-section via LogMessage and trims each section's
    /// trailing newline, so re-insert one between sections.</summary>
    private sealed class StringCollectorLogger : ICharacterViewerLogger
    {
        private readonly StringBuilder _sb = new(64 * 1024);
        public void LogMessage(string message) => _sb.AppendLine(message);
        public void LogError(string message) => _sb.AppendLine("ERROR: " + message);
        public void LogError(string message, Exception ex) => _sb.AppendLine($"ERROR: {message}: {ex}");
        public override string ToString() => _sb.ToString();
    }
}

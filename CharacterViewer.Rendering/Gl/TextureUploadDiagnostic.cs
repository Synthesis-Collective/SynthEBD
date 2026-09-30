namespace CharacterViewer.Rendering;

/// <summary>Per-upload observation. Times measure CPU submission, not completed GPU work.
/// Decoded counters include any CPU consumers since cache creation, not only this manager.</summary>
public sealed record TextureUploadDiagnostic(string Path, string Route, string Reason,
    string Format, int Width, int Height, int Mips, long UploadedBytes,
    double ReadParseMs, double UploadMs, long DecodeCalls, long DecodedBytes,
    double? ReadMs, double? ParseMs, double DecodeLookupMs);

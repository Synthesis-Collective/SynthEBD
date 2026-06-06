namespace SynthEBD;

/// <summary>
/// A rule for trimming a leading path fragment (<see cref="PathToTrim"/>) off imported asset paths of a
/// given <see cref="Extension"/>, so paths become relative to the expected game folder.
/// </summary>
public class TrimPath
{
    public string Extension { get; set; } = "";
    public string PathToTrim { get; set; } = "";
}
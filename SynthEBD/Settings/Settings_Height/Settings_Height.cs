namespace SynthEBD;

public class Settings_Height
{
    public bool bChangeNPCHeight { get; set; } = true;
    public bool bChangeRaceHeight { get; set; } = true;
    public bool bOverwriteNonDefaultNPCHeights { get; set; } = true;
    public bool bApplyWithoutOverride { get; set; } = false;
    public string SelectedHeightConfig { get; set; } = "";
}
namespace SynthEBD;

/// <summary>Settings POCO for the Height axis, persisted as JSON. Controls whether per-NPC and
/// per-race heights are randomized, override behavior, and which height config is active.</summary>
public class Settings_Height
{
    /// <summary>Randomize each NPC's individual height.</summary>
    public bool bChangeNPCHeight { get; set; } = true;
    /// <summary>Apply the config's per-race base height to RACE records.</summary>
    public bool bChangeRaceHeight { get; set; } = true;
    /// <summary>Overwrite NPC heights that already differ from the default rather than leaving them.</summary>
    public bool bOverwriteNonDefaultNPCHeights { get; set; } = true;
    /// <summary>Write heights without creating override records where possible.</summary>
    public bool bApplyWithoutOverride { get; set; } = false;
    /// <summary>Name of the active height config.</summary>
    public string SelectedHeightConfig { get; set; } = "";
    /// <summary>UI-only: number of height-group tiles laid out per row in the Height Assignment editor.</summary>
    public int HeightGroupsPerRow { get; set; } = 4;
}
using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class RaceAlias
{
    public FormKey Race { get; set; } = new();
    public FormKey AliasRace { get; set; } = new();
    public bool bMale { get; set; } = true;
    public bool bFemale { get; set; } = true;

    public bool bApplyToAssets { get; set; } = false;
    public bool bApplyToBodyGen { get; set; } = false;
    public bool bApplyToHeight { get; set; } = false;
}
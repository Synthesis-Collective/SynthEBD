using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Redirects one race to another for assignment purposes (an NPC of <see cref="Race"/> is treated as
/// <see cref="AliasRace"/>), scoped by sex and by which assignment axes (assets/bodygen/height/headparts)
/// the alias applies to.
/// </summary>
public class RaceAlias
{
    public FormKey Race { get; set; } = new();
    public FormKey AliasRace { get; set; } = new();
    public bool bMale { get; set; } = true;
    public bool bFemale { get; set; } = true;

    public bool bApplyToAssets { get; set; } = false;
    public bool bApplyToBodyGen { get; set; } = false;
    public bool bApplyToHeight { get; set; } = false;
    public bool bApplyToHeadParts { get; set; } = false;
}
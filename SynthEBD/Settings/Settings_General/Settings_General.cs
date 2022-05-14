using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD;

public class Settings_General
{
    public bool bShowToolTips { get; set; } = true;
    public bool bChangeMeshesOrTextures { get; set; } = true;
    public BodyShapeSelectionMode BodySelectionMode { get; set; } = BodyShapeSelectionMode.None;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool bChangeHeight { get; set; } = true;
    public string OutputDataFolder { get; set; } = "";
    public bool bEnableConsistency { get; set; } = true;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bLinkNPCsWithSameName { get; set; } = true;
    public string PatchFileName { get; set; } = "SynthEBD";
    public bool bVerboseModeAssetsNoncompliant { get; set; } = false;
    public bool bVerboseModeAssetsAll { get; set; } = false;
    public List<FormKey> VerboseModeNPClist { get; set; } = new();
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    public List<FormKey> PatchableRaces { get; set; } = new()
    {
        Skyrim.Race.NordRace.FormKey,
        Skyrim.Race.BretonRace.FormKey,
        Skyrim.Race.DarkElfRace.FormKey,
        Skyrim.Race.HighElfRace.FormKey,
        Skyrim.Race.ImperialRace.FormKey,
        Skyrim.Race.OrcRace.FormKey,
        Skyrim.Race.RedguardRace.FormKey,
        Skyrim.Race.WoodElfRace.FormKey,
        Skyrim.Race.ElderRace.FormKey,
        Skyrim.Race.NordRaceVampire.FormKey,
        Skyrim.Race.BretonRaceVampire.FormKey,
        Skyrim.Race.DarkElfRaceVampire.FormKey,
        Skyrim.Race.HighElfRaceVampire.FormKey,
        Skyrim.Race.ImperialRaceVampire.FormKey,
        Skyrim.Race.OrcRaceVampire.FormKey,
        Skyrim.Race.RedguardRaceVampire.FormKey,
        Skyrim.Race.WoodElfRaceVampire.FormKey,
        Skyrim.Race.ElderRaceVampire.FormKey,
        Skyrim.Race.NordRaceAstrid.FormKey,
        Skyrim.Race.DA13AfflictedRace.FormKey,
        Dawnguard.Race.SnowElfRace.FormKey,
        Dawnguard.Race.DLC1NordRace.FormKey,
        Skyrim.Race.KhajiitRace.FormKey,
        Skyrim.Race.KhajiitRaceVampire.FormKey,
        Skyrim.Race.ArgonianRace.FormKey,

        Skyrim.Race.ArgonianRaceVampire.FormKey
    };

    public List<RaceAlias> RaceAliases { get; set; } = new()
    {
        new RaceAlias()
        {
            Race = Skyrim.Race.DA13AfflictedRace.FormKey,
            AliasRace = Skyrim.Race.BretonRace.FormKey,
            bMale = true,
            bFemale = true,
            bApplyToAssets = false,
            bApplyToBodyGen = true,
            bApplyToHeight = true
        },
    };

    public List<RaceGrouping> RaceGroupings { get; set; } = new()
    {
        DefaultRaceGroupings.Humanoid,
        DefaultRaceGroupings.HumanoidPlayable,
        DefaultRaceGroupings.HumanoidNonVampire,
        DefaultRaceGroupings.HumanoidVampire,
        DefaultRaceGroupings.HumanoidYoung,
        DefaultRaceGroupings.HumanoidYoungNonVampire,
        DefaultRaceGroupings.HumanoidYoungVampire,
        DefaultRaceGroupings.Elven,
        DefaultRaceGroupings.ElvenNonVampire,
        DefaultRaceGroupings.ElvenVampire,
        DefaultRaceGroupings.Elder,
        DefaultRaceGroupings.Khajiit,
        DefaultRaceGroupings.Argonian
    };

    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new()
    {
        DefaultAttributeGroups.CannotHaveDefinition,
        DefaultAttributeGroups.MustHaveDefinition,
        DefaultAttributeGroups.MustBeFit,
        DefaultAttributeGroups.MustBeAthletic,
        DefaultAttributeGroups.MustBeMuscular,
        DefaultAttributeGroups.CannotHaveScars,
        DefaultAttributeGroups.CanBeDirty,
        DefaultAttributeGroups.MustBeDirty,
        DefaultAttributeGroups.CanGetChubbyMorph
    };

    public bool OverwritePluginAttGroups { get; set; } = true;
}
public enum BodyShapeSelectionMode
{
    None,
    BodyGen,
    BodySlide
}

public enum BodySlideSelectionMode
{
    OBody,
    AutoBody
}
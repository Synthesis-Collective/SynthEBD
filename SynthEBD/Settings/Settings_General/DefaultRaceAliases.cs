using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>Builds the shipped default <see cref="RaceAlias"/> entries used to seed
/// <see cref="Settings_General.RaceAliases"/> when no user settings exist. Each static field
/// maps a non-patchable race (the Afflicted race, or a Charmers of the Reach custom race) onto a
/// vanilla race for one or more appearance axes.</summary>
public class DefaultRaceAliases
{
    /// <summary>Aliases the DA13 Afflicted race onto the vanilla Breton race (body/height/headparts only, not assets).</summary>
    public static RaceAlias RaceAliasAfflicted = new()
    {
        Race = Skyrim.Race.DA13AfflictedRace.FormKey,
        AliasRace = Skyrim.Race.BretonRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = false,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    /// <summary>Charmers of the Reach (COR_AllRace.esp) custom race aliases onto their vanilla
    /// equivalents for all axes. One field per vanilla race plus its vampire variant.</summary>
    public static RaceAlias RaceAliasCotR_Breton= new()
    {
        Race = FormKey.Factory("005734:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.BretonRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_BretonVampire = new()
    {
        Race = FormKey.Factory("005735:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.BretonRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_Imperial = new()
    {
        Race = FormKey.Factory("05A179:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.ImperialRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_ImperialVampire = new()
    {
        Race = FormKey.Factory("05A17A:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.ImperialRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_Nord = new()
    {
        Race = FormKey.Factory("05A184:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.NordRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_NordVampire = new()
    {
        Race = FormKey.Factory("05A185:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.NordRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_Redguard = new()
    {
        Race = FormKey.Factory("05A18E:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.RedguardRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_RedguardVampire = new()
    {
        Race = FormKey.Factory("05A18F:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.RedguardRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_DarkElf = new()
    {
        Race = FormKey.Factory("05A198:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.DarkElfRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_DarkElfVampire = new()
    {
        Race = FormKey.Factory("05A199:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.DarkElfRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_HighElf = new()
    {
        Race = FormKey.Factory("05A1A2:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.HighElfRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_HighElfVampire = new()
    {
        Race = FormKey.Factory("05A1A3:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.HighElfRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_WoodElf = new()
    {
        Race = FormKey.Factory("05A1AC:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.WoodElfRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_WoodElfVampire = new()
    {
        Race = FormKey.Factory("05A1AD:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.WoodElfRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_Orc = new()
    {
        Race = FormKey.Factory("05A1B0:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.OrcRace.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
    
    public static RaceAlias RaceAliasCotR_OrcVampire = new()
    {
        Race = FormKey.Factory("05A1B1:COR_AllRace.esp"),
        AliasRace = Skyrim.Race.OrcRaceVampire.FormKey,
        bMale = true,
        bFemale = true,
        bApplyToAssets = true,
        bApplyToBodyGen = true,
        bApplyToHeight = true,
        bApplyToHeadParts = true
    };
}
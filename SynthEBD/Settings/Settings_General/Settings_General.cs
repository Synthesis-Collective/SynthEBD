using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD;

/// <summary>The main global settings POCO for SynthEBD, persisted as JSON. Holds the master
/// patch-mode flags (which appearance axes are enabled), output paths, consistency/linking
/// options, the patchable-race list, race aliases, race groupings, attribute groups, verbose
/// logging options, and assorted UI/preview state.</summary>
public class Settings_General
{
    public bool bShowToolTips { get; set; } = true;
    /// <summary>Master switch for the Assets axis (texture/mesh patching).</summary>
    public bool bChangeMeshesOrTextures { get; set; } = true;
    /// <summary>Which body-shape system is active (none, BodyGen, or BodySlide).</summary>
    public BodyShapeSelectionMode BodySelectionMode { get; set; } = BodyShapeSelectionMode.None;
    /// <summary>When using BodySlide, which selector picks the preset (OBody or AutoBody).</summary>
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    /// <summary>Master switch for the Height axis.</summary>
    public bool bChangeHeight { get; set; } = true;
    /// <summary>Master switch for the Head Parts axis.</summary>
    public bool bChangeHeadParts { get; set; } = true;
    /// <summary>Skip head-part assignment for NPCs that use a custom (modded) head.</summary>
    public bool bHeadPartsExcludeCustomHeads { get; set; } = true;
    /// <summary>Override output directory for generated files; empty means use the default.</summary>
    public string OutputDataFolder { get; set; } = "";
    /// <summary>Optional appearance-merge target (EasyNPC or NPC NIF Merger) to coordinate with.</summary>
    public AppearanceMergeType AppearanceMergerType { get; set; } = AppearanceMergeType.None;
    /// <summary>Path to the EasyNPC profile used when <see cref="AppearanceMergerType"/> is EasyNPC.</summary>
    public string EasyNPCprofilePath { get; set; } = "";
    /// <summary>Path to the NPC NIF Merger token file used when <see cref="AppearanceMergerType"/> is NPC2.</summary>
    public string NPC2TokenPath { get; set; } = "";
    /// <summary>Persist assignments between runs so an NPC keeps the same appearance.</summary>
    public bool bEnableConsistency { get; set; } = true;
    public bool ExcludePlayerCharacter { get; set; } = true;
    /// <summary>Exclude preset/template NPCs (e.g. character-creation presets) from patching.</summary>
    public bool ExcludePresets { get; set; } = true;
    /// <summary>Auto-link NPCs sharing the same name so they receive matching assignments.</summary>
    public bool bLinkNPCsWithSameName { get; set; } = true;

    /// <summary>Close the bundled 7-Zip process once extraction/installation completes.</summary>
    public bool Close7ZipWhenFinished { get; set; } = true;
    /// <summary>True until the first successful launch; gates first-run setup prompts.</summary>
    public bool bFirstRun { get; set; } = true;
    /// <summary>Name substrings that exempt NPCs from same-name linking (generic names like "bandit").</summary>
    public List<string> LinkedNPCNameExclusions { get; set; } = new()
    {
        "soldier",
        "headsman",
        "victim",
        "nobleman",
        "werebear",
        "bandit",
        "warrior",
        "bard",
        "khajiit",
        "argonian",
        "citizen",
        "servant",
        "pirate",
        "karita",
        "East Empire Dockworker",
        "Savos Aren",
        "Skooma addict",
        "Courier",
        "The Guardian",
        "Imperial Champion",
        "Stormcloak Champion",
        "Redoran Guard",
        "Reclamation Priest",
        "Imperial Soldier",
        "Enthralled Wizard",
        "Nord",
        "Torture Victim"
    };
    /// <summary>Explicit groups of NPCs that should share a single appearance assignment
    /// (e.g. battle/quest variants of a named character), each with a designated primary.</summary>
    public List<LinkedNPCGroup> LinkedNPCGroups { get; set; } = new()
    {
        new LinkedNPCGroup()
        {
            GroupName = "Legate Rikke",
            NPCFormKeys = new()
            {
                Skyrim.Npc.Rikke.FormKey,
                Skyrim.Npc.CWBattleRikke.FormKey,
                Skyrim.Npc.MQ304Rikke.FormKey
            },
            Primary = Skyrim.Npc.Rikke.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Ulfric Stormcloak",
            NPCFormKeys = new()
            {
                Skyrim.Npc.Ulfric.FormKey,
                Skyrim.Npc.CWBattleUlfric.FormKey,
                Skyrim.Npc.MQ304Ulfric.FormKey
            },
            Primary = Skyrim.Npc.Ulfric.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Galmar Stone-Fist",
            NPCFormKeys = new()
            {
                Skyrim.Npc.Galmar.FormKey,
                Skyrim.Npc.CWBattleGalmar.FormKey,
                Skyrim.Npc.MQ304Galmar.FormKey
            },
            Primary = Skyrim.Npc.Galmar.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "General Tullius",
            NPCFormKeys = new()
            {
                Skyrim.Npc.GeneralTullius.FormKey,
                Skyrim.Npc.CWBattleTullius.FormKey
            },
            Primary = Skyrim.Npc.GeneralTullius.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Froki Whetted-Blade",
            NPCFormKeys = new()
            {
                Skyrim.Npc.dunHunterFroki.FormKey,
                Skyrim.Npc.MQ304Froki.FormKey
            },
            Primary = Skyrim.Npc.dunHunterFroki.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Svaknir",
            NPCFormKeys = new()
            {
                Skyrim.Npc.MQ304Svaknir.FormKey,
                Skyrim.Npc.MS05_dunDeadMensRespite_Svaknir.FormKey
            },
            Primary = Skyrim.Npc.MS05_dunDeadMensRespite_Svaknir.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Kodlak Whitemane",
            NPCFormKeys = new()
            {
                Skyrim.Npc.KodlakWhitemane.FormKey,
                Skyrim.Npc.MQ304Kodlak.FormKey,
                Skyrim.Npc.C04DeadKodlak.FormKey,
                Skyrim.Npc.C06DeadKodlak.FormKey,
                Skyrim.Npc.C06KodlaksGhost.FormKey
            },
            Primary = Skyrim.Npc.KodlakWhitemane.FormKey
        },
        new LinkedNPCGroup()
        {
            GroupName = "Eydis",
            NPCFormKeys = new()
            {
                Skyrim.Npc.Eydis.FormKey,
                Dragonborn.Npc.DLC2dunFahlbtharzExplorerCorpse02.FormKey,
            },
            Primary = Skyrim.Npc.Eydis.FormKey,
        }
    };
    /// <summary>Base name of the generated output plugin (without extension).</summary>
    public string PatchFileName { get; set; } = "SynthEBD";
    /// <summary>Verbose-log only the asset assignments that fail a consistency/compliance check.</summary>
    public bool bVerboseModeAssetsNoncompliant { get; set; } = false;
    /// <summary>Verbose-log every asset assignment (very noisy).</summary>
    public bool bVerboseModeAssetsAll { get; set; } = false;
    /// <summary>Restrict verbose asset logging to these specific NPCs.</summary>
    public List<FormKey> VerboseModeNPClist { get; set; } = new();
    /// <summary>Include per-NPC attribute evaluation detail in verbose logs.</summary>
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    /// <summary>The set of races SynthEBD is allowed to patch (defaults to vanilla playable races and their vampire/variant forms).</summary>
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

    /// <summary>Race redirects that map a non-patchable race onto a patchable one per axis
    /// (defaults from <see cref="DefaultRaceAliases"/>, e.g. Afflicted and Charmers of the Reach races).</summary>
    public List<RaceAlias> RaceAliases { get; set; } = new()
    {
        DefaultRaceAliases.RaceAliasAfflicted,
        DefaultRaceAliases.RaceAliasCotR_Breton,
        DefaultRaceAliases.RaceAliasCotR_BretonVampire,
        DefaultRaceAliases.RaceAliasCotR_DarkElf,
        DefaultRaceAliases.RaceAliasCotR_DarkElfVampire,
        DefaultRaceAliases.RaceAliasCotR_HighElf,
        DefaultRaceAliases.RaceAliasCotR_HighElfVampire,
        DefaultRaceAliases.RaceAliasCotR_Imperial,
        DefaultRaceAliases.RaceAliasCotR_ImperialVampire,
        DefaultRaceAliases.RaceAliasCotR_Nord,
        DefaultRaceAliases.RaceAliasCotR_NordVampire,
        DefaultRaceAliases.RaceAliasCotR_Orc,
        DefaultRaceAliases.RaceAliasCotR_OrcVampire,
        DefaultRaceAliases.RaceAliasCotR_Redguard,
        DefaultRaceAliases.RaceAliasCotR_RedguardVampire,
        DefaultRaceAliases.RaceAliasCotR_WoodElf,
        DefaultRaceAliases.RaceAliasCotR_WoodElfVampire
    };

    /// <summary>Named, reusable sets of races used by config filters (defaults from <see cref="DefaultRaceGroupings"/>).</summary>
    public List<RaceGrouping> RaceGroupings { get; set; } = new()
    {
        DefaultRaceGroupings.Humanoid,
        DefaultRaceGroupings.HumanoidPlayable,
        DefaultRaceGroupings.HumanoidPlayableNonVampire,
        DefaultRaceGroupings.HumanoidNonVampire,
        DefaultRaceGroupings.HumanoidVampire,
        DefaultRaceGroupings.HumanoidYoung,
        DefaultRaceGroupings.HumanoidYoungNonVampire,
        DefaultRaceGroupings.HumanoidYoungVampire,
        DefaultRaceGroupings.Elven,
        DefaultRaceGroupings.ElvenNonVampire,
        DefaultRaceGroupings.ElvenVampire,
        DefaultRaceGroupings.Breton,
        DefaultRaceGroupings.DarkElf,
        DefaultRaceGroupings.HighElf,
        DefaultRaceGroupings.Imperial,
        DefaultRaceGroupings.Nord,
        DefaultRaceGroupings.Orc,
        DefaultRaceGroupings.Redguard,
        DefaultRaceGroupings.WoodElf,
        DefaultRaceGroupings.Elder,
        DefaultRaceGroupings.Khajiit,
        DefaultRaceGroupings.Argonian
    };

    /// <summary>When loading a config plugin, replace its race groupings with these global ones rather than merging.</summary>
    public bool OverwritePluginRaceGroups { get; set; } = true;

    /// <summary>Named, reusable NPC-attribute sets referenced by config rules (defaults from <see cref="DefaultAttributeGroups"/>).</summary>
    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new()
    {
        DefaultAttributeGroups.CannotHaveDefinition,
        DefaultAttributeGroups.MustBeFit,
        DefaultAttributeGroups.MustBeAthletic,
        DefaultAttributeGroups.MustBeMuscular,
        DefaultAttributeGroups.CannotHaveScars,
        DefaultAttributeGroups.CanBeDirty,
        DefaultAttributeGroups.MustBeDirty,
        DefaultAttributeGroups.CanGetChubbyMorph,
        DefaultAttributeGroups.MustGetYoungFace,
        DefaultAttributeGroups.MatureFace,
        DefaultAttributeGroups.HaggardFace,
        DefaultAttributeGroups.Age40,
        DefaultAttributeGroups.Age40Rough,
        DefaultAttributeGroups.Age50,
        DefaultAttributeGroups.Freckles,
        DefaultAttributeGroups.Rough01,
        DefaultAttributeGroups.Rough02,
        DefaultAttributeGroups.CharmersOfTheReachHeads
    };

    /// <summary>When loading a config plugin, replace its attribute groups with these global ones rather than merging.</summary>
    public bool OverwritePluginAttGroups { get; set; } = true;

    /// <summary>Skip pre-patch validation of settings/config integrity.</summary>
    public bool bDisableValidation { get; set; } = false;

    /// <summary>Generate a detailed per-NPC report for the NPCs chosen by <see cref="DetailedReportSelector"/>.</summary>
    public bool bUseDetailedReportSelection { get; set; } = false;
    public DetailedReportNPCSelector DetailedReportSelector { get; set; } = new();
    /// <summary>In NPC pickers, filter the list to NPCs that share the selected NPC's armature.</summary>
    public bool bFilterNPCsByArmature { get; set; } = true;
    public bool bShowTroubleshootingSettings { get; set; } = false;
    /// <summary>Tracks whether the troubleshooting-settings warning has already been shown (one-time UI flag).</summary>
    public bool bTroubleShootingWarningDisplayed { get; set; } = false;
    /// <summary>Tracks whether the head-part patching warning has already been shown (one-time UI flag).</summary>
    public bool bHeadPartWarningDisplayed { get; set; } = false;
    /// <summary>True once the main UI has been opened; used to distinguish UI vs headless runs.</summary>
    public bool bUIopened { get; set; } = false;
    public bool bShow3DPreview { get; set; } = true;
    /// <summary>Persisted width (px) of the previewer pane on the Specific NPC Assignments screen.</summary>
    public double SpecificNPCPreviewerWidth { get; set; } = 525;
    /// <summary>Persisted width (px) of the previewer pane on the Consistency screen.</summary>
    public double ConsistencyPreviewerWidth { get; set; } = 525;
    /// <summary>Name of the selected CharacterViewer lighting layout preset.</summary>
    public string CharacterViewerLightingLayout { get; set; } = "";
    /// <summary>Name of the selected CharacterViewer lighting color scheme.</summary>
    public string CharacterViewerLightingColorScheme { get; set; } = "";
    /// <summary>User-defined CharacterViewer lighting layouts (in addition to the built-in ones).</summary>
    public List<CharacterViewerLightingLayout> UserLightingLayouts { get; set; } = new();
    /// <summary>User-defined CharacterViewer lighting color schemes (in addition to the built-in ones).</summary>
    public List<CharacterViewerLightingColorScheme> UserLightingColorSchemes { get; set; } = new();
    public bool CharacterViewerVerboseLog { get; set; } = false;
    /// <summary>Strategy key for how DDS textures are loaded into the previewer (e.g. "BmpStream").</summary>
    public string TextureLoadStrategy { get; set; } = "BmpStream";
    /// <summary>Settings for which NPCs are offered as stand-ins in the NIF previewer.</summary>
    public NifPreviewNpcSettings PreviewNpcs { get; set; } = new();
    /// <summary>Mods never imported as asset sources (the base game masters); skipped in SkyPatcher mode.</summary>
    public List<ModKey> BlockedModsFromImport { get; set; } = new()
    {
        ModKey.FromNameAndExtension("Skyrim.esm"),
        ModKey.FromNameAndExtension("Dawnguard.esm"),
        ModKey.FromNameAndExtension("Dragonborn.esm"),
        ModKey.FromNameAndExtension("Hearthfires.esm"),
    }; // do not import these mods in SkyPatcher mode.
}

/// <summary>Which body-shape system drives the Body Shape axis.</summary>
public enum BodyShapeSelectionMode
{
    /// <summary>Body Shape axis disabled.</summary>
    None,
    /// <summary>Use BodyGen morph templates.</summary>
    BodyGen,
    /// <summary>Use BodySlide presets.</summary>
    BodySlide
}

/// <summary>When BodySlide is active, which selector assigns the preset.</summary>
public enum BodySlideSelectionMode
{
    /// <summary>OBody NG-style descriptor-driven selection.</summary>
    OBody,
    /// <summary>AutoBody-style selection.</summary>
    AutoBody
}

/// <summary>Optional external appearance-merger tool that SynthEBD coordinates output with.</summary>
public enum AppearanceMergeType
{
    /// <summary>No merger.</summary>
    None,
    /// <summary>EasyNPC.</summary>
    EasyNPC,
    /// <summary>NPC NIF Merger.</summary>
    NPC2
}
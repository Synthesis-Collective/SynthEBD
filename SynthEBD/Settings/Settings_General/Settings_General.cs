using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD
{
    public class Settings_General
    {
        public Settings_General()
        {
            this.bShowToolTips = true;
            this.bChangeMeshesOrTextures = true;
            this.BodySelectionMode = BodyShapeSelectionMode.None;
            this.BSSelectionMode = BodySlideSelectionMode.OBody;
            this.bChangeHeight = false;
            this.OutputDataFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
            this.bEnableConsistency = true;
            this.ExcludePlayerCharacter = true;
            this.ExcludePresets = true;
            this.bLinkNPCsWithSameName = true;
            this.PatchFileName = "SynthEBD";
            this.bVerboseModeAssetsNoncompliant = false;
            this.bVerboseModeAssetsAll = false;
            this.VerboseModeNPClist = new List<FormKey>();
            this.bLoadSettingsFromDataFolder = false;
            this.PatchableRaces = new List<FormKey>()
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

            this.RaceAliases = new List<RaceAlias>()
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
            this.RaceGroupings = new List<RaceGrouping>()
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
            this.AttributeGroups = new HashSet<AttributeGroup>()
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
            this.OverwritePluginAttGroups = true;
        }

        public bool bShowToolTips { get; set; }
        public bool bChangeMeshesOrTextures { get; set; }
        public BodyShapeSelectionMode BodySelectionMode { get; set; }
        public BodySlideSelectionMode BSSelectionMode { get; set; }
        public bool bChangeHeight { get; set; }
        public string OutputDataFolder { get; set; }
        public bool bEnableConsistency { get; set; }
        public bool ExcludePlayerCharacter { get; set; }
        public bool ExcludePresets { get; set; }
        public bool bLinkNPCsWithSameName { get; set; }
        public string PatchFileName { get; set; }

        public bool bVerboseModeAssetsNoncompliant { get; set; }
        public bool bVerboseModeAssetsAll { get; set; }
        public List<FormKey> VerboseModeNPClist { get; set; } // enable FormKey (multi?) picker for this
        public bool bLoadSettingsFromDataFolder { get; set; }

        public List<FormKey> PatchableRaces { get; set; } // enable FormKey (multi?) picker for this

        public List<RaceAlias> RaceAliases { get; set; }

        public List<RaceGrouping> RaceGroupings { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        public bool OverwritePluginAttGroups { get; set; }
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
}

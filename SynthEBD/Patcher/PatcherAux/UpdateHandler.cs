using DynamicData;
using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class UpdateHandler // handles backward compatibility for previous SynthEBD versions
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherState _patcherState;
    private readonly PatcherIO _patcherIO;
    private readonly Logger _logger;
    private readonly VM_Settings_General _generalVM;
    private readonly VM_SettingsTexMesh _texMeshVM;
    private readonly VM_RaceGrouping.Factory _raceGroupingFactory;

    public UpdateHandler(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, PatcherState patcherState, PatcherIO patcherIO, Logger logger, VM_Settings_General generalVM, VM_SettingsTexMesh texMeshVM, VM_RaceGrouping.Factory raceGroupingFactory)
    {
        _environmentProvider = environmentProvider;
        _paths = paths;
        _patcherState = patcherState;
        _patcherIO = patcherIO; 
        _logger = logger;
        _generalVM = generalVM;
        _texMeshVM = texMeshVM;
        _raceGroupingFactory = raceGroupingFactory;
    }

    public void CheckBackwardCompatibility()
    {
        UpdateAssetPacks(_texMeshVM);
        UpdateV1012(_generalVM);
        UpdateV1013(_generalVM);
        UpdateV1013RecordTemplates();
        UpdateV1016AttributeGroups();
        UpdateV1018RecordTemplates();
        UpdateV1025RaceGroupings();
        UpdateV1028Toggle();
        UpdateV1032AttributeGroups();
        UpdateV1048RaceAliases();
        UpdateV1053CotrAttributes();
    }
    private void UpdateAssetPacks(VM_SettingsTexMesh texMeshVM)
    {
        texMeshVM.ConfigUpdateAll(new());
    }    
    public void CleanSPIDiniHeadParts()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"), _logger);
    }
    public void CleanSPIDiniOBody()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"), _logger);
    }
    public void CleanOldBodySlideDict()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"), _logger);
    }

    private void UpdateV1012(VM_Settings_General generalVM)
    {
        if (!_patcherState.UpdateLog.Performed1_0_1_2Update)
        {
            var missingNames = v1012UniqueNameExclusions.Where(x => !generalVM.LinkedNameExclusions.Select(y => y.Content).Contains(x)).ToHashSet();
            var dispText = "v1.0.1.2 Update: It is suggested to add the following names to your Linked Unique NPC Name Exclusions. Would you like to do this automatically?" + Environment.NewLine + String.Join(Environment.NewLine, missingNames);
            if (missingNames.Any() && MessageWindow.DisplayNotificationYesNo("Update Unique Name Exclusions?", dispText))
            {
                foreach (var name in missingNames)
                {
                    generalVM.LinkedNameExclusions.Add(new(name, generalVM.LinkedNameExclusions));
                }
            }

            _patcherState.UpdateLog.Performed1_0_1_2Update = true;
        }
    }

    private void UpdateV1013(VM_Settings_General generalVM)
    {
        if (!_patcherState.UpdateLog.Performed1_0_1_3Update)
        {
            if (!generalVM.RaceGroupingEditor.RaceGroupings.Where(x => x.Label == DefaultRaceGroupings.HumanoidPlayableNonVampire.Label).Any())
            {
                var newGrouping = _raceGroupingFactory(DefaultRaceGroupings.HumanoidPlayableNonVampire, generalVM.RaceGroupingEditor);
                generalVM.RaceGroupingEditor.RaceGroupings.Add(newGrouping);
            }
            _patcherState.UpdateLog.Performed1_0_1_3Update = true;
        }
    }

    private void UpdateV1013RecordTemplates()
    {
        if (_patcherState.UpdateLog.Performed1_0_1_3RTUpdate)
        {
            return;
        }
        string defaultRecordTemplatesStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources");

        var newTemplateNames = new string[] { "Record Templates - 3BA - pamonha.esp", "Record Templates - BHUNP - pamonha.esp" };

        bool allCopied = true;

        foreach (var newPlugin in newTemplateNames)
        {
            var source = Path.Combine(defaultRecordTemplatesStartPath, newPlugin);
            var dest = Path.Combine(_paths.RecordTemplatesDirPath, newPlugin);

            if (File.Exists(source) && !File.Exists(dest))
            {
                if (!_patcherIO.TryCopyResourceFile(source, dest, _logger, out string errorStr))
                {
                    _logger.LogError("Failed to copy new record template during update." + Environment.NewLine + "Source: " + source + Environment.NewLine + "Destination: " + dest + Environment.NewLine + errorStr);
                    allCopied = false;
                }
            }
        }

        if (allCopied)
        {
            _patcherState.UpdateLog.Performed1_0_1_3RTUpdate = true;
        }
    }

    private void UpdateV1016AttributeGroups()
    {
        if (!_patcherState.UpdateLog.Performed1_0_1_6AttributeUpdate)
        {
            var athleticGroup = _generalVM.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.MustBeAthletic.Label).FirstOrDefault();
            UpdateV1016_Aux_AddFaction(athleticGroup);

            var muscularGroup = _generalVM.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.MustBeMuscular.Label).FirstOrDefault();
            UpdateV1016_Aux_AddFaction(muscularGroup);

            _patcherState.UpdateLog.Performed1_0_1_6AttributeUpdate = true;
        }
    }

    private void UpdateV1016_Aux_AddFaction(VM_AttributeGroup group)
    {
        if (group != null)
        {
            var defaultAtt = group.Attributes.Where(att => att.GroupedSubAttributes.Where(subAtt => subAtt.Type == NPCAttributeType.Faction).Any()).FirstOrDefault();
            if (defaultAtt != null)
            {
                var factionSubAtt = defaultAtt.GroupedSubAttributes.Where(subAtt => subAtt.Type == NPCAttributeType.Faction).FirstOrDefault();
                if (factionSubAtt != null && factionSubAtt.Attribute as VM_NPCAttributeFactions != null)
                {
                    var factionAtt = factionSubAtt.Attribute as VM_NPCAttributeFactions;
                    if (!factionAtt.FactionFormKeys.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Faction.JobHousecarlFaction.FormKey))
                    {
                        factionAtt.FactionFormKeys.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Faction.JobHousecarlFaction.FormKey);
                    }
                }
            }
        }
    }

    private void UpdateV1018RecordTemplates()
    {
        if (_patcherState.UpdateLog.Performed1_0_1_8RTUpdate)
        {
            return;
        }
        string defaultRecordTemplatesStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources");

        var newPlugin = "Record Templates - The New Gentleman.esp";

        var source = Path.Combine(defaultRecordTemplatesStartPath, newPlugin);
        var dest = Path.Combine(_paths.RecordTemplatesDirPath, newPlugin);

        if (File.Exists(source) && !File.Exists(dest))
        {
            if (!_patcherIO.TryCopyResourceFile(source, dest, _logger, out string errorStr))
            {
                _logger.LogError("Failed to copy new record template during update." + Environment.NewLine + "Source: " + source + Environment.NewLine + "Destination: " + dest + Environment.NewLine + errorStr);
            }
            else
            {
                _patcherState.UpdateLog.Performed1_0_1_8RTUpdate = true;
            }
        }
    }

    private void UpdateV1025RaceGroupings()
    {
        if (!_patcherState.UpdateLog.Performed1_0_2_5RGUpdate)
        {
            List<VM_RaceGrouping> toUpdateVMs = new();
            var humanoidPlayableVM = _generalVM.RaceGroupingEditor.RaceGroupings.Where(x => x.Label.Equals("Humanoid Playable", StringComparison.OrdinalIgnoreCase) && (x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey) || x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey))).FirstOrDefault();
            if (humanoidPlayableVM != null)
            {
                toUpdateVMs.Add(humanoidPlayableVM);
            }
            
            foreach (var assetPack in _texMeshVM.AssetPacks)
            {
                humanoidPlayableVM = assetPack.RaceGroupingEditor.RaceGroupings.Where(x => x.Label.Equals("Humanoid Playable", StringComparison.OrdinalIgnoreCase) && (x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey) || x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey))).FirstOrDefault();
                if (humanoidPlayableVM != null)
                {
                    toUpdateVMs.Add(humanoidPlayableVM);
                }
            }

            List<RaceGrouping> toUpdateMs = new();
            foreach (var assetPack in _patcherState.AssetPacks)
            {
                var humanoidPlayableM = assetPack.RaceGroupings.Where(x => x.Label.Equals("Humanoid Playable", StringComparison.OrdinalIgnoreCase) && (x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey) || x.Races.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey))).FirstOrDefault();
                if (humanoidPlayableM != null)
                {
                    toUpdateMs.Add(humanoidPlayableM);
                }
            }

            if ((toUpdateVMs.Any() || toUpdateMs.Any()) && MessageWindow.DisplayNotificationYesNo("Version 1.0.2.5 Update", new List<string>() { "In previous SynthEBD versions, the Humanoid Playable race grouping erroneously included Elder Race.", "Would you like to fix this? (Recommend: Yes)"}, Environment.NewLine))
            {
                foreach (var vm in toUpdateVMs)
                {
                    RemoveEldersFromGrouping(vm.Races);
                }

                foreach (var m in toUpdateMs)
                {
                    RemoveEldersFromGrouping(m.Races);
                }
            }
        }
        _patcherState.UpdateLog.Performed1_0_2_5RGUpdate = true;
    }

    private void RemoveEldersFromGrouping(ICollection<FormKey> raceGroupingList)
    {
        if (raceGroupingList.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey))
        {
            raceGroupingList.Remove(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey);
        }
        if (raceGroupingList.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey))
        {
            raceGroupingList.Remove(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey);
        }
    }

    private void UpdateV1028Toggle()
    {
        if (!_patcherState.UpdateLog.Performed1_0_2_8Update && 
            _texMeshVM.bLegacyEBDMode == true &&
            MessageWindow.DisplayNotificationYesNo("Update 1.0.2.8", "In previous versions of SynthEBD, the \"Use Original EBD Scripts\" setting was enabled by default. This was an error - the original EBD scripts should not be used other than for troubleshooting. Would you like to correct this setting? Note that if you are re-running SynthEBD on an existing save, you will need to clean save first (see instructions on the SynthEBD Nexus page)."))
        {
            _texMeshVM.bLegacyEBDMode = false;
        }
        _patcherState.UpdateLog.Performed1_0_2_8Update = true;
    }

    private void UpdateV1032AttributeGroups()
    {
        if (!_patcherState.UpdateLog.Performed1_0_3_2AttributeUpdate && !_patcherState.GeneralSettings.AttributeGroups.Where(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label).Any())
        {
            _generalVM.AttributeGroupMenu.AddAttributeGroupFromModel(DefaultAttributeGroups.CharmersOfTheReachHeads);

            foreach (var assetPack in _texMeshVM.AssetPacks)
            {
                if (!assetPack.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label).Any())
                {
                    assetPack.AttributeGroupMenu.AddAttributeGroupFromModel(DefaultAttributeGroups.CharmersOfTheReachHeads);
                }
            }
        }
        _patcherState.UpdateLog.Performed1_0_3_2AttributeUpdate = true;
    }

    private void UpdateV1048RaceAliases()
    {
        if (_patcherState.UpdateLog.Performed1_0_4_8_RaceAliasCOTRUpdates)
        {
            return;
        }
        
        // first update attribute group:
        var attGroup = _generalVM.AttributeGroupMenu.Groups
            .FirstOrDefault(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label);
        if (attGroup != null)
        {
            var cotrMods = attGroup.Attributes.FirstOrDefault((x => x.GroupedSubAttributes.FirstOrDefault() != null
                && x.GroupedSubAttributes.FirstOrDefault().Attribute as VM_NPCAttributeMod != null)
                ).GroupedSubAttributes.First().Attribute as VM_NPCAttributeMod;
            
            if (cotrMods != null)
            {
                var erroneous = cotrMods.ModKeys.FirstOrDefault(x => x.FileName == "0AuriReplacer.esp.esp");
                if (erroneous != null)
                {
                    cotrMods.ModKeys.Remove(erroneous);
                    cotrMods.ModKeys.Add(ModKey.TryFromFileName("0AuriReplacer.esp").Value);
                }
                
                var gentle = cotrMods.ModKeys.FirstOrDefault(x => x.FileName == "Gentle Auri.esp");
                if (gentle == null)
                {
                    cotrMods.ModKeys.Add(ModKey.TryFromFileName("Gentle Auri.esp").Value);
                }
            }
        }
        
        // then upate aliases
        List<string> cotrRaceStrs = new()
        {
            "005734:COR_AllRace.esp",
            "005735:COR_AllRace.esp",
            "05A179:COR_AllRace.esp",
            "05A17A:COR_AllRace.esp",
            "05A184:COR_AllRace.esp",
            "05A185:COR_AllRace.esp",
            "05A18E:COR_AllRace.esp",
            "05A18F:COR_AllRace.esp",
            "05A198:COR_AllRace.esp",
            "05A199:COR_AllRace.esp",
            "05A1A2:COR_AllRace.esp",
            "05A1A3:COR_AllRace.esp",
            "05A1AC:COR_AllRace.esp",
            "05A1AD:COR_AllRace.esp",
            "05A1B0:COR_AllRace.esp",
            "05A1B1:COR_AllRace.esp"
        };

        List<string> toUpdateCOTRAliases = new();

        HashSet<RaceAlias> cotrRaceAliases = new()
        {
            DefaultRaceAliases.RaceAliasCotR_Breton,
            DefaultRaceAliases.RaceAliasCotR_BretonVampire,
            DefaultRaceAliases.RaceAliasCotR_DarkElf,
            DefaultRaceAliases.RaceAliasCotR_DarkElfVampire,
            DefaultRaceAliases.RaceAliasCotR_HighElf,
            DefaultRaceAliases.RaceAliasCotR_HighElfVampire,
            DefaultRaceAliases.RaceAliasCotR_Imperial,
            DefaultRaceAliases.RaceAliasCotR_Imperial,
            DefaultRaceAliases.RaceAliasCotR_Nord,
            DefaultRaceAliases.RaceAliasCotR_NordVampire,
            DefaultRaceAliases.RaceAliasCotR_Orc,
            DefaultRaceAliases.RaceAliasCotR_OrcVampire,
            DefaultRaceAliases.RaceAliasCotR_Redguard,
            DefaultRaceAliases.RaceAliasCotR_RedguardVampire,
            DefaultRaceAliases.RaceAliasCotR_WoodElf,
            DefaultRaceAliases.RaceAliasCotR_WoodElfVampire
        };
        
        foreach (string cotrFormKeyStr in cotrRaceStrs)
        {
            if (!_patcherState.GeneralSettings.RaceAliases.Any(x => x.Race.ToString() == cotrFormKeyStr))
            {
                toUpdateCOTRAliases.Add(cotrFormKeyStr);
            }
        }

        if (toUpdateCOTRAliases.Any())
        {
            if (MessageWindow.DisplayNotificationYesNo("Update Race Aliases?",
                    "Some config files now have Charmers of the Reach support. Would you like to update your Race Aliases to support the default CotR Vanilla Races? Press yes if you want to make sure SynthEBD patches NPCs using CotR faces when CotR-supporting config files are installed."))
            {
                foreach (var formKeyStr in toUpdateCOTRAliases)
                {
                    var correspondingAlias = cotrRaceAliases.FirstOrDefault(x => x.Race.ToString() == formKeyStr);
                    if (correspondingAlias != null)
                    {
                        var aliasVM = new VM_RaceAlias(correspondingAlias, _generalVM, _environmentProvider);
                        _generalVM.raceAliases.Add(aliasVM);
                    }
                }
            }
        }

        _patcherState.UpdateLog.Performed1_0_4_8_RaceAliasCOTRUpdates = true;
    }

    private void UpdateV1053CotrAttributes()
    {
        if (_patcherState.UpdateLog.Performed1_0_5_3_CotrAttributeUpdates)
        {
            return;
        }

        List<VM_NPCAttributeMod> toUpdate = new();
        
        var cotrAttributeGroup = _generalVM.AttributeGroupMenu.Groups.FirstOrDefault(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label);
        if (cotrAttributeGroup != null)
        {
            foreach (var attribute in cotrAttributeGroup.Attributes)
            {
                foreach (var subAttribute in attribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Mod))
                {
                    var editable = subAttribute.Attribute as VM_NPCAttributeMod;
                    if (editable != null)
                    {
                        toUpdate.Add(editable);
                    }
                }
            }
        }

        foreach (var config in _texMeshVM.AssetPacks)
        {
            cotrAttributeGroup = config.AttributeGroupMenu.Groups.FirstOrDefault(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label);
            if (cotrAttributeGroup != null)
            {
                foreach (var attribute in cotrAttributeGroup.Attributes)
                {
                    foreach (var subAttribute in attribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Mod))
                    {
                        var editable = subAttribute.Attribute as VM_NPCAttributeMod;
                        if (editable != null)
                        {
                            toUpdate.Add(editable);
                        }
                    }
                }
            }
        }

        if (toUpdate.Any() && MessageWindow.DisplayNotificationYesNo("Update Charmers of the Reach Attribute Group?",
                "SynthEBD 1.0.5.3 includes new modes for Mod-type NPC Attributes, which improves handling of mods containing Charmers of the Reach heads.\n" +
                "Would you like to automatically update the Attribute Group that handles CotR heads?\n" +
                "Press Yes unless you know what you're doing"))
        {
            foreach (var attribute in toUpdate)
            {
                attribute.ModActionType = ModAttributeEnum.WinningAppearanceIsFrom;
            }
        }
        
        _patcherState.UpdateLog.Performed1_0_5_3_CotrAttributeUpdates = true;
    }

    public Dictionary<string, string> V09PathReplacements { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Diffuse", "Diffuse.GivenPath" },
        { "NormalOrGloss", "NormalOrGloss.GivenPath" },
        { "GlowOrDetailMap", "GlowOrDetailMap.GivenPath" },
        { "BacklightMaskOrSpecular", "BacklightMaskOrSpecular.GivenPath" },
        { "Height", "Height.GivenPath" }
    };

    public HashSet<string> v1012UniqueNameExclusions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
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
}

public class UpdateLog
{
    public bool Performed1_0_1_2Update { get; set; } = false;
    public bool Performed1_0_1_3Update { get; set; } = false;
    public bool Performed1_0_1_3RTUpdate { get; set; } = false;
    public bool Performed1_0_1_6AttributeUpdate { get; set; } = false;
    public bool Performed1_0_1_8RTUpdate { get; set; } = false;
    public bool Performed1_0_2_5RGUpdate { get; set; } = false;
    public bool Performed1_0_2_8Update { get; set; } = false;
    public bool Performed1_0_3_2AttributeUpdate { get; set; } = false;
    public bool Performed1_0_4_8_RaceAliasCOTRUpdates { get; set; } = false;
    public bool Performed1_0_5_3_CotrAttributeUpdates { get; set; } = false;
}

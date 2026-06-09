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

/// <summary>
/// Migrates user settings and bundled data forward across SynthEBD versions. On startup
/// <see cref="CheckBackwardCompatibility"/> determines the last applied version and runs each intervening
/// migration in order (cumulatively). Individual update routines may copy bundled record templates, fix race
/// groupings/aliases, add attribute groups, and prompt the user via message windows. Operates on the
/// settings view models, so it mutates in-memory settings and can pop UI dialogs.
/// </summary>
public class UpdateHandler // handles backward compatibility for previous SynthEBD versions
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherState _patcherState;
    private readonly PatcherIO _patcherIO;
    private readonly Logger _logger;
    private readonly VM_Settings_General _generalVM;
    private readonly VM_SettingsTexMesh _texMeshVM;
    private readonly VM_SettingsOBody _oBodyVM;
    private readonly VM_SettingsBodyGen _bodyGenVM;
    private readonly VM_RaceGrouping.Factory _raceGroupingFactory;

    /// <summary>Captures the environment, paths, patcher state, IO helper, logger, settings VMs (General, TexMesh, OBody, BodyGen), and race-grouping factory used by the migrations.</summary>
    public UpdateHandler(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, PatcherState patcherState, PatcherIO patcherIO, Logger logger, VM_Settings_General generalVM, VM_SettingsTexMesh texMeshVM, VM_SettingsOBody oBodyVM, VM_SettingsBodyGen bodyGenVM, VM_RaceGrouping.Factory raceGroupingFactory)
    {
        _environmentProvider = environmentProvider;
        _paths = paths;
        _patcherState = patcherState;
        _patcherIO = patcherIO;
        _logger = logger;
        _generalVM = generalVM;
        _texMeshVM = texMeshVM;
        _oBodyVM = oBodyVM;
        _bodyGenVM = bodyGenVM;
        _raceGroupingFactory = raceGroupingFactory;
    }

    /// <summary>
    /// Entry point for version migration. Always runs asset-pack config updates, stamps a fresh install with
    /// the current version, derives the last-applied version from legacy booleans if needed, then runs each
    /// version's migration whose threshold exceeds the applied version (cumulatively). Finally stamps
    /// LastAppliedVersion to the current version. Mutates settings and may show update dialogs.
    /// </summary>
    public void CheckBackwardCompatibility()
    {
        UpdateAssetPacks(_texMeshVM); // always runs — handles its own internal config versioning

        ProgramVersion currentVersion = PatcherState.Version;

        // Fresh install — no prior settings to migrate, so stamp the current version and skip all updates.
        if (_patcherState.GeneralSettings.bFirstRun)
        {
            _patcherState.UpdateLog.LastAppliedVersion = PatcherState.Version;
            return;
        }

        // Migrate from the old boolean-based UpdateLog if LastAppliedVersion has not yet been set.
        // Inspect the booleans to infer the highest version the user has already applied.
        if (string.IsNullOrEmpty(_patcherState.UpdateLog.LastAppliedVersion))
        {
            _patcherState.UpdateLog.LastAppliedVersion = DeriveVersionFromBooleans();
        }

        ProgramVersion appliedVersion = _patcherState.UpdateLog.LastAppliedVersion;

        if (appliedVersion >= currentVersion) return;

        // Updates are cumulative: every block whose threshold exceeds appliedVersion will run,
        // so a user jumping from 1.0.1.2 to 1.0.6.7 picks up every intermediate patch.
        if (appliedVersion < "1.0.1.2") UpdateV1012(_generalVM);
        if (appliedVersion < "1.0.1.3") UpdateV1013(_generalVM);
        if (appliedVersion < "1.0.1.3") UpdateV1013RecordTemplates();
        if (appliedVersion < "1.0.1.6") UpdateV1016AttributeGroups();
        if (appliedVersion < "1.0.1.8") UpdateV1018RecordTemplates();
        if (appliedVersion < "1.0.2.5") UpdateV1025RaceGroupings();
        if (appliedVersion < "1.0.2.8") UpdateV1028Toggle();
        if (appliedVersion < "1.0.3.2") UpdateV1032AttributeGroups();
        if (appliedVersion < "1.0.4.8") UpdateV1048RaceAliases();
        if (appliedVersion < "1.0.5.3") UpdateV1053CotrAttributes();
        if (appliedVersion < "1.0.5.5") UpdateV1055CotrAttributes();
        if (appliedVersion < "1.0.6.8") UpdateV1068();
        if (appliedVersion < "1.0.7.0") UpdateV1070RaceAliases();
        if (appliedVersion < "1.0.7.0") UpdateV1070AttributeGroupRename();

        _patcherState.UpdateLog.LastAppliedVersion = PatcherState.Version;
    }

    /// <summary>
    /// Infers the highest version already applied from the legacy boolean flags.
    /// Called once to populate LastAppliedVersion when migrating from an old UpdateLog.
    /// </summary>
    private string DeriveVersionFromBooleans()
    {
        var log = _patcherState.UpdateLog;
        if (log.Performed1_0_5_5_CotrAttributeUpdates) return "1.0.5.5";
        if (log.Performed1_0_5_3_CotrAttributeUpdates) return "1.0.5.3";
        if (log.Performed1_0_4_8_RaceAliasCOTRUpdates) return "1.0.4.8";
        if (log.Performed1_0_3_2AttributeUpdate) return "1.0.3.2";
        if (log.Performed1_0_2_8Update) return "1.0.2.8";
        if (log.Performed1_0_2_5RGUpdate) return "1.0.2.5";
        if (log.Performed1_0_1_8RTUpdate) return "1.0.1.8";
        if (log.Performed1_0_1_6AttributeUpdate) return "1.0.1.6";
        if (log.Performed1_0_1_3RTUpdate || log.Performed1_0_1_3Update) return "1.0.1.3";
        if (log.Performed1_0_1_2Update) return "1.0.1.2";
        return "0.0.0.0";
    }

    /// <summary>Runs each asset pack's internal config-version update via the TexMesh VM. Always executed, independent of the version gate.</summary>
    private void UpdateAssetPacks(VM_SettingsTexMesh texMeshVM)
    {
        texMeshVM.ConfigUpdateAll(new());
    }
    /// <summary>Deletes a stale SPID head-part distributor ini from a previous output run. Writes to disk.</summary>
    public void CleanSPIDiniHeadParts()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"), _logger);
    }
    /// <summary>Deletes a stale SPID BodySlide distributor ini from a previous output run. Writes to disk.</summary>
    public void CleanSPIDiniOBody()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"), _logger);
    }
    /// <summary>Deletes the legacy BodySlideDict.json left by older versions. Writes to disk.</summary>
    public void CleanOldBodySlideDict()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"), _logger);
    }

    /// <summary>v1.0.1.2: offers to add a recommended set of generic names to the linked-unique name exclusions. Prompts the user; may mutate settings.</summary>
    private void UpdateV1012(VM_Settings_General generalVM)
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
    }

    /// <summary>v1.0.1.3: adds the "Humanoid Playable Non-Vampire" default race grouping if missing. Mutates settings.</summary>
    private void UpdateV1013(VM_Settings_General generalVM)
    {
        if (!generalVM.RaceGroupingEditor.RaceGroupings.Where(x => x.Label == DefaultRaceGroupings.HumanoidPlayableNonVampire.Label).Any())
        {
            var newGrouping = _raceGroupingFactory(DefaultRaceGroupings.HumanoidPlayableNonVampire, generalVM.RaceGroupingEditor);
            generalVM.RaceGroupingEditor.RaceGroupings.Add(newGrouping);
        }
    }

    /// <summary>v1.0.1.3: copies the bundled 3BA and BHUNP record-template plugins into the record-templates folder if absent. Writes files; logs copy failures.</summary>
    private void UpdateV1013RecordTemplates()
    {
        string defaultRecordTemplatesStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources");

        var newTemplateNames = new string[] { "Record Templates - 3BA - pamonha.esp", "Record Templates - BHUNP - pamonha.esp" };

        foreach (var newPlugin in newTemplateNames)
        {
            var source = Path.Combine(defaultRecordTemplatesStartPath, newPlugin);
            var dest = Path.Combine(_paths.RecordTemplatesDirPath, newPlugin);

            if (File.Exists(source) && !File.Exists(dest))
            {
                if (!_patcherIO.TryCopyResourceFile(source, dest, _logger, out string errorStr))
                {
                    _logger.LogError("Failed to copy new record template during update." + Environment.NewLine + "Source: " + source + Environment.NewLine + "Destination: " + dest + Environment.NewLine + errorStr);
                }
            }
        }
    }

    /// <summary>v1.0.1.6: adds the Housecarl faction to the default "Must Be Athletic" and "Must Be Muscular" attribute groups. Mutates settings.</summary>
    private void UpdateV1016AttributeGroups()
    {
        var athleticGroup = _generalVM.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.MustBeAthletic.Label).FirstOrDefault();
        UpdateV1016_Aux_AddFaction(athleticGroup);

        var muscularGroup = _generalVM.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.MustBeMuscular.Label).FirstOrDefault();
        UpdateV1016_Aux_AddFaction(muscularGroup);
    }

    /// <summary>Helper for <see cref="UpdateV1016AttributeGroups"/>: adds the JobHousecarlFaction form key to the group's first faction sub-attribute if not already present. Mutates settings.</summary>
    private void UpdateV1016_Aux_AddFaction(VM_AttributeGroup? group)
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

    /// <summary>v1.0.1.8: copies the bundled "The New Gentleman" record-template plugin into the record-templates folder if absent. Writes files; logs copy failures.</summary>
    private void UpdateV1018RecordTemplates()
    {
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
        }
    }

    /// <summary>
    /// v1.0.2.5: detects "Humanoid Playable" race groupings (in general settings, asset-pack VMs, and asset-pack
    /// models) that erroneously include Elder Race/Elder Race Vampire, and offers to remove them. Prompts the
    /// user; may mutate settings.
    /// </summary>
    private void UpdateV1025RaceGroupings()
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

    /// <summary>Removes Elder Race and Elder Race Vampire form keys from the given race list, if present. Mutates the collection.</summary>
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

    /// <summary>v1.0.2.8: if the legacy "Use Original EBD Scripts" toggle was left enabled by the old default, offers to disable it. Prompts the user; may mutate settings.</summary>
    private void UpdateV1028Toggle()
    {
        if (_texMeshVM.bLegacyEBDMode == true &&
            MessageWindow.DisplayNotificationYesNo("Update 1.0.2.8", "In previous versions of SynthEBD, the \"Use Original EBD Scripts\" setting was enabled by default. This was an error - the original EBD scripts should not be used other than for troubleshooting. Would you like to correct this setting? Note that if you are re-running SynthEBD on an existing save, you will need to clean save first (see instructions on the SynthEBD Nexus page)."))
        {
            _texMeshVM.bLegacyEBDMode = false;
        }
    }

    /// <summary>v1.0.3.2: adds the "Charmers of the Reach Heads" default attribute group to general settings and each asset pack if missing. Mutates settings.</summary>
    private void UpdateV1032AttributeGroups()
    {
        if (!_patcherState.GeneralSettings.AttributeGroups.Where(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label).Any())
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
    }

    /// <summary>
    /// v1.0.4.8: fixes an erroneous Auri replacer mod key and adds "Gentle Auri.esp" in the CotR attribute
    /// group, then offers to add the default Charmers of the Reach vanilla-race aliases that are not yet
    /// present. Prompts the user; may mutate settings.
    /// </summary>
    private void UpdateV1048RaceAliases()
    {
        // first update attribute group:
        var attGroup = _generalVM.AttributeGroupMenu.Groups
            .FirstOrDefault(x => x.Label == DefaultAttributeGroups.CharmersOfTheReachHeads.Label);
        if (attGroup != null)
        {
            // null-safe: FirstOrDefault can return null when the group has no Mod sub-attribute (unexpected settings shape);
            // the downstream `if (cotrMods != null)` handles that, so do not deref the FirstOrDefault result directly.
            var cotrAttribute = attGroup.Attributes.FirstOrDefault(x => x.GroupedSubAttributes.FirstOrDefault()?.Attribute is VM_NPCAttributeMod);
            var cotrMods = cotrAttribute?.GroupedSubAttributes.First().Attribute as VM_NPCAttributeMod;

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

        // then update aliases
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
    }

    /// <summary>v1.0.5.3: offers to switch the CotR-heads attribute group's Mod-type attributes to the new "WinningAppearanceIsFrom" mode. Prompts the user; may mutate settings.</summary>
    private void UpdateV1053CotrAttributes()
    {
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
    }

    /// <summary>v1.0.6.8: shows an informational dialog describing new Script/NifEdit modes for face textures and head parts, and the removal of mandatory updates. Pops UI; no settings change.</summary>
    private void UpdateV1068()
    {
        MessageWindow.DisplayNotificationOK("Version 1.0.6.8 Update Notes",
            """
            Hello, returning user! This new version 1.0.6.8 has a few important updates that you should know about:

            1) Face textures can now be applied either by script, or by directly editing the FaceGen Nif Files.
            - This is controllable in the Textures and Meshes menu.
            - For you, it'll be kept in Script mode unless you change it manually (in case you have a current playthrough)
            - For new users, NifEdit mode will be default.
            - NifEdit mode is slower to patch and requires hard drive space for the modified nif files, but does not use any in-game scripts and therefore can't conflict with other mods that edit NPC appearance in-game.
            - If you choose to remain in Script mode, the face texture script has been slightly optimized to mitigate event spamming.
            - Do not switch from Script mode to NifEdit mode mid-playthrough unless you're comfortable with FallrimTools/Resaver.

            2) Headparts can now also be applied either by script, or by directly editing the FaceGen Nif Files.
            - Unlike Script Mode, NifEdit mode for headparts does not corrupt NPC faces.
            - Please continue treating this feature as semi-experimental, and be warned that some headpart types might not work - you'll have to do a test run and look in game.
            - Script Mode for headparts continues to be purely experimental and available only for testing. It continues to have all the issues documented in the YouTube video on the SynthEBD Nexus page. It was forwarded by user request from the original EBD code, and suffers from all the bugs that the original had when applying to NPCs with custom sculpts (which in a modern load order is almost all of them). Don't use it unless you're experimenting or really know what you're doing.

            3) Eval has been replaced in the SynthEBD code. No more mandatory monthly updates! Now SynthEBD will update only when I have new content or bug fixes to share.
            """);
    }

    /// <summary>v1.0.7.0: silently repairs the Charmers of the Reach Imperial Vampire race alias, which the
    /// defaults (and the 1.0.4.8 backfill set) omitted in favor of a duplicate Imperial entry. For users who
    /// already use the CotR Imperial alias, drops the redundant duplicate and adds the missing Imperial Vampire
    /// alias. Skips users without the CotR Imperial alias so CotR support a user removed is not reintroduced.</summary>
    private void UpdateV1070RaceAliases()
    {
        var aliasVMs = _generalVM.raceAliases;
        FormKey imperialSourceRace = DefaultRaceAliases.RaceAliasCotR_Imperial.Race;
        RaceAlias imperialVampire = DefaultRaceAliases.RaceAliasCotR_ImperialVampire;

        // Only repair settings that actually use the CotR Imperial alias; don't reintroduce CotR support a user removed.
        if (!aliasVMs.Any(x => x.Race.Equals(imperialSourceRace)))
        {
            return;
        }

        bool changed = false;

        // The defaults listed the CotR Imperial alias twice; drop any redundant copies, keeping the first.
        foreach (var duplicate in aliasVMs.Where(x => x.Race.Equals(imperialSourceRace)).Skip(1).ToList())
        {
            aliasVMs.Remove(duplicate);
            changed = true;
        }

        // Add the CotR Imperial Vampire alias the duplicate had displaced, if the user lacks it.
        if (!aliasVMs.Any(x => x.Race.Equals(imperialVampire.Race)))
        {
            aliasVMs.Add(new VM_RaceAlias(imperialVampire, _generalVM, _environmentProvider));
            changed = true;
        }

        if (changed)
        {
            _logger.LogMessage("Update 1.0.7.0: restored the Charmers of the Reach Imperial Vampire race alias.");
        }
    }

    /// <summary>
    /// v1.0.7.0: rewrites attribute-group labels that were renamed in this version (see
    /// <see cref="AttributeGroupLabelMigrator.RenamedLabels"/> - the "Mildy"->"Mildly" MatureFace typo fix)
    /// across every loaded attribute-group menu: General Settings (which the Head Part rules share), each asset
    /// pack, OBody, and each male/female BodyGen config. Attribute groups are referenced by label, and in the
    /// view-model layer a reference is a live pointer to the menu's <see cref="VM_AttributeGroup"/> definition
    /// (its label is read on serialization), so renaming the definitions here updates every Allowed/Disallowed/
    /// ForceIf reference - including group-in-group definitions - without walking each rule. Mutates the VMs.
    /// </summary>
    private void UpdateV1070AttributeGroupRename()
    {
        RenameAttributeGroupLabels(_generalVM.AttributeGroupMenu); // also covers Head Part rules, which share the General menu
        foreach (var assetPack in _texMeshVM.AssetPacks)
        {
            RenameAttributeGroupLabels(assetPack.AttributeGroupMenu);
        }
        RenameAttributeGroupLabels(_oBodyVM.AttributeGroupMenu);
        foreach (var bodyGenConfig in _bodyGenVM.MaleConfigs.Concat(_bodyGenVM.FemaleConfigs))
        {
            RenameAttributeGroupLabels(bodyGenConfig.AttributeGroupMenu);
        }
    }

    /// <summary>Applies the <see cref="AttributeGroupLabelMigrator.RenamedLabels"/> map to each group definition's
    /// label in an attribute-group menu. The menu's checkbox references hold these same VM instances, so renaming
    /// the definition propagates to every reference when the rules are serialized back to their models.</summary>
    private static void RenameAttributeGroupLabels(VM_AttributeGroupMenu menu)
    {
        if (menu == null) { return; }
        foreach (var group in menu.Groups)
        {
            group.Label = AttributeGroupLabelMigrator.Rename(group.Label);
        }
    }

    /// <summary>v1.0.5.5: adds a set of additional CotR-related mod keys (MOS/Refined plugins) to the CotR-heads attribute group's Mod-type attributes. Mutates settings (no prompt).</summary>
    private void UpdateV1055CotrAttributes()
    {
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

        List<ModKey> newCotrKeys = new()
        {
            ModKey.FromNameAndExtension("MOSRefinedDawnguard.esp"),
            ModKey.FromNameAndExtension("MOSRefinedDragonborn.esp"),
            ModKey.FromNameAndExtension("MOSUniqueNPC.esp"),
            ModKey.FromNameAndExtension("GoreRefined.esp"),
            ModKey.FromNameAndExtension("LucienRefined.esp"),
            ModKey.FromNameAndExtension("RemielRefined.esp"),
            ModKey.FromNameAndExtension("SeranaRefined.esp"),
            ModKey.FromNameAndExtension("0SkeeverRefined.esp")
        };

        foreach (var attribute in toUpdate)
        {
            foreach (var newModKey in newCotrKeys)
            {
                if (!attribute.ModKeys.Contains(newModKey))
                {
                    attribute.ModKeys.Add(newModKey);
                }
            }
        }
    }

    /// <summary>Legacy texture-path key remappings used to migrate v0.9-era asset paths to the current ".GivenPath" suffixed form.</summary>
    public Dictionary<string, string> V09PathReplacements { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Diffuse", "Diffuse.GivenPath" },
        { "NormalOrGloss", "NormalOrGloss.GivenPath" },
        { "GlowOrDetailMap", "GlowOrDetailMap.GivenPath" },
        { "BacklightMaskOrSpecular", "BacklightMaskOrSpecular.GivenPath" },
        { "Height", "Height.GivenPath" }
    };

    /// <summary>Recommended generic NPC names suggested for the linked-unique exclusion list by the v1.0.1.2 migration.</summary>
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

/// <summary>
/// Persisted record (UpdateLog.json) of which version migrations have been applied. Going forward this is the
/// single <see cref="LastAppliedVersion"/> string; the legacy per-update booleans are retained only for
/// deserializing old files and one-time migration into <see cref="LastAppliedVersion"/>.
/// </summary>
public class UpdateLog
{
    /// <summary>
    /// The last SynthEBD version for which all backward-compatibility updates were successfully applied.
    /// Replaces the per-update boolean flags going forward.
    /// </summary>
    public string LastAppliedVersion { get; set; } = string.Empty;

    // Legacy boolean flags — retained for deserialization of existing UpdateLog.json files
    // and for one-time migration into LastAppliedVersion via DeriveVersionFromBooleans().
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
    public bool Performed1_0_5_5_CotrAttributeUpdates { get; set; } = false;
}

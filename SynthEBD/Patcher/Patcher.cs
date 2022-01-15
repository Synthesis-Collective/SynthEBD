using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;
using Mutagen.Bethesda.Plugins.Records;

namespace SynthEBD
{
    public class Patcher
    {
        //Synchronous version for debugging only
        public static void RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
        //public static async Task RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
        {
            ModKey.TryFromName(PatcherSettings.General.patchFileName, ModType.Plugin, out var patchModKey);
            var outputMod = new SkyrimMod(patchModKey, SkyrimRelease.SkyrimSE);
            MainLinkCache = GameEnvironmentProvider.MyEnvironment.LoadOrder.ToMutableLinkCache(outputMod);
            ResolvePatchableRaces();
            InitializeIgnoredArmorAddons();
            UpdateRecordTemplateAdditonalRaces(assetPacks, recordTemplateLinkCache, recordTemplatePlugins);
            BodyGenTracker = new BodyGenAssignmentTracker();
            UniqueAssignmentsByName.Clear();
            UniqueNPCData.UniqueNameExclusions = linkedNPCNameExclusions;

            Logger.UpdateStatus("Patching", false);
            Logger.StartTimer();
            Logger.Instance.PatcherExecutionStart = DateTime.Now;

            statusBar.IsPatching = true;

            HashSet<LinkedNPCGroupInfo> generatedLinkGroups = new HashSet<LinkedNPCGroupInfo>();
            CategorizedFlattenedAssetPacks availableAssetPacks = null;

            var allNPCs = GameEnvironmentProvider.MyEnvironment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>();
            HashSet<INpcGetter> skippedLinkedNPCs = new HashSet<INpcGetter>();

            Keyword EBDFaceKW = null;
            Keyword EBDScriptKW = null;
            Spell EBDHelperSpell = null;

            Spell bodySlideAssignmentSpell = null;

            HeightConfig currentHeightConfig = null;

            if (PatcherSettings.General.bChangeMeshesOrTextures)
            {
                HashSet<FlattenedAssetPack> flattenedAssetPacks = new HashSet<FlattenedAssetPack>();
                flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x, PatcherSettings.General.RaceGroupings)).ToHashSet();
                PathTrimmer.TrimFlattenedAssetPacks(flattenedAssetPacks, PatcherSettings.TexMesh.TrimPaths);
                availableAssetPacks = new CategorizedFlattenedAssetPacks(flattenedAssetPacks);

                EBDCoreRecords.CreateCoreRecords(outputMod, out EBDFaceKW, out EBDScriptKW, out EBDHelperSpell);
                EBDCoreRecords.ApplyHelperSpell(outputMod, EBDHelperSpell);
            }

            // Several operations are performed that mutate the input settings. For Asset Packs this does not affect saved settings because the operations are performed on the derived FlattenedAssetPacks, but for BodyGen configs and OBody settings these are made directly to the settings files. Therefore, create a deep copy of the configs and operate on those to avoid altering the user's saved settings upon exiting the program
            BodyGenConfigs copiedBodyGenConfigs = JSONhandler<BodyGenConfigs>.Deserialize(JSONhandler<BodyGenConfigs>.Serialize(bodyGenConfigs));
            Settings_OBody copiedOBodySettings = JSONhandler<Settings_OBody>.Deserialize(JSONhandler<Settings_OBody>.Serialize(PatcherSettings.OBody));
            copiedOBodySettings.CurrentlyExistingBodySlides = PatcherSettings.OBody.CurrentlyExistingBodySlides; // JSONIgnored so doesn't get serialized/deserialized

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                // Pre-process some aspects of the configs to improve performance. Mutates the input configs so be sure to use a copy to avoid altering users settings
                BodyGenPreprocessing.CompileBodyGenRaces(copiedBodyGenConfigs);
                BodyGenPreprocessing.FlattenGroupAttributes(copiedBodyGenConfigs);
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                copiedOBodySettings.BodySlidesFemale = copiedOBodySettings.BodySlidesFemale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).ToList(); // don't assign BodySlides that have been uninstalled
                copiedOBodySettings.BodySlidesMale = copiedOBodySettings.BodySlidesMale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).ToList();
                OBodyPreprocessing.CompilePresetRaces(copiedOBodySettings);
                OBodyPreprocessing.FlattenPresetAttributes(copiedOBodySettings);

                var bodyslidesLoaded = outputMod.Globals.AddNewShort();
                bodyslidesLoaded.EditorID = "SynthEBDBodySlidesLoaded";
                bodyslidesLoaded.Data = 0;

                OBodyWriter.CreateBodySlideLoaderQuest(outputMod, bodyslidesLoaded);
                bodySlideAssignmentSpell = OBodyWriter.CreateOBodyAssignmentSpell(outputMod, bodyslidesLoaded);
            }

            if (PatcherSettings.General.bChangeHeight)
            {
                currentHeightConfig = heightConfigs.Where(x => x.Label == PatcherSettings.Height.SelectedHeightConfig).FirstOrDefault();
                if (currentHeightConfig == null)
                {
                    Logger.LogError("Could not find selected Height Config:" + PatcherSettings.Height.SelectedHeightConfig + ". Heights will not be assigned.");
                }
                else
                {
                    HeightPatcher.AssignRacialHeight(currentHeightConfig, outputMod);
                }
            }

            int npcCounter = 0;
            statusBar.ProgressBarMax = allNPCs.Count();
            statusBar.ProgressBarCurrent = 0;
            statusBar.ProgressBarDisp = "Patched " + statusBar.ProgressBarCurrent + " NPCs";
            // Patch main NPCs
            MainLoop(allNPCs, true, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, consistency, specificNPCAssignments, blockList, linkedNPCGroups,  recordTemplateLinkCache, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, bodySlideAssignmentSpell, statusBar);
            // Finish assigning non-primary linked NPCs
            MainLoop(skippedLinkedNPCs, false, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, consistency, specificNPCAssignments, blockList, linkedNPCGroups, recordTemplateLinkCache, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, bodySlideAssignmentSpell, statusBar);

            Logger.StopTimer();
            Logger.LogMessage("Finished patching in " + Logger.GetEllapsedTime());
            Logger.UpdateStatus("Finished Patching in " + Logger.GetEllapsedTime(), false);

            string patchOutputPath = System.IO.Path.Combine(PatcherSettings.General.OutputDataFolder, PatcherSettings.General.patchFileName + ".esp");
            GameEnvironmentProvider.MyEnvironment.Dispose();
            PatcherIO.WritePatch(patchOutputPath, outputMod);

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                BodyGenWriter.WriteBodyGenOutputs(copiedBodyGenConfigs);
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                OBodyWriter.CopyBodySlideScript();
                OBodyWriter.WriteAssignmentDictionary();
            }

            statusBar.IsPatching = false;
        }

        public static ILinkCache<ISkyrimMod, ISkyrimModGetter> MainLinkCache;

        public static HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces;

        public static HashSet<IFormLinkGetter<IArmorAddonGetter>> IgnoredArmorAddons;

        public static Dictionary<string, Dictionary<Gender, UniqueNPCData.UniqueNPCTracker>> UniqueAssignmentsByName = new Dictionary<string, Dictionary<Gender, UniqueNPCData.UniqueNPCTracker>>();


        private static void timer_Tick(object sender, EventArgs e)
        {
            Logger.UpdateStatus("Finished Patching", false);
        }

        public class CategorizedFlattenedAssetPacks
        {
            public CategorizedFlattenedAssetPacks(HashSet<FlattenedAssetPack> availableAssetPacks)
            {
                PrimaryMale = availableAssetPacks.Where(x => x.Gender == Gender.Male && x.Type == FlattenedAssetPack.AssetPackType.Primary).ToHashSet();
                PrimaryFemale = availableAssetPacks.Where(x => x.Gender == Gender.Female && x.Type == FlattenedAssetPack.AssetPackType.Primary).ToHashSet();
                MixInMale = availableAssetPacks.Where(x => x.Gender == Gender.Male && x.Type == FlattenedAssetPack.AssetPackType.MixIn).ToHashSet();
                MixInFemale = availableAssetPacks.Where(x => x.Gender == Gender.Female && x.Type == FlattenedAssetPack.AssetPackType.MixIn).ToHashSet();
            }
            public HashSet<FlattenedAssetPack> PrimaryMale { get; set; }
            public HashSet<FlattenedAssetPack> PrimaryFemale { get; set; }
            public HashSet<FlattenedAssetPack> MixInMale { get; set; }
            public HashSet<FlattenedAssetPack> MixInFemale { get; set; }
        }

        private static void MainLoop(IEnumerable<INpcGetter> npcCollection, bool skipLinkedSecondaryNPCs, SkyrimMod outputMod, CategorizedFlattenedAssetPacks sortedAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, HeightConfig currentHeightConfig, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<LinkedNPCGroup> linkedNPCGroups, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, int npcCounter, HashSet<LinkedNPCGroupInfo> generatedLinkGroups, HashSet<INpcGetter> skippedLinkedNPCs, Keyword EBDFaceKW, Keyword EBDScriptKW, Spell bodySlideAssignmentSpell, VM_StatusBar statusBar)
        {
            bool blockAssets;
            bool blockBodyShape;
            bool blockHeight;
            bool assetsAssigned = false;
            bool bodyShapeAssigned = false;

            BlockedNPC blockListNPCEntry;
            BlockedPlugin blockListPluginEntry;

            HashSet<FlattenedAssetPack> primaryAssetPacks = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedAssetPack> mixInAssetPacks = new HashSet<FlattenedAssetPack>();

            foreach (var npc in npcCollection)
            {
                npcCounter++;

                var currentNPCInfo = new NPCInfo(npc, linkedNPCGroups, generatedLinkGroups, specificNPCAssignments, consistency);

                assetsAssigned = false;
                bodyShapeAssigned = false;

                #region Linked NPC Groups
                if (skipLinkedSecondaryNPCs && currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
                {
                    skippedLinkedNPCs.Add(npc);
                    continue;
                }
                else
                {
                    statusBar.ProgressBarCurrent++;
                }
                #endregion

                if (statusBar.ProgressBarCurrent % 1000 == 0)
                {
                    statusBar.ProgressBarDisp = "Patched " + statusBar.ProgressBarCurrent + " NPCs";
                }

                #region link by name
                if (PatcherSettings.General.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique)
                {
                    UniqueNPCData.InitializeUniqueNPC(currentNPCInfo);
                }
                #endregion

                #region Detailed logging
                if (PatcherSettings.General.verboseModeNPClist.Contains(npc.FormKey) || PatcherSettings.General.bVerboseModeAssetsAll || PatcherSettings.General.bVerboseModeAssetsNoncompliant)
                {
                    Logger.TriggerNPCReporting(currentNPCInfo);
                }
                if (PatcherSettings.General.verboseModeNPClist.Contains(npc.FormKey) || PatcherSettings.General.bVerboseModeAssetsAll)
                {
                    Logger.TriggerNPCReportingSave(currentNPCInfo);
                }

                Logger.InitializeNewReport(currentNPCInfo);
                #endregion

                #region Block List
                blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(blockList, npc.FormKey);
                blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(blockList, npc.FormKey);

                if (blockListNPCEntry.Assets || blockListPluginEntry.Assets) { blockAssets = true; }
                else { blockAssets = false; }

                if (blockListNPCEntry.BodyGen || blockListPluginEntry.BodyGen) { blockBodyShape = true; }
                else { blockBodyShape = false; }

                if (blockListNPCEntry.Height || blockListPluginEntry.Height) { blockHeight = true; }
                else { blockHeight = false; }
                #endregion

                bodyShapeAssigned = false;

                if (PatcherSettings.General.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
                {
                    continue;
                }

                if (PatcherSettings.General.ExcludePresets && npc.EditorID.Contains("Preset"))
                {
                    continue;
                }

                AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment assignedPrimaryComboAndBodyShape = new AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment();

                #region Primary Assets (and optionally Body Shape) assignment
                if (PatcherSettings.General.bChangeMeshesOrTextures && !blockAssets && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.AssetsRace))
                {
                    switch (currentNPCInfo.Gender)
                    {
                        case Gender.Female: 
                            primaryAssetPacks = sortedAssetPacks.PrimaryFemale;
                            mixInAssetPacks = sortedAssetPacks.MixInFemale;
                            break;
                        case Gender.Male:
                            primaryAssetPacks = sortedAssetPacks.PrimaryMale;
                            mixInAssetPacks = sortedAssetPacks.MixInMale;
                            break;
                    }

                    assignedPrimaryComboAndBodyShape = AssetAndBodyShapeSelector.ChooseCombinationAndBodyShape(out assetsAssigned, out bodyShapeAssigned, primaryAssetPacks, bodyGenConfigs, oBodySettings, currentNPCInfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary, null);
                    if (assetsAssigned)
                    {
                        RecordGenerator.CombinationToRecords(assignedPrimaryComboAndBodyShape.AssignedCombination, currentNPCInfo, recordTemplateLinkCache, outputMod);
                        var npcRecord = outputMod.Npcs.GetOrAddAsOverride(currentNPCInfo.NPC);
                        if (npcRecord.Keywords == null) { npcRecord.Keywords = new Noggog.ExtendedList<IFormLinkGetter<IKeywordGetter>>(); }
                        npcRecord.Keywords.Add(EBDFaceKW);
                        npcRecord.Keywords.Add(EBDScriptKW);
                        AssetSelector.RecordAssetConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedCombination, currentNPCInfo);
                    }
                    if (bodyShapeAssigned)
                    {
                        switch (PatcherSettings.General.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen:
                                BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs.Select(x => x.Label).ToList());
                                BodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs, currentNPCInfo);
                                break;

                            case BodyShapeSelectionMode.BodySlide:
                                BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, assignedPrimaryComboAndBodyShape.AssignedOBodyPreset.Label);
                                OBodyWriter.ApplyBodySlideSpell(npc, bodySlideAssignmentSpell, outputMod);
                                OBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedOBodyPreset, currentNPCInfo);
                                break;
                        }
                    }
                }
                #endregion

                #region Body Shape assignment (if assets not assigned with Assets)
                switch (PatcherSettings.General.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.None: break;

                    case BodyShapeSelectionMode.BodyGen:
                        if (!blockBodyShape && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && BodyGenSelector.BodyGenAvailableForGender(currentNPCInfo.Gender, bodyGenConfigs))
                        {
                            Logger.LogReport("Assigning a BodyGen morph independently of Asset Combination", false, currentNPCInfo);
                            var assignedMorphs = BodyGenSelector.SelectMorphs(currentNPCInfo, out bool success, bodyGenConfigs, null, out _);
                            if (success)
                            {
                                BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedMorphs.Select(x => x.Label).ToList());
                                BodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(assignedMorphs, currentNPCInfo);
                                assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs = assignedMorphs;
                            }
                            else
                            {
                                Logger.LogReport("Could not independently assign a BodyGen Morph.", true, currentNPCInfo);
                            }
                        }
                        break;
                    case BodyShapeSelectionMode.BodySlide:
                        if (!blockBodyShape && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && OBodySelector.CurrentNPCHasAvailablePresets(currentNPCInfo, oBodySettings))
                        {
                            Logger.LogReport("Assigning a BodySlide preset independently of Asset Combination", false, currentNPCInfo);
                            var assignedPreset = OBodySelector.SelectBodySlidePreset(currentNPCInfo, out bool success, oBodySettings, null, out _);
                            if (success)
                            {
                                BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, assignedPreset.Label);
                                OBodyWriter.ApplyBodySlideSpell(npc, bodySlideAssignmentSpell, outputMod);
                                OBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(assignedPreset, currentNPCInfo);
                                assignedPrimaryComboAndBodyShape.AssignedOBodyPreset = assignedPreset;
                            }
                            else
                            {
                                Logger.LogReport("Could not independently assign a BodySlide preset.", true, currentNPCInfo);
                            }
                        }
                        break;
                }
                #endregion

                if (PatcherSettings.General.bChangeMeshesOrTextures && !blockAssets && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.AssetsRace))
                {
                    #region Asset Replacer assignment
                    if (assetsAssigned) // assign direct replacers
                    {
                        var assignedReplacers = AssetSelector.SelectAssetReplacers(assignedPrimaryComboAndBodyShape.AssignedCombination.AssetPack, currentNPCInfo, assignedPrimaryComboAndBodyShape);
                        foreach (var replacerCombination in assignedReplacers)
                        {
                            RecordGenerator.ReplacerCombinationToRecords(replacerCombination, currentNPCInfo, outputMod);
                        }
                    }
                    #endregion

                    #region MixIn Asset assignment
                    foreach (var mixInConfig in mixInAssetPacks)
                    {
                        var assignedMixIn = AssetAndBodyShapeSelector.ChooseCombinationAndBodyShape(out assetsAssigned, out _, new HashSet<FlattenedAssetPack>() { mixInConfig }, bodyGenConfigs, oBodySettings, currentNPCInfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn, assignedPrimaryComboAndBodyShape);
                        if (assetsAssigned)
                        {
                            RecordGenerator.CombinationToRecords(assignedMixIn.AssignedCombination, currentNPCInfo, recordTemplateLinkCache, outputMod);
                            AssetSelector.RecordAssetConsistencyAndLinkedNPCs(assignedMixIn.AssignedCombination, currentNPCInfo, mixInConfig.GroupName);
                        }
                    }
                    #endregion
                }

                #region Height assignment
                if (PatcherSettings.General.bChangeHeight && !blockHeight && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.HeightRace))
                {
                    HeightPatcher.AssignNPCHeight(currentNPCInfo, currentHeightConfig, outputMod);
                }
                #endregion

                Logger.SaveReport(currentNPCInfo);
            }
        }

        private static void UpdateRecordTemplateAdditonalRaces(List<AssetPack> assetPacks, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
        {
            Dictionary<string, HashSet<string>> patchedTemplates = new Dictionary<string, HashSet<string>>();
            foreach (var assetPack in assetPacks)
            {
                HashSet<FormKey> templatesToPatch = new HashSet<FormKey>() { assetPack.DefaultRecordTemplate};
                foreach (var additionalTemplate in assetPack.AdditionalRecordTemplateAssignments)
                {
                    templatesToPatch.Add(additionalTemplate.TemplateNPC);
                }

                foreach (var path in assetPack.RecordTemplateAdditionalRacesPaths)
                {
                    foreach (FormKey templateFK in templatesToPatch)
                    {
                        if (patchedTemplates.ContainsKey(templateFK.ToString()) && patchedTemplates[templateFK.ToString()].Contains(path))
                        {
                            continue;
                        }

                        var templateMod = recordTemplatePlugins.Where(x => x.ModKey.ToString() == templateFK.ModKey.ToString()).FirstOrDefault();
                        if (templateMod == null)
                        {
                            Logger.LogError("Could not find record template plugin " + templateFK.ToString());
                        }

                        if (recordTemplateLinkCache.TryResolve<INpcGetter>(templateFK, out var template))
                        {
                            try
                            {
                                if (!RecordPathParser.GetNearestParentGetter(template, path, recordTemplateLinkCache, false, out IMajorRecordGetter parentRecordGetter, out string relativePath))
                                {
                                    continue;
                                }

                                var parentRecord = RecordGenerator.GetOrAddGenericRecordAsOverride(parentRecordGetter, templateMod);

                                if (RecordPathParser.GetObjectAtPath(parentRecord, relativePath, new Dictionary<dynamic, Dictionary<string, dynamic>>(), recordTemplateLinkCache, false, out dynamic additionalRaces))
                                {
                                    foreach (var race in PatcherSettings.General.patchableRaces)
                                    {
                                        additionalRaces.Add(race.AsLink<IRaceGetter>());
                                    }
                                }
                                if (!patchedTemplates.ContainsKey(templateFK.ToString()))
                                {
                                    patchedTemplates.Add(templateFK.ToString(), new HashSet<string>());
                                }
                                patchedTemplates[templateFK.ToString()].Add(path);
                            }
                            catch
                            {
                                Logger.LogError("Could not patch additional races expected at " + path + " in template NPC " + Logger.GetNPCLogNameString(template));
                                continue;
                            }
                        }
                        else
                        {
                            Logger.LogError("Could not resolve template NPC " + template.ToString());
                        }
                    }
                }
            }
        }

        public static void ResolvePatchableRaces()
        {
            PatchableRaces = new HashSet<IFormLinkGetter<IRaceGetter>>();
            foreach (var raceFK in PatcherSettings.General.patchableRaces)
            {
                if (MainLinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
                {
                    PatchableRaces.Add(raceGetter.AsLinkGetter());
                }
            }
            PatchableRaces.Add(Skyrim.Race.DefaultRace.TryResolve(MainLinkCache).AsLinkGetter());
        }

        private static void InitializeIgnoredArmorAddons()
        {
            IgnoredArmorAddons = new HashSet<IFormLinkGetter<IArmorAddonGetter>>();
            IgnoredArmorAddons.Add(Skyrim.ArmorAddon.NakedTorsoWerewolfBeast.FormKey.AsLinkGetter<IArmorAddonGetter>());
            IgnoredArmorAddons.Add(Dawnguard.ArmorAddon.DLC1NakedVampireLord.FormKey.AsLinkGetter<IArmorAddonGetter>());
            IgnoredArmorAddons.Add(Dragonborn.ArmorAddon.DLC2NakedTorsoWerebearBeast.FormKey.AsLinkGetter<IArmorAddonGetter>());
        }

        public static BodyGenAssignmentTracker BodyGenTracker = new BodyGenAssignmentTracker(); // tracks unique selected morphs so that only assigned morphs are written to the generated templates.ini
        public static Dictionary<FormKey, string> BodySlideTracker = new Dictionary<FormKey, string>(); // tracks which NPCs get which bodyslide presets
        public static Dictionary<string, int> EdidCounts = new Dictionary<string, int>(); // tracks the number of times a given record template was assigned so that a newly copied record can have its editor ID incremented

        public class BodyGenAssignmentTracker
        {
            public BodyGenAssignmentTracker()
            {
                NPCAssignments = new Dictionary<FormKey, List<string>>();
                AllChosenMorphsMale = new Dictionary<string, HashSet<string>>();
                AllChosenMorphsFemale = new Dictionary<string, HashSet<string>>();
            }
            public Dictionary<FormKey, List<string>> NPCAssignments;
            public Dictionary<string, HashSet<string>> AllChosenMorphsMale;
            public Dictionary<string, HashSet<string>> AllChosenMorphsFemale;
        }
    }
}

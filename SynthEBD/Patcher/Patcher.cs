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
        public static void RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, HashSet<TrimPath> trimPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
        //public static async Task RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, HashSet<TrimPath> trimPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
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

            statusBar.IsPatching = true;

            HashSet<LinkedNPCGroupInfo> generatedLinkGroups = new HashSet<LinkedNPCGroupInfo>();
            HashSet<FlattenedAssetPack> maleAssetPacks = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedAssetPack> femaleAssetPacks = new HashSet<FlattenedAssetPack>();

            var allNPCs = GameEnvironmentProvider.MyEnvironment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>();
            HashSet<INpcGetter> skippedLinkedNPCs = new HashSet<INpcGetter>();

            Keyword EBDFaceKW = null;
            Keyword EBDScriptKW = null;
            Spell EBDHelperSpell = null;

            HeightConfig currentHeightConfig = null;

            if (PatcherSettings.General.bChangeMeshesOrTextures)
            {
                HashSet<FlattenedAssetPack> flattenedAssetPacks = new HashSet<FlattenedAssetPack>();
                flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x, PatcherSettings.General.RaceGroupings, PatcherSettings.General.bEnableBodyGenIntegration)).ToHashSet();
                PathTrimmer.TrimFlattenedAssetPacks(flattenedAssetPacks, trimPaths);
                maleAssetPacks = flattenedAssetPacks.Where(x => x.Gender == Gender.male).ToHashSet();
                femaleAssetPacks = flattenedAssetPacks.Where(x => x.Gender == Gender.female).ToHashSet();

                EBDCoreRecords.CreateCoreRecords(outputMod, out EBDFaceKW, out EBDScriptKW, out EBDHelperSpell);
                EBDCoreRecords.ApplyHelperSpell(outputMod, EBDHelperSpell);
            }

            if (PatcherSettings.General.bEnableBodyGenIntegration)
            {
                CompileBodyGenRaces(bodyGenConfigs);
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
            MainLoop(allNPCs, true, outputMod, maleAssetPacks, femaleAssetPacks, bodyGenConfigs, currentHeightConfig, consistency, specificNPCAssignments, blockList, linkedNPCGroups,  recordTemplateLinkCache, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, statusBar);
            // Finish assigning non-primary linked NPCs
            MainLoop(skippedLinkedNPCs, false, outputMod, maleAssetPacks, femaleAssetPacks, bodyGenConfigs, currentHeightConfig, consistency, specificNPCAssignments, blockList, linkedNPCGroups, recordTemplateLinkCache, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, statusBar);

            Logger.StopTimer();
            Logger.LogMessage("Finished patching in " + Logger.GetEllapsedTime());
            Logger.UpdateStatus("Finished Patching in " + Logger.GetEllapsedTime(), false);

            if (PatcherSettings.General.bChangeMeshesOrTextures || PatcherSettings.General.bChangeHeight)
            {
                string patchOutputPath = System.IO.Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, PatcherSettings.General.patchFileName + ".esp");
                PatcherIO.WritePatch(patchOutputPath, outputMod);
            }

            if (PatcherSettings.General.bEnableBodyGenIntegration)
            {
                BodyGenWriter.WriteBodyGenOutputs(bodyGenConfigs);
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

        private static void MainLoop(IEnumerable<INpcGetter> npcCollection, bool skipLinkedSecondaryNPCs, SkyrimMod outputMod, HashSet<FlattenedAssetPack> maleAssetPacks, HashSet<FlattenedAssetPack> femaleAssetPacks, BodyGenConfigs bodyGenConfigs, HeightConfig currentHeightConfig, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<LinkedNPCGroup> linkedNPCGroups, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, int npcCounter, HashSet<LinkedNPCGroupInfo> generatedLinkGroups, HashSet<INpcGetter> skippedLinkedNPCs, Keyword EBDFaceKW, Keyword EBDScriptKW, VM_StatusBar statusBar)
        {
            bool blockAssets;
            bool blockBodyGen;
            bool blockHeight;
            bool bodyGenAssignedWithAssets;

            BlockedNPC blockListNPCEntry;
            BlockedPlugin blockListPluginEntry;

            HashSet<FlattenedAssetPack> availableAssetPacks = new HashSet<FlattenedAssetPack>();

            foreach (var npc in npcCollection)
            {
                npcCounter++;

                var currentNPCInfo = new NPCInfo(npc, linkedNPCGroups, generatedLinkGroups, specificNPCAssignments, consistency);

                // link group
                if (skipLinkedSecondaryNPCs && currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
                {
                    skippedLinkedNPCs.Add(npc);
                    continue;
                }
                else
                {
                    statusBar.ProgressBarCurrent++;
                }

                if (statusBar.ProgressBarCurrent % 1000 == 0)
                {
                    statusBar.ProgressBarDisp = "Patched " + statusBar.ProgressBarCurrent + " NPCs";
                }

                // link by name
                if (PatcherSettings.General.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique)
                {
                    UniqueNPCData.InitializeUniqueNPC(currentNPCInfo);
                }

                Logger.InitializeNewReport(currentNPCInfo);

                blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(blockList, npc.FormKey);
                blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(blockList, npc.FormKey);

                if (blockListNPCEntry.Assets || blockListPluginEntry.Assets) { blockAssets = true; }
                else { blockAssets = false; }

                if (blockListNPCEntry.BodyGen || blockListPluginEntry.BodyGen) { blockBodyGen = true; }
                else { blockBodyGen = false; }

                if (blockListNPCEntry.Height || blockListPluginEntry.Height) { blockHeight = true; }
                else { blockHeight = false; }

                bodyGenAssignedWithAssets = false;

                if (PatcherSettings.General.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
                {
                    continue;
                }

                if (PatcherSettings.General.ExcludePresets && npc.EditorID.Contains("Preset"))
                {
                    continue;
                }

                // Assets/BodyGen assignment
                if (PatcherSettings.General.bChangeMeshesOrTextures && !blockAssets && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.AssetsRace))
                {
                    switch (currentNPCInfo.Gender)
                    {
                        case Gender.female: availableAssetPacks = femaleAssetPacks; break;
                        case Gender.male: availableAssetPacks = maleAssetPacks; break;
                    }

                    var assignedComboAndBodyGen = AssetAndBodyGenSelector.ChooseCombinationAndBodyGen(out bodyGenAssignedWithAssets, availableAssetPacks, bodyGenConfigs, currentNPCInfo, blockBodyGen);
                    if (assignedComboAndBodyGen.Item1 != null)
                    {
                        RecordGenerator.CombinationToRecords(assignedComboAndBodyGen.Item1, currentNPCInfo, recordTemplateLinkCache, outputMod);
                        var npcRecord = outputMod.Npcs.GetOrAddAsOverride(currentNPCInfo.NPC);
                        if (npcRecord.Keywords == null) { npcRecord.Keywords = new Noggog.ExtendedList<IFormLinkGetter<IKeywordGetter>>(); }
                        npcRecord.Keywords.Add(EBDFaceKW);
                        npcRecord.Keywords.Add(EBDScriptKW);
                    }
                    if (bodyGenAssignedWithAssets)
                    {
                        BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedComboAndBodyGen.Item2);
                        currentNPCInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignedComboAndBodyGen.Item2;

                        // assign to linked group if necessary 
                        if (currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
                        {
                            currentNPCInfo.AssociatedLinkGroup.AssignedMorphs = assignedComboAndBodyGen.Item2;
                        }
                        // assign to unique NPC list if necessary
                        if (PatcherSettings.General.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique && UniqueAssignmentsByName[currentNPCInfo.Name][currentNPCInfo.Gender].AssignedMorphs == null)
                        {
                            UniqueAssignmentsByName[currentNPCInfo.Name][currentNPCInfo.Gender].AssignedMorphs = assignedComboAndBodyGen.Item2;
                        }
                    }
                }

                // BodyGen assignment (if assets not assigned in Assets/BodyGen section)
                if (PatcherSettings.General.bEnableBodyGenIntegration && !blockBodyGen && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.BodyGenRace) && !bodyGenAssignedWithAssets && BodyGenSelector.BodyGenAvailableForGender(currentNPCInfo.Gender, bodyGenConfigs))
                {
                    var assignedMorphs = BodyGenSelector.SelectMorphs(currentNPCInfo, out bool success, bodyGenConfigs, null, new BodyGenSelector.BodyGenSelectorStatusFlag());
                    if (success)
                    {
                        BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedMorphs);
                        currentNPCInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignedMorphs;

                        // assign to linked group if necessary
                        if (currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
                        {
                            currentNPCInfo.AssociatedLinkGroup.AssignedMorphs = assignedMorphs;
                        }
                        // assign to unique NPC list if necessary
                        if (PatcherSettings.General.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique && UniqueAssignmentsByName[currentNPCInfo.Name][currentNPCInfo.Gender].AssignedMorphs == null)
                        {
                            UniqueAssignmentsByName[currentNPCInfo.Name][currentNPCInfo.Gender].AssignedMorphs = assignedMorphs;
                        }
                    }
                }

                // Height assignment
                if (PatcherSettings.General.bChangeHeight && !blockHeight && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.HeightRace))
                {
                    HeightPatcher.AssignNPCHeight(currentNPCInfo, currentHeightConfig, outputMod);
                }
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
                                if (!RecordPathParser.GetNearestParentGetter(template, path, recordTemplateLinkCache, out IMajorRecordGetter parentRecordGetter, out string relativePath))
                                {
                                    continue;
                                }

                                var parentRecord = RecordGenerator.GetOrAddGenericRecordAsOverride(parentRecordGetter, templateMod);

                                if (RecordPathParser.GetObjectAtPath(parentRecord, relativePath, new Dictionary<dynamic, Dictionary<string, dynamic>>(), recordTemplateLinkCache, out dynamic additionalRaces))
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

        private static void ResolvePatchableRaces()
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

        /// <summary>
        /// Initializes the Compiled(Dis)AllowedRaces property in BodyGenConfigs by merging their AllowedRaces and AllowedRaceGroupings
        /// </summary>
        /// <param name="bodyGenConfigs"></param>
        private static void CompileBodyGenRaces(BodyGenConfigs bodyGenConfigs)
        {
            foreach (var config in bodyGenConfigs.Male)
            {
                CompileBodyGenConfigRaces(config);
            }
            foreach (var config in bodyGenConfigs.Female)
            {
                CompileBodyGenConfigRaces(config);
            }
        }

        /// <summary>
        /// Initializes the Compiled(Dis)AllowedRaces property in BodyGenConfig classes by merging their AllowedRaces and AllowedRaceGroupings
        /// </summary>
        /// <param name="bodyGenConfig"></param>
        private static void CompileBodyGenConfigRaces (BodyGenConfig bodyGenConfig)
        {
            foreach (var template in bodyGenConfig.Templates)
            {
                template.CompiledAllowedRaces = new HashSet<FormKey>(template.AllowedRaces);
                foreach (var allowedRaceGrouping in template.AllowedRaceGroupings)
                {
                    var grouping = PatcherSettings.General.RaceGroupings.Where(x => x.Label == allowedRaceGrouping).FirstOrDefault();
                    if (grouping == null) { continue; }
                    template.CompiledAllowedRaces = template.AllowedRaces.Union(grouping.Races).ToHashSet();
                }

                template.CompiledDisallowedRaces = new HashSet<FormKey>(template.DisallowedRaces);
                foreach (var disallowedRaceGrouping in template.DisallowedRaceGroupings)
                {
                    var grouping = PatcherSettings.General.RaceGroupings.Where(x => x.Label == disallowedRaceGrouping).FirstOrDefault();
                    if (grouping == null) { continue; }
                    template.CompiledDisallowedRaces = template.DisallowedRaces.Union(grouping.Races).ToHashSet();
                }
            }
        }

        public static BodyGenAssignmentTracker BodyGenTracker = new BodyGenAssignmentTracker();
        public static Dictionary<string, int> EdidCounts = new Dictionary<string, int>();

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

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

namespace SynthEBD
{
    public class MainLoop
    {
        //Synchronous version for debugging only
        public static void RunPatcher(List<AssetPack> assetPacks, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, HashSet<TrimPath> trimPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
        //public static async Task RunPatcher(List<AssetPack> assetPacks, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, HashSet<TrimPath> trimPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
        {
            ModKey.TryFromName(PatcherSettings.General.patchFileName, ModType.Plugin, out var patchModKey);
            var outputMod = new SkyrimMod(patchModKey, SkyrimRelease.SkyrimSE);
            MainLinkCache = GameEnvironmentProvider.MyEnvironment.LoadOrder.ToMutableLinkCache(outputMod);

            PropertyCache = new();

            ResolvePatchableRaces();
            InitializeIgnoredArmorAddons();
            UpdateRecordTemplateAdditonalRaces(assetPacks, recordTemplateLinkCache, recordTemplatePlugins);

            Dictionary<string, int> edidCounts = new Dictionary<string, int>();

            Logger.UpdateStatus("Patching", false);
            Logger.StartTimer();

            BlockedNPC blockListNPCEntry;
            BlockedPlugin blockListPluginEntry;
            HashSet<LinkedNPCGroupInfo> generatedLinkGroups = new HashSet<LinkedNPCGroupInfo>();
            HashSet<FlattenedAssetPack> maleAssetPacks = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedAssetPack> femaleAssetPacks = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedAssetPack> availableAssetPacks = new HashSet<FlattenedAssetPack>();

            bool blockAssets;
            bool blockBodyGen;
            bool blockHeight;
            bool bodyGenAssignedWithAssets;

            if (PatcherSettings.General.bChangeMeshesOrTextures)
            {
                HashSet<FlattenedAssetPack> flattenedAssetPacks = new HashSet<FlattenedAssetPack>();
                flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x, PatcherSettings.General.RaceGroupings, PatcherSettings.General.bEnableBodyGenIntegration)).ToHashSet();
                PathTrimmer.TrimFlattenedAssetPacks(flattenedAssetPacks, trimPaths);
                maleAssetPacks = flattenedAssetPacks.Where(x => x.Gender == Gender.male).ToHashSet();
                femaleAssetPacks = flattenedAssetPacks.Where(x => x.Gender == Gender.female).ToHashSet();
            }

            int npcCounter = 0;
            foreach (var npc in GameEnvironmentProvider.MyEnvironment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>())
            {
                npcCounter++;
                if (npcCounter % 100 == 0) 
                {
                    Logger.LogMessage("Examined " + npcCounter.ToString() + " NPCs in " + Logger.GetEllapsedTime()); 
                }

                var currentNPCInfo = new NPCInfo(npc, linkedNPCGroups, generatedLinkGroups, specificNPCAssignments, consistency);

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
                    //var availableAssetPacks = AssetPacksByRaceGender[new Tuple<FormKey, Gender>(currentNPCInfo.AssetsRace, currentNPCInfo.Gender)];
                    switch (currentNPCInfo.Gender)
                    {
                        case Gender.female: availableAssetPacks = femaleAssetPacks; break;
                        case Gender.male: availableAssetPacks = maleAssetPacks; break;
                    }
                    
                    var assignedComboAndBodyGen = AssetAndBodyGenSelector.ChooseCombinationAndBodyGen(out bodyGenAssignedWithAssets, availableAssetPacks, currentNPCInfo);
                    if (assignedComboAndBodyGen.Item1 != null)
                    {
                        RecordGenerator.CombinationToRecords(assignedComboAndBodyGen.Item1, currentNPCInfo, recordTemplateLinkCache, outputMod, edidCounts);
                    }
                }

                // BodyGen assignment (if assets not assigned in Assets/BodyGen section)
                if (PatcherSettings.General.bEnableBodyGenIntegration && !blockBodyGen && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.BodyGenRace) && !bodyGenAssignedWithAssets)
                {

                }

                // Height assignment
                if (PatcherSettings.General.bChangeHeight && !blockHeight && PatcherSettings.General.patchableRaces.Contains(currentNPCInfo.HeightRace))
                {

                }
            }

            Logger.StopTimer();
            Logger.LogMessage("Finished patching.");
            Logger.UpdateStatus("Finished Patching", false);

            string outputPath = System.IO.Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, PatcherSettings.General.patchFileName + ".esp");
            try
            {
                if (System.IO.File.Exists(outputPath))
                {
                    System.IO.File.Delete(outputPath);
                }
                outputMod.WriteToBinary(outputPath);
                Logger.LogMessage("Wrote output file at " + outputPath + ".");
            }
            catch { Logger.LogErrorWithStatusUpdate("Could not write output file to " + outputPath, ErrorType.Error); };
        }

        public static ILinkCache<ISkyrimMod, ISkyrimModGetter> MainLinkCache;

        public static Dictionary<Type, Dictionary<string, System.Reflection.PropertyInfo>> PropertyCache;

        public static HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces;

        public static HashSet<IFormLinkGetter<IArmorAddonGetter>> IgnoredArmorAddons;

        private static void timer_Tick(object sender, EventArgs e)
        {
            Logger.UpdateStatus("Finished Patching", false);
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
                                var parentRecordGetter = RecordPathParser.GetNearestParentGetter(template, path, recordTemplateLinkCache, out string relativePath);
                                if (parentRecordGetter == null) { continue; }
                                var parentRecord = RecordGenerator.GetOrAddGenericRecordAsOverride(parentRecordGetter, templateMod);
                                var additionalRaces = RecordPathParser.GetObjectAtPath(parentRecord, relativePath, new Dictionary<dynamic, Dictionary<string, dynamic>>(), recordTemplateLinkCache);
                                if (additionalRaces != null)
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
    }
}

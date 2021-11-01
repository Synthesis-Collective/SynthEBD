using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class MainLoop
    {
        public static async Task RunPatcher(Settings_General generalSettings, Settings_TexMesh texMeshSettings, Settings_Height heightSettings, Settings_BodyGen bodyGenSettings, List<AssetPack> assetPacks, List<HeightConfig> heightConfigs, HashSet<SpecificNPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, HashSet<TrimPath> trimPaths)
        {
            Logger.UpdateStatus("Patching", false);

            var env = new GameEnvironmentProvider().MyEnvironment;

            FormKey assetsRace;
            FormKey bodyGenRace;
            FormKey heightRace;

            BlockedNPC blockListNPCEntry;
            BlockedPlugin blockListPluginEntry;

            bool blockAssets;
            bool blockBodyGen;
            bool blockHeight;

            HashSet<FlattenedAssetPack> flattenedAssetPacks = new HashSet<FlattenedAssetPack>();
            if (generalSettings.bChangeMeshesOrTextures)
            {
                flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x, generalSettings.RaceGroupings, generalSettings.bEnableBodyGenIntegration)).ToHashSet();
            }

            int npcCounter = 0;
            foreach (var npc in env.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                npcCounter++;
                if (npcCounter % 100 == 0) { Logger.LogMessage("Examined" + npcCounter.ToString() + " NPCs."); }

                assetsRace = AliasHandler.GetAliasTexMesh(generalSettings, npc.Race.FormKey);
                bodyGenRace = AliasHandler.GetAliasBodyGen(generalSettings, npc.Race.FormKey);
                heightRace = AliasHandler.GetAliasHeight(generalSettings, npc.Race.FormKey);

                blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(blockList, npc.FormKey);
                blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(blockList, npc.FormKey);

                if (blockListNPCEntry.Assets || blockListPluginEntry.Assets) { blockAssets = true; }
                else { blockAssets = false; }

                if (blockListNPCEntry.BodyGen || blockListPluginEntry.BodyGen) { blockBodyGen = true; }
                else { blockBodyGen = false; }

                if (blockListNPCEntry.Height || blockListPluginEntry.Height) { blockHeight = true; }
                else { blockHeight = false; }

                /*
                if (generalSettings.bExcludePlayerCharacter && npc.FormKey.ModKey.ToString() == "Skyrim.esm" && npc.FormKey.IDString == "000007")
                {
                    continue;
                }
                */

                /*
                if (generalSettings.bExcludePresets && npc.EditorID.Contains("Preset"))
                {
                    continue;
                }
                */

                // Assets/BodyGen assignment
                if (generalSettings.bChangeMeshesOrTextures && !blockAssets && generalSettings.patchableRaces.Contains(assetsRace))
                {

                }

                // BodyGen assignment (if assets not assigned in Assets/BodyGen section)
                if (generalSettings.bEnableBodyGenIntegration && !blockBodyGen && generalSettings.patchableRaces.Contains(bodyGenRace))
                {

                }

                // Height assignment
                if (generalSettings.bChangeHeight && !blockHeight && generalSettings.patchableRaces.Contains(heightRace))
                {

                }
            }


            Logger.LogMessage("Finished patching.");
        }
    }
}

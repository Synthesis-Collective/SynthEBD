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
        public static void RunPatcher(Settings_General generalSettings, Settings_TexMesh texMeshSettings, Settings_Height heightSettings, Settings_BodyGen bodyGenSettings, List<AssetPack> AssetPacks, List<HeightConfig> HeightConfigs, HashSet<SpecificNPCAssignment> SpecificNPCAssignments, BlockList BlockList, HashSet<string> LinkedNPCNameExclusions, HashSet<LinkedNPCGroup> LinkedNPCGroups, HashSet<TrimPath> TrimPaths)
        {
            var env = new GameEnvironmentProvider().MyEnvironment;

            FormKey assetsRace;
            FormKey bodyGenRace;
            FormKey heightRace;

            BlockedNPC blockListNPCEntry;
            BlockedPlugin blockListPluginEntry;

            bool blockAssets;
            bool blockBodyGen;
            bool blockHeight;

            foreach (var npc in env.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                assetsRace = AliasHandler.GetAliasTexMesh(generalSettings, npc.Race.FormKey);
                bodyGenRace = AliasHandler.GetAliasBodyGen(generalSettings, npc.Race.FormKey);
                heightRace = AliasHandler.GetAliasHeight(generalSettings, npc.Race.FormKey);

                blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(BlockList, npc.FormKey);
                blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(BlockList, npc.FormKey);

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
        }
    }
}

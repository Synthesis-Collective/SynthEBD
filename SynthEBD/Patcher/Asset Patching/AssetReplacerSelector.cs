using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AssetReplacerSelector
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly AssetAndBodyShapeSelector _abSelector;
        private readonly AssetSelector _assetSelector;
        private readonly RecordPathParser _recordPathParser;
        private readonly DictionaryMapper _dictionaryMapper;
        public AssetReplacerSelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AssetAndBodyShapeSelector abSelector, AssetSelector assetSelector, RecordPathParser recordPathParser, DictionaryMapper dictionaryMapper)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _abSelector = abSelector;
            _assetSelector = assetSelector;
            _recordPathParser = recordPathParser;
            _dictionaryMapper = dictionaryMapper;
        }
        public HashSet<SubgroupCombination> SelectAssetReplacers(FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment currentAssignments)
        {
            HashSet<SubgroupCombination> combinations = new HashSet<SubgroupCombination>();
            // determine which replacer groups are valid for the current NPC
            foreach (var replacerGroup in chosenAssetPack.AssetReplacerGroups)
            {
                HashSet<string> targetPaths = new HashSet<string>();
                // get collection of paths that must be matched

                foreach (var subgroupsAtIndex in replacerGroup.Subgroups)
                {
                    foreach (var subgroup in subgroupsAtIndex)
                    {
                        foreach (var path in subgroup.Paths)
                        {
                            if (!targetPaths.Contains(path.Destination))
                            {
                                targetPaths.Add(path.Destination);
                            }
                        }
                    }
                }

                // check if NPC has those paths

                bool assignReplacer = true;
                var destinationType = SelectRecordType(targetPaths, out FormKey destinationFK);
                if (destinationType == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
                {
                    assignReplacer = CheckIfReplacerTargetExists(destinationFK, npcInfo.NPC);
                }
                else if (destinationType != SubgroupCombination.DestinationSpecifier.Generic)
                {
                    assignReplacer = CheckIfReplacerTargetExists(destinationType, npcInfo.NPC, _environmentProvider.LinkCache);
                }
                else // destinationType = SubgroupCombination.DestinationSpecifier.Generic
                {
                    foreach (string destPath in targetPaths)
                    {
                        if (!(_recordPathParser.GetObjectAtPath(npcInfo.NPC, npcInfo.NPC, destPath, new Dictionary<string, dynamic>(), _environmentProvider.LinkCache, true, _logger.GetNPCLogNameString(npcInfo.NPC), out dynamic objAtPath) && objAtPath is not null))
                        {
                            assignReplacer = false;
                            break;
                        }
                    }
                }

                if (assignReplacer)
                {
                    var virtualFlattenedAssetPack = FlattenedAssetPack.CreateVirtualFromReplacerGroup(replacerGroup, _dictionaryMapper, _patcherState);
                    var assignedCombination = _abSelector.GenerateCombinationWithBodyShape(new HashSet<FlattenedAssetPack>() { virtualFlattenedAssetPack }, null, null, null, npcInfo, true, AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual, currentAssignments, new());

                    if (assignedCombination != null)
                    {
                        assignedCombination.DestinationType = destinationType;
                        assignedCombination.ReplacerDestinationFormKey = destinationFK;
                        combinations.Add(assignedCombination);
                        _assetSelector.RecordAssetConsistencyAndLinkedNPCs(assignedCombination, npcInfo, replacerGroup);
                    }
                }
            }

            return combinations;
        }

        public static SubgroupCombination.DestinationSpecifier SelectRecordType(HashSet<string> targetPaths, out FormKey fkToMatch)
        {
            fkToMatch = new FormKey();
            foreach (var specifier in AssetReplacerHardcodedPaths.ReplacersByPaths)
            {
                if (MiscFunctions.StringHashSetsEqualCaseInvariant(targetPaths, specifier.Paths))
                {
                    if (specifier.DestSpecifier == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
                    {
                        fkToMatch = specifier.DestFormKeySpecifier;
                    }
                    return specifier.DestSpecifier;
                }
            }

            return SubgroupCombination.DestinationSpecifier.Generic;
        }

        public static bool CheckIfReplacerTargetExists(SubgroupCombination.DestinationSpecifier specifier, INpcGetter npc, ILinkCache linkCache)
        {
            switch (specifier)
            {
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_04.dds", linkCache); // none of the vanilla records use this texture so can't check for FormKey
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_06.dds", linkCache); // none of the vanilla records use this texture so can't check for FormKey
                default: return false;
            }
        }

        public static bool CheckIfReplacerTargetExists(FormKey specifierFK, INpcGetter npc)
        {
            return npc.HeadParts.Where(x => x.FormKey == specifierFK).Any();
        }

        public static bool HasSpecialHeadPartTexture(INpcGetter npc, string diffusePath, ILinkCache linkCache)
        {
            foreach (var part in npc.HeadParts)
            {
                if (linkCache.TryResolve<IHeadPartGetter>(part.FormKey, out var headPartGetter) && linkCache.TryResolve<ITextureSetGetter>(headPartGetter.TextureSet.FormKey, out var headPartTextureSetGetter) && headPartTextureSetGetter.Diffuse.DataRelativePath.Equals(diffusePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

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
    /// <summary>
    /// Selects which asset-replacer groups of a chosen asset pack apply to a given NPC. A replacer group is
    /// assigned only if the NPC actually possesses the records/paths the group targets (a specific head part,
    /// a hardcoded special head-part texture, or a generic record path). For each valid group it builds a
    /// virtual flattened asset pack and defers to <see cref="AssetSelector"/> to pick a concrete subgroup
    /// combination. Part of the asset-selection stage of the patcher.
    /// </summary>
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
        /// <summary>
        /// Returns the set of subgroup combinations from the chosen asset pack's replacer groups that are valid
        /// for the NPC. For each group it gathers the destination paths, determines the destination record type,
        /// verifies the NPC has the required target(s), and (if so) assigns a virtual replacer combination,
        /// recording consistency/linked-NPC data via <see cref="AssetSelector"/>.
        /// </summary>
        public HashSet<SubgroupCombination> SelectAssetReplacers(FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, List<BodyGenConfig.BodyGenTemplate> assignedBodyGen, List<BodySlideSetting> assignedBodySlides)
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
                    var assignedCombination = _assetSelector.AssignAssets(npcInfo, AssetSelector.AssetPackAssignmentMode.ReplacerVirtual, new HashSet<FlattenedAssetPack>() { virtualFlattenedAssetPack }, assignedBodyGen, assignedBodySlides, out _);
                    
                    if (assignedCombination != null)
                    {
                        assignedCombination.DestinationType = destinationType;
                        assignedCombination.ReplacerDestinationFormKey = destinationFK;
                        combinations.Add(assignedCombination);
                        _assetSelector.RecordReplacerAssetConsistencyAndLinkedNPCs(assignedCombination, npcInfo, replacerGroup);
                    }
                }
            }

            return combinations;
        }

        /// <summary>
        /// Maps a set of replacer destination paths to a hardcoded <see cref="SubgroupCombination.DestinationSpecifier"/>
        /// (matching against <see cref="AssetReplacerHardcodedPaths.ReplacersByPaths"/>), outputting the target
        /// head-part FormKey when the specifier is a head-part type. Returns Generic if no hardcoded match.
        /// </summary>
        public static SubgroupCombination.DestinationSpecifier SelectRecordType(HashSet<string> targetPaths, out FormKey fkToMatch)
        {
            fkToMatch = new FormKey();
            foreach (var specifier in AssetReplacerHardcodedPaths.ReplacersByPaths)
            {
                if (new HashSet<string>(targetPaths, StringComparer.OrdinalIgnoreCase).SetEquals(specifier.Paths))
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

        /// <summary>
        /// Checks whether the NPC has the special-marker head-part texture implied by the given hardcoded
        /// <paramref name="specifier"/> (used for face gash overlays that have no distinguishing FormKey).
        /// Returns false for unrecognized specifiers.
        /// </summary>
        public static bool CheckIfReplacerTargetExists(SubgroupCombination.DestinationSpecifier specifier, INpcGetter npc, ILinkCache linkCache)
        {
            switch (specifier)
            {
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_04.dds", linkCache); // none of the vanilla records use this texture so can't check for FormKey
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_06.dds", linkCache); // none of the vanilla records use this texture so can't check for FormKey
                default: return false;
            }
        }

        /// <summary>
        /// Returns true if the NPC wears the head part identified by <paramref name="specifierFK"/>.
        /// </summary>
        public static bool CheckIfReplacerTargetExists(FormKey specifierFK, INpcGetter npc)
        {
            return npc.HeadParts.Where(x => x.FormKey == specifierFK).Any();
        }

        /// <summary>
        /// Returns true if any of the NPC's head parts uses a texture set whose diffuse path equals
        /// <paramref name="diffusePath"/> (case-insensitive).
        /// </summary>
        public static bool HasSpecialHeadPartTexture(INpcGetter npc, string diffusePath, ILinkCache linkCache)
        {
            foreach (var part in npc.HeadParts)
            {
                if (linkCache.TryResolve<IHeadPartGetter>(part.FormKey, out var headPartGetter) && linkCache.TryResolve<ITextureSetGetter>(headPartGetter.TextureSet.FormKey, out var headPartTextureSetGetter) && headPartTextureSetGetter.Diffuse.DataRelativePath.Path.Equals(diffusePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

using Noggog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class ConfigDrafter
    {
        private readonly VM_SubgroupPlaceHolder.Factory _subgroupPlaceHolderFactory;

        public ConfigDrafter(VM_SubgroupPlaceHolder.Factory subgroupPlaceHolderFactory)
        {
            _subgroupPlaceHolderFactory = subgroupPlaceHolderFactory;
        }


        public void DraftConfigFromTextures(VM_AssetPack config, List<string> rootFolderPaths, bool rootPathsHavePrefix, HashSet<string> unmatchedFiles)
        {
            var allFiles = new List<string>();
            foreach (var path in rootFolderPaths)
            {
                allFiles.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            }
            unmatchedFiles = new(allFiles.Where(x => x.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))); // remove files from this list as they're matched

            foreach (var type in Enum.GetValues(typeof(TextureType)))
            {
                var textureType = (TextureType)type;
                var searchNames = TypeToFileNames[textureType];
                var matchedFiles = GetMatchingFiles(allFiles, searchNames);

                if (matchedFiles.Any())
                {
                    unmatchedFiles.RemoveWhere(x => matchedFiles.Contains(x));

                    var subGroupLabels = TypeToSubgroupLabels[textureType];
                    var topLevelPlaceHolder = config.Subgroups.Where(x => x.ID == subGroupLabels.Item1).FirstOrDefault();
                    if (topLevelPlaceHolder == null)
                    {
                        topLevelPlaceHolder = _subgroupPlaceHolderFactory(CreateSubgroupModel(subGroupLabels.Item1, subGroupLabels.Item2), null, config, config.Subgroups);
                        config.Subgroups.Add(topLevelPlaceHolder);
                    }

                    CreateSubgroupsFromPaths(matchedFiles, rootFolderPaths, rootPathsHavePrefix, topLevelPlaceHolder, config);

                    CleanRedundantSubgroups(topLevelPlaceHolder);

                    ReplaceTextureNamesRecursive(topLevelPlaceHolder, textureType, config); // custom naming and rules based on texture identity
                }
            }
        }

        public void CreateSubgroupsFromPaths(List<string> paths, List<string> rootFolderPaths, bool rootPathsHavePrefix, VM_SubgroupPlaceHolder topLevelPlaceHolder, VM_AssetPack config)
        {
            var longestPath = GetLongestDirectoryStructure(paths);
            LastParentPlaceHolders.Clear();
            LastParentGroupings.Clear();

            foreach (var path in paths)
            {
                LastParentPlaceHolders.Add(path, topLevelPlaceHolder);
            }

            for (int currentPathStage = 0; currentPathStage < longestPath; currentPathStage++)
            {
                var pathGroups = paths.GroupBy(x => string.Join(Path.DirectorySeparatorChar, x.Split(Path.DirectorySeparatorChar).ToList().GetRange(0, currentPathStage + 1))).ToArray();
                if (pathGroups.Count() == 1)
                {
                    continue;
                }

                foreach (var pathGroup in pathGroups)
                {
                    var parentPlaceHolder = LastParentPlaceHolders[pathGroup.First()];
                    var texturesInGroup = paths.Where(x => x.StartsWith(pathGroup.Key + Path.DirectorySeparatorChar)).ToArray(); // match directory separator as well to avoid erroneously adding textures from "\textures\example" into the group from "textures\exam"

                    if (paths.Contains(pathGroup.Key) && GetMatchingRootFolder(rootFolderPaths, pathGroup.Key, rootPathsHavePrefix, out var rootFolderPath))// this is the file itself
                    {
                        var fileName = Path.GetFileName(pathGroup.Key);

                        // create a final new subgroup to house this file
                        VM_SubgroupPlaceHolder newPlaceHolder = _subgroupPlaceHolderFactory(CreateSubgroupModel("", fileName), parentPlaceHolder, config, parentPlaceHolder.Subgroups);
                        newPlaceHolder.AssociatedModel.ID = newPlaceHolder.ID;
                        parentPlaceHolder.Subgroups.Add(newPlaceHolder);

                        // add file to the subgroup's texture list
                        var destination = "";
                        if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(fileName))
                        {
                            destination = FilePathDestinationMap.FileNameToDestMap[fileName];
                        }

                        newPlaceHolder.AssociatedModel.Paths.Add(new FilePathReplacement()
                        {
                            Source = pathGroup.Key.Replace(rootFolderPath, string.Empty).TrimStart(Path.DirectorySeparatorChar),
                            Destination = destination
                        });
                        paths.Remove(pathGroup.Key);
                    }
                    else if (ShouldCreateNewSubgroup(pathGroup, currentPathStage))
                    {
                        var lastFolder = pathGroup.Key.Split(Path.DirectorySeparatorChar).Last();
                        var subgroupName = CapitalizeWordsPreserveCapitalized(lastFolder);
                        VM_SubgroupPlaceHolder newPlaceHolder = _subgroupPlaceHolderFactory(CreateSubgroupModel("", subgroupName), parentPlaceHolder, config, parentPlaceHolder.Subgroups);
                        newPlaceHolder.AssociatedModel.ID = newPlaceHolder.ID;
                        parentPlaceHolder.Subgroups.Add(newPlaceHolder);

                        foreach (var path in texturesInGroup)
                        {
                            LastParentPlaceHolders[path] = newPlaceHolder;
                        }
                    }

                    foreach (var path in texturesInGroup)
                    {
                        LastParentGroupings[path] = pathGroup;
                    }
                }
            }
        }

        private Dictionary<string, VM_SubgroupPlaceHolder> LastParentPlaceHolders { get; set; } = new();
        private Dictionary<string, IGrouping<string, string>> LastParentGroupings { get; set; } = new();

        private bool GetMatchingRootFolder(List<string> rootFolders, string path, bool trimPrefix, out string match)
        {
            foreach (var candidate in rootFolders)
            {
                if (path.StartsWith(candidate))
                {
                    if (trimPrefix)
                    {
                        var splitPath = candidate.Split(Path.DirectorySeparatorChar).ToList();
                        match = String.Join(Path.DirectorySeparatorChar, splitPath.GetRange(0, splitPath.Count - 2)); // remove "textures\\Prefix" from the root folder
                    }
                    else
                    {
                        match = candidate;
                    }
                    return true;
                }
            }
            match = string.Empty;
            return false;
        }

        private static bool CleanRedundantSubgroups(VM_SubgroupPlaceHolder currentSubgroup)
        {
            for (int i = 0; i < currentSubgroup.Subgroups.Count; i++)
            {
                if (CleanRedundantSubgroups(currentSubgroup.Subgroups[i]))
                {
                    currentSubgroup.Subgroups.RemoveAt(i);
                }
            }

            var parentSubgroup = currentSubgroup.ParentSubgroup;
            if (parentSubgroup != null && parentSubgroup.Subgroups.Count == 1)
            {
                if (currentSubgroup.Subgroups.Any()) // if this is a "bridging" subgroup that contains other subgroups, bring its subgroups up to the parent
                {
                    for (int i = 0; i < currentSubgroup.Subgroups.Count; i++)
                    {
                        var toMove = currentSubgroup.Subgroups[i];
                        toMove.ParentSubgroup = parentSubgroup;
                        toMove.ParentCollection = parentSubgroup.Subgroups;
                        parentSubgroup.Subgroups.Add(toMove);
                        currentSubgroup.Subgroups.RemoveAt(i);
                        i--;

                        if (toMove.AssociatedModel.Paths.Any() && toMove.AssociatedModel.Paths.First().Source.EndsWith(toMove.AssociatedModel.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            toMove.AssociatedModel.Name = currentSubgroup.AssociatedModel.Name;
                            toMove.Name = toMove.AssociatedModel.Name;
                            toMove.AutoGenerateID(false, 0);
                        }
                    }
                }
                else // if this is a lowest-level subgroup, bring its textures up to the parent
                {
                    foreach (var path in currentSubgroup.AssociatedModel.Paths)
                    {
                        parentSubgroup.AssociatedModel.Paths.Add(path);
                    }
                }
                return true;
            }
            return false;
        }

        private int GetLongestDirectoryStructure(List<string> paths)
        {
            int longestPath = 0;
            foreach (string path in paths)
            {
                var split = path.Split(Path.DirectorySeparatorChar);
                if (split.Length > longestPath)
                {
                    longestPath = split.Length;
                }
            }
            return longestPath;
        }

        private bool ShouldCreateNewSubgroup(IGrouping<string, string> pathGroup, int currentPathStage)
        {
            var firstPath = pathGroup.First();
            if (!LastParentGroupings.ContainsKey(firstPath))
            {
                return false;
            }

            return pathGroup.Count() != LastParentGroupings[firstPath].Count();
        }

        private AssetPack.Subgroup CreateSubgroupModel(string id, string name)
        {
            var subgroup = new AssetPack.Subgroup();
            subgroup.ID = id;
            subgroup.Name = name;
            return subgroup;
        }

        private void ReplaceTextureNamesRecursive(VM_SubgroupPlaceHolder subgroup, TextureType type, VM_AssetPack config)
        {
            if (subgroup.AssociatedModel.Paths.Count == 1)
            {
                var path = subgroup.AssociatedModel.Paths.First().Source;
                var folder = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path).ToLower();
                if (folder != null && fileName != null)
                {
                    if (type == TextureType.HeadNormal)
                    {
                        ReplaceHeadNormalName(folder, fileName, subgroup);
                    }
                    ReplaceSubgroupNameByFile(fileName, subgroup, config);
                }
            }

            foreach (var sg in subgroup.Subgroups)
            {
                ReplaceTextureNamesRecursive(sg, type, config);
            }
        }

        private void ReplaceHeadNormalName(string folder, string fileName, VM_SubgroupPlaceHolder subgroup)
        {
            if (folder != null)
            {
                folder = folder.ToLower().Split(Path.DirectorySeparatorChar).Last();

                if (fileName != null && subgroup.AssociatedModel.Name == fileName) // don't rename if subgroup has already been renamed
                {
                    switch (folder)
                    {
                        case "maleold":
                            subgroup.AssociatedModel.Name = "Elder";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Elder");
                            break;
                        case "femaleold":
                            subgroup.AssociatedModel.Name = "Elder";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Elder");
                            break;
                        case "bretonmale":
                            subgroup.AssociatedModel.Name = "Breton";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Breton");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey); // Bretons get Afflicted normals
                            break;
                        case "bretonfemale":
                            subgroup.AssociatedModel.Name = "Breton";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Breton");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey); // Bretons get Afflicted normals
                            break;
                        case "darkelfmale":
                            subgroup.AssociatedModel.Name = "Dark Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Dark Elf");
                            break;
                        case "darkelffemale":
                            subgroup.AssociatedModel.Name = "Dark Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Dark Elf");
                            break;
                        case "male":
                            switch (fileName)
                            {
                                case "malehead_msn.dds":
                                    subgroup.AssociatedModel.Name = "Nord";
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add("Nord");
                                    break;
                                case "maleheadvampire_msn.dds":
                                    subgroup.AssociatedModel.Name = "Vampire";
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add("Humanoid Young Vampire");
                                    break;
                            }
                            break;
                        case "female":
                            switch (fileName)
                            {
                                case "femalehead_msn.dds":
                                    subgroup.AssociatedModel.Name = "Nord";
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add("Nord");
                                    break;
                                case "femaleheadvampire_msn.dds":
                                    subgroup.AssociatedModel.Name = "Vampire";
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add("Humanoid Young Vampire");
                                    break;
                                case "astridhead_msn.dds":
                                    subgroup.AssociatedModel.Name = "Astrid";
                                    subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey);
                                    break;
                            }
                            break;
                        case "highelfmale":
                            subgroup.AssociatedModel.Name = "High Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("High Elf");
                            break;
                        case "highelffemale":
                            subgroup.AssociatedModel.Name = "High Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("High Elf");
                            break;
                        case "orcmale":
                            subgroup.AssociatedModel.Name = "Orc";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Orc");
                            break;
                        case "femaleorc":
                            subgroup.AssociatedModel.Name = "Orc";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Orc");
                            break;
                        case "imperialmale":
                            subgroup.AssociatedModel.Name = "Imperial";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Imperial");
                            break;
                        case "imperialfemale":
                            subgroup.AssociatedModel.Name = "Imperial";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Imperial");
                            break;
                        case "redguardmale":
                            subgroup.AssociatedModel.Name = "Redguard";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Redguard");
                            break;
                        case "redguardfemale":
                            subgroup.AssociatedModel.Name = "Redguard";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Redguard");
                            break;
                        case "woodelfmale":
                            subgroup.AssociatedModel.Name = "Wood Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Wood Elf");
                            break;
                        case "woodelffemale":
                            subgroup.AssociatedModel.Name = "Wood Elf";
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Wood Elf");
                            break; // this is a weird one. Texture Set SkinHeadFemaleWoodElf (03D2AC:Skyrim.esm) points to HighElfFemale\FemaleHead_msn.dds. After completion, subgroup should be checked. If no wood elf normal exists, the allowed races on High Elf should be modified to include wood elves.
                        default: break;
                    }
                }
                subgroup.Name = subgroup.AssociatedModel.Name;
                subgroup.AutoGenerateID(false, 0);
            }
        }

        private void ReplaceSubgroupNameByFile(string fileName, VM_SubgroupPlaceHolder subgroup, VM_AssetPack config)
        {
            foreach (var entry in TextureToSubgroupName)
            {
                if (subgroup.AssociatedModel.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && entry.Value.Contains(fileName, StringComparer.OrdinalIgnoreCase)) // don't rename if subgroup has already been renamed
                {
                    subgroup.AssociatedModel.Name = entry.Key;
                    subgroup.Name = subgroup.AssociatedModel.Name;
                    subgroup.AutoGenerateID(false, 0);
                }
            }

            if (fileName.Equals("maleheaddetail_age40.dds", StringComparison.OrdinalIgnoreCase) || fileName.Equals("femaleheaddetail_age40.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Age40.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("maleheaddetail_age50.dds", StringComparison.OrdinalIgnoreCase) || fileName.Equals("femaleheaddetail_age50.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Age50.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("maleheaddetail_age40rough.dds", StringComparison.OrdinalIgnoreCase) || fileName.Equals("femaleheaddetail_age40rough.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Age40Rough.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("femaleheaddetail_rough.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Rough01.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("maleheaddetail_rough01.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Rough01.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("maleheaddetail_rough02.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Rough02.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
            else if (fileName.Equals("femaleheaddetail_frekles.dds", StringComparison.OrdinalIgnoreCase))
            {
                var group = config.AttributeGroupMenu.Groups.Where(x => x.Label == DefaultAttributeGroups.Freckles.Label).FirstOrDefault();
                if (group != null)
                {
                    AddAttributeGroup(subgroup, group);
                }
            }
        }

        private void AddAttributeGroup(VM_SubgroupPlaceHolder subgroup, VM_AttributeGroup group)
        {
            var attribute = new NPCAttribute();
            var subAttribute = new NPCAttributeGroup();
            subAttribute.Type = NPCAttributeType.Group;
            subAttribute.ForceMode = AttributeForcing.ForceIfAndRestrict;
            subAttribute.SelectedLabels.Add(group.Label);
            attribute.SubAttributes.Add(subAttribute);
            subgroup.AssociatedModel.AllowedAttributes.Add(attribute);
        }

        private static readonly Dictionary<TextureType, (string, string)> TypeToSubgroupLabels = new()
        {
            { TextureType.HeadDiffuse, ("HD", "Head Diffuse")},
            { TextureType.HeadNormal, ("HN", "Head Normals") },
            { TextureType.HeadSubsurface, ("HS", "Head Subsurface") },
            { TextureType.HeadSpecular, ("HSp", "Head Specular") },
            { TextureType.HeadDetail, ("HC", "Head Complexion")},
            { TextureType.BodyDiffuse, ("BD", "Body Diffuse") },
            { TextureType.BodyNormal,("BN", "Body Normals") },
            { TextureType.BodySubsurface,("BS", "Body Subsurface") },
            { TextureType.BodySpecular,("BSp", "Body Specular")  },
            { TextureType.HandsDiffuse, ("HaD", "Hands Diffuse") },
            { TextureType.HandsNormal, ("HaN", "Hands Normals")},
            { TextureType.HandsSubsurface, ("HaS", "Hands Subsurface")},
            { TextureType.HandsSpecular, ("HaSp", "Hands Specular") },
            { TextureType.FeetDiffuse, ("FD", "Feet Diffuse") },
            { TextureType.FeetNormal, ("FN", "Feet Normals") },
            { TextureType.FeetSubsurface, ("FS", "Feet Subsurface") },
            { TextureType.FeetSpecular, ("FSp", "Feet Specular") }
        };

        private static readonly Dictionary<TextureType, HashSet<string>> TypeToFileNames = new()
        {
            { TextureType.HeadDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malehead.dds", "maleheadvampire.dds", "maleheadafflicted.dds", "maleheadsnowelf.dds", "KhajiitMaleHead.dds", "ArgonianMaleHead.dds", "femalehead.dds", "femaleheadvampire.dds", "femaleheadafflicted.dds", "AstridHead.dds", "argonianfemalehead.dds" } },
            { TextureType.HeadNormal, new(StringComparer.OrdinalIgnoreCase) { "malehead_msn.dds", "maleheadvampire_msn.dds", "maleheadorc_msn.dds", "KhajiitMaleHead_msn.dds", "ArgonianMaleHead_msn.dds", "femalehead_msn.dds", "femaleheadvampire_msn.dds", "femaleheadorc_msn.dds", "AstridHead_msn.dds", "argonianfemalehead_msn.dds" } },
            { TextureType.HeadSubsurface, new(StringComparer.OrdinalIgnoreCase) { "malehead_sk.dds", "femalehead_sk.dds", "femaleheadvampire_sk.dds" } },
            { TextureType.HeadSpecular, new(StringComparer.OrdinalIgnoreCase) { "malehead_s.dds", "khajiitmalehead_s.dds", "ArgonianMaleHead_s.dds", "femalehead_s.dds", "femaleheadvampire_s.dds", "AstridHead_s.dds", "argonianfemalehead_s.dds" } },
            { TextureType.HeadDetail, new(StringComparer.OrdinalIgnoreCase) { "blankdetailmap.dds", "maleheaddetail_age40.dds", "maleheaddetail_age40rough.dds", "maleheaddetail_age50.dds", "maleheaddetail_rough01.dds", "maleheaddetail_rough02.dds", "KhajiitOld.dds", "ArgonianMaleHeadOld.dds", "femaleheaddetail_age40.dds", "femaleheaddetail_age50.dds", "femaleheaddetail_rough.dds", "femaleheaddetail_age40rough.dds", "femaleheaddetail_frekles.dds", "ArgonianFemaleHeadOld.dds" } },
            { TextureType.BodyDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malebody_1.dds", "malebodyafflicted.dds", "malebodysnowelf.dds", "bodymale.dds", "argonianmalebody.dds", "femalebody_1.dds", "femalebodyafflicted.dds", "AstridBody.dds", "femalebody.dds", "argonianfemalebody.dds" } },
            { TextureType.BodyNormal, new(StringComparer.OrdinalIgnoreCase) { "maleBody_1_msn.dds", "bodymale_msn.dds", "argonianmalebody_msn.dds", "femaleBody_1_msn.dds", "AstridBody_msn.dds", "femalebody_msn.dds", "argonianfemalebody_msn.dds" } },
            { TextureType.BodySubsurface, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_sk.dds", "femalebody_1_sk.dds" } },
            { TextureType.BodySpecular, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_s.dds", "bodymale_s.dds", "argonianmalebody_s.dds", "femalebody_1_s.dds", "AstridBody_s.dds", "femalebody_s.dds", "argonianfemalebody_s.dds" } },
            { TextureType.HandsDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malehands_1.dds", "malehandsafflicted.dds", "malehandssnowelf.dds", "HandsMale.dds", "ArgonianMaleHands.dds", "femalehands_1.dds", "femalehandsafflicted.dds", "AstridHands.dds", "femalehands.dds",  "argonianfemalehands.dds"} },
            { TextureType.HandsNormal, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_msn.dds", "HandsMale_msn.dds", "ArgonianMaleHands_msn.dds", "femalehands_1_msn.dds", "AstridHands_msn.dds", "femalehands_msn.dds","argonianfemalehands_msn.dds" } },
            { TextureType.HandsSubsurface, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_sk.dds", "femalehands_1_sk.dds" } },
            { TextureType.HandsSpecular, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_s.dds", "handsmale_s.dds", "ArgonianMaleHands_s.dds", "femalehands_1_s.dds", "AstridHands_s.dds", "femalehands_s.dds", "argonianfemalehands_s.dds" } },
            { TextureType.FeetDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet.dds", "femalebody_1_feet.dds" } },
            { TextureType.FeetNormal, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_msn_feet.dds", "femalebody_1_msn_feet.dds" } },
            { TextureType.FeetSubsurface, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet_sk.dds", "femalebody_1_feet_sk.dds" } },
            { TextureType.FeetSpecular, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet_s.dds", "femalebody_1_feet_s.dds" } }
        };

        private static readonly Dictionary<string, HashSet<string>> TextureToSubgroupName = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Default", new(StringComparer.OrdinalIgnoreCase) { "malehead.dds", "femalehead.dds", "malehead_sk.dds", "femalehead_sk.dds", "malehead_s.dds", "femalehead_s.dds", "blankdetailmap.dds", "malebody_1.dds", "femalebody_1.dds", "maleBody_1_msn.dds", "femalebody_msn.dds", "malebody_1_s.dds", "femalebody_1_s.dds", "malehands_1.dds" , "femalehands_1.dds", "malehands_1_msn.dds", "femalehands_1_msn.dds", "malehands_1_sk.dds", "femalehands_1_sk.dds", "malehands_1_s.dds", "femalehands_1_s.dds", "malebody_1_feet.dds", "femalebody_1_feet.dds", "malebody_1_msn_feet.dds", "femalebody_1_msn_feet.dds", "malebody_1_feet_sk.dds", "femalebody_1_feet_sk.dds", "malebody_1_feet_s.dds", "femalebody_1_feet_s.dds" } },
            { "Vampire", new(StringComparer.OrdinalIgnoreCase) { "maleheadvampire.dds", "femaleheadvampire.dds", "maleheadvampire_msn.dds", "femaleheadvampire_sk.dds", "femaleheadvampire_s.dds" } },
            { "Afflicted", new(StringComparer.OrdinalIgnoreCase) { "maleheadafflicted.dds", "femaleheadafflicted.dds", "malebodyafflicted.dds", "femalebodyafflicted.dds", "malehandsafflicted.dds", "femalehandsafflicted.dds" } },
            { "Snow Elf", new(StringComparer.OrdinalIgnoreCase) { "maleheadsnowelf.dds", "malebodysnowelf.dds", "malehandssnowelf.dds" } },
            { "Khajiit", new(StringComparer.OrdinalIgnoreCase) { "KhajiitMaleHead.dds", "bodymale.dds", "femalebody.dds", "HandsMale.dds", "femalehands.dds", "HandsMale_msn.dds", "femalehands_msn.dds", "handsmale_s.dds", "femalehands_s.dds" } },
            { "Khajiit Old", new(StringComparer.OrdinalIgnoreCase) { "KhajiitOld.dds" } },
            { "Argonian", new(StringComparer.OrdinalIgnoreCase) { "ArgonianMaleHead.dds", "argonianfemalehead.dds", "ArgonianMaleHead_s.dds" , "argonianfemalehead_s.dds", "argonianmalebody.dds", "argonianfemalebody.dds", "argonianmalebody_msn.dds", "argonianfemalebody_msn.dds", "argonianmalebody_s.dds", "femalebody_s.dds", "ArgonianMaleHands.dds", "argonianfemalehands.dds", "ArgonianMaleHands_msn.dds", "argonianfemalehands_msn.dds", "ArgonianMaleHands_s.dds", "argonianfemalehands_s.dds" } },
            { "Argonian Old", new(StringComparer.OrdinalIgnoreCase) { "ArgonianMaleHeadOld.dds", "ArgonianFemaleHeadOld.dds" } },
            { "Astrid", new(StringComparer.OrdinalIgnoreCase) { "AstridHead.dds", "AstridHead_msn.dds", "AstridHead_s.dds", "AstridBody.dds", "AstridBody_msn.dds", "AstridBody_s.dds", "AstridHands.dds" } },
            { "Age 40", new(StringComparer.OrdinalIgnoreCase) { "maleheaddetail_age40.dds", "femaleheaddetail_age40.dds" } },
            { "Age 40 Rough", new(StringComparer.OrdinalIgnoreCase) { "maleheaddetail_age40rough.dds", "femaleheaddetail_age40rough.dds" } },
            { "Age 50", new(StringComparer.OrdinalIgnoreCase) { "maleheaddetail_age50.dds", "femaleheaddetail_age50.dds" } },
            { "Rough", new(StringComparer.OrdinalIgnoreCase) { "femaleheaddetail_rough.dds" } },
            { "Rough1", new(StringComparer.OrdinalIgnoreCase) { "maleheaddetail_rough01.dds" } },
            { "Rough2", new(StringComparer.OrdinalIgnoreCase) { "maleheaddetail_rough02.dds" } },
            { "Freckles", new(StringComparer.OrdinalIgnoreCase) { "femaleheaddetail_frekles.dds" } }
        };

        private static List<string> GetMatchingFiles(IEnumerable<string> files, HashSet<string> fileNames)
        {
            List<string> matchingFilePaths = new List<string>();

            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                if (fileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    matchingFilePaths.Add(filePath);
                }
            }

            return matchingFilePaths;
        }

        private static string CapitalizeWordsPreserveCapitalized(string input)
        {
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

            string[] words = input.Split(' ');

            for (int i = 0; i < words.Length; i++)
            {
                string currentWord = words[i];

                if (!string.IsNullOrEmpty(currentWord))
                {
                    currentWord = words[i][0].ToString().ToUpper() + currentWord.Remove(0, 1);
                    words[i] = currentWord;
                }
            }

            return string.Join(" ", words);
        }
    }

    public enum TextureType
    {
        HeadDiffuse,
        HeadNormal,
        HeadSubsurface,
        HeadSpecular,
        HeadDetail,
        BodyDiffuse,
        BodyNormal,
        BodySubsurface,
        BodySpecular,
        HandsDiffuse,
        HandsNormal,
        HandsSubsurface,
        HandsSpecular,
        FeetDiffuse,
        FeetNormal,
        FeetSubsurface,
        FeetSpecular
    }
}
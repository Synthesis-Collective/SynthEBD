using ControlzEx.Standard;
using Microsoft.Build.Logging.StructuredLogger;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Mutagen.Bethesda.Plugins.Binary.Processing.BinaryFileProcessor;
using static SynthEBD.AssetPack;

namespace SynthEBD
{
    public class ConfigDrafter
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        private readonly VM_SubgroupPlaceHolder.Factory _subgroupPlaceHolderFactory;

        public ConfigDrafter(IEnvironmentStateProvider environmentStateProvider, VM_SubgroupPlaceHolder.Factory subgroupPlaceHolderFactory)
        {
            _environmentStateProvider = environmentStateProvider;
            _subgroupPlaceHolderFactory = subgroupPlaceHolderFactory;
        }

        public string SuccessString = "Success";
        private const string DefaultSubgroupName = "Main";

        // returns all .dds file paths within rootFolderPaths
        public List<string> GetDDSFiles(List<string> rootFolderPaths)
        {
            var allFiles = new List<string>();
            foreach (var path in rootFolderPaths)
            {
                var filesInDir = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                allFiles.AddRange(filesInDir.Where(x => x.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)));
            }
            return allFiles;
        }

        public (List<string>, List<string>) CategorizeFiles(List<string> allTexturePaths)
        {
            List<string> categorizedFiles = new();
            foreach (var type in Enum.GetValues(typeof(TextureType)))
            {
                var textureType = (TextureType)type;
                var searchNames = TypeToFileNames[textureType];
                var matchedFiles = GetMatchingFiles(allTexturePaths, searchNames);
                categorizedFiles.AddRange(matchedFiles);
            }
            List<string> unCategorizedFiles = allTexturePaths.Where(x => !categorizedFiles.Contains(x)).ToList();

            return (categorizedFiles, unCategorizedFiles);
        }

        public string DraftConfigFromTextures(VM_AssetPack config, List<string> categorizedTexturePaths, List<string> uncategorizedTexturePaths, List<string> ignoredTexturePaths, List<string> rootFolderPaths, bool rootPathsHavePrefix, bool autoApplyNames, bool autoApplyRules, bool autoApplyLinkage)
        {
            var validCategorizedTexturePaths = categorizedTexturePaths.Where(x => !ignoredTexturePaths.Contains(x)).ToList();
            var validUncategorizedTexturePaths = GetMatchingUnknownFiles(uncategorizedTexturePaths.Where(uPath => !ignoredTexturePaths.Where(iPath => uPath.EndsWith(iPath)).Any())); // EndsWith rather than Contains because uPaths are full paths from drive root while iPaths have the root folder paths pre-trimmed

            // check file path validity if not using mod manager
            if (rootPathsHavePrefix)
            {
                foreach (var texturePath in validCategorizedTexturePaths)
                {
                    if (!CheckRootPathPrefix(texturePath, out string errorStr))
                    {
                        return errorStr;
                    }
                }
            }

            // detect gender
            var fileNames = validCategorizedTexturePaths.Select(x => x.Split(Path.DirectorySeparatorChar).Last()).ToList();
            if (fileNames.Intersect(ExpectedFilesByGender[Gender.Female], StringComparer.OrdinalIgnoreCase).Any())
            {
                config.Gender = Gender.Female;
            }
            else if (fileNames.Intersect(ExpectedFilesByGender[Gender.Male], StringComparer.OrdinalIgnoreCase).Any())
            {
                config.Gender = Gender.Male;
            }

            foreach (var type in Enum.GetValues(typeof(TextureType)))
            {
                List<string> texturePaths = new();
                var textureType = (TextureType)type;
                switch (textureType)
                {
                    case TextureType.UnknownDiffuse: texturePaths = validUncategorizedTexturePaths[TextureType.UnknownDiffuse]; break;
                    case TextureType.UnknownNormal: texturePaths = validUncategorizedTexturePaths[TextureType.UnknownNormal]; break;
                    case TextureType.UnknownSubsurface: texturePaths = validUncategorizedTexturePaths[TextureType.UnknownSubsurface]; break;
                    case TextureType.UnknownSpecular: texturePaths = validUncategorizedTexturePaths[TextureType.UnknownSpecular]; break;
                    case TextureType.UnknownComplexion: texturePaths = validUncategorizedTexturePaths[TextureType.UnknownComplexion]; break;
                    default: texturePaths = GetMatchingFiles(validCategorizedTexturePaths, TypeToFileNames[textureType]); break;
                }
                if (texturePaths.Any())
                {
                    CreateSubgroupsByType(config, textureType, texturePaths, rootFolderPaths, rootPathsHavePrefix, autoApplyNames, autoApplyRules);
                }
            }

            if (autoApplyLinkage)
            {
                LinkSubgroupsByName(config);
            }
            ClearEmptyTopLevels(config);

            return SuccessString;
        }

        public void CreateSubgroupsByType(VM_AssetPack config, TextureType textureType, List<string> texturePaths, List<string> rootFolderPaths, bool rootPathsHavePrefix, bool autoApplyNames, bool autoApplyRules)
        {
            var subGroupLabels = TypeToSubgroupLabels[textureType];
            var topLevelPlaceHolder = config.Subgroups.Where(x => x.ID == subGroupLabels.Item1).FirstOrDefault();
            if (topLevelPlaceHolder == null)
            {
                topLevelPlaceHolder = _subgroupPlaceHolderFactory(CreateSubgroupModel(subGroupLabels.Item1, subGroupLabels.Item2), null, config, config.Subgroups);
                config.Subgroups.Add(topLevelPlaceHolder);
            }

            CreateSubgroupsFromPaths(texturePaths, rootFolderPaths, rootPathsHavePrefix, topLevelPlaceHolder, config);

            CleanRedundantSubgroups(topLevelPlaceHolder);

            if (autoApplyNames)
            {
                ReplaceTextureNamesRecursive(topLevelPlaceHolder, textureType, config); // custom naming and rules based on texture identity
            }

            if (autoApplyRules)
            {
                AddRulesBySubgroupNameRecursive(topLevelPlaceHolder);
            }

            if (textureType == TextureType.HeadNormal && config.Gender == Gender.Female)
            {
                AddNecessaryWoodElfNormals(topLevelPlaceHolder);
            }

            if (textureType == TextureType.BodyDiffuse || textureType == TextureType.BodyNormal || textureType == TextureType.BodySpecular || textureType == TextureType.BodySubsurface)
            {
                ReplicateBodyToFeetAndTail(topLevelPlaceHolder);
            }

            if (textureType == TextureType.EtcDiffuse || textureType == TextureType.EtcNormal || textureType == TextureType.EtcSubsurface || textureType == TextureType.EtcSpecular)
            {
                AddSecondaryEtcTexture(topLevelPlaceHolder, textureType);
            }

            CheckNordNamesRecursive(topLevelPlaceHolder);

            PopNordAndVampireSubgroupsUp(topLevelPlaceHolder);

            SortSubgroupsRecursive(topLevelPlaceHolder);
        }

        public void CreateSubgroupsFromPaths(List<string> paths, List<string> rootFolderPaths, bool rootPathsHavePrefix, VM_SubgroupPlaceHolder topLevelPlaceHolder, VM_AssetPack config)
        {
            // special handling if there's only one matching texture
            if (paths.Count == 1)
            {
                var newPath = new FilePathReplacement() { Source = RemoveRootFolder(paths.First(), rootFolderPaths, rootPathsHavePrefix) };
                if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(Path.GetFileName(paths.First())))
                {
                    newPath.Destination = FilePathDestinationMap.FileNameToDestMap[Path.GetFileName(paths.First())];
                }
                topLevelPlaceHolder.AssociatedModel.Paths.Add(newPath);
                paths.Remove(paths.First());
                return;
            }

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

                    if (paths.Contains(pathGroup.Key))// this is the file itself
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
                            Source = RemoveRootFolder(pathGroup.Key, rootFolderPaths, rootPathsHavePrefix),
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

        public string RemoveRootFolder(string path, List<string> rootFolders, bool trimPrefix)
        {
            if (GetMatchingRootFolder(rootFolders, path, trimPrefix, out string rootFolderPath))
            {
                return path.Replace(rootFolderPath, string.Empty).TrimStart(Path.DirectorySeparatorChar);
            }
            return path;
        }
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
                return true;
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

        private void UpdateSubgroupName(VM_SubgroupPlaceHolder subgroup, string name)
        {
            subgroup.AssociatedModel.Name = name;
            subgroup.Name = name;
            subgroup.AutoGenerateID(false, 0);
            subgroup.AssociatedModel.ID = subgroup.ID;
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

            // minor sculpting
            if (subgroup.AssociatedModel.Name.Contains("female", StringComparison.OrdinalIgnoreCase))
            {
                var candidateName = Regex.Replace(subgroup.AssociatedModel.Name, "female", "", RegexOptions.IgnoreCase);
                if (candidateName.Length > 0)
                {
                    UpdateSubgroupName(subgroup, CapitalizeWordsPreserveCapitalized(candidateName));
                }
            }
            else if (subgroup.AssociatedModel.Name.Contains("male", StringComparison.OrdinalIgnoreCase))
            {
                var candidateName = Regex.Replace(subgroup.AssociatedModel.Name, "male", "", RegexOptions.IgnoreCase);
                if (candidateName.Length > 0)
                {
                    UpdateSubgroupName(subgroup, CapitalizeWordsPreserveCapitalized(candidateName));
                }
            }

            if (subgroup.AssociatedModel.Name.Equals("Male", StringComparison.OrdinalIgnoreCase) || subgroup.AssociatedModel.Name.Equals("Female", StringComparison.OrdinalIgnoreCase)) // these are almost always going to be pointing to the default Nord textures
            {
                UpdateSubgroupName(subgroup, "Nord");
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

                if (fileName != null && (subgroup.AssociatedModel.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) || folder.Contains(subgroup.AssociatedModel.Name, StringComparison.OrdinalIgnoreCase))) // don't rename if subgroup has already been renamed, unless it was moved to a higher level subgroup in the CleanRedundantSubgroups() function
                {
                    switch (folder)
                    {
                        case "maleold":
                            UpdateSubgroupName(subgroup, "Elder");
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Elder");
                            break;
                        case "femaleold":
                            UpdateSubgroupName(subgroup, "Elder");
                            subgroup.AssociatedModel.AllowedRaceGroupings.Add("Elder");
                            break;
                        case "bretonmale":
                            UpdateSubgroupName(subgroup, "Breton");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.BretonRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey); // Bretons get Afflicted normals
                            break;
                        case "bretonfemale":
                            UpdateSubgroupName(subgroup, "Breton");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.BretonRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey); // Bretons get Afflicted normals
                            break;
                        case "darkelfmale":
                            UpdateSubgroupName(subgroup, "Dark Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRaceVampire.FormKey); // From Dawnguard's SkinHeadFemaleOrcVampire
                            break;
                        case "darkelffemale":
                            UpdateSubgroupName(subgroup, "Dark Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRace.FormKey);
                            break;
                        case "male":
                            switch (fileName)
                            {
                                case "malehead_msn.dds": // occasionall used as generic by some config authors, so check race compatibility
                                    if (ParentSubgroupsPermitRace(subgroup, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey))
                                    {
                                        UpdateSubgroupName(subgroup, "Nord");
                                        subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey);
                                    }
                                    break;
                                case "maleheadvampire_msn.dds":
                                    UpdateSubgroupName(subgroup, "Vampire");
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add(DefaultRaceGroupings.HumanoidYoungVampire.Label);
                                    break;
                            }
                            break;
                        case "female":
                            switch (fileName)
                            {
                                case "femalehead_msn.dds":
                                    // occasionall used as generic by some config authors, so check race compatibility
                                    if (ParentSubgroupsPermitRace(subgroup, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey))
                                    {
                                        UpdateSubgroupName(subgroup, "Nord");
                                        subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey);
                                    }
                                    break;
                                case "femaleheadvampire_msn.dds":
                                    UpdateSubgroupName(subgroup, "Vampire");
                                    subgroup.AssociatedModel.AllowedRaceGroupings.Add(DefaultRaceGroupings.HumanoidYoungVampire.Label);
                                    break;
                                case "astridhead_msn.dds":
                                    UpdateSubgroupName(subgroup, "Astrid");
                                    subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey);
                                    break;
                            }
                            break;
                        case "highelfmale":
                            UpdateSubgroupName(subgroup, "High Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey); // Snow Elves get High Elf normals
                            break;
                        case "highelffemale":
                            UpdateSubgroupName(subgroup, "High Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey); // Snow Elves get High Elf normals
                            break;
                        case "orcmale":
                            UpdateSubgroupName(subgroup, "Orc");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRace.FormKey);
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRaceVampire.FormKey); // From Dawnguard's SkinHeadFemaleOrcVampire
                            break;
                        case "femaleorc":
                            UpdateSubgroupName(subgroup, "Orc");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRace.FormKey);
                            break;
                        case "imperialmale":
                            UpdateSubgroupName(subgroup, "Imperial");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ImperialRace.FormKey);
                            break;
                        case "imperialfemale":
                            UpdateSubgroupName(subgroup, "Imperial");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ImperialRace.FormKey);
                            break;
                        case "redguardmale":
                            UpdateSubgroupName(subgroup, "Redguard");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.RedguardRace.FormKey);
                            break;
                        case "redguardfemale":
                            UpdateSubgroupName(subgroup, "Redguard");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.RedguardRace.FormKey);
                            break;
                        case "woodelfmale":
                            UpdateSubgroupName(subgroup, "Wood Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey);
                            break;
                        case "woodelffemale":
                            UpdateSubgroupName(subgroup, "Wood Elf");
                            subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey);
                            break;
                        default: break;
                    }
                }
            }
        }

        private void AddNecessaryWoodElfNormals(VM_SubgroupPlaceHolder topLevelHeadNormals) // this is a weird one. Texture Set SkinHeadFemaleWoodElf (03D2AC:Skyrim.esm) points to HighElfFemale\FemaleHead_msn.dds. If no wood elf normal is provided by the texture mod, the allowed races on High Elf should be modified to include wood elves.
        {
            var allNormals = topLevelHeadNormals.GetChildren();

            var highElfSubgroupNames = RaceFormKeyToRaceString[Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRace.FormKey];
            var highElfNormals = allNormals.Where(subgroup => highElfSubgroupNames.Any(x => subgroup.AssociatedModel.Name.Contains(x, StringComparison.OrdinalIgnoreCase))).ToArray();

            var woodElfSubgroupNames = RaceFormKeyToRaceString[Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey];
            var woodElfNormals = allNormals.Where(subgroup => woodElfSubgroupNames.Any(x => subgroup.AssociatedModel.Name.Contains(x, StringComparison.OrdinalIgnoreCase))).ToArray();

            if (highElfNormals.Any() && !woodElfNormals.Any())
            {
                foreach (var subgroup in highElfNormals)
                {
                    subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey);
                }
            }
        }

        private void ReplaceSubgroupNameByFile(string fileName, VM_SubgroupPlaceHolder subgroup, VM_AssetPack config)
        {
            foreach (var entry in TextureToSubgroupName)
            {
                if (subgroup.AssociatedModel.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && entry.Value.Contains(fileName, StringComparer.OrdinalIgnoreCase)) // don't rename if subgroup has already been renamed
                {
                    UpdateSubgroupName(subgroup, entry.Key);
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

        private void AddRulesBySubgroupNameRecursive(VM_SubgroupPlaceHolder subgroup)
        {
            AddRulesBySubgroupName(subgroup);
            foreach (var sg in subgroup.Subgroups)
            {
                AddRulesBySubgroupNameRecursive(sg);
            }
        }

        private void AddRulesBySubgroupName(VM_SubgroupPlaceHolder subgroup)
        {
            var raceFormKey = GetRaceFormKeyFromName(subgroup.AssociatedModel.Name);
            if (raceFormKey != null && !subgroup.AssociatedModel.AllowedRaces.Contains(raceFormKey.Value) && ParentSubgroupsPermitRace(subgroup, raceFormKey.Value))
            {
                subgroup.AssociatedModel.AllowedRaces.Add(raceFormKey.Value);
            }

            if (subgroup.AssociatedModel.Name.Contains("Vampire", StringComparison.OrdinalIgnoreCase)) // complex handling
            {
                var parentSubgroups = new List<VM_SubgroupPlaceHolder>();
                GetParentSubgroups(subgroup, parentSubgroups);
                bool parentRaceSpecified = false;
                foreach (var parent in parentSubgroups)
                {
                    var parentRaceFormKey = GetRaceFormKeyFromName(parent.AssociatedModel.Name); // do any of the parent subgroup names imply a specific race for this branch of the subgroup tree?
                    if (parentRaceFormKey != null && CorrespondingVampireRaces.ContainsKey(parentRaceFormKey.Value))
                    {
                        parentRaceSpecified = true;
                        var vampireCounterpart = CorrespondingVampireRaces[parentRaceFormKey.Value]; // if yes, get the vampire analogue of that race and add it to both this subgroup and the root node
                        subgroup.AssociatedModel.AllowedRaces.Add(vampireCounterpart);
                        if (!parent.AssociatedModel.AllowedRaces.Contains(vampireCounterpart))
                        {
                            parent.AssociatedModel.AllowedRaces.Add(vampireCounterpart);
                        }

                        var otherSubgroups = new List<VM_SubgroupPlaceHolder>();
                        GetOtherBottomSubgroups(parent, subgroup, otherSubgroups);
                        foreach (var sg in otherSubgroups) // within this branch of the subgroup tree, prevent other non-vampire subgroups from inheriting the root allowed vampire race by adding it to the exclusion race
                        {
                            if (!sg.AssociatedModel.Name.Contains("Vampire", StringComparison.OrdinalIgnoreCase) && !sg.AssociatedModel.DisallowedRaces.Contains(vampireCounterpart))
                            {
                                sg.AssociatedModel.DisallowedRaces.Add(vampireCounterpart);
                            }
                        }
                    }
                }
                if (!parentRaceSpecified) // if none of the subgroups above the current subgroup imply a specific race, let this Vampire subgroup go to any humanoid vampires. 
                {
                    subgroup.AssociatedModel.AllowedRaceGroupings.Add(DefaultRaceGroupings.HumanoidYoungVampire.Label);
                }
            }

            if (subgroup.AssociatedModel.Name == DefaultSubgroupName)
            {
                subgroup.AssociatedModel.AllowedRaceGroupings.Add(DefaultRaceGroupings.HumanoidPlayableNonVampire.Label);
            }

            // special handling for Elder NPCs (technically not applying rules by name, but not worth adding a whole separate function just for this)
            if (subgroup.AssociatedModel.Paths.Where(x => x.Source.Contains("maleold", StringComparison.OrdinalIgnoreCase)).Any()) // search term covers femaleold as well
            {
                if (!subgroup.AssociatedModel.AllowedRaces.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey))
                {
                    subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey);
                }
                if (!subgroup.AssociatedModel.AllowedRaces.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey))
                {
                    subgroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRaceVampire.FormKey);
                }
            }
        }

        private void GetParentSubgroups(VM_SubgroupPlaceHolder subgroup, List<VM_SubgroupPlaceHolder> parents)
        {
            if (subgroup.ParentSubgroup is not null)
            {
                parents.Add(subgroup.ParentSubgroup);
                GetParentSubgroups(subgroup.ParentSubgroup, parents);
            }
        }

        private void GetOtherBottomSubgroups(VM_SubgroupPlaceHolder root, VM_SubgroupPlaceHolder subgroupToExclude, List<VM_SubgroupPlaceHolder> otherBottomLevelSubgroups)
        {
            foreach (var subgroup in root.Subgroups)
            {
                GetOtherBottomSubgroups(subgroup, subgroupToExclude, otherBottomLevelSubgroups);
            }

            if (root.AssociatedModel.Paths.Any() && root != subgroupToExclude)
            {
                otherBottomLevelSubgroups.Add(root);
            }
        }

        private FormKey? GetRaceFormKeyFromName(string subgroupName)
        {
            foreach (var entry in RaceFormKeyToRaceString)
            {
                foreach (var name in entry.Value)
                {
                    if (subgroupName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Key;
                    }
                }
            }
            return null;
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
            { TextureType.FeetSpecular, ("FSp", "Feet Specular") },
            { TextureType.EtcDiffuse, ("ED", "Etc Diffuse") },
            { TextureType.EtcNormal, ("EN", "Etc Normals") },
            { TextureType.EtcSubsurface, ("ES", "Etc Subsurface") },
            { TextureType.EtcSpecular, ("ESp", "Etc Specular") },
            { TextureType.UnknownDiffuse, ("UD", "Unknown Diffuse") },
            { TextureType.UnknownNormal, ("UN", "Unknown Normals") },
            { TextureType.UnknownSubsurface, ("US", "Unknown Subsurface") },
            { TextureType.UnknownSpecular, ("USp", "Unknown Specular") },
            { TextureType.UnknownComplexion, ("UC", "Unknown Complexion") }
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
            { TextureType.FeetSpecular, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet_s.dds", "femalebody_1_feet_s.dds" } },
            { TextureType.EtcDiffuse, new(StringComparer.OrdinalIgnoreCase) { "femalebody_etc_v2_1.dds" } },
            { TextureType.EtcNormal, new(StringComparer.OrdinalIgnoreCase) { "femalebody_etc_v2_1_msn.dds" } },
            { TextureType.EtcSubsurface, new(StringComparer.OrdinalIgnoreCase) { "femalebody_etc_v2_1_sk.dds" } },
            { TextureType.EtcSpecular, new(StringComparer.OrdinalIgnoreCase) { "femalebody_etc_v2_1_s.dds" } },
            { TextureType.UnknownDiffuse, new() },
            { TextureType.UnknownNormal, new() },
            { TextureType.UnknownSubsurface, new() },
            { TextureType.UnknownSpecular, new() },
            { TextureType.UnknownComplexion, new() }
        };

        private static readonly Dictionary<string, HashSet<string>> TextureToSubgroupName = new(StringComparer.OrdinalIgnoreCase)
        {
            { DefaultSubgroupName, new(StringComparer.OrdinalIgnoreCase) { "malehead.dds", "femalehead.dds", "malehead_sk.dds", "femalehead_sk.dds", "malehead_s.dds", "femalehead_s.dds", "blankdetailmap.dds", "malebody_1.dds", "femalebody_1.dds", "maleBody_1_msn.dds", "femalebody_msn.dds", "malebody_1_s.dds", "femalebody_1_s.dds", "malehands_1.dds" , "femalehands_1.dds", "malehands_1_msn.dds", "femalehands_1_msn.dds", "malehands_1_sk.dds", "femalehands_1_sk.dds", "malehands_1_s.dds", "femalehands_1_s.dds", "malebody_1_feet.dds", "femalebody_1_feet.dds", "malebody_1_msn_feet.dds", "femalebody_1_msn_feet.dds", "malebody_1_feet_sk.dds", "femalebody_1_feet_sk.dds", "malebody_1_feet_s.dds", "femalebody_1_feet_s.dds" } },
            { "Vampire", new(StringComparer.OrdinalIgnoreCase) { "maleheadvampire.dds", "femaleheadvampire.dds", "maleheadvampire_msn.dds", "femaleheadvampire_sk.dds", "femaleheadvampire_s.dds" } },
            { "Afflicted", new(StringComparer.OrdinalIgnoreCase) { "maleheadafflicted.dds", "femaleheadafflicted.dds", "malebodyafflicted.dds", "femalebodyafflicted.dds", "malehandsafflicted.dds", "femalehandsafflicted.dds", "malebodyafflicted_feet.dds", "femalebodyafflicted_feet.dds" } },
            { "Snow Elf", new(StringComparer.OrdinalIgnoreCase) { "maleheadsnowelf.dds", "malebodysnowelf.dds", "malehandssnowelf.dds", "malebodysnowelf_feet.dds", "femalebodysnowelf_feet.dds" } },
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

        private static readonly Dictionary<FormKey, HashSet<string>> RaceFormKeyToRaceString = new()
        {
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Argonian" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.BretonRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Breton" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Dark Elf", "DarkElf", "Dunmer" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "High Elf", "HighElf", "Altmer" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ImperialRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Imperial" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Khajiit" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Nord", "Male", "Female" } }, // most "Male" or "Female" subgroups will have inherited their name from the parent folder which points to the default textures which are supposed to be for nords (e.g. malehead_msn.dds)
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Orc", "Orsimer" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.RedguardRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Redguard" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Wood Elf", "WoodElf", "Bosmer" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Afflicted" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Snow Elf", "SnowElf" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Astrid" } },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey, new(StringComparer.OrdinalIgnoreCase) { "Old" } }
        };

        public void ChooseLeastSpecificPath(IEnumerable<VM_SimpleSelectableCollectionMemberString> candidates) // try to select the most generic directory path
        {
            var acceptablePaths = new List<VM_SimpleSelectableCollectionMemberString>();
            foreach (var candidate in candidates)
            {
                bool unMatched = true;
                var splitPath = candidate.Content.Split(Path.DirectorySeparatorChar).ToList();
                var canidateDir = string.Join(Path.DirectorySeparatorChar, splitPath.GetRange(0, splitPath.Count - 1)); // ignore file names
              
                foreach (var entry in RaceFormKeyToRaceString)
                {
                    var matchStrings = new HashSet<string>(entry.Value);
                    if (entry.Key == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey)
                    {
                        matchStrings = new() { "Nord" };
                    }
                    else if (entry.Key == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey)
                    {
                        continue;
                    }

                    if(!matchStrings.Where(x => canidateDir.Contains(x, StringComparison.OrdinalIgnoreCase)).Any())
                    {
                        unMatched = false;
                        break;
                    }    
                }

                if (unMatched)
                {
                    acceptablePaths.Add(candidate);
                }
            }

            if (acceptablePaths.Any())
            {
                acceptablePaths.OrderBy(x => x.Content.Length).First().IsSelected = false;
            }
            else if (candidates.Any())
            {
                candidates.OrderBy(x => x.Content.Length).First().IsSelected = false;
            }
        }

        private static readonly Dictionary<FormKey, FormKey> CorrespondingVampireRaces = new()
        {
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.BretonRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.BretonRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DarkElfRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.HighElfRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ImperialRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ImperialRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.OrcRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.RedguardRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.RedguardRaceVampire.FormKey },
            { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.WoodElfRaceVampire.FormKey }
        };

        private static readonly Dictionary<Gender, HashSet<string>> ExpectedFilesByGender = new()
        {
            {Gender.Male, new(StringComparer.OrdinalIgnoreCase) { "malehead.dds", "malehead_sk.dds", "malehead_s.dds", "malebody_1.dds", "maleBody_1_msn.dds", "malebody_1_s.dds", "malehands_1.dds", "malehands_1_msn.dds", "malehands_1_sk.dds", "malehands_1_s.dds", "malebody_1_feet.dds", "malebody_1_msn_feet.dds", "malebody_1_feet_sk.dds", "malebody_1_feet_s.dds" } },
            {Gender.Female, new(StringComparer.OrdinalIgnoreCase) { "femalehead.dds", "femalehead_sk.dds", "femalehead_s.dds", "femalebody_1.dds", "femalebody_msn.dds", "femalebody_1_s.dds", "femalehands_1.dds", "femalehands_1_msn.dds", "femalehands_1_sk.dds", "femalehands_1_s.dds", "femalebody_1_feet.dds", "femalebody_1_msn_feet.dds", "femalebody_1_feet_sk.dds", "femalebody_1_feet_s.dds", "femalebody_etc_v2_1.dds", "femalebody_etc_v2_1_msn.dds", "femalebody_etc_v2_1_s.dds", "femalebody_etc_v2_1_sk.dds" } }
        };

        private static List<string> GetMatchingFiles(IEnumerable<string> files, HashSet<string> fileNamesToMatch)
        {
            List<string> matchingFilePaths = new List<string>();

            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                if (fileNamesToMatch.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    matchingFilePaths.Add(filePath);
                }
            }

            return matchingFilePaths;
        }

        private static Dictionary<TextureType, List<string>> GetMatchingUnknownFiles(IEnumerable<string> unknownFiles) // expects a list of files which have already been pre-sorted as uncategorized
        {
            Dictionary<TextureType, List<string>> unknownsCategorized = new()
            {
                { TextureType.UnknownDiffuse, new() },
                { TextureType.UnknownNormal, new() },
                { TextureType.UnknownSpecular, new() },
                { TextureType.UnknownSubsurface, new() },
                { TextureType.UnknownComplexion, new() }
            };

            foreach (var file in unknownFiles)
            {
                if (file.EndsWith("_msn.dds", StringComparison.OrdinalIgnoreCase))
                {
                    unknownsCategorized[TextureType.UnknownNormal].Add(file);
                }
                else if (file.EndsWith("_sk.dds", StringComparison.OrdinalIgnoreCase))
                {
                    unknownsCategorized[TextureType.UnknownSubsurface].Add(file);
                }
                else if (file.EndsWith("_s.dds", StringComparison.OrdinalIgnoreCase))
                {
                    unknownsCategorized[TextureType.UnknownSpecular].Add(file);
                }
                else if (file.Contains("HeadDetail", StringComparison.OrdinalIgnoreCase))
                {
                    unknownsCategorized[TextureType.UnknownComplexion].Add(file);
                }
                else
                {
                    unknownsCategorized[TextureType.UnknownDiffuse].Add(file);
                }
            }
            return unknownsCategorized;
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

        private bool GetPrefix(VM_AssetPack config, string[] filesInDir, string rootPath)
        {
            var firstSubPath = filesInDir.Select(x => x.Replace(rootPath, "").TrimStart(Path.DirectorySeparatorChar)).Where(x => x.StartsWith("textures", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (firstSubPath != null)
            {
                var pathSplit = firstSubPath.Split(Path.DirectorySeparatorChar);
                if (pathSplit.Length >= 2 && pathSplit[0].Equals("textures", StringComparison.OrdinalIgnoreCase))
                {
                    config.ShortName = pathSplit[1];
                    return true;
                }
            }
            return false;
        }

        private void LinkSubgroupsByName(VM_AssetPack config) // must be called after all IDs have been finalized.
        {
            foreach (var topLevelSubgroup in config.Subgroups)
            {
                var currentSubgroups = topLevelSubgroup.GetChildren();

                var otherSubgroups = new List<VM_SubgroupPlaceHolder>();
                foreach (var otherTopLevel in config.Subgroups.Where(x => x != topLevelSubgroup))
                {
                    otherTopLevel.GetChildren(otherSubgroups);
                }

                foreach (var subgroup in currentSubgroups)
                {
                    foreach (var candidate in otherSubgroups)
                    {
                        if (subgroup.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase) && !IgnoreForSubgroupLinkage(candidate.Name))
                        {
                            subgroup.AssociatedModel.RequiredSubgroups.Add(candidate.ID);
                        }
                    }
                }
            }
        }

        private bool IgnoreForSubgroupLinkage(string name)
        {
            foreach (var racialSubgroupIDs in RaceFormKeyToRaceString.Values) // these should already be restricted via Allowed Races, and trying to add required subgroups can inadvertently lead to promiscuous linkage with other nested subgroups of the same name.
            {
                if (racialSubgroupIDs.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (name.Equals(DefaultSubgroupName) || name.Equals("Vampire", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private void ReplicateBodyToFeetAndTail(VM_SubgroupPlaceHolder subgroup)
        {
            foreach (var sg in subgroup.Subgroups)
            {
                ReplicateBodyToFeetAndTail(sg);
            }

            if (subgroup.AssociatedModel.Paths.Count == 1)
            {
                var firstPath = subgroup.AssociatedModel.Paths.First();

                bool isBeastTexture = firstPath.Source.Contains("Argonian", StringComparison.OrdinalIgnoreCase) ||
                    firstPath.Source.Contains("Khajiit", StringComparison.OrdinalIgnoreCase) ||
                    TextureToSubgroupName["Argonian"].Contains(Path.GetFileName(firstPath.Source), StringComparer.OrdinalIgnoreCase) ||
                    TextureToSubgroupName["Argonian Old"].Contains(Path.GetFileName(firstPath.Source), StringComparer.OrdinalIgnoreCase) ||
                    TextureToSubgroupName["Khajiit"].Contains(Path.GetFileName(firstPath.Source), StringComparer.OrdinalIgnoreCase) ||
                    TextureToSubgroupName["Khajiit Old"].Contains(Path.GetFileName(firstPath.Source), StringComparer.OrdinalIgnoreCase);

                var feetPath = new FilePathReplacement() { Source = firstPath.Source };
                bool feetMatched = true;

                switch (firstPath.Destination)
                {
                    case FilePathDestinationMap.Dest_TorsoFemaleDiffuse: feetPath.Destination = FilePathDestinationMap.Dest_FeetFemaleDiffuse; break;
                    case FilePathDestinationMap.Dest_TorsoFemaleNormal: feetPath.Destination = FilePathDestinationMap.Dest_FeetFemaleNormal; break;
                    case FilePathDestinationMap.Dest_TorsoFemaleSubsurface: feetPath.Destination = FilePathDestinationMap.Dest_FeetFemaleSubsurface; break;
                    case FilePathDestinationMap.Dest_TorsoFemaleSpecular: feetPath.Destination = FilePathDestinationMap.Dest_FeetFemaleSpecular; break;
                    case FilePathDestinationMap.Dest_TorsoMaleDiffuse: feetPath.Destination = FilePathDestinationMap.Dest_FeetMaleDiffuse; break;
                    case FilePathDestinationMap.Dest_TorsoMaleNormal: feetPath.Destination = FilePathDestinationMap.Dest_FeetMaleNormal; break;
                    case FilePathDestinationMap.Dest_TorsoMaleSubsurface: feetPath.Destination = FilePathDestinationMap.Dest_FeetMaleSubsurface; break;
                    case FilePathDestinationMap.Dest_TorsoMaleSpecular: feetPath.Destination = FilePathDestinationMap.Dest_FeetMaleSpecular; break;
                    default: feetMatched = false; break;
                }

                if (feetMatched)
                {
                    subgroup.AssociatedModel.Paths.Add(feetPath);
                }

                if (isBeastTexture)
                {
                    var tailPath = new FilePathReplacement() { Source = firstPath.Source };
                    bool tailMatched = true;

                    switch (firstPath.Destination)
                    {
                        case FilePathDestinationMap.Dest_TorsoFemaleDiffuse: tailPath.Destination = FilePathDestinationMap.Dest_TailFemaleDiffuse; break;
                        case FilePathDestinationMap.Dest_TorsoFemaleNormal: tailPath.Destination = FilePathDestinationMap.Dest_TailFemaleNormal; break;
                        case FilePathDestinationMap.Dest_TorsoFemaleSubsurface: tailPath.Destination = FilePathDestinationMap.Dest_TailFemaleSubsurface; break;
                        case FilePathDestinationMap.Dest_TorsoFemaleSpecular: tailPath.Destination = FilePathDestinationMap.Dest_TailFemaleSpecular; break;
                        case FilePathDestinationMap.Dest_TorsoMaleDiffuse: tailPath.Destination = FilePathDestinationMap.Dest_TailMaleDiffuse; break;
                        case FilePathDestinationMap.Dest_TorsoMaleNormal: tailPath.Destination = FilePathDestinationMap.Dest_TailMaleNormal; break;
                        case FilePathDestinationMap.Dest_TorsoMaleSubsurface: tailPath.Destination = FilePathDestinationMap.Dest_TailMaleSubsurface; break;
                        case FilePathDestinationMap.Dest_TorsoMaleSpecular: tailPath.Destination = FilePathDestinationMap.Dest_TailMaleSpecular; break;
                        default: tailMatched = false; break;
                    }

                    if (tailMatched)
                    {
                        subgroup.AssociatedModel.Paths.Add(tailPath);
                    }
                }
            }
        }

        private void ClearEmptyTopLevels(VM_AssetPack config)
        {
            for (int i = 0; i < config.Subgroups.Count; i++)
            {
                var currentTopLevel = config.Subgroups[i];
                var allSubgroups = currentTopLevel.GetChildren().And(currentTopLevel);
                if (!allSubgroups.Where(x => x.AssociatedModel.Paths.Any()).Any())
                {
                    config.Subgroups.RemoveAt(i);
                    i--;
                }
            }
        }

        public bool CheckRootPathPrefix(string path, out string errorStr)
        {
            var texturesDir = Path.Combine(_environmentStateProvider.DataFolderPath, "Textures");
            if (!path.Contains(texturesDir, StringComparison.OrdinalIgnoreCase))
            {
                errorStr = "Expected the following texture path to start with the Game Data Folder\\Textures Path: " + path;
                return false;
            }

            var fileName = Path.GetFileName(path);
            if (path.Replace(texturesDir, string.Empty, StringComparison.OrdinalIgnoreCase).Replace(fileName, String.Empty, StringComparison.OrdinalIgnoreCase).Trim(Path.DirectorySeparatorChar).Length == 0)
            {
                errorStr = "Expected the following texture path to exist in a subfolder of the Game Data Folder\\Textures Path: " + path;
                return false;
            }

            errorStr = string.Empty;
            return true;
        }

        public void AddSecondaryEtcTexture(VM_SubgroupPlaceHolder topLevel, TextureType type)
        {
            var withFiles = topLevel.GetChildren().And(topLevel).Where(x => x.AssociatedModel.Paths.Any()).ToList();
            foreach (var subgroup in withFiles)
            {
                var firstPath = subgroup.AssociatedModel.Paths.First();
                var secondPath = new FilePathReplacement() { Source = firstPath.Source };
                switch(type)
                {
                    case TextureType.EtcDiffuse: secondPath.Destination = FilePathDestinationMap.Dest_EtcFemaleDiffuseSecondary; break;
                    case TextureType.EtcNormal: secondPath.Destination = FilePathDestinationMap.Dest_EtcFemaleNormalSecondary; break;
                    case TextureType.EtcSubsurface: secondPath.Destination = FilePathDestinationMap.Dest_EtcFemaleSubsurfaceSecondary; break;
                    case TextureType.EtcSpecular: secondPath.Destination = FilePathDestinationMap.Dest_EtcFemaleSpecularSecondary; break;
                }
                subgroup.AssociatedModel.Paths.Add(secondPath);
            }
        }

        public void SortSubgroupsRecursive(VM_SubgroupPlaceHolder subgroup)
        {
            subgroup.Subgroups.Sort(x => x.AssociatedModel.Name, false);
            foreach (var sg in subgroup.Subgroups)
            {
                SortSubgroupsRecursive(sg);
            }
        }

        public void CheckNordNamesRecursive(VM_SubgroupPlaceHolder subgroup) // "male" and "female" is a confusing path because in some cases it's supposed to apply specifically to Nords, while in other cases it's for all humanoid races. Thanks Bethesda. This function checks if a subgroup has neighbors with other races, and if not reverts the name back to DefaultSubgroupName
        {
            if(subgroup.AssociatedModel.Name == "Nord" && subgroup.ParentSubgroup != null)
            {
                bool hasOtherRacialSubgroups = false;
                foreach (var sg in subgroup.ParentSubgroup.Subgroups.Where(x => !x.Equals(subgroup)).ToArray())
                {
                    // does subgroup have a racial name? Ignore "Elder" and "Vampire" in this consideration
                    foreach (var entry in RaceFormKeyToRaceString)
                    {
                        if (entry.Key.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ElderRace.FormKey) || entry.Key.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey))
                        {
                            continue;
                        }
                        if (entry.Value.Where(x => sg.AssociatedModel.Name.Contains(x, StringComparison.OrdinalIgnoreCase)).Any())
                        {
                            hasOtherRacialSubgroups = true;
                            break;
                        }
                    }
                }

                if (!hasOtherRacialSubgroups && ParentSubgroupsPermitRace(subgroup, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey))
                {
                    UpdateSubgroupName(subgroup, subgroup.AssociatedModel.Name.Replace("Nord", DefaultSubgroupName, StringComparison.OrdinalIgnoreCase));
                    if (subgroup.AssociatedModel.AllowedRaces.Contains(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey))
                    {
                        subgroup.AssociatedModel.AllowedRaces.RemoveWhere(x => x.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey));
                    }
                }
            }

            foreach (var sg in subgroup.Subgroups)
            {
                CheckNordNamesRecursive(sg);
            }
        }

        private bool ParentSubgroupsPermitRace(VM_SubgroupPlaceHolder subgroup, FormKey raceFormKey)
        {
            var parents = subgroup.GetParents();

            foreach (var parent in parents)
            {
                if (parent.AssociatedModel.AllowedRaces.Any() && !parent.AssociatedModel.AllowedRaces.Contains(raceFormKey))
                {
                    return false;
                }
                if (parent.AssociatedModel.DisallowedRaces.Contains(raceFormKey))
                {
                    return false;
                }

                if (parent.AssociatedModel.AllowedRaceGroupings.Any())
                {
                    bool raceMatchedByAnyGrouping = false;
                    foreach (var arg in parent.AssociatedModel.AllowedRaceGroupings)
                    {
                        var correspondingGroup = subgroup.ParentAssetPack.RaceGroupingEditor.RaceGroupings.Where(x => x.Label == arg).FirstOrDefault();
                        if (correspondingGroup != null && correspondingGroup.Races.Contains(raceFormKey))
                        {
                            raceMatchedByAnyGrouping = true;
                            break;
                        }
                    }
                    if (!raceMatchedByAnyGrouping)
                    {
                        return false;
                    }
                }

                if (parent.AssociatedModel.DisallowedRaceGroupings.Any())
                {
                    foreach (var drg in parent.AssociatedModel.DisallowedRaceGroupings)
                    {
                        var correspondingGroup = subgroup.ParentAssetPack.RaceGroupingEditor.RaceGroupings.Where(x => x.Label == drg).FirstOrDefault();
                        if (correspondingGroup != null && correspondingGroup.Races.Contains(raceFormKey))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public bool PopNordAndVampireSubgroupsUp(VM_SubgroupPlaceHolder subgroup) // A frequent pattern of the auto-naming algorithm is creating "Nord" subgroups containing a DefaultSubgroupName subgroup for actual nords and a Vampire subgroup for vampires. This function flattens them into their parent
        {
            bool currentSubgroupRemoved = false;
            if (subgroup.AssociatedModel.Name == "Nord" &&
                subgroup.ParentSubgroup != null && 
                subgroup.Subgroups.Count == 2 && 
                subgroup.Subgroups.Where(x => x.AssociatedModel.Name == DefaultSubgroupName).Any() && 
                subgroup.Subgroups.Where(x => x.AssociatedModel.Name == "Vampire").Any() &&
                ParentSubgroupsPermitRace(subgroup, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey) &&
                ParentSubgroupsPermitRace(subgroup, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceVampire.FormKey)) // probably don't need to check the other vampire races
            {
                var nordGroup = subgroup.Subgroups.Where(x => x.AssociatedModel.Name == DefaultSubgroupName).FirstOrDefault();
                var vampireGroup = subgroup.Subgroups.Where(x => x.AssociatedModel.Name == "Vampire").FirstOrDefault();
                
                nordGroup.AssociatedModel.DisallowedRaces.Clear();
                nordGroup.AssociatedModel.AllowedRaces.Add(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey);

                vampireGroup.AssociatedModel.AllowedRaces = new();
                vampireGroup.AssociatedModel.AllowedRaceGroupings.Clear();
                vampireGroup.AssociatedModel.AllowedRaceGroupings.Add(DefaultRaceGroupings.HumanoidYoungVampire.Label);


                if (subgroup.ParentSubgroup.ParentSubgroup != null)
                {
                    nordGroup.ParentSubgroup = subgroup.ParentSubgroup.ParentSubgroup;
                }
                subgroup.ParentSubgroup.Subgroups.Add(nordGroup);
                subgroup.Subgroups.Remove(nordGroup);
                UpdateSubgroupName(nordGroup, "Nord");  // updates ID as well as name

                if (subgroup.ParentSubgroup.ParentSubgroup != null)
                {
                    vampireGroup.ParentSubgroup = subgroup.ParentSubgroup.ParentSubgroup;
                }
                subgroup.ParentSubgroup.Subgroups.Add(vampireGroup);
                subgroup.Subgroups.Remove(vampireGroup);
                UpdateSubgroupName(vampireGroup, "Vampire"); // updates ID as well as name

                if (!subgroup.Subgroups.Any() && !subgroup.AssociatedModel.Paths.Any())
                {
                    subgroup.ParentSubgroup.Subgroups.Remove(subgroup);
                    currentSubgroupRemoved = true;
                }
            }

            for (int i = 0; i < subgroup.Subgroups.Count; i++)
            {
                if (PopNordAndVampireSubgroupsUp(subgroup.Subgroups[i]))
                {
                    i--;
                }
            }
            return currentSubgroupRemoved;
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
        FeetSpecular,
        EtcDiffuse,
        EtcNormal,
        EtcSubsurface,
        EtcSpecular,
        UnknownDiffuse,
        UnknownNormal,
        UnknownSubsurface,
        UnknownSpecular,
        UnknownComplexion
    }
}
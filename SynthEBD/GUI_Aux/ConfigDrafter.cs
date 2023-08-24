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


        public void DraftConfigFromTextures(VM_AssetPack config, string rootFolderPath)
        {
            foreach (var type in Enum.GetValues(typeof(TextureType)))
            {
                var textureType = (TextureType)type;
                var searchNames = TypeToFileNames[textureType];
                var matchedFiles = GetMatchingFiles(rootFolderPath, searchNames);

                if (matchedFiles.Any())
                {
                    var subGroupLabels = TypeToSubgroupLabels[textureType];
                    var topLevelPlaceHolder = config.Subgroups.Where(x => x.ID == subGroupLabels.Item1).FirstOrDefault();
                    if (topLevelPlaceHolder == null)
                    {
                        topLevelPlaceHolder = _subgroupPlaceHolderFactory(CreateSubgroupModel(subGroupLabels.Item1, subGroupLabels.Item2), null, config, config.Subgroups);
                        config.Subgroups.Add(topLevelPlaceHolder);
                    }

                    CreateSubgroupsFromPaths(matchedFiles, rootFolderPath, topLevelPlaceHolder, config);
                    CleanRedundantSubgroups(topLevelPlaceHolder);
                }
            }

        }

        public void CreateSubgroupsFromPaths(List<string> paths, string rootFolderPath, VM_SubgroupPlaceHolder topLevelPlaceHolder, VM_AssetPack config)
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

                foreach(var pathGroup in pathGroups)
                {
                    var parentPlaceHolder = LastParentPlaceHolders[pathGroup.First()];
                    var texturesInGroup = paths.Where(x => x.StartsWith(pathGroup.Key)).ToArray();

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
                    
                    foreach(var path in texturesInGroup)
                    {
                        LastParentGroupings[path] = pathGroup;
                    }
                }
            }
        }

        private Dictionary<string, VM_SubgroupPlaceHolder> LastParentPlaceHolders { get; set; } = new();
        private Dictionary<string, IGrouping<string, string>> LastParentGroupings { get; set; } = new();

        private static bool CleanRedundantSubgroups(VM_SubgroupPlaceHolder currentSubgroup)
        {
            for (int i = 0; i < currentSubgroup.Subgroups.Count; i++)
            {
                if(CleanRedundantSubgroups(currentSubgroup.Subgroups[i]))
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
            foreach(string path in paths)
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
            if (!LastParentGroupings.ContainsKey(pathGroup.First()))
            {
                return false;
            }
            return pathGroup.Count() != LastParentGroupings[pathGroup.First()].Count();
        }

        private AssetPack.Subgroup CreateSubgroupModel(string id, string name)
        {
            var subgroup = new AssetPack.Subgroup();
            subgroup.ID = id;
            subgroup.Name = name;
            return subgroup;
        }

        private static readonly Dictionary<TextureType, (string,string)> TypeToSubgroupLabels = new()
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
            { TextureType.BodySpecular, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_s.dds", "bodymale_s.dds", "argonianmalebody_s.dds", "femalebody_1_s.dds", "AstridBody_s.dds", "femalebody_s.dds" } },
            { TextureType.HandsDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malehands_1.dds", "malehandsafflicted.dds", "malehandssnowelf.dds", "HandsMale.dds", "ArgonianMaleHands.dds", "femalehands_1.dds", "femalehandsafflicted.dds", "AstridHands.dds", "femalehands.dds",  "argonianfemalehands.dds"} },
            { TextureType.HandsNormal, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_msn.dds", "HandsMale_msn.dds", "ArgonianMaleHands_msn.dds", "femalehands_1_msn.dds", "AstridHands_msn.dds", "femalehands_msn.dds","argonianfemalehands_msn.dds" } },
            { TextureType.HandsSubsurface, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_sk.dds", "femalehands_1_sk.dds" } },
            { TextureType.HandsSpecular, new(StringComparer.OrdinalIgnoreCase) { "malehands_1_s.dds", "handsmale_s.dds", "ArgonianMaleHands_s.dds", "femalehands_1_s.dds", "AstridHands_s.dds", "femalehands_s.dds", "argonianfemalebody_s.dds", "argonianfemalehands_s.dds" } },
            { TextureType.FeetDiffuse, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet.dds", "femalebody_1_feet.dds" } },
            { TextureType.FeetNormal, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_msn_feet.dds", "femalebody_1_msn_feet.dds" } },
            { TextureType.FeetSubsurface, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet_sk.dds", "femalebody_1_feet_sk.dds" } },
            { TextureType.FeetSpecular, new(StringComparer.OrdinalIgnoreCase) { "malebody_1_feet_s.dds", "femalebody_1_feet_s.dds" } }
        };

        private static List<string> GetMatchingFiles(string directoryPath, HashSet<string> fileNames)
        {
            List<string> matchingFilePaths = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine("Directory not found.");
                return matchingFilePaths;
            }

            string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD
{
    public class AssetPack : IModelHasSubgroups
    {
        public AssetPack()
        {
            this.GroupName = "";
            this.ShortName = "";
            this.ConfigType = AssetPackType.Primary;
            this.Gender = Gender.Male;
            this.DisplayAlerts = true;
            this.UserAlert = "";
            this.Subgroups = new List<Subgroup>();
            this.DefaultRecordTemplate = new FormKey();
            this.AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
            this.AssociatedBodyGenConfigName = "";
            this.DefaultRecordTemplateAdditionalRacesPaths = new HashSet<string>();
            this.AttributeGroups = new HashSet<AttributeGroup>();
            this.ReplacerGroups = new List<AssetReplacerGroup>();
        }

        public string GroupName { get; set; }
        public string ShortName { get; set; }
        public AssetPackType ConfigType { get; set; }
        public Gender Gender { get; set; }
        public bool DisplayAlerts { get; set; }
        public string UserAlert { get; set; }
        public List<Subgroup> Subgroups { get; set; } // don't change to HashSet - need indexing for RequiredSubgroups
        public FormKey DefaultRecordTemplate { get; set; }
        public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
        public string AssociatedBodyGenConfigName { get; set; }
        public List<AssetReplacerGroup> ReplacerGroups { get; set; }
        public HashSet<string> DefaultRecordTemplateAdditionalRacesPaths { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        public ConfigDistributionRules DistributionRules { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public string FilePath { get; set; }

        public bool Validate(List<string> errors, BodyGenConfigs bodyGenConfigs)
        {
            bool isValidated = true;

            BodyGenConfig referencedBodyGenConfig = new BodyGenConfig();

            if (string.IsNullOrWhiteSpace(GroupName))
            {
                errors.Add("Name cannot be empty");
                isValidated = false;
            }
            if (string.IsNullOrWhiteSpace(ShortName) && ReplacerGroups.Any())
            {
                errors.Add("Prefix cannot be empty if replacers are included in a group");
                isValidated = false;
            }

            if (DefaultRecordTemplate == null || DefaultRecordTemplate.IsNull)
            {
                errors.Add("A default record template must be set.");
                isValidated = false;
            }

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !string.IsNullOrWhiteSpace(AssociatedBodyGenConfigName))
            {
                BodyGenConfig matchedConfig = null;
                switch(Gender)
                {
                    case Gender.Male: matchedConfig = bodyGenConfigs.Male.Where(x => x.Label == AssociatedBodyGenConfigName).FirstOrDefault(); break;
                    case Gender.Female: matchedConfig = bodyGenConfigs.Female.Where(x => x.Label == AssociatedBodyGenConfigName).FirstOrDefault(); break;
                }
                if (matchedConfig != null)
                {
                    referencedBodyGenConfig = matchedConfig;
                }
                else
                {
                    errors.Add("The expected associated BodyGen config " + AssociatedBodyGenConfigName + " could not be found.");
                    isValidated = false;
                }
            }

            if (!ValidateSubgroups(Subgroups, errors, this, referencedBodyGenConfig))
            {
                isValidated = false;
            }
            foreach (var replacer in ReplacerGroups)
            {
                if (!ValidateReplacer(replacer, referencedBodyGenConfig, errors))
                {
                    isValidated = false;
                }
            }

            if(!isValidated)
            {
                errors.Insert(0, "Errors detected in Config File " + GroupName);
            }

            return isValidated;
        }

        public class ConfigDistributionRules : IProbabilityWeighted
        {
            public ConfigDistributionRules()
            {
                this.AllowedRaces = new HashSet<FormKey>();
                this.AllowedRaceGroupings = new HashSet<string>();
                this.DisallowedRaces = new HashSet<FormKey>();
                this.DisallowedRaceGroupings = new HashSet<string>();
                this.AllowedAttributes = new HashSet<NPCAttribute>();
                this.DisallowedAttributes = new HashSet<NPCAttribute>();
                this.AllowUnique = true;
                this.AllowNonUnique = true;
                this.AddKeywords = new HashSet<string>();
                this.ProbabilityWeighting = 1;
                this.AllowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>();
                this.DisallowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>();
                this.AllowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>();
                this.DisallowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>();
                this.WeightRange = new NPCWeightRange();
            }

            public HashSet<FormKey> AllowedRaces { get; set; }
            public HashSet<string> AllowedRaceGroupings { get; set; }
            public HashSet<FormKey> DisallowedRaces { get; set; }
            public HashSet<string> DisallowedRaceGroupings { get; set; }
            public HashSet<NPCAttribute> AllowedAttributes { get; set; }
            public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
            public bool AllowUnique { get; set; }
            public bool AllowNonUnique { get; set; }
            public HashSet<string> AddKeywords { get; set; }
            public double ProbabilityWeighting { get; set; }
            public HashSet<BodyShapeDescriptor> AllowedBodyGenDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> DisallowedBodyGenDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> AllowedBodySlideDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> DisallowedBodySlideDescriptors { get; set; }
            public NPCWeightRange WeightRange { get; set; }

            public static string SubgroupIDString = "ConfigDistributionRules";
            public static string SubgroupNameString = "Main Distribution Rules";

            public static Subgroup CreateInheritanceParent(ConfigDistributionRules rules)
            {
                Subgroup subgroup = new Subgroup();
                subgroup.ID = SubgroupIDString;
                subgroup.Name = SubgroupNameString;
                subgroup.AllowedRaces = new HashSet<FormKey>(rules.AllowedRaces);
                subgroup.AllowedRaceGroupings = new HashSet<string>(rules.AllowedRaceGroupings);
                subgroup.DisallowedRaces = new HashSet<FormKey>(rules.DisallowedRaces);
                subgroup.DisallowedRaceGroupings = new HashSet<string>(rules.DisallowedRaceGroupings);
                subgroup.AllowedAttributes = new HashSet<NPCAttribute>(rules.AllowedAttributes);
                subgroup.DisallowedAttributes = new HashSet<NPCAttribute>(rules.DisallowedAttributes);
                subgroup.AllowUnique = rules.AllowUnique;
                subgroup.AllowNonUnique = rules.AllowNonUnique;
                subgroup.AddKeywords = new HashSet<string>(rules.AddKeywords);
                subgroup.ProbabilityWeighting = rules.ProbabilityWeighting;
                subgroup.AllowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>(rules.AllowedBodyGenDescriptors);
                subgroup.DisallowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>(rules.DisallowedBodyGenDescriptors);
                subgroup.AllowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>(rules.AllowedBodySlideDescriptors);
                subgroup.DisallowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>(rules.DisallowedBodySlideDescriptors);
                subgroup.WeightRange = new NPCWeightRange() { Lower = rules.WeightRange.Lower, Upper = rules.WeightRange.Upper};
                return subgroup;
            }
        }

        public class Subgroup : IProbabilityWeighted, IModelHasSubgroups
        {
            public Subgroup()
            {
                this.ID = "";
                this.Name = "";
                this.Enabled = true;
                this.DistributionEnabled = true;
                this.AllowedRaces = new HashSet<FormKey>();
                this.AllowedRaceGroupings = new HashSet<string>();
                this.DisallowedRaces = new HashSet<FormKey>();
                this.DisallowedRaceGroupings = new HashSet<string>();
                this.AllowedAttributes = new HashSet<NPCAttribute>();
                this.DisallowedAttributes = new HashSet<NPCAttribute>();
                this.AllowUnique = true;
                this.AllowNonUnique = true;
                this.RequiredSubgroups = new HashSet<string>();
                this.ExcludedSubgroups = new HashSet<string>();
                this.AddKeywords = new HashSet<string>();
                this.ProbabilityWeighting = 1;
                this.Paths = new HashSet<FilePathReplacement>();
                this.AllowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>();
                this.DisallowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor>();
                this.AllowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>();
                this.DisallowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor>();
                this.WeightRange = new NPCWeightRange();
                this.Subgroups = new List<Subgroup>();

                this.TopLevelSubgroupID = "";
            }
            
            public string ID { get; set; }
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public bool DistributionEnabled { get; set; }
            public HashSet<FormKey> AllowedRaces { get; set; }
            public HashSet<string> AllowedRaceGroupings { get; set; }
            public HashSet<FormKey> DisallowedRaces { get; set; }
            public HashSet<string> DisallowedRaceGroupings { get; set; }
            public HashSet<NPCAttribute> AllowedAttributes { get; set; }
            public HashSet<NPCAttribute> DisallowedAttributes { get; set; } 
            public bool AllowUnique { get; set; }
            public bool AllowNonUnique { get; set; }
            public HashSet<string> RequiredSubgroups { get; set; }
            public HashSet<string> ExcludedSubgroups { get; set; }
            public HashSet<string> AddKeywords { get; set; }
            public double ProbabilityWeighting { get; set; }
            public HashSet<FilePathReplacement> Paths { get; set; }
            public HashSet<BodyShapeDescriptor> AllowedBodyGenDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> DisallowedBodyGenDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> AllowedBodySlideDescriptors { get; set; }
            public HashSet<BodyShapeDescriptor> DisallowedBodySlideDescriptors { get; set; }
            public NPCWeightRange WeightRange { get; set; }
            public List<Subgroup> Subgroups { get; set; }
            public string TopLevelSubgroupID { get; set; }
        }

        private static bool ValidateSubgroups(List<AssetPack.Subgroup> subgroups, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig)
        {
            bool isValid = true;
            for (int i = 0; i < subgroups.Count; i++)
            {
                if (!ValidateSubgroup(subgroups[i], errors, parent, bodyGenConfig, i))
                {
                    isValid = false;
                }
            }
            return isValid;
        }

        private static bool ValidateSubgroup(Subgroup subgroup, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig, int topLevelIndex)
        {
            if (!subgroup.Enabled) { return true; }

            bool isValid = true;
            List<string> subErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(subgroup.ID))
            {
                subErrors.Add("Subgroup does not have an ID");
            }
            if (!ValidateID(subgroup.ID))
            {
                subErrors.Add("ID must be alphanumeric or .");
                isValid = false;
            }

            if (HasDuplicateSubgroupIDs(subgroup, subErrors))
            {
                isValid = false;
            }
            
            if (string.IsNullOrWhiteSpace(subgroup.Name))
            {
                subErrors.Add("Subgroup must have a name");
                isValid = false;
            }

            var thisPosition = new List<int> { topLevelIndex };
            var otherPostitions = new List<int>();
            for (int i = 0; i < parent.Subgroups.Count; i++)
            {
                if (i == topLevelIndex)
                {
                    continue;
                }
                else
                {
                    otherPostitions.Add(i);
                }
            }

            foreach (var id in subgroup.RequiredSubgroups)
            {
                if (GetSubgroupByID(id, parent, out _, thisPosition) != null)
                {
                    subErrors.Add("Cannot use " + id + " as a required subgroup because it is in the same branch as " + subgroup.ID);
                    isValid = false;
                }
                else if (GetSubgroupByID(id, parent, out _, otherPostitions) == null)
                {
                    subErrors.Add("Cannot use " + id + " as a required subgroup because it was not found in the subgroup tree");
                    isValid = false;
                }
            }

            foreach (var id in subgroup.ExcludedSubgroups)
            {
                if (GetSubgroupByID(id, parent, out _, thisPosition) != null)
                {
                    subErrors.Add("Cannot use " + id + " as an excluded subgroup because it is in the same branch as " + subgroup.ID);
                    isValid = false;
                }
                else if (GetSubgroupByID(id, parent, out _, otherPostitions) == null)
                {
                    subErrors.Add("Cannot use " + id + " as an excluded subgroup because it was not found in the subgroup tree");
                    isValid = false;
                }
            }

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen && bodyGenConfig != null)
            {
                foreach (var descriptor in subgroup.AllowedBodyGenDescriptors)
                {
                    if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
                    {
                        subErrors.Add("Allowed descriptor " + descriptor.Signature + " is invalid because it is not contained within the associated BodyGen config's descriptors");
                        isValid=false;
                    }
                }
                foreach (var descriptor in subgroup.DisallowedBodyGenDescriptors)
                {
                    if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
                    {
                        subErrors.Add("Disallowed descriptor " + descriptor.Signature + " is invalid because it is not contained within the associated BodyGen config's descriptors");
                        isValid = false;
                    }
                }
            }

            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                foreach (var descriptor in subgroup.AllowedBodySlideDescriptors)
                {
                    if (!descriptor.CollectionContainsThisDescriptor(PatcherSettings.OBody.TemplateDescriptors))
                    {
                        subErrors.Add("Allowed descriptor " + descriptor.Signature + " is invalid because it is not contained within your O/AutoBody descriptors");
                        isValid = false;
                    }
                }
                foreach (var descriptor in subgroup.DisallowedBodySlideDescriptors)
                {
                    if (!descriptor.CollectionContainsThisDescriptor(PatcherSettings.OBody.TemplateDescriptors))
                    {
                        subErrors.Add("Disallowed descriptor " + descriptor.Signature + " is invalid because it is not contained within your O/AutoBody descriptors");
                        isValid = false;
                    }
                }
            }

            foreach (var path in subgroup.Paths)
            {
                var fullPath = System.IO.Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, path.Source);
                if (!System.IO.File.Exists(fullPath) && !BSAHandler.ReferencedPathExists(path.Source, out bool archiveExists, out string modName))
                {
                    string pathError = "No file exists at " + fullPath;
                    if (archiveExists)
                    {
                        pathError += " or any BSA archives corresponding to " + modName;
                    }
                    subErrors.Add(pathError);
                    isValid = false;
                }
            }

            if (!isValid)
            {
                subErrors.Insert(0, "Subgroup " + subgroup.ID + ":" + subgroup.Name + " within branch " + (topLevelIndex + 1).ToString());
                errors.AddRange(subErrors);
            }

            foreach (var subSubgroup in subgroup.Subgroups)
            {
                if (!ValidateSubgroup(subSubgroup, errors, parent, bodyGenConfig, topLevelIndex))
                {
                    isValid = false;
                }
            }

            return isValid;
        }

        private static bool ValidateID(string id)
        {
            string tmp = id.Replace(".", "");
            if (tmp.All(char.IsLetterOrDigit))
            {
                return true;
            }
            else;
            { return false; }
        }

        private static bool ValidateReplacer(AssetReplacerGroup group, BodyGenConfig bodyGenConfig, List<string> errors)
        {
            bool isValid = true;
            if (string.IsNullOrWhiteSpace(group.Label))
            {
                errors.Add("A group with an empty name was detected");
                isValid = false;
            }

            ValidateSubgroups(group.Subgroups, errors, group, bodyGenConfig);
            return isValid;
        }

        private static bool HasDuplicateSubgroupIDs(IModelHasSubgroups model, List<string> errors)
        {
            List<string> ids = new List<string>();
            List<string> duplicates = new List<string>();
            foreach (var subgroup in model.Subgroups)
            {
                GetIDDuplicates(subgroup, ids, duplicates);
            }

            if (duplicates.Any())
            {
                errors.Add("Duplicate subgroup IDs within the same parent config are not allowed. The following IDs were found to be duplicated:");
                foreach (string id in duplicates)
                {
                    errors.Add(id);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private static void GetIDDuplicates(IModelHasSubgroups model, List<string> searched, List<string> duplicates)
        {
            foreach (var subgroup in model.Subgroups)
            {
                if (!searched.Contains(subgroup.ID))
                {
                    searched.Add(subgroup.ID);
                }
                else
                {
                    duplicates.Add(subgroup.ID);
                }

                foreach (var subSubgroup in subgroup.Subgroups)
                {
                    GetIDDuplicates(subSubgroup, searched, duplicates);
                }
            }
        }

        private static Subgroup GetSubgroupByID(string id, IModelHasSubgroups model, out bool foundMultiple, List<int> topLevelSubgroupsToSearch)
        {
            List<Subgroup> matched = new List<Subgroup>();

            for (int i = 0; i < model.Subgroups.Count; i++)
            {
                if (!topLevelSubgroupsToSearch.Contains(i)) { continue; }
                GetSubgroupByID(id, model.Subgroups[i], matched);
            }

          
            if (matched.Count > 1)
            {
                foundMultiple = true;
            }
            else
            {
                foundMultiple = false;
            }

            return matched.FirstOrDefault();
        }

        private static void GetSubgroupByID(string id, IModelHasSubgroups model, List<Subgroup> matched)
        {
            for (int i = 0; i < model.Subgroups.Count; i++)
            {
                var subgroup = model.Subgroups[i];
                if (subgroup.ID == id)
                {
                    matched.Add(subgroup);
                }
                GetSubgroupByID(id, subgroup, matched);
            }
        }
    }

    public enum AssetPackType
    {
        Primary,
        MixIn
    }
    public class AssetReplacerGroup : IModelHasSubgroups
    {
        public AssetReplacerGroup()
        {
            this.Label = "";
            this.Subgroups = new List<AssetPack.Subgroup>();
            this.TemplateNPCFormKey = new FormKey();
        }

        public string Label { get; set; }
        public List<AssetPack.Subgroup> Subgroups { get; set; }
        public FormKey TemplateNPCFormKey { get; set; }
    }

    public class RecordReplacerSpecifier
    {
        public RecordReplacerSpecifier()
        {
            Paths = new HashSet<string>();
        }
        public HashSet<string> Paths { get; set; }
        public FormKey DestFormKeySpecifier { get; set; }
        public SubgroupCombination.DestinationSpecifier DestSpecifier { get; set; }
    }

    public interface IModelHasSubgroups
    {
        public List<AssetPack.Subgroup> Subgroups { get; set; }
    }

    /* Will probably need to discard - can't edit tints
    public class TintMaskSelector
    {
        public TintMaskSelector()
        {

        }

        public string TexturePath { get; set; }
        public TintAssets.TintMaskType MaskType { get; set; }
        public TintMaskDistributionMode DistributionMode { get; set; }

    }

    public enum TintMaskDistributionMode
    {
        Replace, // replace a given tint mask path with the given selector
        DistributeAndReplace, // distribute to any NPC if the attribute allows for it, but remove a given path if the NPC has it
        Distribute // distribute to any NPC without replacing any existing tint masks
    }

    public class TintColorSelector : IProbabilityWeighted
    {

    } */

    // Backward compatibility classes for loading zEBD settings files and converting to synthEBD
    class ZEBDAssetPack
    {
        public ZEBDAssetPack()
        {
            this.groupName = "";
            this.gender = Gender.Male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new HashSet<ZEBDSubgroup>();
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public HashSet<ZEBDSubgroup> subgroups { get; set; }

        public class ZEBDSubgroup
        {
            public ZEBDSubgroup()
            {
                this.id = "";
                this.name = "";
                this.enabled = true;
                this.distributionEnabled = true;
                this.allowedRaces = new List<string>();
                this.allowedRaceGroupings = new List<RaceGrouping>();
                this.disallowedRaces = new List<string>();
                this.disallowedRaceGroupings = new List<RaceGrouping>();
                this.allowedAttributes = new List<string[]>();
                this.disallowedAttributes = new List<string[]>();
                this.forceIfAttributes = new List<string[]>();
                this.bAllowUnique = true;
                this.bAllowNonUnique = true;
                this.requiredSubgroups = new List<string>();
                this.excludedSubgroups = new List<string>();
                this.addKeywords = new List<string>();
                this.probabilityWeighting = 1;
                this.paths = new List<string[]>();
                this.allowedBodyGenDescriptors = new List<string>();
                this.disallowedBodyGenDescriptors = new List<string>();
                this.weightRange = new string[] { null, null };
                this.subgroups = new List<ZEBDSubgroup>();
            }

            public string id { get; set; }
            public string name { get; set; }
            public bool enabled { get; set; }
            public bool distributionEnabled { get; set; }
            public List<string> allowedRaces { get; set; }
            public List<RaceGrouping> allowedRaceGroupings { get; set; }
            public List<RaceGrouping> disallowedRaceGroupings { get; set; }
            public List<string> disallowedRaces { get; set; }
            public List<string[]> allowedAttributes { get; set; }
            public List<string[]> disallowedAttributes { get; set; }
            public List<string[]> forceIfAttributes { get; set; }
            public bool bAllowUnique { get; set; }
            public bool bAllowNonUnique { get; set; }
            public List<string> requiredSubgroups { get; set; }
            public List<string> excludedSubgroups { get; set; }
            public List<string> addKeywords { get; set; }
            public double probabilityWeighting { get; set; }
            public List<string[]> paths { get; set; }
            public List<string> allowedBodyGenDescriptors { get; set; }
            public List<string> disallowedBodyGenDescriptors { get; set; }
            public string[] weightRange { get; set; }
            public List<ZEBDSubgroup> subgroups { get; set; }

            public string hashKey { get; set; }

            public static AssetPack.Subgroup ToSynthEBDSubgroup(ZEBDSubgroup g, List<RaceGrouping> raceGroupings, string topLevelSubgroupID, string assetPackName, List<string> conversionErrors)
            {
                AssetPack.Subgroup s = new AssetPack.Subgroup();

                s.ID = g.id;
                s.Name = g.name;
                s.Enabled = g.enabled;
                s.DistributionEnabled = g.distributionEnabled;
                s.AllowedAttributes = Converters.StringArraysToAttributes(g.allowedAttributes);
                s.DisallowedAttributes = Converters.StringArraysToAttributes(g.disallowedAttributes);
                Converters.zEBDForceIfAttributesToAllowed(s.AllowedAttributes, Converters.StringArraysToAttributes(g.forceIfAttributes));
                s.AllowUnique = g.bAllowUnique;
                s.AllowNonUnique = g.bAllowNonUnique;
                s.RequiredSubgroups = new HashSet<string>(g.requiredSubgroups);
                s.ExcludedSubgroups = new HashSet<string>(g.excludedSubgroups);
                s.AddKeywords = new HashSet<string>(g.addKeywords);
                s.ProbabilityWeighting = g.probabilityWeighting;

                s.Paths = new HashSet<FilePathReplacement>();
                foreach (string[] pathPair in g.paths)
                {
                    string newSource = pathPair[0];
                    if (newSource.StartsWith('\\'))
                    {
                        newSource = newSource.Remove(0, 1);
                    }
                    if (pathPair[0].StartsWith("Skyrim.esm", StringComparison.OrdinalIgnoreCase) && (pathPair[0].EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || pathPair[0].EndsWith(".nif", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (pathPair[0].EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                        {
                            newSource = newSource.Replace("Skyrim.esm", "Skyrim.esm\\Textures", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (pathPair[0].EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                        {
                            newSource = newSource.Replace("Skyrim.esm", "Skyrim.esm\\Meshes", StringComparison.OrdinalIgnoreCase);
                        }
                    }

                    string newDest = "";
                    if (zEBDTexturePathConversionDict.ContainsKey(pathPair[1].ToLower()))
                    {
                        newDest = zEBDTexturePathConversionDict[pathPair[1].ToLower()];
                    }
                    else
                    {
                        conversionErrors.Add("Subgroup: " + g.id + ": The destination path " + pathPair[1] + " was not recognized as a default path, so it could not be converted to SynthEBD format. Please upgrade it manually.");
                    }
                    s.Paths.Add(new FilePathReplacement { Source = newSource, Destination = newDest });
                }

                s.WeightRange = Converters.StringArrayToWeightRange(g.weightRange);

                foreach (string id in g.allowedRaces)
                {
                    bool continueSearch = true;
                    // first see if it belongs to a RaceGrouping
                    foreach (var group in raceGroupings)
                    {
                        if (group.Label == id)
                        {
                            s.AllowedRaceGroupings.Add(group.Label);
                            continueSearch = false;
                            break;
                        }
                        else if (zEBDtoSynthEBDRaceGroupingNames.ContainsKey(id))
                        {
                            s.AllowedRaceGroupings.Add(zEBDtoSynthEBDRaceGroupingNames[id]);
                            continueSearch = false;
                            break;
                        }
                    }

                    // if not, see if it is a race EditorID
                    if (continueSearch == true)
                    {
                        FormKey raceFormKey = Converters.RaceEDID2FormKey(id);
                        if (raceFormKey.IsNull == false)
                        {
                            s.AllowedRaces.Add(raceFormKey);
                        }
                    }
                }

                foreach (string id in g.disallowedRaces)
                {
                    bool continueSearch = true;
                    // first see if it belongs to a RaceGrouping
                    foreach (var group in raceGroupings)
                    {
                        if (group.Label == id)
                        {
                            s.DisallowedRaceGroupings.Add(group.Label);
                            continueSearch = false;
                            break;
                        }
                        else if (zEBDtoSynthEBDRaceGroupingNames.ContainsKey(id))
                        {
                            s.DisallowedRaceGroupings.Add(zEBDtoSynthEBDRaceGroupingNames[id]);
                            continueSearch = false;
                            break;
                        }
                    }

                    // if not, see if it is a race EditorID
                    if (continueSearch == true)
                    {
                        FormKey raceFormKey = Converters.RaceEDID2FormKey(id);
                        if (raceFormKey.IsNull == false)
                        {
                            s.DisallowedRaces.Add(raceFormKey);
                        }
                    }
                }

                foreach (string str in g.allowedBodyGenDescriptors)
                {
                    s.AllowedBodyGenDescriptors.Add(Converters.StringToBodyShapeDescriptor(str));
                }
                foreach (string str in g.disallowedBodyGenDescriptors)
                {
                    s.DisallowedBodyGenDescriptors.Add(Converters.StringToBodyShapeDescriptor(str));
                }

                if (topLevelSubgroupID == "")
                {
                    s.TopLevelSubgroupID = s.ID; // this is the top level subgroup
                }
                else
                {
                    s.TopLevelSubgroupID = topLevelSubgroupID;
                }

                foreach (var sg in g.subgroups)
                {
                    s.Subgroups.Add(ToSynthEBDSubgroup(sg, raceGroupings, s.TopLevelSubgroupID, assetPackName, conversionErrors));
                }

                return s;
            }

            public static Dictionary<string, string> zEBDtoSynthEBDRaceGroupingNames = new Dictionary<string, string>()
            {
                {"humanoid", "Humanoid" },
                {"humanoidStandard", "Humanoid Playable" },
                {"humanoid_NonVamp", "Humanoid Non-Vampire" },
                {"humanoidVampire", "Humanoid Vampire" },
                {"humanoidYoung", "Humanoid Young" },
                {"humanoidYoungNonVampire", "Humanoid Young Non-Vampire" },
                {"humanoidYoungVampire", "Humanoid Young Vampire" },
                {"elven", "Elven" },
                {"elvenNonVampire", "Elven Non-Vampire" },
                {"elvenVampire", "Elven Vampire" },
                {"elder", "Elder" },
                {"khajiit", "Khajiit" },
                {"argonian", "Argonian" }
            };
        }

        public static AssetPack ToSynthEBDAssetPack(ZEBDAssetPack z, List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
        {
            List<string> conversionErrors = new List<string>();
            AssetPack s = new AssetPack();
            s.GroupName = z.groupName;
            s.Gender = z.gender;
            s.DisplayAlerts = z.displayAlerts;
            s.UserAlert = z.userAlert;
            foreach (ZEBDAssetPack.ZEBDSubgroup sg in z.subgroups)
            {
                s.Subgroups.Add(ZEBDAssetPack.ZEBDSubgroup.ToSynthEBDSubgroup(sg, raceGroupings, "", z.groupName, conversionErrors));
            }

            // Apply default record templates
            FormKey KhajiitRaceFK;
            FormKey KhajiitRaceVampireFK;
            FormKey ArgonianRaceFK;
            FormKey ArgonianRaceVampireFK;
            FormKey.TryFactory("013745:Skyrim.esm", out KhajiitRaceFK);
            FormKey.TryFactory("088845:Skyrim.esm", out KhajiitRaceVampireFK);
            FormKey.TryFactory("013740:Skyrim.esm", out ArgonianRaceFK);
            FormKey.TryFactory("08883A:Skyrim.esm", out ArgonianRaceVampireFK);

            foreach (var plugin in recordTemplatePlugins)
            {
                if (plugin.ModKey.Name == "Record Templates")
                {
                    switch (s.Gender)
                    {
                        case Gender.Female:
                            s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultFemale");
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                                TemplateNPC = GetNPCByEDID(plugin, "KhajiitFemale"), 
                                Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK },
                                AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                            });
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                                TemplateNPC = GetNPCByEDID(plugin, "ArgonianFemale"), 
                                Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK },
                                AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                            });
                            break;

                        case Gender.Male:
                            s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultMale");
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                                TemplateNPC = GetNPCByEDID(plugin, "KhajiitMale"), 
                                Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK },
                                AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                            });
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                                TemplateNPC = GetNPCByEDID(plugin, "ArgonianMale"), 
                                Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK },
                                AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                            });
                            break;
                    }

                }
            }

            // add new AdditionalRacesPaths - zEBD accomplished this in a way that is impractical w/ Mutagen due to strong typing
            s.DefaultRecordTemplateAdditionalRacesPaths = new HashSet<string>()
            {
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].AdditionalRaces",
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].AdditionalRaces",
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].AdditionalRaces",
            };

            bool hasBodyGen = false;
            foreach (var subgroup in z.subgroups)
            {
                hasBodyGen = zEBDConfigReferencesBodyGen(subgroup);
                if (hasBodyGen) { break; }
            }
            if (hasBodyGen)
            {
                var linkBodyGenWindow = new Window_LinkZEBDAssetPackToBodyGen();
                var linkBodyGen = new VM_LinkZEBDAssetPackToBodyGen(availableBodyGenConfigs, s.Gender, s.GroupName, linkBodyGenWindow);
                linkBodyGenWindow.DataContext = linkBodyGen;
                linkBodyGenWindow.ShowDialog();
                if (linkBodyGen.SelectedConfig != null)
                {
                    s.AssociatedBodyGenConfigName = linkBodyGen.SelectedConfig.Label;
                }
            }

            if (conversionErrors.Any())
            {
                string logFile = string.Join("_", z.groupName.Split(System.IO.Path.GetInvalidFileNameChars())) + ".txt";
                string logPath = System.IO.Path.Combine(PatcherSettings.Paths.LogFolderPath, logFile);
                Task.Run(() => PatcherIO.WriteTextFile(logPath, conversionErrors));
                Logger.LogMessage(conversionErrors);
                CustomMessageBox.DisplayNotificationOK("Import Error", "Errors were encountered during upgrade of a zEBD Config File. Please see log at " + logPath + ".");
            }

            return s;
        }

        private static HashSet<string> AdditionalRacesPathsBeastRaces = new HashSet<string>(){
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].AdditionalRaces",
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].AdditionalRaces",
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].AdditionalRaces",
                "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].AdditionalRaces"
            };

        private static FormKey GetNPCByEDID(SkyrimMod plugin, string edid)
        {
            foreach (var npc in plugin.Npcs)
            {
                if (npc.EditorID == edid)
                {
                    return npc.FormKey;
                }
            }
            return new FormKey();
        }

        private static bool zEBDConfigReferencesBodyGen(ZEBDSubgroup subgroup)
        {
            if (subgroup.allowedBodyGenDescriptors.Count > 0) { return true; }
            if (subgroup.disallowedBodyGenDescriptors.Count > 0) { return true; }
            foreach (var subSubgroup in subgroup.subgroups)
            {
                if (zEBDConfigReferencesBodyGen(subSubgroup)) { return true; }
            }
            return false;
        }
        
        private static Dictionary<string, string> zEBDTexturePathConversionDict = new Dictionary<string, string>()
        {            
            // common
            {"blankdetailmap.dds", "HeadTexture.Height"},
            {"malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"},
            {"malebody_1_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"}, // common male tail sk
            {"malehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"},
            {"malebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"},
            {"femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap"},
            {"femalehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap"},
            {"femalebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap"},
            // male humanoid
            {"malehead.dds", "HeadTexture.Diffuse" },
            {"malehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"malehead_sk.dds", "HeadTexture.GlowOrDetailMap"},
            {"malehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"malebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"malehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"malehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"malehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"malebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"malebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"malebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            // female humanoid
            {"femalehead.dds", "HeadTexture.Diffuse" },
            {"femalehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"femalehead_sk.dds", "HeadTexture.GlowOrDetailMap"},
            {"femalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"femalebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"femalehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"femalehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"femalehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"femalebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"femalebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"femalebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            // male argonian
            {"argonianmalehead.dds", "HeadTexture.Diffuse" },
            {"argonianmalehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"argonianmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"argonianmalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"argonianmalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"argonianmalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"argonianmalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"argonianmalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"argonianmalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"argonianmalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"argonianmalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"argonianmalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            // female argonian
            {"argonianfemalehead.dds", "HeadTexture.Diffuse" },
            {"argonianfemalehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"argonianfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"argonianfemalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"argonianfemalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"argonianfemalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"argonianfemalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"argonianfemalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"argonianfemalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"argonianfemalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"argonianfemalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"argonianfemalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            // male khajiit
            {"khajiitmalehead.dds", "HeadTexture.Diffuse" },
            {"khajiitmalehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"khajiitmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"handsmale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"handsmale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"handsmale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"bodymale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"bodymale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"bodymale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"bodymale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"},
            {"bodymale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse"},
            {"bodymale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss"},
            {"bodymale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular"},
            {"bodymale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap"},
            // female khajiit
            {"khajiitfemalehead.dds", "HeadTexture.Diffuse" },
            {"khajiitfemalehead_msn.dds", "HeadTexture.NormalOrGloss"},
            {"khajiitfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
            {"bodyfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"bodyfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"bodyfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"bodyfemale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap"},
            {"handsfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"handsfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"handsfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"bodyfemale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"bodyfemale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"bodyfemale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"bodyfemale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse"},
            {"bodyfemale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss"},
            {"bodyfemale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"},
            {"bodyfemale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap"},
        };
    }
}

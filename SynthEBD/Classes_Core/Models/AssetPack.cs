using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class AssetPack : IModelHasSubgroups
{
    public string GroupName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public AssetPackType ConfigType { get; set; } = AssetPackType.Primary;
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public List<Subgroup> Subgroups { get; set; } = new(); // don't change to HashSet - need indexing for RequiredSubgroups
    public FormKey DefaultRecordTemplate { get; set; } = new();
    public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public string AssociatedBodyGenConfigName { get; set; } = "";
    public List<AssetReplacerGroup> ReplacerGroups { get; set; } = new();
    public HashSet<string> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    public ConfigDistributionRules DistributionRules { get; set; }
    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }

    public class ConfigDistributionRules : IProbabilityWeighted
    {
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public HashSet<string> AddKeywords { get; set; } = new();
        public double ProbabilityWeighting { get; set; } = 1;
        public HashSet<BodyShapeDescriptor> AllowedBodyGenDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodyGenDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> AllowedBodySlideDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodySlideDescriptors { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();

        public static string SubgroupIDString = "ConfigDistributionRules";
        public static string SubgroupNameString = "Main Distribution Rules";

        public static AssetPack.Subgroup CreateInheritanceParent(ConfigDistributionRules rules)
        {
            AssetPack.Subgroup subgroup = new AssetPack.Subgroup();
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
        public string ID { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool DistributionEnabled { get; set; } = true;
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public HashSet<string> RequiredSubgroups { get; set; } = new();
        public HashSet<string> ExcludedSubgroups { get; set; } = new();
        public HashSet<string> AddKeywords { get; set; } = new();
        public double ProbabilityWeighting { get; set; } = 1;
        public HashSet<FilePathReplacement> Paths { get; set; } = new();
        public HashSet<BodyShapeDescriptor> AllowedBodyGenDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodyGenDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> AllowedBodySlideDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodySlideDescriptors { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();
        public List<Subgroup> Subgroups { get; set; } = new();
        public string TopLevelSubgroupID { get; set; } = "";
    }
}

public enum AssetPackType
{
    Primary,
    MixIn
}
public class AssetReplacerGroup : IModelHasSubgroups
{
    public string Label { get; set; } = "";
    public List<AssetPack.Subgroup> Subgroups { get; set; } = new();
    public FormKey TemplateNPCFormKey { get; set; } = new();
}

public class RecordReplacerSpecifier
{
    public HashSet<string> Paths { get; set; } = new();
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
    public string groupName { get; set; } = "";
    public Gender gender { get; set; } = Gender.Male;
    public bool displayAlerts { get; set; } = true;
    public string userAlert { get; set; } = "";
    public HashSet<ZEBDSubgroup> subgroups { get; set; } = new();

    public class ZEBDSubgroup
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public bool enabled { get; set; } = true;
        public bool distributionEnabled { get; set; } = true;
        public List<string> allowedRaces { get; set; } = new();
        public List<RaceGrouping> allowedRaceGroupings { get; set; } = new();
        public List<RaceGrouping> disallowedRaceGroupings { get; set; } = new();
        public List<string> disallowedRaces { get; set; } = new();
        public List<string[]> allowedAttributes { get; set; } = new();
        public List<string[]> disallowedAttributes { get; set; } = new();
        public List<string[]> forceIfAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public List<string> requiredSubgroups { get; set; } = new();
        public List<string> excludedSubgroups { get; set; } = new();
        public List<string> addKeywords { get; set; } = new();
        public double probabilityWeighting { get; set; } = 1;
        public List<string[]> paths { get; set; } = new();
        public List<string> allowedBodyGenDescriptors { get; set; } = new();
        public List<string> disallowedBodyGenDescriptors { get; set; } = new();
        public string[] weightRange { get; set; } = new string[] { null, null };
        public List<ZEBDSubgroup> subgroups { get; set; } = new();

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
        {"malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"},
        {"malebody_1_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"}, // common male tail sk
        {"malehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"},
        {"malebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"},
        {"femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap"},
        {"femalehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap"},
        {"femalebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap"},
        // male humanoid
        {"malehead.dds", "HeadTexture.Diffuse" },
        {"malehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"malehead_sk.dds", "HeadTexture.GlowOrDetailMap"},
        {"malehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"malebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"malehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"malehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"malehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"malebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"malebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"malebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        // female humanoid
        {"femalehead.dds", "HeadTexture.Diffuse" },
        {"femalehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"femalehead_sk.dds", "HeadTexture.GlowOrDetailMap"},
        {"femalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"femalebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"femalehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"femalehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"femalehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"femalebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"femalebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"femalebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        // male argonian
        {"argonianmalehead.dds", "HeadTexture.Diffuse" },
        {"argonianmalehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"argonianmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"argonianmalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"argonianmalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"argonianmalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"argonianmalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"argonianmalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"argonianmalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"argonianmalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"argonianmalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"argonianmalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        // female argonian
        {"argonianfemalehead.dds", "HeadTexture.Diffuse" },
        {"argonianfemalehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"argonianfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"argonianfemalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"argonianfemalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"argonianfemalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"argonianfemalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"argonianfemalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"argonianfemalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"argonianfemalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"argonianfemalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"argonianfemalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        // male khajiit
        {"khajiitmalehead.dds", "HeadTexture.Diffuse" },
        {"khajiitmalehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"khajiitmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"handsmale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"handsmale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"handsmale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"bodymale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"bodymale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"bodymale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"bodymale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"},
        {"bodymale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse"},
        {"bodymale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss"},
        {"bodymale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular"},
        {"bodymale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap"},
        // female khajiit
        {"khajiitfemalehead.dds", "HeadTexture.Diffuse" },
        {"khajiitfemalehead_msn.dds", "HeadTexture.NormalOrGloss"},
        {"khajiitfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular"},
        {"bodyfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"bodyfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"bodyfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"bodyfemale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap"},
        {"handsfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"handsfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"handsfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"bodyfemale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"bodyfemale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"bodyfemale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"bodyfemale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse"},
        {"bodyfemale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss"},
        {"bodyfemale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular"},
        {"bodyfemale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap"},
    };
}
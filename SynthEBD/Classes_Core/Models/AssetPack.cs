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
    public class AssetPack
    {
        public AssetPack()
        {
            this.GroupName = "";
            this.Gender = Gender.male;
            this.DisplayAlerts = true;
            this.UserAlert = "";
            this.Subgroups = new List<Subgroup>();
            this.DefaultRecordTemplate = new FormKey();
            this.AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
            this.AssociatedBodyGenConfigName = "";
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public bool DisplayAlerts { get; set; }
        public string UserAlert { get; set; }
        public List<Subgroup> Subgroups { get; set; } // don't change to HashSet - need indexing for RequiredSubgroups
        public FormKey DefaultRecordTemplate { get; set; }
        public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
        public string AssociatedBodyGenConfigName { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public string FilePath { get; set; }

        public class Subgroup : IProbabilityWeighted
        {
            public Subgroup()
            {
                this.id = "";
                this.name = "";
                this.enabled = true;
                this.distributionEnabled = true;
                this.allowedRaces = new HashSet<FormKey>();
                this.allowedRaceGroupings = new HashSet<string>();
                this.disallowedRaces = new HashSet<FormKey>();
                this.disallowedRaceGroupings = new HashSet<string>();
                this.allowedAttributes = new HashSet<NPCAttribute>();
                this.disallowedAttributes = new HashSet<NPCAttribute>();
                this.bAllowUnique = true;
                this.bAllowNonUnique = true;
                this.requiredSubgroups = new HashSet<string>();
                this.excludedSubgroups = new HashSet<string>();
                this.addKeywords = new HashSet<string>();
                this.ProbabilityWeighting = 1;
                this.paths = new HashSet<FilePathReplacement>();
                this.allowedBodyGenDescriptors = new HashSet<BodyGenConfig.MorphDescriptor>();
                this.disallowedBodyGenDescriptors = new HashSet<BodyGenConfig.MorphDescriptor>();
                this.weightRange = new NPCWeightRange();
                this.subgroups = new HashSet<Subgroup>();

                this.TopLevelSubgroupID = "";
            }
            
            public string id { get; set; }
            public string name { get; set; }
            public bool enabled { get; set; }
            public bool distributionEnabled { get; set; }
            public HashSet<FormKey> allowedRaces { get; set; }
            public HashSet<string> allowedRaceGroupings { get; set; }
            public HashSet<FormKey> disallowedRaces { get; set; }
            public HashSet<string> disallowedRaceGroupings { get; set; }
            public HashSet<NPCAttribute> allowedAttributes { get; set; }
            public HashSet<NPCAttribute> disallowedAttributes { get; set; } 
            public bool bAllowUnique { get; set; }
            public bool bAllowNonUnique { get; set; }
            public HashSet<string> requiredSubgroups { get; set; }
            public HashSet<string> excludedSubgroups { get; set; }
            public HashSet<string> addKeywords { get; set; }
            public int ProbabilityWeighting { get; set; }
            public HashSet<FilePathReplacement> paths { get; set; }
            public HashSet<BodyGenConfig.MorphDescriptor> allowedBodyGenDescriptors { get; set; }
            public HashSet<BodyGenConfig.MorphDescriptor> disallowedBodyGenDescriptors { get; set; }
            public NPCWeightRange weightRange { get; set; }
            public HashSet<Subgroup> subgroups { get; set; }
            public string TopLevelSubgroupID { get; set; }
        }
    }

    // Backward compatibility classes for loading zEBD settings files and converting to synthEBD
    class ZEBDAssetPack
    {
        public ZEBDAssetPack()
        {
            this.groupName = "";
            this.gender = Gender.male;
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
            public int probabilityWeighting { get; set; }
            public List<string[]> paths { get; set; }
            public List<string> allowedBodyGenDescriptors { get; set; }
            public List<string> disallowedBodyGenDescriptors { get; set; }
            public string[] weightRange { get; set; }
            public List<ZEBDSubgroup> subgroups { get; set; }

            public string hashKey {get; set;}

            public static AssetPack.Subgroup ToSynthEBDSubgroup(ZEBDSubgroup g, List<RaceGrouping> raceGroupings, string topLevelSubgroupID)
            {
                AssetPack.Subgroup s = new AssetPack.Subgroup();

                s.id = g.id;
                s.name = g.name;
                s.enabled = g.enabled;
                s.distributionEnabled = g.distributionEnabled;
                s.allowedAttributes = Converters.StringArraysToAttributes(g.allowedAttributes);
                s.disallowedAttributes = Converters.StringArraysToAttributes(g.disallowedAttributes);
                Converters.zEBDForceIfAttributesToAllowed(s.allowedAttributes, Converters.StringArraysToAttributes(g.forceIfAttributes));
                s.bAllowUnique = g.bAllowUnique;
                s.bAllowNonUnique = g.bAllowNonUnique;
                s.requiredSubgroups = new HashSet<string>(g.requiredSubgroups);
                s.excludedSubgroups = new HashSet<string>(g.excludedSubgroups);
                s.addKeywords = new HashSet<string>(g.addKeywords);
                s.ProbabilityWeighting = g.probabilityWeighting;
                
                s.paths = new HashSet<FilePathReplacement>();
                foreach (string[] pathPair in g.paths)
                {
                    s.paths.Add(new FilePathReplacement { Source = pathPair[0], Destination = pathPair[1] });
                }

                s.weightRange = Converters.StringArrayToWeightRange(g.weightRange);

                foreach (string id in g.allowedRaces)
                {
                    bool continueSearch = true;
                    // first see if it belongs to a RaceGrouping
                    foreach (var group in raceGroupings)
                    {
                        if (group.Label == id)
                        {
                            s.allowedRaceGroupings.Add(group.Label);
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
                            s.allowedRaces.Add(raceFormKey);
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
                            s.disallowedRaceGroupings.Add(group.Label);
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
                            s.disallowedRaces.Add(raceFormKey);
                        }
                    }
                }

                foreach (string str in g.allowedBodyGenDescriptors)
                {
                    s.allowedBodyGenDescriptors.Add(Converters.StringToMorphDescriptor(str));
                }
                foreach (string str in g.disallowedBodyGenDescriptors)
                {
                    s.disallowedBodyGenDescriptors.Add(Converters.StringToMorphDescriptor(str));
                }

                if (topLevelSubgroupID == "")
                {
                    s.TopLevelSubgroupID = s.id; // this is the top level subgroup
                }
                else
                {
                    s.TopLevelSubgroupID = topLevelSubgroupID;
                }

                foreach (var sg in g.subgroups)
                {
                    s.subgroups.Add(ToSynthEBDSubgroup(sg, raceGroupings, s.TopLevelSubgroupID));
                }

                return s;
            } 
        }

        public static AssetPack ToSynthEBDAssetPack(ZEBDAssetPack z, List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
        {
            AssetPack s = new AssetPack();
            s.GroupName = z.groupName;
            s.Gender = z.gender;
            s.DisplayAlerts = z.displayAlerts;
            s.UserAlert = z.userAlert;
            foreach (ZEBDAssetPack.ZEBDSubgroup sg in z.subgroups)
            {
                s.Subgroups.Add(ZEBDAssetPack.ZEBDSubgroup.ToSynthEBDSubgroup(sg, raceGroupings, ""));
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
                    switch(s.Gender)
                    {
                        case Gender.female:
                            s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultFemale");
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { TemplateNPC = GetNPCByEDID(plugin, "KhajiitFemale"), Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK } });
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { TemplateNPC = GetNPCByEDID(plugin, "ArgonianFemale"), Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK } });
                            break;

                        case Gender.male:
                            s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultMale");
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { TemplateNPC = GetNPCByEDID(plugin, "KhajiitMale"), Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK } });
                            s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { TemplateNPC = GetNPCByEDID(plugin, "ArgonianMale"), Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK } });
                            break;
                    }
                    
                }
            }

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

            return s;
        }

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
    }
}

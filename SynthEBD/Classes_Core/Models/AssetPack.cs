using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class AssetPack
    {
        public AssetPack()
        {
            this.groupName = "";
            this.gender = Gender.male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new HashSet<Subgroup>();
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public HashSet<Subgroup> subgroups { get; set; }

        public class Subgroup
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
                this.forceIfAttributes = new HashSet<NPCAttribute>();
                this.bAllowUnique = true;
                this.bAllowNonUnique = true;
                this.requiredSubgroups = new HashSet<string>();
                this.excludedSubgroups = new HashSet<string>();
                this.addKeywords = new HashSet<string>();
                this.probabilityWeighting = 1;
                this.paths = new HashSet<FilePathReplacement>();
                this.allowedBodyGenDescriptors = new HashSet<string>();
                this.disallowedBodyGenDescriptors = new HashSet<string>();
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
            public HashSet<NPCAttribute> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
            public HashSet<NPCAttribute> disallowedAttributes { get; set; } 
            public HashSet<NPCAttribute> forceIfAttributes { get; set; }
            public bool bAllowUnique { get; set; }
            public bool bAllowNonUnique { get; set; }
            public HashSet<string> requiredSubgroups { get; set; }
            public HashSet<string> excludedSubgroups { get; set; }
            public HashSet<string> addKeywords { get; set; }
            public int probabilityWeighting { get; set; }
            public HashSet<FilePathReplacement> paths { get; set; }
            public HashSet<string> allowedBodyGenDescriptors { get; set; }
            public HashSet<string> disallowedBodyGenDescriptors { get; set; }
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
            public List<string[]> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
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
                s.forceIfAttributes = Converters.StringArraysToAttributes(g.forceIfAttributes);
                s.bAllowUnique = g.bAllowUnique;
                s.bAllowNonUnique = g.bAllowNonUnique;
                s.requiredSubgroups = new HashSet<string>(g.requiredSubgroups);
                s.excludedSubgroups = new HashSet<string>(g.excludedSubgroups);
                s.addKeywords = new HashSet<string>(g.addKeywords);
                s.probabilityWeighting = g.probabilityWeighting;
                
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

        public static AssetPack ToSynthEBDAssetPack(ZEBDAssetPack z, List<RaceGrouping> raceGroupings)
        {
            AssetPack s = new AssetPack();
            s.groupName = z.groupName;
            s.gender = z.gender;
            s.displayAlerts = z.displayAlerts;
            s.userAlert = z.userAlert;
            foreach (ZEBDAssetPack.ZEBDSubgroup sg in z.subgroups)
            {
                s.subgroups.Add(ZEBDAssetPack.ZEBDSubgroup.ToSynthEBDSubgroup(sg, raceGroupings, ""));
            }

            return s;
        }
    }
}

using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SpecificNPCAssignment
    {
        public SpecificNPCAssignment()
        {
            this.DispName = "";
            this.NPCFormKey = new FormKey();
            this.ForcedAssetPackName = "";
            this.ForcedSubgroupIDs = new HashSet<string>();
            this.ForcedHeight = "";
            this.ForcedBodyGenMorphNames = new HashSet<string>();
        }

        public string DispName { get; set; }
        public FormKey NPCFormKey { get; set; }
        public string ForcedAssetPackName { get; set; }
        public HashSet<string> ForcedSubgroupIDs { get; set; }
        public string ForcedHeight { get; set; }
        public HashSet<string> ForcedBodyGenMorphNames { get; set; }
    }

    public class zEBDSpecificNPCAssignment
    {
        public zEBDSpecificNPCAssignment()
        {
            this.name = "";
            this.formID = "";
            this.EDID = "";
            this.rootPlugin = "";
            this.race = "";
            this.gender = "";
            this.forcedAssetPack = "";
            this.forcedSubgroups = new HashSet<zEBDForcedSubgroup>();
            this.forcedHeight = "";
            this.forcedBodyGenMorphs = new HashSet<string>();
            this.displayString = "";
        }
        public string name { get; set; }
        public string formID { get; set; }
        public string EDID { get; set; }
        public string rootPlugin { get; set; }
        public string race { get; set; }
        public string gender { get; set; }
        public string forcedAssetPack { get; set; }
        public HashSet<zEBDForcedSubgroup> forcedSubgroups { get; set; }
        public string forcedHeight { get; set; }
        public HashSet<string> forcedBodyGenMorphs { get; set; }
        public string displayString { get; set; }

        public class zEBDForcedSubgroup
        {
            public string id { get; set; }
            public string description { get; set; }
            public string topLevelSubgroup { get; set; }
        }

        public static HashSet<SpecificNPCAssignment> ToSynthEBDNPCAssignments(HashSet<zEBDSpecificNPCAssignment> inputSet)
        {
            var outputSet = new HashSet<SpecificNPCAssignment>();
            var env = new GameEnvironmentProvider().MyEnvironment;

            foreach (var z in inputSet)
            {
                SpecificNPCAssignment s = new SpecificNPCAssignment();
                s.NPCFormKey = Converters.zEBDSignatureToFormKey(z.rootPlugin, z.formID, env);
                s.ForcedAssetPackName = z.forcedAssetPack;
                foreach (var zFS in z.forcedSubgroups)
                {
                    s.ForcedSubgroupIDs.Add(zFS.id);
                }
                s.ForcedHeight = z.forcedHeight;
                s.ForcedBodyGenMorphNames = z.forcedBodyGenMorphs;
                outputSet.Add(s);
            }
            return outputSet;
        }

    }
}

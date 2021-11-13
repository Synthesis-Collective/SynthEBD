using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class NPCAssignment
    {
        public NPCAssignment()
        {
            this.DispName = "";
            this.NPCFormKey = new FormKey();
            this.AssetPackName = "";
            this.SubgroupIDs = null;
            this.Height = "";
            this.BodyGenMorphNames = null;
        }

        public string DispName { get; set; }
        public FormKey NPCFormKey { get; set; }
        public string AssetPackName { get; set; }
        public HashSet<string> SubgroupIDs { get; set; }
        public string Height { get; set; }
        public HashSet<string> BodyGenMorphNames { get; set; }
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

        public static HashSet<NPCAssignment> ToSynthEBDNPCAssignments(HashSet<zEBDSpecificNPCAssignment> inputSet)
        {
            var outputSet = new HashSet<NPCAssignment>();
            var env = GameEnvironmentProvider.MyEnvironment;

            foreach (var z in inputSet)
            {
                NPCAssignment s = new NPCAssignment();
                s.NPCFormKey = Converters.zEBDSignatureToFormKey(z.rootPlugin, z.formID, env);
                s.AssetPackName = z.forcedAssetPack;
                foreach (var zFS in z.forcedSubgroups)
                {
                    s.SubgroupIDs.Add(zFS.id);
                }
                s.Height = z.forcedHeight;
                s.BodyGenMorphNames = z.forcedBodyGenMorphs;
                outputSet.Add(s);
            }
            return outputSet;
        }

    }
}

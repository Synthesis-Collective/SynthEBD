using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class NPCAssignment
{
    public string DispName { get; set; } = "";
    public FormKey NPCFormKey { get; set; } = new();
    public string AssetPackName { get; set; } = "";
    public List<string> SubgroupIDs { get; set; } = null; // order matters
    public float? Height { get; set; } = null;
    public List<string> BodyGenMorphNames { get; set; } = null; // order matters
    public string BodySlidePreset { get; set; } = "";
    public List<AssetReplacerAssignment> AssetReplacerAssignments { get; set; } = new();
    public List<MixInAssignment> MixInAssignments { get; set;} = new();
    public Dictionary<HeadPart.TypeEnum, FormKeyEditorIDPair> HeadParts { get; set; } = new()
    {
        { HeadPart.TypeEnum.Eyebrows, new() },
        { HeadPart.TypeEnum.Eyes, new() },
        { HeadPart.TypeEnum.Face, new() },
        { HeadPart.TypeEnum.FacialHair, new() },
        { HeadPart.TypeEnum.Hair, new() },
        { HeadPart.TypeEnum.Misc, new() },
        { HeadPart.TypeEnum.Scars, new() }
    };

    public class AssetReplacerAssignment
    {
        public string AssetPackName { get; set; } = "";
        public string ReplacerName { get; set; } = "";
        public List<string> SubgroupIDs { get; set; } = new();
    }

    public class MixInAssignment
    {
        public string AssetPackName { get; set; } = "";
        public List<string> SubgroupIDs { get; set; } = new();
        public List<AssetReplacerAssignment> AssetReplacerAssignments { get; set; } = new();
    }
}

public class zEBDSpecificNPCAssignment
{
    public string name { get; set; } = "";
    public string formID { get; set; } = "";
    public string EDID { get; set; } = "";
    public string rootPlugin { get; set; } = "";
    public string race { get; set; } = "";
    public string gender { get; set; } = "";
    public string forcedAssetPack { get; set; } = "";
    public List<zEBDForcedSubgroup> forcedSubgroups { get; set; } = new();
    public string forcedHeight { get; set; } = "";
    public List<string> forcedBodyGenMorphs { get; set; } = new();
    public string displayString { get; set; } = "";

    public class zEBDForcedSubgroup
    {
        public string id { get; set; }
        public string description { get; set; }
        public string topLevelSubgroup { get; set; }
    }

    public static HashSet<NPCAssignment> ToSynthEBDNPCAssignments(HashSet<zEBDSpecificNPCAssignment> inputSet)
    {
        var outputSet = new HashSet<NPCAssignment>();
        var env = PatcherEnvironmentProvider.Instance.Environment;

        foreach (var z in inputSet)
        {
            NPCAssignment s = new NPCAssignment();
            s.NPCFormKey = Converters.zEBDSignatureToFormKey(z.rootPlugin, z.formID, env);
            s.AssetPackName = z.forcedAssetPack;
            foreach (var zFS in z.forcedSubgroups)
            {
                s.SubgroupIDs.Add(zFS.id);
            }

            if (float.TryParse(z.forcedHeight, out var forcedHeight))
            {
                s.Height = forcedHeight;
            }
            else
            {
                Logger.LogError("Error in zEBD Specific NPC Assignment: Cannot interpret height" + z.forcedHeight);
            }

            s.BodyGenMorphNames = z.forcedBodyGenMorphs;
            outputSet.Add(s);
        }
        return outputSet;
    }

}
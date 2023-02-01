using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class PatcherState
{
    // Settings
    public Settings_General GeneralSettings { get; set; }
    public Settings_TexMesh TexMeshSettings { get; set; }
    public Settings_BodyGen BodyGenSettings { get; set; }
    public Settings_OBody OBodySettings { get; set; }
    public Settings_Height HeightSettings { get; set; }
    public Settings_Headparts HeadPartSettings { get; set; }
    public Settings_ModManager ModManagerSettings { get; set; }

    // Plugins

    public List<AssetPack> AssetPacks { get; set; }
    public List<HeightConfig> HeightConfigs { get; set; }
    public BodyGenConfigs BodyGenConfigs { get; set; }
    public Dictionary<string, NPCAssignment> Consistency { get; set; }
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public BlockList BlockList { get; set; }
    public List<SkyrimMod> RecordTemplatePlugins { get; set; }
    public HashSet<NPCAssignment> SpecificNPCAssignments { get; set; }

    public string GetStateLogStr()
    {
        System.Text.StringBuilder sb = new();

        if (GeneralSettings == null)
        {
            sb.AppendLine("General Settings: Null");
        }
        if (TexMeshSettings == null)
        {
            sb.AppendLine("Asset Settings: Null");
        }
        if (BodyGenSettings == null)
        {
            sb.AppendLine("BodyGen Settings: Null");
        }
        if (OBodySettings == null)
        {
            sb.AppendLine("BodySlide Settings: Null");
        }
        if (HeightSettings == null)
        {
            sb.AppendLine("Height Settings: Null");
        }
        if (HeadPartSettings == null)

        if (AssetPacks != null)
        {
            sb.AppendLine("Primary Config Files");
            foreach (var primary in AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary))
            {
                sb.AppendLine("\t" + primary.GroupName);
            }
            sb.AppendLine(("MixIn Config Files"));
            foreach (var mixin in AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn))
            {
                sb.AppendLine("\t" + mixin.GroupName);
            }
        }
        else
        {
            sb.AppendLine("AssetPacks: Null");
        }

        if (BodyGenConfigs != null)
        {
            if (BodyGenConfigs.Male != null)
            {
                sb.AppendLine("Male BodyGen Configs");
                foreach (var bgc in BodyGenConfigs.Male)
                {
                    sb.AppendLine("\t" + bgc.Label);
                }
            }
            else
            {
                sb.AppendLine("Male BodyGen Configs: Null");
            }

            if (BodyGenConfigs.Female != null)
            {
                sb.AppendLine("Female BodyGen Configs");
                foreach (var bgc in BodyGenConfigs.Female)
                {
                    sb.AppendLine("\t" + bgc.Label);
                }
            }
            else
            {
                sb.AppendLine("Female BodyGen Configs: Null");
            }
        }
        else
        {
            sb.Append("BodyGen Configs: Null");
        }

        return sb.ToString();
    }
}
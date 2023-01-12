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
}
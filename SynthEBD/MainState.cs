using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class MainState
{
    public List<AssetPack> AssetPacks { get; set; }
    public List<HeightConfig> HeightConfigs { get; set; }
    public BodyGenConfigs BodyGenConfigs { get; set; }
    public Dictionary<string, NPCAssignment> Consistency { get; set; }
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public BlockList BlockList { get; set; }
    public List<SkyrimMod> RecordTemplatePlugins { get; set; }
    public HashSet<NPCAssignment> SpecificNPCAssignments { get; set; }
    public HashSet<string> LinkedNPCNameExclusions { get; set; }
    public HashSet<LinkedNPCGroup> LinkedNPCGroups { get; set; }
}
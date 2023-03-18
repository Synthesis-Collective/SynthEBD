using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class BlockList
{
    public HashSet<BlockedNPC> NPCs { get; set; } = new();
    public HashSet<BlockedPlugin> Plugins { get; set; } = new();
}

public class BlockedNPC
{
    public FormKey FormKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool VanillaBodyPath { get; set; } = false;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
    public bool HeadParts { get; set; } = false;

    public Dictionary<HeadPart.TypeEnum, bool> HeadPartTypes { get; set; } = new()
    {
        {HeadPart.TypeEnum.Eyebrows, false },
        {HeadPart.TypeEnum.Eyes, false },
        {HeadPart.TypeEnum.Face, false },
        {HeadPart.TypeEnum.FacialHair, false },
        {HeadPart.TypeEnum.Hair, false },
        {HeadPart.TypeEnum.Misc, false },
        {HeadPart.TypeEnum.Scars, false }
    };
}

public class BlockedPlugin
{
    public ModKey ModKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool VanillaBodyPath { get; set; } = false;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
    public bool HeadParts { get; set; } = false;
    public Dictionary<HeadPart.TypeEnum, bool> HeadPartTypes { get; set; } = new()
    {
        {HeadPart.TypeEnum.Eyebrows, false },
        {HeadPart.TypeEnum.Eyes, false },
        {HeadPart.TypeEnum.Face, false },
        {HeadPart.TypeEnum.FacialHair, false },
        {HeadPart.TypeEnum.Hair, false },
        {HeadPart.TypeEnum.Misc, false },
        {HeadPart.TypeEnum.Scars, false }
    };
}

public class zEBDBlockList
{
    private IEnvironmentStateProvider _environmentProvider;
    private Converters _converters;
    public zEBDBlockList(IEnvironmentStateProvider environmentProvider, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _converters = converters;
    }
    public HashSet<zEBDBlockedNPC> blockedNPCs { get; set; } = new();
    public HashSet<zEBDBlockedPlugin> blockedPlugins { get; set; } = new();

    public BlockList ToSynthEBD()
    {
        BlockList sList = new BlockList();

        foreach (var npc in blockedNPCs)
        {
            BlockedNPC blockedNPC = new BlockedNPC();
            blockedNPC.FormKey = _converters.zEBDSignatureToFormKey(npc.rootPlugin, npc.formID, _environmentProvider);
            blockedNPC.Assets = npc.bBlockAssets;
            blockedNPC.Height = npc.bBlockHeight;
            blockedNPC.BodyShape = npc.bBlockBodyGen;
            sList.NPCs.Add(blockedNPC);
        }

        foreach (var plugin in blockedPlugins)
        {
            BlockedPlugin blockedPlugin = new BlockedPlugin();
            blockedPlugin.ModKey = ModKey.FromNameAndExtension(plugin.name);
            blockedPlugin.Assets = plugin.bBlockAssets;
            blockedPlugin.Height = plugin.bBlockHeight;
            blockedPlugin.BodyShape = plugin.bBlockBodyGen;
            sList.Plugins.Add(blockedPlugin);
        }

        return sList;
    }
}

public class zEBDBlockedNPC
{
    public string name { get; set; } = "";
    public string formID { get; set; } = "";
    public string EDID { get; set; } = "";
    public string rootPlugin { get; set; } = "";
    public string displayString { get; set; } = "";
    public bool bBlockAssets { get; set; } = true;
    public bool bBlockHeight { get; set; } = false;
    public bool bBlockBodyGen { get; set; } = false;
}

public class zEBDBlockedPlugin
{
    public string name { get; set; } = "";
    public bool bBlockAssets { get; set; } = true;
    public bool bBlockHeight { get; set; } = false;
    public bool bBlockBodyGen { get; set; } = false;
}
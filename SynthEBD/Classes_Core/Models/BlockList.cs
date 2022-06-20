using Mutagen.Bethesda.Plugins;

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
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
}

public class BlockedPlugin
{
    public ModKey ModKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
}

public class zEBDBlockList
{
    public HashSet<zEBDBlockedNPC> blockedNPCs { get; set; } = new();
    public HashSet<zEBDBlockedPlugin> blockedPlugins { get; set; } = new();

    public static BlockList ToSynthEBD(zEBDBlockList zList)
    {
        BlockList sList = new BlockList();
        var env = PatcherEnvironmentProvider.Instance.Environment;

        foreach (var npc in zList.blockedNPCs)
        {
            BlockedNPC blockedNPC = new BlockedNPC();
            blockedNPC.FormKey = Converters.zEBDSignatureToFormKey(npc.rootPlugin, npc.formID, env);
            blockedNPC.Assets = npc.bBlockAssets;
            blockedNPC.Height = npc.bBlockHeight;
            blockedNPC.BodyShape = npc.bBlockBodyGen;
            sList.NPCs.Add(blockedNPC);
        }

        foreach (var plugin in zList.blockedPlugins)
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
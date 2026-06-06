using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>The set of NPCs and plugins excluded (in whole or by individual axis) from patching.</summary>
public class BlockList
{
    public HashSet<BlockedNPC> NPCs { get; set; } = new();
    public HashSet<BlockedPlugin> Plugins { get; set; } = new();
}

/// <summary>Per-NPC block flags: which assignment axes (assets, vanilla body path, height, body shape, head parts and per-type) to skip for one NPC.</summary>
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

/// <summary>Per-plugin block flags: like <see cref="BlockedNPC"/> but applied to every NPC originating from a plugin.</summary>
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

/// <summary>Backwards-compatibility loader for old zEBD block lists; converts them to a SynthEBD <see cref="BlockList"/> via <see cref="ToSynthEBD"/>.</summary>
public class zEBDBlockList
{
    private IEnvironmentStateProvider _environmentProvider;
    private Converters _converters;
    /// <summary>Captures the environment (for FormKey resolution) and the zEBD signature converter.</summary>
    public zEBDBlockList(IEnvironmentStateProvider environmentProvider, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _converters = converters;
    }
    public HashSet<zEBDBlockedNPC> blockedNPCs { get; set; } = new();
    public HashSet<zEBDBlockedPlugin> blockedPlugins { get; set; } = new();

    /// <summary>Converts the loaded zEBD blocked NPCs/plugins into a SynthEBD <see cref="BlockList"/>, mapping only the assets/height/body-shape axes the old format supported.</summary>
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

/// <summary>Old zEBD blocked-NPC DTO.</summary>
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

/// <summary>Old zEBD blocked-plugin DTO.</summary>
public class zEBDBlockedPlugin
{
    public string name { get; set; } = "";
    public bool bBlockAssets { get; set; } = true;
    public bool bBlockHeight { get; set; } = false;
    public bool bBlockBodyGen { get; set; } = false;
}
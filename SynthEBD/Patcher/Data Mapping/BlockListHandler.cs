using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Resolves the effective block status (which patcher axes are suppressed) for an NPC from the
/// user's <see cref="BlockList"/>, both by direct NPC entry and by any plugin in the NPC's override
/// chain that is plugin-level blocked.
/// </summary>
class BlockListHandler
{
    /// <summary>
    /// Returns the <see cref="BlockedNPC"/> entry matching <paramref name="npcFormKey"/>, or a default
    /// (nothing blocked) entry when the NPC is not present in the block list.
    /// </summary>
    public static BlockedNPC GetCurrentNPCBlockStatus(BlockList blockList, FormKey npcFormKey)
    {
        var output = blockList.NPCs.Where(x => x.FormKey == npcFormKey).FirstOrDefault();

        if (output == null)
        {
            output = new BlockedNPC();
            output.Assets = false;
            output.BodyShape = false;
            output.Height = false;
        }

        return output;
    }

        
    /// <summary>
    /// Aggregates plugin-level block status for an NPC by walking every plugin in its override-context chain
    /// and OR-ing the per-axis block flags (assets, vanilla body path, body shape, height, head parts and
    /// per-head-part-type flags) of any matching <see cref="BlockedPlugin"/> entry.
    /// </summary>
    /// <param name="linkCache">Link cache used to resolve the NPC's override contexts across the load order.</param>
    /// <returns>A combined <see cref="BlockedPlugin"/> describing which axes are blocked for this NPC.</returns>
    public static BlockedPlugin GetCurrentPluginBlockStatus(BlockList blockList, FormKey npcFormKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(npcFormKey).ToList(); // [0] is winning override. [Last] is source plugin

        var output = new BlockedPlugin();
        output.Assets = false;
        output.BodyShape = false;
        output.Height = false;

        foreach (var modKey in contexts.Select(x => x.ModKey).ToArray())
        {
            var blockedPlugin = blockList.Plugins.Where(x => x.ModKey == modKey).FirstOrDefault();
            if (blockedPlugin != null)
            {
                if (blockedPlugin.Assets)
                {
                    output.Assets = true;
                }
                if (blockedPlugin.VanillaBodyPath)
                {
                    output.VanillaBodyPath = true;
                }
                if (blockedPlugin.BodyShape)
                {
                    output.BodyShape = true;
                }
                if (blockedPlugin.Height)
                {
                    output.Height = true;
                }
                if (blockedPlugin.HeadParts)
                {
                    output.HeadParts = true;
                    foreach (var headPartType in Enum.GetValues(typeof(HeadPart.TypeEnum)).Cast<HeadPart.TypeEnum>())
                    {
                        if (blockedPlugin.HeadPartTypes[headPartType]) { output.HeadPartTypes[headPartType] = true; }
                        else { output.HeadPartTypes[headPartType] = false; }
                    }
                }
            }
        }
           
        return output;
    }
        
}
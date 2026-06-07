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
public class BlockListHandler
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

        // Match each context-chain plugin to its block-list entry (null where the plugin isn't blocked),
        // preserving order, then merge. The merge OR-aggregates every flag, so order does not affect the result.
        var contributingPlugins = contexts.Select(context => blockList.Plugins.FirstOrDefault(x => x.ModKey == context.ModKey));
        return MergeBlockedPlugins(contributingPlugins);
    }

    /// <summary>
    /// Combines the block flags contributed by the block-list plugins matching an NPC's context chain into a
    /// single <see cref="BlockedPlugin"/>. Every flag — including each per-head-part-type flag — is
    /// OR-aggregated: an axis is blocked for the NPC if <em>any</em> contributing plugin blocks it, so a later
    /// plugin in the chain never clears a block contributed by an earlier one. Null slots (context plugins not
    /// in the block list) are skipped. Pure helper extracted from <see cref="GetCurrentPluginBlockStatus"/> for testability.
    /// </summary>
    /// <param name="contributingPlugins">Block-list entries matching the NPC's context-chain plugins (null per slot where unblocked).</param>
    public static BlockedPlugin MergeBlockedPlugins(IEnumerable<BlockedPlugin?> contributingPlugins)
    {
        var output = new BlockedPlugin();
        output.Assets = false;
        output.BodyShape = false;
        output.Height = false;

        foreach (var blockedPlugin in contributingPlugins)
        {
            if (blockedPlugin == null)
            {
                continue;
            }
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
                    // OR-aggregate per type (matching every other flag): only ever set true, never clear a
                    // block contributed by an earlier plugin in the chain.
                    if (blockedPlugin.HeadPartTypes[headPartType]) { output.HeadPartTypes[headPartType] = true; }
                }
            }
        }

        return output;
    }
        
}
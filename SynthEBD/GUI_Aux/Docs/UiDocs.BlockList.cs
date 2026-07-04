namespace SynthEBD;

// 3-part documentation for the Block List menu (blocked NPCs and blocked plugins).
public static partial class UiDocs
{
    private static void RegisterBlockList()
    {
        Add("BlockList.NPCs",
            layperson: "The list of individual NPCs you have excluded from patching. Each entry names one NPC and lets you pick exactly which kinds of changes (looks, body shape, height, head parts) to withhold from them.",
            technical: "Each row is a BlockedNPC entry in BlockList.json (NPC Configuration folder), matched at patch time by NPC FormKey via BlockListHandler.GetCurrentNPCBlockStatus. The selected entry's per-axis block flags are edited in the panel to the right and are checked before any assignment is rolled for that NPC.",
            motivation: "Some NPCs - typically followers or characters with hand-crafted appearances - should keep their original look; per-NPC blocking excludes just them without turning off any module.");

        Add("BlockList.Plugins",
            layperson: "The list of mods (plugins) whose NPCs you have excluded from patching. Blocking a plugin protects every NPC that the plugin adds or edits.",
            technical: "Each row is a BlockedPlugin entry in BlockList.json. At patch time BlockListHandler.GetCurrentPluginBlockStatus walks every plugin in the NPC's override chain and OR-merges the per-axis flags of each blocked plugin found there, so an NPC is affected if any plugin that provides or overrides it is listed.",
            motivation: "Appearance overhauls and follower packs can touch hundreds of NPCs; blocking the plugin protects them all with one entry instead of one NPC at a time.");

        Add("BlockList.NpcPicker",
            layperson: "Pick which NPC this entry blocks. You can search by name, editor ID, or record ID.",
            technical: "Sets BlockedNPC.FormKey via the name-resolving FormKey picker, scoped to NPC records in the current load order. At patch time the entry matches its NPC by exact FormKey.",
            motivation: "A block entry must identify one unique record; the FormKey does that reliably even when several NPCs share a name.");

        Add("BlockList.PluginPicker",
            layperson: "Pick which plugin this entry blocks. Every NPC added or edited by this plugin will be protected.",
            technical: "Sets BlockedPlugin.ModKey from the current load order. An NPC is affected when this plugin appears anywhere in its override chain - as the NPC's source plugin or any plugin overriding it - so the block extends to NPCs the plugin merely edits, not just ones it adds.",
            motivation: "Identifying mods by plugin file lets one entry cover an entire mod's NPC roster.");

        Add("BlockList.BlockAssets",
            layperson: "Stops new skin textures and meshes from being given to the blocked NPC, or to every NPC of a blocked plugin. This is checked by default for new entries because protecting looks is the most common reason to block.",
            technical: "The entry's Assets flag (default true for new entries). Patcher.IsBlockedForAssets skips asset selection when the NPC's own entry or the OR-merged plugin entries set it, so no asset combination, record overrides, FaceGen, or asset script/ini data are generated for that NPC.",
            motivation: "The usual reason to block is that a character already wears the skin its author intended; a per-axis flag protects that while still allowing height or body shape changes if you want them.");

        Add("BlockList.BlockVanillaBodyPath",
            layperson: "Exempts the blocked NPC, or a blocked plugin's NPCs, from the Destandalone feature that forces everyone onto your standard body models. Use it for characters whose special custom body is intentional.",
            technical: "The entry's VanillaBodyPath flag. VanillaBodyPathSetter.IsBlockedForVanillaBodyPaths consults the NPC entry and the OR-merged plugin entries and registers exempt NPCs so the vanilla-body-path rewrite of their worn armor is skipped. It only has an effect when Force Vanilla Body Mesh Paths is enabled.",
            motivation: "Some mods ship NPCs whose unique body meshes are the whole point (custom followers, UBE characters); rewriting them to the standard body paths would visually break those characters.");

        Add("BlockList.BlockHeight",
            layperson: "Stops the blocked NPC, or every NPC of a blocked plugin, from having their height changed.",
            technical: "The entry's Height flag. Patcher.IsBlockedForHeight skips height assignment when the NPC's entry or the OR-merged plugin entries set it, leaving the record's existing height untouched.",
            motivation: "Height edits can clash with mods that give characters deliberate statures or scenes tuned around them; this opt-out protects those characters.");

        Add("BlockList.ImportFromZEBD",
            layperson: "Loads a block list created with zEBD, the older zEdit-based version of this patcher, so you do not have to rebuild your exclusions by hand. It replaces what is currently shown; click Save to keep the result.",
            technical: "Opens a file picker for a zEBD BlockList.json, parses it in the zEBD format, converts it to SynthEBD's BlockedNPC/BlockedPlugin model, and reloads both UI lists from the result, replacing the currently displayed entries. Nothing is written to disk until Save.",
            motivation: "SynthEBD is the successor to zEBD; migrating users should not lose the block lists they curated there.");
    }
}

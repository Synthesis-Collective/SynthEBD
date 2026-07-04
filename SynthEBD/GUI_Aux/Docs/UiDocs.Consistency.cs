namespace SynthEBD;

// 3-part documentation for the Consistency menu and the per-NPC consistency assignment view.
public static partial class UiDocs
{
    private static void RegisterConsistency()
    {
        Add("Consistency.ClearAssetAssignments",
            layperson: "Erases the remembered texture-and-mesh choices for EVERY NPC, so the next patcher run re-rolls all looks from scratch. Remembered body shapes, heights, and head parts are kept.",
            technical: "For every entry in the consistency dictionary (Consistency.json), clears AssetPackName, SubgroupIDs, AssetReplacerAssignments, and MixInAssignments. Despite sitting above the NPC search box, it acts on all NPCs, not just the selected one.",
            motivation: "After installing or reorganizing config files, remembered asset picks can reference outdated packs or ignore your new content; wiping only the asset axis re-rolls looks without also reshuffling bodies and heights.");

        Add("Consistency.ClearBodyShapeAssignments",
            layperson: "Erases the remembered body shape (BodySlide preset or BodyGen morphs) for EVERY NPC so body shapes are re-rolled on the next run. Other remembered choices are kept.",
            technical: "For every entry in the consistency dictionary, clears BodyGenMorphNames and empties BodySlidePreset. Acts on all NPCs regardless of the current selection.",
            motivation: "Useful when switching body systems or overhauling your preset lineup - old remembered shapes would otherwise keep winning over the new options.");

        Add("Consistency.ClearHeightAssignments",
            layperson: "Erases the remembered height for EVERY NPC so heights are re-rolled on the next run.",
            technical: "Sets Height to null on every entry in the consistency dictionary. Acts on all NPCs. A stored height is also ignored automatically at patch time if it falls outside the currently configured range for the NPC's race and gender.",
            motivation: "Lets you re-randomize heights after changing height ranges or distributions without disturbing remembered looks and bodies.");

        Add("Consistency.ClearHeadPartAssignments",
            layperson: "Erases the remembered head parts (hair, eyes, brows, and so on) for EVERY NPC so they are re-rolled on the next run.",
            technical: "Clears the HeadParts dictionary on every entry in the consistency dictionary. Acts on all NPCs regardless of the current selection.",
            motivation: "Handy after changing which head part mods you distribute - remembered picks from removed mods would otherwise linger in the file.");

        Add("Consistency.ClearAll",
            layperson: "Deletes the entire memory of past assignments for all NPCs, after a confirmation prompt. The next patcher run re-rolls everything: looks, body shapes, heights, and head parts.",
            technical: "After a yes/no confirmation, clears the whole PatcherState.Consistency dictionary that backs Consistency.json.",
            motivation: "The full reset for when you want a genuinely fresh shuffle, or when a stale consistency file is causing odd repeated assignments.");

        Add("Consistency.SearchNpc",
            layperson: "Pick an NPC to see what SynthEBD remembers assigning to them. Their stored choices appear below, where you can remove them piece by piece.",
            technical: "Selects the NPC FormKey whose entry is looked up in the consistency dictionary; when one exists it is materialized into the assignment view below. NPCs without a stored entry display nothing.",
            motivation: "Troubleshooting one character starts here: inspect what was remembered, then surgically delete the parts you want re-rolled.");

        Add("Consistency.ClearCurrentNpc",
            layperson: "Deletes everything SynthEBD remembers about the currently selected NPC. Only this NPC gets fully re-rolled on the next run.",
            technical: "Removes the selected NPC's entry from the consistency dictionary entirely - all axes at once - unlike the X buttons in the panel, which clear individual stored items.",
            motivation: "The per-NPC reset: re-roll one character completely while everyone else keeps their remembered look.");

        Add("Consistency.AssetPack",
            layperson: "The main config file this NPC received last run. The X forgets it (together with its chosen subgroups) so the NPC's primary look is re-rolled next run.",
            technical: "NPCAssignment.AssetPackName, matched by name against the loaded primary asset packs on the next run; when it and the stored subgroups still pass current rules, the same combination is re-derived. The X empties the name and clears the stored subgroup list.",
            motivation: "Shows at a glance where an NPC's look came from and gives a one-click way to re-roll just their primary assets.");

        Add("Consistency.Subgroups",
            layperson: "The specific options (subgroups) chosen from that config file last run - which skin variant, which detail features, and so on. Remove a single entry with its X to re-roll just that slot.",
            technical: "NPCAssignment.SubgroupIDs, displayed with resolved name chains when the source config is loaded ('Not Loaded' otherwise). On the next run the asset selector tries to re-derive a combination containing these subgroup IDs if they are still valid under current rules.",
            motivation: "Lets you keep most of a remembered look while re-rolling one piece of it, such as only the face texture.");

        Add("Consistency.MixInAssignments",
            layperson: "The Mix-In config files (add-on looks like overlays, freckles, or body hair) this NPC received last run, with the options chosen from each.",
            technical: "NPCAssignment.MixInAssignments: one entry per mix-in pack holding its name, chosen SubgroupIDs, and a DeclinedAssignment flag. Each entry is consulted when that mix-in pack rolls for this NPC on the next run.",
            motivation: "Mix-ins roll separately from the primary config file, so their memory is stored - and cleared - separately too.");

        Add("Consistency.MixInName",
            layperson: "The name of this remembered Mix-In config file. The X forgets the whole Mix-In assignment so it re-rolls on the next run.",
            technical: "The MixInAssignment's AssetPackName; the asset selector matches it against the mix-in pack with the same group name during that pack's roll.",
            motivation: "Identifies which mix-in this memory block belongs to when several are assigned.");

        Add("Consistency.MixInSubgroups",
            layperson: "The options that were chosen from this Mix-In config file for this NPC last run.",
            technical: "The SubgroupIDs stored for this mix-in; on the next run the selector attempts to re-derive the same mix-in combination from them when still valid.",
            motivation: "Keeps add-on details, like a specific overlay pattern, stable across runs.");

        Add("Consistency.MixInDeclined",
            layperson: "Shows whether this NPC rolled 'no' for this Mix-In last time. When checked, the NPC stays without the Mix-In on future runs instead of re-rolling the odds every time.",
            technical: "The MixInAssignment's DeclinedAssignment flag (read-only here). Mix-ins carry a probability of skipping each NPC; the declined outcome is recorded so AssetSelector reuses it instead of re-rolling the probability. Delete the whole mix-in entry to make the NPC roll again.",
            motivation: "Without remembering the 'no' results, a low-probability mix-in would gradually spread to nearly every NPC as patcher runs accumulate.");

        Add("Consistency.AssetReplacers",
            layperson: "Remembered asset replacers - targeted swaps such as scars or dirt variants - that were assigned to this NPC last run.",
            technical: "NPCAssignment.AssetReplacerAssignments: each entry stores its source config file, replacer group name, and chosen subgroup IDs, consulted when replacers are assigned on the next run.",
            motivation: "Replacers cover distinctive details that should stay put once given - a signature scar should not wander between runs.");

        Add("Consistency.BodyGenMorphs",
            layperson: "The BodyGen body morphs this NPC received last run. Remove one with its X to re-roll it next run. Applies when body shapes are distributed via BodyGen.",
            technical: "NPCAssignment.BodyGenMorphNames. On the next run BodyGenSelector re-selects the stored morph combination when it is still available and rule-compliant, and otherwise rolls anew.",
            motivation: "Keeps body shapes persistent between runs; a changed body is one of the most noticeable mid-playthrough differences.");

        Add("Consistency.BodySlide",
            layperson: "The BodySlide preset this NPC received last run. The X forgets it so a new preset is rolled next run. Applies when body shapes are distributed via BodySlide.",
            technical: "NPCAssignment.BodySlidePreset, a preset label. OBodySelector re-picks the stored preset when it still exists and passes current rules; consistency presets are not reused while OBody's multiple-assignments mode is enabled.",
            motivation: "Keeps each NPC's silhouette stable across patcher re-runs instead of reshuffling bodies every time.");

        Add("Consistency.Height",
            layperson: "The height multiplier this NPC received last run. The X forgets it so a new height is rolled next run.",
            technical: "NPCAssignment.Height, a scale multiplier where 1 is race default. HeightPatcher reuses it only while it falls inside the currently configured range for the NPC's race and gender; out-of-range values are re-rolled.",
            motivation: "Keeps heights stable between runs while still honoring any height ranges you have since tightened.");
    }
}

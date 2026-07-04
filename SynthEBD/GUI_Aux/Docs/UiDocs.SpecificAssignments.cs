namespace SynthEBD;

// 3-part documentation for the Specific NPC Assignments menu and the per-NPC assignment editor.
public static partial class UiDocs
{
    private static void RegisterSpecificAssignments()
    {
        Add("SpecificAssignments.AddNew",
            layperson: "Creates a new forced-assignment entry. Pick an NPC inside it, then choose exactly which looks, body shape, height, or head parts that character must receive.",
            technical: "Adds a blank NPCAssignment placeholder to the list and opens its editor. Entries persist to Specific NPC Assignments.json (NPC Configuration folder) via the Save button, and the patcher consults them per NPC ahead of random selection.",
            motivation: "The entry point for hand-authoring a character's appearance while the rest of the load order stays randomized.");

        Add("SpecificAssignments.Remove",
            layperson: "Deletes this forced assignment. The NPC returns to normal randomized patching on the next run.",
            technical: "Removes the placeholder and its NPCAssignment model from the list; the removal is written to Specific NPC Assignments.json when you click Save.",
            motivation: "Forced choices should be easy to undo when you change your mind about a character.");

        Add("SpecificAssignments.ImportFromZEBD",
            layperson: "Loads forced NPC assignments created with zEBD, the older zEdit-based patcher, so hand-picked looks you set up there carry over. Imported entries are added to the current list; click Save to keep them.",
            technical: "Opens a file picker for zEBD's forced NPC list (ForceNPCList.json), converts each zEBD entry into a SynthEBD NPCAssignment, and appends the results to the list; entries whose NPC cannot be resolved are skipped and logged.",
            motivation: "Migrating users should not have to recreate hand-curated character setups from scratch.");

        Add("SpecificAssignments.NpcPicker",
            layperson: "Pick the NPC this entry applies to. The choices below (config files, body presets, morphs, head parts) then filter to match that character.",
            technical: "Sets NPCAssignment.NPCFormKey via the name-resolving picker. Changing it re-derives the NPC's gender and refreshes the available asset packs, BodySlides, BodyGen morphs, and head parts; at patch time the entry is matched to the NPC by this FormKey.",
            motivation: "Everything else in the entry hangs off one concrete NPC record, so that record is chosen first.");

        Add("SpecificAssignments.ForcedAssetPack",
            layperson: "The config file this NPC must draw their main look from. Anything you do not force below is still randomized within that config file.",
            technical: "Sets NPCAssignment.AssetPackName. During asset selection the named pack becomes the only primary candidate and supersedes any consistency pack; if it is missing or disabled at run time, the patcher logs the problem and falls back to a random pack.",
            motivation: "Guarantees a character's look comes from the intended source config - usually the first step of hand-crafting an NPC.");

        Add("SpecificAssignments.ForcedSubgroups",
            layperson: "Drag options from the Available tree to the Selected list to lock in specific parts of the look, such as one particular skin texture. Unforced slots are still rolled normally.",
            technical: "Fills NPCAssignment.SubgroupIDs. Forced subgroups are pinned into the chosen combination and bypass their own distribution-rule checks (allowed races, attributes, and so on), while the remaining slots are selected randomly and stay rule-checked.",
            motivation: "Pins the parts you care about while keeping some variety; deliberately overrides config rules because an explicit user choice should win.");

        Add("SpecificAssignments.ForcedAssetReplacers",
            layperson: "Forces specific asset replacers - targeted swaps such as scars or dirt variants - from the main config file onto this NPC.",
            technical: "Fills NPCAssignment.AssetReplacerAssignments: each entry names a replacer group from the forced primary pack plus its chosen subgroup IDs, consulted when replacers are assigned for this NPC.",
            motivation: "Replacer details are exactly the kind of distinctive touch, like a signature scar, that users want guaranteed on a particular character.");

        Add("SpecificAssignments.ForcedMixIns",
            layperson: "Forces chosen Mix-In config files (add-on looks like overlays or body hair) onto this NPC - or, with Decline checked, forbids one - instead of leaving it to chance.",
            technical: "Fills NPCAssignment.MixInAssignments. When the matching mix-in pack rolls for this NPC, a forced entry bypasses the probability skip, its subgroups can be forced as well, and DeclinedAssignment forces the opposite outcome (the mix-in is skipped outright).",
            motivation: "Mix-ins are probabilistic by design; specific characters often need a definite yes or no rather than odds.");

        Add("SpecificAssignments.ForcedMixInSubgroups",
            layperson: "Drag options from this Mix-In's tree to the Selected list to lock in exactly which of its looks the NPC gets.",
            technical: "Fills the SubgroupIDs of this MixInAssignment; like primary forced subgroups they are pinned into the mix-in's combination, while unforced slots roll normally.",
            motivation: "A mix-in usually offers many variants; forcing subgroups picks the exact one instead of merely guaranteeing the mix-in itself.");

        Add("SpecificAssignments.MixInAssetReplacers",
            layperson: "Forces asset replacers that belong to this Mix-In config file onto the NPC.",
            technical: "Fills this MixInAssignment's AssetReplacerAssignments - the same mechanism as the primary pack's forced replacers, scoped to the mix-in pack.",
            motivation: "Mix-in packs can carry their own replacer groups, so forcing them lives with the mix-in entry.");

        Add("SpecificAssignments.DistributionOrder",
            layperson: "The order in which config files (main look and Mix-Ins) are applied to this NPC, overriding the global order for this character only. Drag and drop to rearrange.",
            technical: "NPCAssignment.AssetOrder: a per-NPC copy of the assignment order (the 'Primary & Body Shape' slot plus each mix-in name). When present it replaces TexMeshSettings.AssetOrder in the per-NPC assignment loop.",
            motivation: "Order decides precedence when multiple configs touch the same texture slots; one character occasionally needs different layering than the global default.");

        Add("SpecificAssignments.SyncThisAssetOrder",
            layperson: "Resets this NPC's distribution order to match the main order from the Textures and Meshes menu.",
            technical: "Replaces this assignment's AssetOrder with the current contents of TexMeshSettings' assignment order and refreshes the ordering menu.",
            motivation: "Per-NPC orders drift as configs are added or renamed; one click re-aligns this entry with the global order.");

        Add("SpecificAssignments.SyncAllAssetOrders",
            layperson: "Resets the distribution order of EVERY entry in the Specific NPC Assignments list to match the main Textures and Meshes order.",
            technical: "Runs the same sync as the 'this' button for every assignment in the list, overwriting each entry's AssetOrder.",
            motivation: "After reorganizing configs globally, this refreshes all per-NPC orders at once instead of entry by entry.");

        Add("SpecificAssignments.ForcedHeight",
            layperson: "Locks this NPC's height. Enter a multiplier where 1 is the normal size for their race - for example 1.05 makes them five percent taller.",
            technical: "Sets NPCAssignment.Height (parsed as a float; blank means unforced). HeightPatcher uses the forced value directly, bypassing the height config's range and distribution and ignoring consistency; the Height module must still be enabled, and the setting that skips NPCs with non-default heights still applies.",
            motivation: "Some characters have canonical statures - a towering guard, a short thief - that randomization should never change.");

        Add("SpecificAssignments.ForcedBodySlide",
            layperson: "Locks which BodySlide preset this NPC's body uses. Shown when body shapes are distributed via BodySlide; the list only offers presets matching the NPC's sex.",
            technical: "Sets NPCAssignment.BodySlidePreset by preset label. OBodySelector picks the matching preset directly from the currently available pool; if the label is no longer among the available presets, the miss is logged and a preset is rolled normally.",
            motivation: "Guarantees a character's exact silhouette, for example matching a follower to their advertised body preset.");

        Add("SpecificAssignments.ForcedMorphs",
            layperson: "Locks BodyGen morphs on this NPC: drag morphs from Available to Selected. Shown when body shapes are distributed via BodyGen.",
            technical: "Fills NPCAssignment.BodyGenMorphNames. BodyGenSelector keeps only morph combinations that contain every forced morph name, while the remaining slots are still rolled within the config's combination rules.",
            motivation: "BodyGen builds bodies from combinable morphs; forcing specific ones anchors the parts of the shape you care about while the rest stays randomized.");
    }
}

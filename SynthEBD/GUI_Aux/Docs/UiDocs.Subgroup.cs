namespace SynthEBD;

// 3-part documentation for the per-subgroup editor (UC_Subgroup plus its hosted file-path and
// probability-modifier rows) and the Subgroup Linker dialog ("Subgroup.*" / "SubgroupLinker.*" keys).
public static partial class UiDocs
{
    private static void RegisterSubgroup()
    {
        // ===== Subgroup editor (UC_Subgroup) =====

        Add("Subgroup.ID",
            layperson: "A short unique identifier for this subgroup, like 'HD.C.1'. Logs and other subgroups refer to this subgroup by its ID, while the friendlier Name is what you mostly read in the editor.",
            technical: "The handle used by Required/Excluded Subgroup links, Specific NPC Assignments, the consistency file, and the assignment log. Editing the ID here automatically rewrites Required/Excluded references to the old ID throughout this config file, but references stored outside it (consistency, Specific NPC Assignments) keep the old string.",
            motivation: "Separating a compact machine-facing ID from the display Name keeps links and logs terse while names stay free to be long and readable.");

        Add("Subgroup.AutoGenerateID",
            layperson: "Builds this subgroup's ID automatically from its Name and its position in the tree, so you do not have to invent one.",
            technical: "Generates a dotted ID from the ancestor chain's existing ID segments plus an abbreviation of this subgroup's Name (word initials, numbers kept whole), then appends a counter if needed to stay unique within the config. Required/Excluded references to the old ID are updated automatically.",
            motivation: "Hand-writing hierarchical IDs is tedious and error-prone; generated IDs mirror the tree structure, which keeps logs and linker searches readable.");

        Add("Subgroup.AutoGenerateIDChildren",
            layperson: "Auto-generates IDs for every subgroup nested under this one, based on their Names. This subgroup's own ID is left unchanged.",
            technical: "Runs the dotted-ID generation recursively over all descendants while skipping this node itself, rebuilding each child ID from its ancestor segments and Name abbreviation with the same uniqueness enforcement and Required/Excluded reference updating as the single-ID generator.",
            motivation: "After renaming or reorganizing a branch, this refreshes the whole branch's IDs in one click instead of visiting every child.");

        Add("Subgroup.AutoGenerateIDAll",
            layperson: "Auto-generates IDs for every subgroup in this entire config file from their Names.",
            technical: "Clears and regenerates the dotted IDs of all subgroups in the asset pack from the top level down, with the same abbreviation, uniqueness, and Required/Excluded reference updating as the single-ID generator.",
            motivation: "The one-click way to bring a whole config to consistent, tree-derived IDs - typically after imports or renames leave mixed ID styles behind.");

        Add("Subgroup.Name",
            layperson: "The human-readable name for this subgroup, shown in the editor tree and in reports. Pick something descriptive like 'Dirty' or 'Freckled'.",
            technical: "Display text only - distribution logic keys off the ID, not the Name. The Name feeds the ID auto-generators (its word initials become ID segments) and is written alongside the ID in logs and the assignment log.",
            motivation: "Because the machine-facing identity lives in the ID, names can be renamed freely for clarity without breaking links between subgroups.");

        Add("Subgroup.BatchRename",
            layperson: "Shows the From/To fields for renaming many subgroups at once: every subgroup name in this config file containing the From text gets it replaced with the To text.",
            technical: "Toggles the inline bulk-rename controls; the actual replacement runs from the Apply button. Matching is a case-sensitive substring search over every subgroup Name in the asset pack.",
            motivation: "Config authors often need to fix a term that appears in dozens of subgroup names - a texture line's name, a typo - and renaming them one at a time does not scale.");

        Add("Subgroup.BatchRenameApply",
            layperson: "Performs the rename: every subgroup name in this config file containing the From text has it replaced with the To text, and a message reports how many changed.",
            technical: "Case-sensitive Contains/Replace over every subgroup Name in the asset pack. Each renamed subgroup also has its ID - and its descendants' IDs - regenerated from the new name, with Required/Excluded references updated. Blank From or To values are rejected.",
            motivation: "One action fixes a naming mistake everywhere it appears while keeping the generated IDs in sync with the new names.");

        Add("Subgroup.Notes",
            layperson: "Free-form notes about this subgroup - what the textures are, where they came from, or why its rules are set up the way they are. Purely informational.",
            technical: "Stored with the subgroup in the config json and displayed only here; the patcher never reads it.",
            motivation: "Distribution rules get intricate and configs get shared - notes let the author explain intent to future editors, including their future self.");

        Add("Subgroup.Enabled",
            layperson: "The master on/off switch for this subgroup. When off, this subgroup and everything nested under it are completely ignored by the patcher.",
            technical: "A disabled subgroup is skipped during config flattening before any of its rules are read, and its children are never visited - the whole branch is invisible to random distribution, ForceIf attributes, and even Specific NPC Assignments.",
            motivation: "Lets you park broken or unwanted content without deleting it; the definitions, rules, and links all survive for when it is switched back on.");

        Add("Subgroup.DistributionEnabled",
            layperson: "Whether ordinary, randomly patched NPCs can receive this subgroup. When off, only NPCs you target directly - through Specific NPC Assignments or this subgroup's ForceIf attributes - will get it.",
            technical: "During validation a subgroup with this off is rejected unless the NPC is specifically assigned to it or matched at least one of its ForceIf attributes - a ForceIf match overrides the flag. A parent with this off forces it off for all descendants during flattening.",
            motivation: "Lets a shared config carry character-specific looks - like a follower's signature skin - without those assets leaking into general randomization.");

        Add("Subgroup.AllowUnique",
            layperson: "Whether NPCs flagged as Unique - one-of-a-kind named characters such as followers and quest-givers - can receive this subgroup.",
            technical: "Checks the Unique flag in the NPC record's configuration: when this is off, any Unique-flagged NPC fails validation for this subgroup. Specific NPC Assignments bypass the check, and a parent with this off forces it off for descendants.",
            motivation: "Important characters are where a bad match is most noticeable; this keeps mass-distribution content off them, or - with the opposite toggle - reserves special content for them.");

        Add("Subgroup.AllowNonUnique",
            layperson: "Whether ordinary, non-unique NPCs - generic guards, bandits, and other repeatable spawns - can receive this subgroup.",
            technical: "The mirror of Allow Unique NPCs: when off, any NPC without the Unique configuration flag fails validation for this subgroup. Specific NPC Assignments bypass the check, and a parent with this off forces it off for descendants.",
            motivation: "Distinctive looks lose their impact when every third bandit wears them; excluding non-unique NPCs reserves a look for named characters.");

        Add("Subgroup.ProbabilityWeighting",
            layperson: "How likely this subgroup is to be picked compared to its alternatives - weighting 2 is drawn about twice as often as weighting 1. It is a relative weight, not a percentage.",
            technical: "Used as this subgroup's weight in the weighted-random draw that fills its position in a combination (and in seed-subgroup selection), after multiplication by any matching Probability Modifiers. During flattening a child's weighting is multiplied by its ancestors', so nested weightings compound.",
            motivation: "Makes common variants common and rare variants rare without duplicating subgroups or writing extra rules.");

        Add("Subgroup.AllowedRaces",
            layperson: "The races that may receive this subgroup. Leave the list empty to allow every race (other rules still apply).",
            technical: "If non-empty, the NPC's asset race (after any race aliasing) must be in the list or the subgroup fails validation. During flattening the list is intersected with ancestors' allowed races and disallowed races are subtracted; a subgroup whose result comes up empty is pruned along with its entire branch.",
            motivation: "Skin textures are made for specific heads and bodies; race gating is the primary guarantee they land where they fit.");

        Add("Subgroup.AllowedRaceGroupings",
            layperson: "Named bundles of races, like 'Humanoid Playable', that may receive this subgroup - one checkbox instead of picking each race by hand.",
            technical: "Checked groupings' member races are merged into the allowed-race set when the config is flattened. Labels resolve against the effective grouping set: the config file's local definitions merged with General Settings ones, with a General definition superseding a same-label local one when the overwrite toggle is on.",
            motivation: "Groupings centralize race lists so one change propagates to every rule using the label - and configs ship local copies so they keep working for users who never defined those groupings.");

        Add("Subgroup.DisallowedRaces",
            layperson: "Races that must never receive this subgroup, even if an allowed grouping would otherwise include them.",
            technical: "An NPC whose asset race is in this list fails validation outright. During flattening disallowed races accumulate down the tree and are subtracted from the allowed set, so a disallow always beats an allow.",
            motivation: "Lets you carve exceptions out of broad allowances - allow a whole grouping, then knock out the one race the textures do not suit.");

        Add("Subgroup.DisallowedRaceGroupings",
            layperson: "Named bundles of races that must never receive this subgroup.",
            technical: "Checked groupings' member races are merged into the disallowed-race set at flatten time, using the same label resolution as the allowed side, and are then subtracted from any allowed races.",
            motivation: "Excludes a maintained family of races - for example all beast races - in one click, consistently with every other rule that references the grouping.");

        Add("Subgroup.AllowedAttributes",
            layperson: "Conditions an NPC must meet to receive this subgroup - being in a faction or class, having a certain name, coming from a certain plugin, and so on. If any are listed, the NPC must satisfy at least one.",
            technical: "Each attribute is a group of sub-attributes that must all match (AND); separate attributes are alternatives (OR). Attributes marked ForceIf also steer selection: candidates with the highest matched ForceIf weight are preferred before random weighting applies, and a ForceIf match overrides a disabled 'Distribute to non-forced NPCs' flag. Group-type sub-attributes resolve against this config file's local Attribute Groups, which are synced from General Settings at load per the overwrite toggle.",
            motivation: "Race alone rarely identifies the right NPCs; attributes let looks follow factions, classes, or any record property - and ForceIf turns the same filter into a targeting tool.");

        Add("Subgroup.DisallowedAttributes",
            layperson: "Conditions that disqualify an NPC: anyone matching any of these never receives this subgroup.",
            technical: "Evaluated with the same matcher as Allowed Attributes; if the NPC matches any entry the subgroup is rejected. Disallowed attributes have no ForceIf role - they only veto.",
            motivation: "Keeps particular NPCs, factions, or entire mods' characters out of a look without enumerating everything that is allowed.");

        Add("Subgroup.WeightRange",
            layperson: "The range of NPC Weight values - the 0 to 100 thin-to-heavy slider on every NPC record - that can receive this subgroup, inclusive at both ends.",
            technical: "NPCs whose record Weight falls outside Lower..Upper fail validation for this subgroup. During flattening the range narrows to the intersection of this subgroup's and its ancestors' ranges.",
            motivation: "Some textures only read correctly on certain builds - a defined-muscle normal map looks painted on at weight 0 - so they can be limited to the weights they were made for.");

        Add("Subgroup.RequiredSubgroups",
            layperson: "Other subgroups that must be part of any look that includes this one - drag them here from the tree. Several entries from the same top-level group act as alternatives: any one of them satisfies the requirement.",
            technical: "Stored as IDs grouped by top-level position. When this subgroup enters a combination, the candidates at each constrained position are trimmed to the required set (requiring a non-bottom subgroup accepts any of its descendants); if a position cannot be satisfied, the combination is rejected and the generator backtracks. Requirements inherit from parent subgroups, with a child's more specific entries superseding an ancestor's umbrella entries.",
            motivation: "Keeps matched pieces together - a torso texture that only lines up with its own hand and head textures can enforce the pairing instead of hoping the dice cooperate.");

        Add("Subgroup.ExcludedSubgroups",
            layperson: "Other subgroups that may never appear in the same look as this one - drag them here from the tree.",
            technical: "Stored as IDs grouped by top-level position. When this subgroup enters a combination, candidates whose chain contains an excluded ID are removed from their position (excluding a parent excludes all its descendants); if that empties a position, this subgroup cannot be used for the NPC. Exclusions accumulate from parents and are also trimmed out of inherited required sets.",
            motivation: "Prevents visually clashing pairings - for example a heavily scarred face option combining with a pristine body texture.");

        Add("Subgroup.AddKeywords",
            layperson: "Keyword names stamped onto any NPC who receives this subgroup, so other mods can recognize the assigned look.",
            technical: "Each string becomes a Keyword record - created once in the output plugin and reused - added to the patched NPC's Keywords list when a combination containing this subgroup is applied. Keywords inherit from parent subgroups.",
            motivation: "Gives downstream tools a hook: keyword-based distributors or scripts can match outfits, effects, or dialogue to the look SynthEBD chose.");

        Add("Subgroup.AssetPaths",
            layperson: "The actual files this subgroup delivers. Each row points a texture or mesh in your Data folder at the place on the NPC where it belongs (like 'Head Diffuse'); a green border means the entry checks out, red means something was not found.",
            technical: "Each row maps a Data-relative source file to a destination record path, validated by resolving the path against a reference NPC (from the config's record templates or an explicit reference). At patch time the generated records' string at the destination is replaced with the source file. Children inherit their ancestors' paths and add their own.",
            motivation: "This is the payload - every other setting on this page only decides who receives these files. Spreading paths across the tree lets one combination assemble a complete look from independent pieces.");

        Add("Subgroup.AllowedBodyGenDescriptors",
            layperson: "When body shapes come from BodyGen, only morphs tagged with these descriptors (like 'Build: Athletic') may be paired with this subgroup.",
            technical: "Compared against the descriptors annotated on BodyGen morphs: the primary combination constrains the subsequent morph selection, and a mix-in subgroup assigned after a body exists is rejected if the assigned morph fails its descriptors. The match mode chooses whether a morph must carry all listed descriptors or any one of them.",
            motivation: "Skin textures are often shape-specific; descriptors give assets and body shapes a shared vocabulary so muscular textures land on muscular morphs.");

        Add("Subgroup.DisallowedBodyGenDescriptors",
            layperson: "BodyGen morphs tagged with any of these descriptors will not be paired with this subgroup.",
            technical: "The veto side of the BodyGen pairing gate: a morph matching these descriptors (per the match mode, default any-of) is excluded from pairing, and an already-assigned matching morph rejects this subgroup during mix-in validation. Disallowed descriptors merge down the tree and are trimmed from the allowed set.",
            motivation: "Blocking the few shapes a texture cannot survive is often easier than listing every shape it can.");

        Add("Subgroup.AllowedBodySlideDescriptors",
            layperson: "When body shapes come from BodySlide (OBody/AutoBody), only presets tagged with these descriptors may be paired with this subgroup.",
            technical: "Compared against the descriptors annotated on BodySlide presets, evaluated at the NPC's weight (annotations can differ between a preset's low- and high-weight forms). The primary combination constrains the preset choice, and a mix-in subgroup assigned after a preset exists is rejected if that preset fails the check. The match mode selects all-of versus any-of.",
            motivation: "The same shape-consistency insurance as the BodyGen version, for setups that distribute BodySlide presets instead.");

        Add("Subgroup.DisallowedBodySlideDescriptors",
            layperson: "BodySlide presets tagged with any of these descriptors will not be paired with this subgroup.",
            technical: "The veto side of the BodySlide pairing gate, evaluated at the NPC's weight like the allowed side. Disallowed descriptors merge down the tree and are trimmed from the allowed set; a merge that leaves nothing allowed prunes the subgroup branch.",
            motivation: "Quickly fences off the handful of preset styles a texture cannot survive - typically extreme or novelty silhouettes.");

        Add("Subgroup.PreferredBodySlideDescriptors",
            layperson: "A soft ranking of the body styles this texture looks best on. Unlike Allowed/Disallowed, it never blocks anything - it only nudges the BodySlide choice toward your top preferences.",
            technical: "Each descriptor carries a priority. After assets are chosen, the BodySlide candidate list is narrowed one descriptor at a time in descending priority (identical descriptors' priorities sum across the combination's subgroups), skipping any step that would leave zero presets. Priorities inherit down the tree with same-descriptor values summed.",
            motivation: "A middle ground between 'must match' and 'anything goes': textures get their ideal shapes when available without ever emptying the preset pool.");

        Add("Subgroup.ProbabilityModifiers",
            layperson: "Fine-tunes this subgroup's odds for particular NPCs: each row pairs a condition with a multiplier that applies when the NPC matches - for example x3 for Nords or x0.25 for bandits.",
            technical: "During weighted-random selection, this subgroup's Probability Weighting is multiplied by the factor of every row whose attribute condition the NPC matches; multiple matching rows multiply together, and rows with blank conditions are ignored. Rows inherit from parent subgroups and from the whole-config distribution rules as independent multiplicative terms.",
            motivation: "Plain weighting treats every NPC the same; modifiers let one subgroup be common on some NPCs and rare on others without cloning it into race- or faction-specific copies.");

        // ===== Hosted rows (UC_AttributeWeightModifier / UC_FilePathReplacement) =====

        Add("Subgroup.ProbabilityModifierFactor",
            layperson: "The multiplier applied to this subgroup's selection odds when an NPC matches the condition on the left. 1 leaves the odds unchanged, higher values boost them, values below 1 suppress them.",
            technical: "When the row's attribute condition matches, the subgroup's effective weight in the random draw is multiplied by this value; factors from multiple matching rows multiply together. A factor of 0 effectively removes the subgroup from the draw for matching NPCs, though ForceIf matches and Specific NPC Assignments still select it by other means.",
            motivation: "One number expresses how strongly the condition should sway distribution - from a gentle nudge to a hard steer - without touching the base weighting every other NPC uses.");

        Add("Subgroup.PathEditDestination",
            layperson: "Switches this row between the friendly destination summary (like 'Torso Diffuse (Male)') and the raw destination path, so you can type or fix a custom destination.",
            technical: "Toggles between the read-only abstract caption and the editable record-path textbox with autocomplete suggestions. The destination is a dotted record path walked from the reference NPC; the border turns green when it resolves to a real texture or model string on that NPC.",
            motivation: "The canned destination menu only covers the standard skin slots; the raw view lets advanced configs target any string field a record template exposes.");

        // ===== Subgroup Linker dialog (Window_SubgroupLinker) =====

        Add("SubgroupLinker.LinkToThese",
            layperson: "Makes THIS subgroup require the checked subgroups in the list: whenever it is assigned, they (or an alternative at the same slot) must be part of the look. Applies immediately and closes the window.",
            technical: "Adds each checked match to this subgroup's Required Subgroups, grouped by top-level position (entries sharing a position act as OR-alternatives). One-directional: the checked subgroups' own requirement lists are untouched.",
            motivation: "The bulk equivalent of dragging matches into the Required list one by one - the point of this dialog for configs with many matched variants.");

        Add("SubgroupLinker.LinkTheseTo",
            layperson: "Makes each checked subgroup require THIS one: whenever any of them is assigned, this subgroup must be in the look too. Applies immediately and closes the window.",
            technical: "Appends this subgroup's ID to each checked subgroup's Required Subgroups list. One-directional: this subgroup's own requirements are unchanged.",
            motivation: "Lets a subgroup declare 'everything matching this search needs me' without opening each of those subgroups.");

        Add("SubgroupLinker.LinkReciprocally",
            layperson: "Links both ways between this subgroup and each checked one, so each requires the other. Applies immediately and closes the window.",
            technical: "Runs the Link These To and Link To These operations back to back, creating mutual required links between this subgroup and every checked match. The checked subgroups are not linked to each other.",
            motivation: "Two-way pairing in one click for pieces that must always travel together, like a head texture and its matching body.");

        Add("SubgroupLinker.LinkWholeGroup",
            layperson: "Links everything to everything: this subgroup and all checked ones each end up requiring all the others. Applies immediately and closes the window.",
            technical: "Cross-links the whole set (this subgroup plus every checked match) so each member requires every other member, skipping pairs that share a top-level position - only one subgroup per position can be assigned, so such links would be unsatisfiable.",
            motivation: "Builds a complete matched set - the head, torso, hands, and feet entries of one skin line - into an all-or-nothing bundle in a single action.");

        Add("SubgroupLinker.UnlinkFromThese",
            layperson: "Removes the checked subgroups from THIS subgroup's Required list. Applies immediately and closes the window.",
            technical: "Deletes each checked match from this subgroup's Required Subgroups collection - the inverse of Link To These. The checked subgroups' own lists are untouched.",
            motivation: "Bulk cleanup when requirements were over-applied or a partner subgroup should no longer be mandatory.");

        Add("SubgroupLinker.UnlinkTheseFrom",
            layperson: "Removes THIS subgroup from each checked subgroup's Required list. Applies immediately and closes the window.",
            technical: "Strips this subgroup's ID from every checked match's Required Subgroups list - the inverse of Link These To.",
            motivation: "Withdraws a subgroup from many inbound requirements at once, for example when it stops being the mandatory partner of its matches.");

        Add("SubgroupLinker.UnlinkReciprocally",
            layperson: "Removes the links in both directions between this subgroup and each checked one. Applies immediately and closes the window.",
            technical: "Runs Unlink From These and Unlink These From together, dissolving mutual required links between this subgroup and the checked matches.",
            motivation: "Undoes a reciprocal pairing as one action instead of two.");

        Add("SubgroupLinker.UnlinkWholeGroup",
            layperson: "Removes every required link among this subgroup and the checked ones - the full teardown of a linked set. Applies immediately and closes the window.",
            technical: "For every pair within the set (this subgroup plus the checked matches), removes each member from the other's Required Subgroups - the inverse of Link Whole Group.",
            motivation: "Dissolves an all-or-nothing bundle in one click when a matched set is being reorganized.");

        Add("SubgroupLinker.SetAsAlternateRequired",
            layperson: "Registers this subgroup as an accepted alternative anywhere other subgroups in this config already require something from its top-level group. Run it after adding a new variant so existing links accept the newcomer too.",
            technical: "Scans every subgroup in the config: any that already requires a subgroup at this subgroup's top-level position gains this subgroup as an additional entry there (entries at one position are OR-alternatives). Descendants of this subgroup's own required chain are skipped, as are neighbors when Exclude Neighbors is checked.",
            motivation: "In heavily cross-linked configs, adding one new option would otherwise mean hand-editing every subgroup that references its siblings; this automates the fan-out.");

        Add("SubgroupLinker.IncludeChainedRequired",
            layperson: "Also registers each subgroup this one requires as an alternative within its own top-level group, not just this subgroup itself.",
            technical: "Recursively repeats the alternate-registration for every member of this subgroup's required-subgroup chain at that member's own top-level position, tracking processed members to avoid infinite loops.",
            motivation: "A new variant usually brings its own required partners; chaining registers the whole bundle in one pass instead of one run per member.");

        Add("SubgroupLinker.ExcludeNeighbors",
            layperson: "Skips the sibling branches next to this subgroup (and next to its required chain) when registering alternatives, so its direct competitors are not told to require it.",
            technical: "During alternate-registration, any subgroup whose ancestry includes the parent of a required-chain member - that member's siblings and their subtrees - is skipped.",
            motivation: "Alternatives at the same spot in the tree are usually mutually exclusive competitors; without this filter they would end up requiring the very subgroup they compete against.");

        Add("SubgroupLinker.UnlinkAllFromThis",
            layperson: "Removes this subgroup from the Required lists of every other subgroup in the whole config file. The search filter above is ignored.",
            technical: "Walks all subgroups in the asset pack and deletes this subgroup's ID from each one's Required Subgroups, regardless of the current matches or checkbox states.",
            motivation: "The escape hatch when a subgroup was over-linked or is being retired - hunting down every inbound reference by hand would mean opening the entire tree.");

        Add("SubgroupLinker.Close",
            layperson: "Closes the dialog without making any changes.",
            technical: "Simply closes the window; no link or unlink command runs. Note that the action buttons apply their changes the moment they are clicked - there is no separate confirm step for this button to cancel.",
            motivation: "Because every action commits immediately, Close is the only guaranteed no-op exit from the dialog.");
    }
}

namespace SynthEBD;

// 3-part documentation for the config-file (asset pack) editor internals: the top-level editor
// chrome and toolbar (UC_AssetPack), the whole-config distribution rules, the record template
// selector, the asset replacer menus, the misc menu, and the asset presenter's preview controls.
public static partial class UiDocs
{
    private static void RegisterAssetPackEditor()
    {
        // ===== Config file editor: identity and metadata (UC_AssetPack) =====

        Add("AssetPack.Name",
            layperson: "The display name of this config file. It identifies the config everywhere else in SynthEBD, such as the enabled-configs list and Specific NPC Assignments.",
            technical: "Mirrors AssetPack.GroupName, the config's identity string: TexMeshSettings.SelectedAssetPacks records enabled configs by it, Specific NPC Assignments and consistency entries reference assigned packs by it, and the distribution order lists Mix-Ins by it.",
            motivation: "Because other settings reference configs by name, renaming one silently orphans those references - pick a stable name early and rename knowingly.");

        Add("AssetPack.Prefix",
            layperson: "A short abbreviation for this config, stamped into the ID of every record SynthEBD generates from it. Keep it short and unique among your configs.",
            technical: "Mirrors AssetPack.ShortName. Generated texture sets and head parts get EditorIDs suffixed with this prefix plus the subgroup combination signature (HardcodedRecordGenerator), the enabled-configs readout displays it, and the validator requires it when the config contains asset replacer groups. The config drafter fills it in from the selected mod archive's prefix.",
            motivation: "The prefix keeps generated records traceable to their source config in xEdit and prevents EditorID collisions between configs - especially for replacers, which clone shared vanilla records.");

        Add("AssetPack.Gender",
            layperson: "Which gender of NPC this config file applies to. Male configs are only ever considered for male NPCs, and female configs for female NPCs.",
            technical: "Mirrors AssetPack.Gender. At patch time the flattened configs are split into male and female Primary/Mix-In pools and each NPC only draws from the pools matching its own gender; in the editor the setting also filters the available BodyGen configs and swaps in gender-appropriate default record templates.",
            motivation: "Skin textures and record templates are inherently gendered - a config built on female meshes and texture layouts would look broken on men, so the split is enforced up front.");

        Add("AssetPack.Type",
            layperson: "Whether this is a Primary config (competes to provide an NPC's main look) or a Mix-In (an add-on layer rolled separately on top). An NPC gets at most one Primary combination but can receive several Mix-Ins.",
            technical: "Mirrors AssetPack.ConfigType. Primaries compete in the shared Primary slot of the distribution order, weighted by their Distribution Probability Weighting; each Mix-In occupies its own slot and rolls an independent percent inclusion chance per NPC, assigning a partial combination layered onto whatever was assigned before it.",
            motivation: "The split lets full skin overhauls coexist with small optional layers like freckles, dirt, or tattoos, without every overlay having to be a complete standalone skin.");

        Add("AssetPack.AssociatedBodyGenConfig",
            layperson: "The BodyGen configuration file whose morphs should be paired with this config's textures. Only relevant when Body Shape Selection is set to BodyGen.",
            technical: "Stored as AssetPack.AssociatedBodyGenConfigName (the BodyGen config's label). When an NPC's primary combination comes from this pack, BodyGenSelector draws candidate morphs from the matching-gender BodyGen config with that label, so the pack's BodyGen descriptor rules are evaluated against that config's descriptor catalog.",
            motivation: "Ties assets to a compatible morph set: a skin designed around a particular body distribution stays paired with the morphs it was drawn for, and both files are guaranteed to use the same descriptor names.");

        // ===== Config file editor: toolbar commands (UC_AssetPack) =====

        Add("AssetPack.Validate",
            layperson: "Checks this config file for problems - missing texture files, broken descriptor or race-grouping references, a missing record template - and reports anything it finds.",
            technical: "Dumps the current (unsaved) editor state plus the BodyGen and OBody menus to models, then runs AssetPackValidator: name/prefix presence, default record template set, associated BodyGen config resolvable, duplicate subgroup IDs, source files present (searching loose files and the config's associated BSAs), descriptor and race-grouping references, and race coverage. Errors go to the log display; success shows a confirmation popup.",
            motivation: "Catches authoring mistakes immediately and per-config, rather than discovering them mid-patch or in the noise of the all-configs validator.");

        Add("AssetPack.Save",
            layperson: "Saves this config file's current state to disk.",
            technical: "Dumps the view model to an AssetPack model and writes it as JSON via SettingsIO_AssetPack, updating the config's source path (a new config gets a file in the asset pack settings folder). A status notification reports success or failure.",
            motivation: "Explicit saving checkpoints your work: together with Discard Changes it lets you experiment freely and roll back to the last known-good version.");

        Add("AssetPack.DiscardChanges",
            layperson: "Throws away your unsaved edits and reloads this config file from its last saved version on disk.",
            technical: "Re-runs SettingsIO_AssetPack.LoadAssetPack on the config's source path and rebuilds the view model from the loaded model, replacing subgroups, distribution rules, replacers, record templates, and metadata wholesale. Logs an error and aborts if the file cannot be parsed.",
            motivation: "The config editor's undo mechanism - it is what makes destructive tools like Auto-Set Destination Paths (which offers to save first for exactly this reason) recoverable.");

        Add("AssetPack.Duplicate",
            layperson: "Creates a copy of this config as a new config file named '<name> (2)' and opens it in the editor.",
            technical: "Dumps the current view model to a model, appends ' (2)' to its name, clears its file path so the first save writes a new file, then adds the copy to the config list and displays it in the primary editor pane.",
            motivation: "A quick starting point for variants - fork a working config and change textures or rules without risking the original.");

        Add("AssetPack.MergeWith",
            layperson: "Imports the subgroups of another config file into this one. Useful for combining a config and its add-on into a single file.",
            technical: "Prompts for another config JSON, loads it, and merges by subgroup ID: top-level subgroups with IDs this config lacks are cloned in whole, while subgroups sharing an ID recurse and merge their children the same way. A popup lists every imported subgroup; config-level settings other than subgroups are not merged.",
            motivation: "Combining related configs by hand would mean recreating whole subgroup trees; ID-based merging pulls in exactly the parts that are new.");

        Add("AssetPack.AutoSetDestinationPaths",
            layperson: "Automatically fills in each asset's destination based on its file name, for every file name SynthEBD recognizes (like malebody_1.dds). It offers to save first so you can undo with Discard Changes.",
            technical: "Walks every subgroup path whose source file name appears in FilePathDestinationMap and assigns the canonical record destination; if that destination is already taken in the subgroup, torso textures fall back to the equivalent Feet or Tail destination when it resolves on a reference record template. Afterward torso textures are duplicated to Feet (when the config assigns none anywhere) and to beast Tails, and all changes are reported.",
            motivation: "Destination paths are long record-path expressions that are tedious and error-prone to type; recognized vanilla-style file names make them fully inferable.");

        Add("AssetPack.ListDisabledSubgroups",
            layperson: "Shows a list of every subgroup in this config that is currently switched off, meaning it can never be assigned to anyone.",
            technical: "Recursively collects each subgroup (at any depth) whose Enabled flag is false and shows their IDs and name paths in a popup. Enabled-off subgroups are skipped entirely at flattening - unlike 'Distribution Enabled' off, which still allows assignment via ForceIf attributes or Specific NPC Assignments.",
            motivation: "Disabled subgroups are easy to lose track of in a large tree; this answers 'why does that texture never appear' at a glance.");

        Add("AssetPack.ListDistributionRules",
            layperson: "Shows a readable summary of every custom distribution rule in this config - the whole-config rules plus any rules set on individual subgroups and asset replacers.",
            technical: "Aggregates the rules summaries into a popup: whole-config rules (allowed/disallowed races, race groupings, attributes, unique/generic gates, probability weighting, weight range) followed by per-subgroup and per-replacer-subgroup summaries. Rules left at their defaults are omitted.",
            motivation: "Distribution surprises usually trace back to a rule buried somewhere in the subgroup tree; a flat listing beats expanding every node to hunt for it.");

        Add("AssetPack.DraftFromMod",
            layperson: "Builds config content automatically by scanning a texture mod's files, creating subgroups from the textures it finds so you do not have to add them by hand. The recommended starting point when turning an installed skin mod into a config.",
            technical: "Opens the Config Drafter initialized to this config: it scans the selected texture directories or mod archives, categorizes the found files into a generated subgroup tree, and can fill in the prefix. If it detects 3BA/BHUNP 'etc' textures or TNG textures, it also applies the matching bundled record templates and additional-races paths automatically.",
            motivation: "Hand-building subgroups for a large skin mod means hundreds of path entries; drafting turns an installed mod into a working skeleton config in minutes.");

        Add("AssetPack.UpdateFromMod",
            layperson: "Repoints this config's asset paths at a new version of its source mod - for example after the mod updated and renamed or moved its files.",
            technical: "Opens the Config Path Remapper: it matches the config's referenced files to the new file set first by MD5 hash and then by path similarity, and surfaces missing source files, unmatched new files, and deprecated paths in sub-menus for confirmation before the changes are written back.",
            motivation: "Mod updates that reorganize files would otherwise break every affected path; hash matching re-links files even when their names changed completely.");

        Add("AssetPack.RemoveReplicateAssets",
            layperson: "Finds textures in this config that are exact copies of each other and collapses them down to one file, so the game loads fewer duplicate textures and wastes less VRAM.",
            technical: "Opens the replicate-texture remover: it hashes the config's textures to find identical files, then remaps duplicate paths onto a single surviving file or removes wholly-replicate subgroups, fixing up Required/Excluded subgroup references as it goes.",
            motivation: "Config authors often ship the same texture under several variant folders; every duplicate the game loads costs VRAM for zero visual difference.");

        // ===== Misc menu (UC_AssetPackMiscMenu) =====

        Add("AssetPack.SetAllAllowedDescriptorModes",
            layperson: "Applies the match mode picked in the dropdown to every 'allowed' body shape descriptor rule in this config, across all subgroups at once.",
            technical: "Sets the allowed BodyGen and BodySlide descriptor match modes on the displayed subgroup and recursively on every subgroup and replacer subgroup in the pack (the whole-config rules are not touched). Modes: Any (a single overlapping descriptor suffices), All (every descriptor category in the rule must be satisfied), Shared (only categories present on both sides must agree).",
            motivation: "Match mode decides how strict descriptor pairing is, and configs are commonly authored with it inconsistent across subgroups; a bulk setter beats visiting each one.");

        Add("AssetPack.SetAllDisallowedDescriptorModes",
            layperson: "Applies the match mode picked in the dropdown to every 'disallowed' body shape descriptor rule in this config, across all subgroups at once.",
            technical: "Sets the disallowed BodyGen and BodySlide descriptor match modes on the displayed subgroup and recursively on every subgroup and replacer subgroup (whole-config rules are untouched). For disallowed rules the conventional mode is Any, where one matching descriptor is enough to reject a pairing.",
            motivation: "Allowed and disallowed rules usually want different strictness, so each side gets its own bulk setter.");

        Add("AssetPack.DeleteMissingDescriptors",
            layperson: "Removes body shape descriptor rules that refer to descriptors which no longer exist in your OBody settings or in this config's associated BodyGen configuration.",
            technical: "Recursively prunes each subgroup's allowed and disallowed BodyGen descriptors that are absent from the associated BodyGen config's template descriptors, and BodySlide descriptors absent from the OBody settings' descriptor menu, then reloads the displayed subgroup.",
            motivation: "Stale references accumulate when descriptor catalogs are renamed or trimmed, and they surface as validation errors; this clears them in one pass instead of one hand edit at a time.");

        Add("AssetPack.AddMixInToSpecificAssignments",
            layperson: "Adds this Mix-In config to every one of your Specific NPC Assignments (for NPCs of the matching gender) in one step, using the options to the right.",
            technical: "For each Specific NPC Assignment whose NPC matches this config's gender: if the assignment has no forced Mix-In entry for this config, one is added with the chosen As Declined state; if an entry already exists, its state is only overwritten when Override Existing is checked. Shown only for Mix-In type configs.",
            motivation: "Forcing (or blocking) a Mix-In across an entire hand-curated NPC list would otherwise mean editing every assignment individually.");

        Add("AssetPack.MixInAsDeclined",
            layperson: "When checked, the entries added to your Specific NPC Assignments mark this Mix-In as declined - meaning those NPCs will specifically NOT receive it.",
            technical: "Sets DeclinedAssignment on the created (or overridden) Mix-In assignment entries. At patch time a declined specific Mix-In assignment rejects the Mix-In for that NPC before the random inclusion roll is ever made.",
            motivation: "Declining per-NPC is the reliable way to exempt curated NPCs from an overlay you otherwise want distributed randomly.");

        Add("AssetPack.MixInOverrideExisting",
            layperson: "If some of your Specific NPC Assignments already reference this Mix-In, update those entries to the As Declined setting chosen here; otherwise existing entries are left untouched.",
            technical: "When the bulk-add encounters an assignment that already contains a Mix-In entry for this config, it overwrites that entry's DeclinedAssignment with the As Declined value only if this box is checked. Newly created entries always use the chosen value regardless.",
            motivation: "Protects hand-tuned per-NPC decisions from being steamrolled by the bulk operation unless you explicitly ask for that.");

        Add("AssetPack.GetBsaAssetsFrom",
            layperson: "Tells SynthEBD which mods' BSA archives may contain this config's texture and mesh files, so files packed inside archives are recognized instead of being reported as missing.",
            technical: "Mirrors AssetPack.AssociatedBsaModKeys. Source-file existence checks - the path status colors in the subgroup editor and the validator's missing-file check - search the BSAs linked to these mods in addition to loose files. Patching itself is unaffected: records store data-relative paths that the game resolves from BSAs at runtime.",
            motivation: "Some asset mods ship BSA-packed; without naming their archives here, a perfectly working config would fail validation and show every path as missing.");

        // ===== Whole-config distribution rules (UC_ConfigDistributionRules) =====

        Add("AssetPack.AllowedBodyGenDescriptors",
            layperson: "Body shape descriptors that a BodyGen morph must have for it to be paired with this config file. Applies to the whole config, on top of any per-subgroup descriptor rules.",
            technical: "Whole-config rules are wrapped in a pseudo-subgroup at flattening, so these descriptors intersect into every subgroup's own allowed set and also gate the entire pack: a morph assigned before assets invalidates any pack whose descriptors do not match it (per the match mode), and a morph chosen after assets is filtered against the assigned combination's inherited descriptors.",
            motivation: "Lets a config declare body-shape compatibility once - for example a skin drawn for heavy bodies pairing only with morphs annotated as such - without repeating the rule in every subgroup.");

        Add("AssetPack.DisallowedBodyGenDescriptors",
            layperson: "Body shape descriptors that a BodyGen morph must NOT have if it is to be paired with this config file. The inverse of the allowed list.",
            technical: "Union-merged into every subgroup's disallowed set at flattening and evaluated at the whole-pack gate: a morph whose annotations match these per the match mode (default Any - one hit rejects) invalidates the pairing in either selection direction, and matching entries are trimmed out of the allowed set.",
            motivation: "Excluding a few incompatible shapes is often easier than enumerating every acceptable one - such as blocking pregnancy morphs for a skin whose torso detail would stretch badly.");

        Add("AssetPack.AllowedBodySlideDescriptors",
            layperson: "Body shape descriptors that a BodySlide preset must have for it to be paired with this config file. Applies to the whole config, on top of any per-subgroup descriptor rules.",
            technical: "Same machinery as the BodyGen variant, but matched against the descriptors annotated on BodySlide presets in the OBody menu, evaluated at the NPC's weight (a preset's descriptors can differ between its low- and high-weight forms). Intersected into every subgroup at flattening and enforced both when validating the pack against an already-assigned preset and when filtering candidate presets after assets are chosen.",
            motivation: "Keeps a skin and its assigned BodySlide coherent config-wide - the classic case is pairing a UNP-family texture set only with UNP-derived presets.");

        Add("AssetPack.DisallowedBodySlideDescriptors",
            layperson: "Body shape descriptors that a BodySlide preset must NOT have if it is to be paired with this config file.",
            technical: "Union-merged into every subgroup's disallowed set at flattening and checked at the whole-pack gate: if the preset's descriptors at the NPC's weight match these per the match mode (default Any), the pack or combination is rejected, and matching entries are trimmed from the allowed set.",
            motivation: "A short exclusion list often expresses compatibility better than a long allowed list - block the few known-bad preset families and accept everything else.");

        Add("AssetPack.ProbabilityModifiers",
            layperson: "Rules that raise or lower this whole config's selection chance for NPCs matching certain attributes - for example, halve it for bandits or double it for nobles. Multiple matching rules multiply together.",
            technical: "Each modifier pairs an NPC attribute with a multiplication factor; the factors of all matched modifiers multiply into the pack's ProbabilityWeighting when the seed pack is chosen (Primary) or into the percent inclusion roll, clamped to 100 (Mix-In). At flattening they are also copied into every subgroup, so per-subgroup selection weights scale identically.",
            motivation: "Allow/disallow rules are binary; modifiers give soft per-NPC-type preference, making a config merely rarer or more common for a class of NPCs instead of forbidden or forced.");

        // ===== Record templates (UC_AssetPackRecordTemplateSelector) =====

        Add("AssetPack.DefaultRecordTemplate",
            layperson: "The stand-in NPC (from SynthEBD's bundled record template plugins) whose records are borrowed whenever a patched NPC lacks a record this config needs - most commonly the WornArmor 'skin' tree that body textures live in.",
            technical: "Mirrors AssetPack.DefaultRecordTemplate, resolved from the record-template link cache rather than the load order. During record generation, destination paths that do not exist on the target NPC are traversed on the template NPC instead and the discovered sub-records are deep-copied into the output plugin; the editor also validates destination paths against it. Additional Templates override it for their listed races, and changing the config's gender re-picks a gender-appropriate default.",
            motivation: "Most NPCs have no dedicated skin record to edit - the template supplies a known-good record structure, matched to the body type the config supports, that the patcher can clone for any NPC.");

        Add("AssetPack.AdditionalRacesPaths",
            layperson: "The spots inside the record template where the races of patched NPCs must be registered so the borrowed records actually work for them - normally the Additional Races lists of the template's armor pieces. The defaults are correct for standard body templates.",
            technical: "Mirrors AssetPack.DefaultRecordTemplateAdditionalRacesPaths. Before generation, the patcher walks each path on the default template and adds every Patchable Race (minus races claimed by an Additional Template) to the FormLink list found there, overriding the template plugin in memory. Without this, cloned armor addons would not render for races absent from their AdditionalRaces arrays.",
            motivation: "Armature records only apply to races explicitly listed on them; auto-registering the patchable races lets one template serve whatever race roster a given load order has.");

        Add("AssetPack.AdditionalRecordTemplates",
            layperson: "Extra template NPCs used instead of the default for specific races - typically Khajiit and Argonian templates, whose skin records differ from the human one. Each entry lists the races it covers and its own Additional Races Paths.",
            technical: "Mirrors AssetPack.AdditionalRecordTemplateAssignments. When a patched NPC's race is in an entry's race list, record generation borrows missing records from that entry's template NPC instead of the default; the entry's races are excluded from the default template's additional-races registration and registered on the entry's own paths instead. Changing the config's gender auto-populates the beast-race entries.",
            motivation: "Beast races (and mods like TNG) need structurally different donor records - per-race templates let one config cover humans, Khajiit, and Argonians with correct records for each.");

        // ===== Asset replacers (UC_AssetPackDirectReplacerMenu / UC_AssetReplacerGroup) =====

        Add("AssetPack.ReplacerGroups",
            layperson: "Each replacer group names one existing detail to be replaced - such as a particular vanilla scar - and holds the subgroups providing the replacement versions. NPCs that do not have the target are skipped automatically.",
            technical: "A group's target is defined by its subgroups' destination paths: hardcoded path sets map to specific head parts or special head-part textures, and anything else is treated as a generic record path that must resolve on the NPC. AssetReplacerSelector assigns the group only if the NPC actually has the target, then picks a combination from the group via a virtual asset pack; the group label identifies the assignment in Specific NPC Assignments and consistency.",
            motivation: "Details like scars are separate records that keep their vanilla textures when the skin changes; targeted replacement upgrades them to match the assigned skin without touching NPCs that lack them.");

        Add("AssetPack.ReplacerSubgroups",
            layperson: "The tree of options for this replacer, built from subgroups just like the main config - each option's paths define which existing asset gets replaced and with what.",
            technical: "Replacer subgroups carry the same distribution rules and file-path semantics as main subgroups, and the union of their destination paths defines the replacer's target. At patch time the group is flattened into a virtual asset pack and the standard combination selection (rules, weighting, consistency) runs against it.",
            motivation: "Reusing the subgroup system means replacers get variants, distribution rules, and drag-and-drop editing for free instead of needing a separate one-off format.");

        Add("AssetPack.ReplacerReferenceNPC",
            layperson: "An example NPC who actually has the thing this replacer targets, used only to check that your destination paths are valid. It has no effect on patching.",
            technical: "Stored as AssetReplacerGroup.TemplateNPCFormKey and fed to the path editor as its reference NPC, so destination paths can be resolved and status-colored against a real record. The patcher never reads it - at run time every NPC is tested directly for the replacer's target paths.",
            motivation: "Replacer destinations point at records the bundled record templates do not have (like a specific scar head part), so path verification needs a real NPC that carries the target.");

        // ===== Asset presenter (UC_AssetPresenter) =====

        Add("AssetPack.LockPreviewNpc",
            layperson: "Keeps the same NPC in the 3D preview while you click around subgroups, instead of letting the preview switch to a different NPC when a subgroup's race rules point elsewhere.",
            technical: "When enabled (and no explicit preview NPC is picked), the render preview reuses the last-loaded NPC rather than re-resolving one from the selected subgroup's effective races and the config's gender. An explicit selection in the preview NPC picker always takes precedence.",
            motivation: "Comparing texture variants is only meaningful on a constant model - without the lock, clicking a Khajiit-only subgroup would swap your human preview out from under you.");

        Add("AssetPack.RandomizePreview",
            layperson: "Rolls the dice the same way the patcher would and shows the resulting combination of textures on the preview NPC.",
            technical: "Runs one repetition of the asset-distribution pipeline against this config (honoring its distribution rules) for the selected preview NPC - or, when none is picked, for the configured preview-NPC defaults in turn until one is compatible - then applies the rolled combination's textures and auxiliary meshes to the render preview. The roll runs on a background thread; a busy overlay covers the viewport until the scene commits.",
            motivation: "Clicking subgroups one at a time shows individual assets, but not what an actual patch run would put together. Randomize previews a distribution-rule-valid combination, so you see the config the way an NPC in game would.");

        Add("AssetPack.ViewCurrentAssets",
            layperson: "Opens a window listing which subgroups are currently shown on the preview NPC and which texture files each one contributed.",
            technical: "Opens a non-modal window bound to the presenter's applied-subgroup records: one entry per subgroup (upserted by ID as selections accumulate; rebuilt wholesale by Randomize; cleared by Reset and config swaps), each listing its asset files' config-relative source paths with the destination record path as tooltip.",
            motivation: "After a few subgroup clicks or a Randomize roll, it is easy to lose track of what is actually on the model. This window answers 'what am I looking at?' without digging through the verbose log.");
    }
}

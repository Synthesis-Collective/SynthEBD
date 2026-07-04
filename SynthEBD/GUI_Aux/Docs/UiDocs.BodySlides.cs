namespace SynthEBD;

// 3-part documentation for the per-BodySlide editor and its annotation tooling: the BodySlide
// preset editor (UC_BodySlideSetting / UC_BodySlideMenu), the Body Type Profile annotation table
// and Suggest Rules panels, the trainer exporter, the BodySlide exchange window
// (BodySlideExchange.* keys), and the body shape descriptor distribution rules (Descriptors.* keys).
public static partial class UiDocs
{
    private static void RegisterBodySlides()
    {
        // ---------- BodySlides menu: preset list ----------

        Add("BodySlides.PresetNameFilter",
            layperson: "Type here to narrow the preset list to names containing that text. Clear it to show everything again.",
            technical: "Bound to VM_BodySlidesMenu.PresetFilterText; TogglePresetVisibility hides presets whose Label does not contain the text (case-insensitive substring). Combines with the Gender and Body Type dropdowns, the descriptor filter, and the Show Hidden toggle.",
            motivation: "Annotation and rule-editing sessions constantly jump between specific presets; a name filter beats scrolling through a list of hundreds.");

        // ---------- Per-BodySlide editor: preview ----------

        Add("BodySlides.PreviewResizer",
            layperson: "Drag this divider left or right to resize the 3D preview pane next to the preset settings.",
            technical: "GridSplitter between UC_BodySlideSetting's settings grid and the embedded Character Viewer column. The viewer column intentionally starts narrow (100 px) so it never obscures the distribution-rule grid; the splitter resizes both columns live.",
            motivation: "The per-preset preview shares space with a dense settings grid; a user-controlled divider lets each task - rule editing or shape inspection - get the screen space it needs.");

        Add("BodySlides.PreviewNpcOverride",
            layperson: "Choose a specific NPC to model this preset in the 3D preview. Leave blank to use the automatic stand-in configured for the current weight tab in the OBody Misc Settings preview table.",
            technical: "Sets VM_BodySlideSetting.PreviewNpcOverride. When blank, RefreshPreview looks up Settings_OBody.PreviewNpcs for the selected weight slot and the preset's gender; when set, the chosen NPC is loaded instead and the preset's morph is applied at the slot's weight. The override is session-only - it is not saved and resets when you switch presets.",
            motivation: "The weight-table defaults are picked purely by record weight, so they may not be a character you care about; a quick override lets you sanity-check a preset on a specific NPC without editing the global preview table.");

        // ---------- Per-BodySlide editor: identity and info ----------

        Add("BodySlides.EditReferencedBodySlide",
            layperson: "Unlocks the BodySlide name box so you can change which installed preset this entry controls. The name must exactly match a preset in your BodySlide installation, so only edit it deliberately.",
            technical: "Toggles VM_BodySlideSetting.ReferenceUnlocked (after a confirmation prompt), enabling the ReferencedBodySlide TextBox. ReferencedBodySlide is the exact preset name handed to OBody/AutoBody at runtime and must match a preset parsed from CalienteTools\\BodySlide\\SliderPresets; entries whose name matches nothing installed get a red border and are never assigned.",
            motivation: "The display Name and the underlying preset reference are separate so cloned entries can be told apart, but editing the real reference can silently break assignment, which earns it a deliberate unlock step.");

        Add("BodySlides.RegistryBodyType",
            layperson: "Shows which body type (like CBBE or HIMBO) SynthEBD matched this preset to. Green means a clean match; '(no match)' or a mismatch warning means the 3D shape analyzer cannot classify this preset.",
            technical: "Looks up the preset's Slider Group in Settings_OBody.BodyTypeRegistry by name and gender (RefreshMatchedRegistryBodyType). A matched entry may report 'slider drift' - sliders the preset sets that are absent from the body's resolved slider catalog. Body Type Profiles are matched through this registry entry, so an unmatched preset is skipped by the mesh-measurement classifier.",
            motivation: "Profile-based classification only works when a preset is tied to the right body topology; surfacing the match (and its drift) here explains why a preset is or is not being auto-annotated.");

        Add("BodySlides.Notes",
            layperson: "Free-form notes about this preset, for your own reference. They have no effect on which NPCs receive the preset.",
            technical: "Mirror of BodySlideSetting.Notes. The patcher never reads this field; it round-trips through the settings JSON and can be shared or withheld via the BodySlide exchange window's Notes toggle.",
            motivation: "Annotation projects accumulate context - where a preset came from, why it is restricted - that belongs with the preset but must not leak into distribution logic.");

        // ---------- Per-BodySlide editor: per-weight descriptors ----------

        Add("BodySlides.WeightSlotDescriptors",
            layperson: "Labels describing what this preset's body looks like (like Build: Curvy), organized into tabs by NPC weight because a preset can look very different at weight 0 versus 100. These labels are how SynthEBD matches body shapes to textures and to descriptor rules.",
            technical: "Edits BodySlideSetting.BodyShapeDescriptorsByWeight, a weight-slot (0-100) to descriptor-set map with default slots at 0/25/50/75/100. At patch time PerWeightDescriptorLookup uses the slot closest to the NPC's record weight (ties round down; empty slots fall through to the next-closest non-empty one) for rule validation and asset-descriptor matching.",
            motivation: "BodySlide presets interpolate between a small and a big shape, so one flat label set would mislabel weight-varying presets; per-weight slots let the labels follow the actual silhouette.");

        Add("BodySlides.AddWeightSlot",
            layperson: "Adds a new descriptor tab at the weight you typed, so you can label how the preset looks at that specific NPC weight.",
            technical: "Runs VM_BodySlideSetting.AddWeightSlotCommand: validates the weight (0-100, no duplicate slot), inserts the new slot in sorted order, and selects it. Re-adding a default slot you previously removed also takes it back off RemovedDefaultWeightSlots.",
            motivation: "The default 0/25/50/75/100 slots suit most presets, but a preset that changes character at a specific weight needs annotation exactly there.");

        Add("BodySlides.RemoveWeightSlot",
            layperson: "Removes this weight tab and its labels from the preset. You are asked to confirm if the tab has labels or is one of the standard slots.",
            technical: "Runs VM_BodySlideSetting.RemoveWeightSlot. Removed default slots are recorded in BodySlideSetting.RemovedDefaultWeightSlots so they stay removed across reloads instead of being re-seeded; distribution then reads the nearest remaining non-empty slot.",
            motivation: "Not every preset needs five annotation points; trimming slots keeps the annotation honest where the preset has nothing distinct to say at a weight.");

        // ---------- Per-BodySlide editor: distribution rules ----------

        Add("BodySlides.AllowedRaces",
            layperson: "Limits this preset to NPCs of the selected races. Leave the list empty to allow all races.",
            technical: "Mirror of BodySlideSetting.AllowedRaces. During selection, BodyShapeCandidateValidator rejects the preset when the list is non-empty and does not contain the NPC's body-shape race (the record race after Race Alias translation). At patch time, checked Allowed Race Groupings are merged into this same list.",
            motivation: "Body presets are often sculpted for specific racial proportions; race gating keeps, say, a delicate elf shape off orcs without needing per-NPC rules.");

        Add("BodySlides.AllowedRaceGroupings",
            layperson: "Limits this preset to NPCs belonging to the checked race groupings (named bundles of races, like Humanoid Playable). Combines with the Allowed Races list.",
            technical: "Mirror of BodySlideSetting.AllowedRaceGroupings, stored as grouping labels. OBodyPreprocessing.CompilePresetRaces resolves each checked label against GeneralSettings.RaceGroupings at patch time and merges the member races into the preset's AllowedRaces before validation.",
            motivation: "Groupings let one checkbox cover a maintained set of races (including modded ones), so rules keep working as grouping definitions evolve instead of hardcoding race lists per preset.");

        Add("BodySlides.DisallowedRaces",
            layperson: "NPCs of the selected races will never receive this preset, even if they pass every other rule.",
            technical: "Mirror of BodySlideSetting.DisallowedRaces. BodyShapeCandidateValidator rejects the preset whenever this list contains the NPC's body-shape race; disallowed entries win over allowed ones. Checked Disallowed Race Groupings merge into this list at patch time.",
            motivation: "Exclusions are simpler to express than exhaustive allow-lists when a preset fits almost everyone - for example, everything except beast races.");

        Add("BodySlides.DisallowedRaceGroupings",
            layperson: "NPCs belonging to the checked race groupings will never receive this preset.",
            technical: "Mirror of BodySlideSetting.DisallowedRaceGroupings (labels). OBodyPreprocessing resolves the labels against GeneralSettings.RaceGroupings at patch time and merges the member races into DisallowedRaces, which the validator then checks against the NPC's body-shape race.",
            motivation: "The same maintainability win as allowed groupings, in exclusion form: block whole categories such as beast races with one checkbox.");

        Add("BodySlides.AllowedAttributes",
            layperson: "Traits an NPC must have to receive this preset, such as belonging to a faction or class. 'Force If' attributes go further: NPCs matching them get this preset preferentially over presets they merely qualify for.",
            technical: "Mirror of BodySlideSetting.AllowedAttributes, evaluated by AttributeMatcher against the OBody attribute-group set. If any non-ForceIf allowed attributes exist, the NPC must match at least one or the preset is invalid; matched ForceIf weights are tallied in NPCInfo.ForceIfMatches, and OBodySelector picks among ForceIf-matched presets before all others. ForceIf matches also override a disabled 'Distribute to non-forced NPCs'.",
            motivation: "This is the main tool for characterful distribution - warriors get powerful builds, scholars get slighter ones - with ForceIf providing a soft 'prefer' tier between hard requirements and pure randomness.");

        Add("BodySlides.DisallowedAttributes",
            layperson: "Traits that ban an NPC from receiving this preset. If the NPC matches any of these, the preset is skipped.",
            technical: "Mirror of BodySlideSetting.DisallowedAttributes. AttributeMatcher tests them as pure restrictions (ForceIf sub-attributes count as ordinary matches here); any match invalidates the preset for that NPC in BodyShapeCandidateValidator.",
            motivation: "Exclusion rules catch cases allow-lists cannot cleanly express, like keeping an exaggerated shape off named story characters or specific factions.");

        Add("BodySlides.WeightRange",
            layperson: "The body-weight range (0 to 100, inclusive) an NPC must fall within to receive this preset.",
            technical: "Mirror of BodySlideSetting.WeightRange. BodyShapeCandidateValidator rejects the preset when the NPC's record Weight is below Lower or above Upper. This gates who receives the preset; it is independent of the per-weight descriptor slots, which describe the preset's shape.",
            motivation: "Some presets only look right in part of the weight spectrum (or are authored for a fixed weight); bounding recipients avoids pairing, say, a gaunt preset with a weight-100 NPC.");

        Add("BodySlides.AllowRandom",
            layperson: "When unchecked, this preset is never handed out randomly - NPCs only receive it through a Specific NPC Assignment or by matching one of its Force If attributes.",
            technical: "Mirror of BodySlideSetting.AllowRandom. BodyShapeCandidateValidator checks this gate last, so a ForceIf match (including ones contributed by descriptor rules) overrides the opt-out. Presets that look like outfit/refit sliders (names containing Clothes, Outfit, Armor, Refit, etc.) are imported with this pre-unchecked and hidden.",
            motivation: "Special-purpose presets - player shapes, outfit refits, joke bodies - need to exist in the list for targeted use without polluting the general random pool.");

        Add("BodySlides.AllowUnique",
            layperson: "Whether unique (named, one-of-a-kind) NPCs may receive this preset.",
            technical: "Mirror of BodySlideSetting.AllowUnique. BodyShapeCandidateValidator rejects the preset for NPCs whose record carries the Unique flag when this is off.",
            motivation: "Lets you reserve generic shapes for crowd NPCs, or conversely keep outlandish presets away from important named characters.");

        Add("BodySlides.AllowNonUnique",
            layperson: "Whether generic (non-unique) NPCs, like guards and bandits, may receive this preset.",
            technical: "Mirror of BodySlideSetting.AllowNonUnique. BodyShapeCandidateValidator rejects the preset for NPCs without the Unique record flag when this is off.",
            motivation: "The complement of Allow Unique NPCs: together they can dedicate a preset to named characters only, background NPCs only, or both.");

        Add("BodySlides.ProbabilityWeighting",
            layperson: "How likely this preset is to be picked compared to the other valid presets. 2 means twice as likely as a weight-1 preset; the number only matters relative to the other candidates.",
            technical: "Mirror of BodySlideSetting.ProbabilityWeighting. After rule filtering, OBodySelector draws one preset via ProbabilityWeighting.SelectByProbability with each candidate weighted by this value times its probability-modifier factor. Not used when consistency returns the previous pick, or in OBody multiple-assignment mode where the whole candidate set is handed to OBody.",
            motivation: "Real populations are not uniform; weighting lets common builds stay common and rare shapes stay rare without banning anything outright.");

        Add("BodySlides.ProbabilityWeightModifiers",
            layperson: "Conditional likelihood tweaks: each row pairs an NPC trait with a multiplier, so this preset can be, say, three times as likely for soldiers without being restricted to them.",
            technical: "Mirror of BodySlideSetting.ProbabilityWeightModifiers. At selection time, ProbabilityWeighting.GetProbabilityModifierFactor multiplies the preset's base weighting by the Factor of every modifier whose attribute the NPC matches (multiple matches multiply; rows with a blank attribute are no-ops).",
            motivation: "Sits between a hard Allowed Attribute and flat weighting: nudge distribution toward or away from NPC categories while keeping everyone eligible.");

        Add("BodySlides.SliderValues",
            layperson: "A read-only list of the slider values this preset sets at low weight (Small) and high weight (Big), as imported from BodySlide.",
            technical: "Displays BodySlideSetting.SliderValues, parsed from the preset's SliderPresets XML at startup. The values feed the body-type classifier, the slider-rule annotator, and the 3D preview deformation; they are not editable here - edit the preset in BodySlide and relaunch.",
            motivation: "Seeing the raw sliders in-app helps diagnose classification or annotation surprises without hunting down the preset's XML.");

        // ---------- Per-BodySlide editor: entry management ----------

        Add("BodySlides.HideUnhide",
            layperson: "Hides this preset from the list (or shows it again) to reduce clutter. Hiding does not stop it from being distributed - use the rule settings for that.",
            technical: "Toggles the placeholder's IsHidden flag, persisted as BodySlideSetting.HideInMenu. Hidden presets appear only when Show Hidden is checked, are skipped by the exchange Export, and survive the Delete All button; the patcher ignores the flag entirely. Outfit/refit-style presets are auto-hidden at import (paired with AllowRandom = false).",
            motivation: "Large BodySlide libraries drown the list in refits and utility presets; hiding keeps the working set readable without deleting entries or changing distribution.");

        Add("BodySlides.ClonePreset",
            layperson: "Makes a copy of this entry that controls the same BodySlide preset. Useful when one preset should carry different rules or labels in different situations - for example when its shape changes drastically with NPC weight.",
            technical: "Runs VM_BodySlideSetting.Clone: duplicates the model (rules, per-weight descriptors, notes), appends an index to the display Name, and inserts the copy after the original; both keep the same ReferencedBodySlide. The exchange import matches multi-entry presets by count, cloning on the receiving end when unambiguous.",
            motivation: "One installed preset sometimes needs to act as several distribution entries - split weight ranges, alternate rule sets - and cloning here is cheaper than duplicating the preset in BodySlide.");

        Add("BodySlides.DeletePreset",
            layperson: "Removes this entry from your settings. If the BodySlide preset itself is still installed, the entry reappears (with default settings) the next time SynthEBD starts.",
            technical: "Removes the placeholder from its parent collection. Entries are (re)created at startup by Settings_OBody.ImportBodySlides scanning CalienteTools\\BodySlide\\SliderPresets, so deletion only sticks for presets no longer on disk; the deleted entry's annotations and rules are lost.",
            motivation: "Mainly for clearing entries whose source presets were uninstalled; the automatic re-import keeps the settings from silently drifting out of sync with the real BodySlide library.");

        // ---------- Body Type Profile editor: annotation table ----------

        Add("BodySlides.AnnotationScan",
            layperson: "Measures every preset of this profile's body type at each configured weight, filling the table below with one row per preset and weight.",
            technical: "VM_PresetAnnotationTable.ScanAsync deforms the preview mesh with each matching preset (Slider Group equal to the profile's body type) at every configured weight slot and runs BodySlideMeasurementEvaluator, storing raw measurement values in the profile's shared MeasurementCache keyed (preset, gender, weight). Cached slices are reused so re-scans only evaluate what changed, and the results also refresh the Match Presets view.",
            motivation: "Rule suggestion needs actual numbers, not just matched descriptors; the table turns the profile's measurements into a sortable dataset you can label and mine.");

        Add("BodySlides.AnnotationWeightSlots",
            layperson: "Sets which NPC weights the scan samples each preset at, as a comma-separated list of whole numbers from 0 to 100 (for example 0,50,100).",
            technical: "Parsed, clamped to 0-100, deduplicated, and sorted by VM_PresetAnnotationTable.SetWeightSlots, then persisted per profile in AnnotatorPreferences.WeightSlots (default 0,25,50,75,100). The table shows only cached rows whose weight is in this set, and the next scan enumerates exactly these slots.",
            motivation: "More slots mean finer weight coverage but proportionally longer scans; a per-profile setting lets simple bodies scan fast while weight-sensitive ones sample densely.");

        Add("BodySlides.AnnotationColumns",
            layperson: "Opens a checklist of the profile's measurements so you can choose which columns the table shows.",
            technical: "Each checkbox flips a VM_AnnotationColumn.IsVisible flag; the DataGrid rebuilds its dynamic measurement columns from VisibleColumns, and the visible set persists per profile in AnnotatorPreferences.VisibleMeasurementColumns (an empty list means show all). Hidden columns are still scanned and cached.",
            motivation: "Profiles can define many measurements, but any one labeling task usually cares about a few; column control keeps the table readable without deleting measurements.");

        Add("BodySlides.AnnotationColumnsAll",
            layperson: "Shows every measurement column in the table.",
            technical: "VM_PresetAnnotationTable.ShowAllColumns sets IsVisible on every column, rebuilds VisibleColumns, and persists the visible set to the profile's AnnotatorPreferences.",
            motivation: "One-click recovery to full visibility after a focused session hid most of the columns.");

        Add("BodySlides.AnnotationColumnsNone",
            layperson: "Hides every measurement column, leaving just the preset, gender, weight, and annotation columns.",
            technical: "VM_PresetAnnotationTable.HideAllColumns clears IsVisible on every column, rebuilds VisibleColumns, and persists the visible set to the profile's AnnotatorPreferences.",
            motivation: "Starting from zero and ticking on the two or three measurements you care about is quicker than un-ticking a long list.");

        // ---------- Body Type Profile editor: Suggest Rules panel ----------

        Add("BodySlides.SuggestRulesAlgorithm",
            layperson: "Chooses the math used to turn your labeled presets into suggested classification rules. The default (OptimalThresholdPerValue) works well in most cases.",
            technical: "Two-way bound to RuleSynthesisAlgorithm and persisted per profile in AnnotatorPreferences.SynthesisAlgorithm. OptimalThresholdPerValue picks each measurement's threshold by maximum Youden's J (TPR - FPR); MedianSplit is the legacy halfway-between-group-medians split; DecisionStump picks the single (measurement, threshold) pair that minimizes Gini impurity.",
            motivation: "No single synthesizer wins on every dataset - noisy labels, overlapping shapes, and small samples each favor different splits - so the choice is exposed instead of hardcoded.");

        Add("BodySlides.SuggestRulesAcceptAll",
            layperson: "Adds every remaining suggestion below to the profile as draft rules in one click. Drafts can be reviewed, edited, or deleted in the Rules tab before they affect anything.",
            technical: "Runs Accept on each pending VM_RuleSuggestion: each becomes a MeasurementRule with IsDraft = true in the profile's Rules collection, its conditions OR-combined (one AndGatedMeasurementGroup per condition). Already-accepted suggestions are skipped.",
            motivation: "When the synthesizer output looks broadly right, promoting it wholesale and pruning in the Rules tab beats clicking Accept dozens of times.");

        Add("BodySlides.SuggestRulesAccept",
            layperson: "Adds this one suggestion to the profile as a draft rule; the row then dims to show it has been taken.",
            technical: "VM_SuggestRulesPanel.Accept materializes the suggestion as a MeasurementRule (IsDraft = true) in the profile's Rules collection, OR-combining its condition previews, then sets IsAccepted so the command disables and the row's opacity drops.",
            motivation: "Cherry-picking strong suggestions while ignoring weak ones keeps the human in the loop on rule quality.");

        // ---------- Annotator training menu ----------

        Add("BodySlides.TrainerExportMenu",
            layperson: "Opens the training-set exporter: pick presets and sliders, then export their slider values together with your descriptor labels for machine-learning work. Intended for contributors improving the automatic preset classifier.",
            technical: "VM_OBodyTrainer.ClickExporterMenu displays and reinitializes VM_OBodyTrainerExporter, which selects BodySlide presets, slider groups, and slider names, then either trains an ML.NET classification model per descriptor category or exports the labeled training set as CSV.",
            motivation: "The ML side of preset classification needs labeled data in bulk; exporting it straight from annotated settings avoids hand-building datasets.");

        // ---------- BodySlide exchange window ----------

        Add("BodySlideExchange.Rules",
            layperson: "Include each preset's full distribution rules (races, attributes, weighting, and so on) in the exchange, not just its shape labels.",
            technical: "Bound to VM_BodySlideExchange.ExchangeRules. On export, the full BodySlideSetting is written instead of only the name plus per-weight descriptors; on import, the incoming entry replaces yours entirely instead of only replacing BodyShapeDescriptorsByWeight and RemovedDefaultWeightSlots. The attribute-group and race-grouping toggles below only appear while this is on.",
            motivation: "Rules are opinionated; this switch lets authors share complete distribution setups while recipients who only want the shape labels can take those without overwriting their own rules.");

        Add("BodySlideExchange.Notes",
            layperson: "Include the free-text notes attached to each preset. Notes are information for the user only and never influence the patcher.",
            technical: "Bound to VM_BodySlideExchange.ExchangeNotes. On export, each preset's Notes field is copied or blanked; on import, the imported notes either replace the target entry's notes or your existing notes are kept.",
            motivation: "Authors often keep working notes that are useful to recipients but just as easily noise; a dedicated toggle serves both preferences.");

        Add("BodySlideExchange.AttributeGroups",
            layperson: "Bundle the attribute groups that the exported rules refer to, so the file also works for users who never defined those groups. On import, only groups you are missing are added.",
            technical: "Bound to VM_BodySlideExchange.IncludeAttributeGroups (shown only while rules are exchanged). Export collects the group labels referenced by preset rules and by the exported descriptors' associated rules and writes those definitions from the OBody attribute-group menu; import adds only groups whose Label is absent from your OBody attribute groups - existing definitions are never overwritten.",
            motivation: "Rules reference attribute groups by name only, so a shared file would silently match nothing on a machine lacking the definitions; carrying them along keeps shared rule sets self-contained.");

        Add("BodySlideExchange.RaceGroupings",
            layperson: "Bundle the race groupings that the exported rules refer to. On import, only groupings you are missing are added to your General settings.",
            technical: "Bound to VM_BodySlideExchange.IncludeRaceGroupings (shown only while rules are exchanged). Export writes the definition of every race-grouping label referenced by preset rules or exported descriptor rules, sourced from the General settings' race-grouping editor; import appends only labels you lack, since race groupings are stored centrally in General settings.",
            motivation: "Like attribute groups, race groupings are label references; shipping the definitions keeps imported race rules from silently never matching.");

        Add("BodySlideExchange.DescriptorMergeMode",
            layperson: "When an imported shape label (descriptor) already exists in your settings, this decides what happens to that descriptor's distribution rules: keep yours, take theirs, or merge the two.",
            technical: "Bound to VM_BodySlideExchange.DescriptorMergeMode (import mode only). Applied per already-existing descriptor during MergeInMissingModels: Skip keeps your rules, Overwrite replaces them with the imported ones, and Merge unions the race/grouping/attribute lists while taking the more restrictive value of each allow flag and of the weight range. Merged descriptors are listed afterward for review.",
            motivation: "Descriptor definitions collide across shared files by design (everyone has a Build: Slim); an explicit merge policy prevents an import from quietly clobbering carefully tuned descriptor rules.");

        Add("BodySlideExchange.Run",
            layperson: "Runs the transfer. Export asks where to save the JSON file; Import asks which file to load and offers to back up your current BodySlide settings first.",
            technical: "Executes VM_BodySlideExchange.ActionCommand in the window's mode. Import matches incoming annotations to your entries by referenced BodySlide, auto-cloning your single entry when the file carries several annotations for one preset and warning when the counts cannot be reconciled; presets you do not have installed are skipped. The window closes on success.",
            motivation: "A single action button per mode keeps the exchange a deliberate, reviewable step - especially on import, where the backup offer guards against regret.");

        // ---------- Body shape descriptor distribution rules ----------

        Add("Descriptors.AllowedRaces",
            layperson: "NPCs must be one of these races to receive any body shape carrying this label. Leave the list empty to allow all races.",
            technical: "Part of this descriptor's BodyShapeDescriptorRules. During selection, any BodySlide preset or BodyGen morph annotated with the descriptor is rejected for an NPC unless this list (with grouping members merged in) is empty or contains the NPC's body-shape race - enforced through BodyShapeDescriptor.PermitNPC inside BodyShapeCandidateValidator.",
            motivation: "Rules on the label itself apply to every body shape tagged with it, so a policy like 'this build is khajiit-only' is written once here instead of on each of dozens of presets.");

        Add("Descriptors.AllowedRaceGroupings",
            layperson: "NPCs must belong to one of the checked race groupings to receive any body shape carrying this label.",
            technical: "Stored as grouping labels on the descriptor's rules. At patch time, OBodyPreprocessing / BodyGenPreprocessing resolve them against GeneralSettings.RaceGroupings and merge the member races into the rules' AllowedRaces before NPCisValid evaluates candidates.",
            motivation: "Grouping references keep label-level race policy short and stable as race lists (including modded races) evolve.");

        Add("Descriptors.DisallowedRaces",
            layperson: "NPCs of these races never receive a body shape carrying this label, regardless of the other rules.",
            technical: "Part of the descriptor's BodyShapeDescriptorRules; NPCisValid rejects a candidate body shape when the NPC's body-shape race is in this list (with grouping members merged in). The rejection applies to the whole preset or morph carrying the descriptor, not just the label.",
            motivation: "One exclusion at the label level beats repeating a Disallowed Race on every preset that shares the trait.");

        Add("Descriptors.DisallowedRaceGroupings",
            layperson: "NPCs belonging to the checked race groupings never receive a body shape carrying this label.",
            technical: "Grouping labels merged into the descriptor rules' DisallowedRaces at patch time (resolved against GeneralSettings.RaceGroupings), then enforced by NPCisValid during body-shape candidate validation.",
            motivation: "Blanket exclusions like 'not for beast races' are exactly what groupings are for; defining them here covers every tagged body shape at once.");
    }
}

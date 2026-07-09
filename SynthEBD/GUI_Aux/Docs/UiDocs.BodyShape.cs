namespace SynthEBD;

// 3-part documentation for the Body Shape menus: (O/Auto)Body Integration and BodyGen Integration.
// Top-level controls only; the deep sub-editors (BodySlide settings, Body Type Profile editor,
// BodyGen template editor, etc.) keep their legacy inline tooltips for a later pass.
public static partial class UiDocs
{
    private static void RegisterBodyShape()
    {
        // ---------- (O/Auto)Body Integration: navigation panel ----------

        Add("OBody.BodySlidesMenu",
            layperson: "Opens the list of BodySlide presets SynthEBD found in your game folder. For each preset you can decide which NPCs are allowed to receive it and how likely it is to be picked.",
            technical: "Shows the BodySlide presets imported from SliderPresets XML files into Settings_OBody.BodySlidesMale/BodySlidesFemale (one BodySlideSetting per preset). The editor sets per-preset distribution rules - allowed/disallowed races and race groupings, attributes, weight range, probability weighting, and body shape descriptors - which OBodySelector evaluates for every NPC during the patcher run.",
            motivation: "Without rules, any preset could land on any NPC of the right gender. This menu is where you shape the distribution, for example keeping muscular presets on warriors or preventing exaggerated shapes from appearing on elderly NPCs.");

        Add("OBody.DescriptorsMenu",
            layperson: "Opens the editor for body shape descriptors: short labels like Build: Curvy or Chest: Busty that describe what a body preset looks like. Other parts of SynthEBD use these labels to match body shapes to NPCs and textures.",
            technical: "Edits Settings_OBody.TemplateDescriptors, a list of BodyShapeDescriptorShell categories each holding descriptor values. Descriptors annotate BodySlides (per weight slot) and are referenced by asset-pack subgroups via Allowed/Disallowed/PrioritizedBodySlideDescriptors, letting AssetAndBodyShapeSelector keep assigned textures and body shapes consistent. Renames and deletions propagate to every BodySlide and asset-pack subgroup that references them.",
            motivation: "Descriptors decouple 'what a body looks like' from 'which preset it is', so a config author can say 'this muscular normal map needs a powerful build' without naming specific presets the user may not have installed.");

        Add("OBody.AttributeGroupsMenu",
            layperson: "Opens the editor for attribute groups used by this menu. An attribute group is a reusable, named bundle of NPC traits (like factions or classes) that body shape rules can refer to by name.",
            technical: "Edits Settings_OBody.AttributeGroups, the OBody-local copy of the named attribute-group definitions. BodySlide and descriptor rules reference groups by label; resolution goes through NPCAttribute.GetAttributeGroupByLabel, where GeneralSettings.OverwritePluginAttGroups decides whether a same-named General group supersedes this local one.",
            motivation: "Local attribute groups let a shared OBody settings file carry its own group definitions so it works on machines where the recipient never defined them, while the General set still allows centralized management.");

        Add("OBody.MiscSettingsMenu",
            layperson: "Opens the remaining BodySlide options: how assignments are handed to OBody or AutoBody in-game, automatic labeling of unlabeled presets, and settings for the preset classifier and 3D preview.",
            technical: "Displays VM_OBodyMiscSettings: the OBody/AutoBody assignment mode selectors, auto-annotation toggle, verbose-script toggle, slider-catalog overrides, body-type family compatibility map, and the per-weight preview NPC table. All values round-trip to Settings_OBody via CopyIn/DumpViewModelToModel.",
            motivation: "These options affect the whole BodySlide pipeline rather than any single preset, so they live on their own page instead of being repeated per BodySlide.");

        Add("OBody.AnnotatorMenu",
            layperson: "Opens the rule editor that labels body presets automatically. You describe what slider values mean (for example, a high weight slider means a chubby build) and SynthEBD applies the matching labels to presets for you.",
            technical: "Edits Settings_OBody.BodySlideClassificationRules (SliderClassificationRulesByBodyType keyed by slider group). BodySlideAnnotator evaluates these rules once per descriptor weight slot to assign body shape descriptors: Small/Big/Either conditions read the preset's authored endpoint slider values (labeling all slots or none), while Interpolated conditions read the weight-blended value at each slot, so their labels can apply to just part of the weight range. Rule-derived annotations are marked as non-manual and are recomputed rather than saved.",
            motivation: "Manually annotating hundreds of installed presets is tedious. Authoring rules once per body type lets every current and future preset be labeled automatically and consistently.");

        Add("OBody.AnnotatorViewerSplitter",
            layperson: "Drag to resize the rule editor and the 3D preview panel.",
            technical: "GridSplitter between the classification-rule editor column and the preview rail (preset list, slider readout, CharacterViewer, NPC search). Same layout pattern as the Label by Measurements editor.",
            motivation: "Rule authoring wants width for AND/OR groups while shape inspection wants width for the 3D view; a splitter lets each task claim the space it needs.");

        Add("OBody.AnnotatorPreviewGender",
            layperson: "Chooses whether the preset list and 3D preview show presets from your male or female BodySlide list.",
            technical: "Selects between Settings_OBody.BodySlidesMale and BodySlidesFemale as the preset-list source, and picks the male/female half of the Misc-settings per-weight preview NPC pair (and of the NPC search results).",
            motivation: "BodySlide presets are distributed per gender, and a preset only previews meaningfully on a body of the matching gender.");

        Add("OBody.AnnotatorPreviewSliderPicker",
            layperson: "Picks which slider's numbers appear in the preset list's Low / High / Interp columns, so you can sort every preset by that slider.",
            technical: "Sets the slider whose authored Small/Big values and interpolated value (at the preview weight) populate VM_AnnotatorPresetRow.Low/High/Interpolated. Options are the body type's registry slider catalog unioned with sliders found in its loaded presets — the same list the rule rows offer. Presets lacking the slider show blank cells.",
            motivation: "When tuning a rule threshold like 'BellyMuscle >= 60', sorting all presets by that slider shows exactly where a proposed cutoff lands across your installed presets.");

        Add("OBody.AnnotatorPresetFilter",
            layperson: "Type here to narrow the preset list to names containing the text.",
            technical: "Case-insensitive substring filter over the preset labels in the list below. Filtering does not change the selected preset or the 3D preview.",
            motivation: "Installed preset collections routinely run to hundreds of entries; scrolling for one by eye is slower than typing three letters.");

        Add("OBody.AnnotatorPresetList",
            layperson: "The presets of this body type. Click a column header to sort by name or by the picked slider's values; click a row to show that preset in the 3D view.",
            technical: "Presets from the gendered BodySlide list whose SliderGroup matches the annotator's selected body type. Selection triggers LoadNpcAsync + ApplyBodySlide(preset, preview weight) on the rail's CharacterViewer. The Interp column is the linear Small-to-Big blend at the current preview weight — the value the Interpolated slider-rule type tests.",
            motivation: "Rules are authored against slider numbers, but whether a threshold is right is a visual question; pairing the sorted numbers with a live 3D preview closes that loop without leaving the annotator.");

        Add("OBody.AnnotatorSliderReadout",
            layperson: "Every slider of the selected preset with its low-weight, high-weight, and current-weight values.",
            technical: "One row per entry in the selected preset's SliderValues dictionary: authored Small (weight 0), Big (weight 100), and the interpolated value at the preview weight. The filter box does a case-insensitive substring match on slider names. Interp recomputes when the preview weight changes.",
            motivation: "When deciding which slider drives a visual feature you see in the preview, you need the full value table of the preset in front of you, not just the one slider you already guessed.");

        Add("OBody.AnnotatorPreviewWeight",
            layperson: "The NPC weight (0-100) the preview shows. It also drives the 'Interp' columns and which NPC the weight search looks for.",
            technical: "Continuous 0-100 value applied as the morph weight in ApplyBodySlide, as the interpolation point for both Interp columns, as the target for Find NPCs at Weight, and as the lookup weight for the default preview NPC (nearest configured slot in the Misc-settings per-weight table; ties round down).",
            motivation: "Interpolated rules label presets differently across the weight range, so verifying them requires scrubbing the preview through weights — one shared weight keeps the numbers, the 3D shape, and the NPC consistent.");

        Add("OBody.AnnotatorFindNpcs",
            layperson: "Lists NPCs of the shown gender whose weight matches the value on the left, so you can preview the preset on a character who actually has that weight in-game.",
            technical: "Background scan of the winning NPC overrides using the same eligibility rules as the Misc-settings Auto-pick: matching gender, exact weight, Unique flag, vanilla Skyrim.esm race with ActorTypeNPC, and vanilla body/skeleton mesh paths (so the BodySlide morph topology is valid for the preview). Results log per-criterion rejection counts when empty.",
            motivation: "Preset annotations gate what NPCs receive at their own weight; picking a real NPC at the weight being inspected makes the preview representative rather than hypothetical.");

        Add("OBody.AnnotatorNpcResults",
            layperson: "NPCs found at the requested weight. Click one to preview the selected preset on that character.",
            technical: "Results of Find NPCs at Weight (name > EditorID > FormKey display). Clicking a row writes the NPC into the override picker below, which reloads the viewer with that NPC.",
            motivation: "Routing the click through the override picker keeps a single source of truth for who is being previewed, visible and editable in one place.");

        Add("OBody.AnnotatorPreviewNpcOverride",
            layperson: "Shows a specific NPC in the 3D preview instead of the default preview NPC. Clear it to go back to the default.",
            technical: "Session-only FormKey override (not saved). When empty, the preview NPC comes from the OBody Misc 'Preview NPC by Weight' table at the configured slot nearest the preview weight, for the shown gender — the same policy as the BodySlides and Label by Measurements previews, adapted for this panel's continuous weight.",
            motivation: "The per-weight defaults keep the panel working out of the box, while an override lets you check how a preset reads on the specific character you actually care about.");

        Add("OBody.AnnotatorSliderType",
            layperson: "Chooses which value of the slider this rule tests. Every preset stores two values per slider - one used at NPC weight 0 (Small) and one at weight 100 (Big) - and the game blends between them based on each NPC's weight. 'Small', 'Big', and 'Either' test those stored endpoint values and label the whole preset. 'Interpolated' tests the blended value at each weight step instead, so the label can apply only to the weights where it is true - for example, 'narrow shoulders' only below the weight where the shoulder slider crosses your threshold.",
            technical: "Sets SliderClassificationRule.SliderType. Small/Big/Either compare the preset XML's authored endpoint values and are weight-independent, so the rule annotates every weight slot or none (legacy whole-preset behavior). Interpolated compares Small + (Big - Small) * weight/100 - the same linear blend the game applies to morphs - evaluated at each slot of BodyShapeDescriptorsByWeight, and the descriptor is added only to the slots where the rule passes. Interpolated values can be fractional, so = and != compare against the nearest whole number; ordered comparators use the raw value. Conditions of different types can be mixed in one AND group; each slot evaluates all of them together.",
            motivation: "A preset is not one shape - it is a family of shapes blended by NPC weight, and a preset can be narrow at low weight yet broad at high weight. Endpoint rules cannot express that, but per-weight descriptor slots already exist and the patcher already reads the slot nearest each NPC's weight, so evaluating the blend per slot lets one simple threshold rule produce weight-aware labels automatically.");

        // DEPRECATED / LEGACY: kept for DocTooltip key parity only - the "Annotator Training" tab is
        // hidden (never-completed ML-on-slider-values trainer, superseded by the geometry/measurement-
        // based Body Type Registry + Profiles). See VM_OBodyTrainer.
        Add("OBody.AnnotatorTrainingMenu",
            layperson: "(Deprecated, hidden.) Opened a tool for training a machine-learning model to classify body presets. That approach was never finished and has been superseded by the geometry/measurement-based Body Type Registry and Body Type Profiles.",
            technical: "Hosts VM_OBodyTrainer / VM_OBodyTrainerExporter: pick BodySlide presets, slider groups, and sliders, then either train an ML.NET classification model per descriptor category or export the labeled training set as CSV.",
            motivation: "The ML-based classifier behind automatic annotation needs labeled training data; this menu lets power users generate that data and retrain models without leaving SynthEBD.");

        Add("OBody.BodyTypeRegistryMenu",
            layperson: "Opens the list of body types (like CBBE, 3BA, BHUNP, HIMBO) that SynthEBD knows about, including how to detect whether each one is installed. The preset classifier uses this list to work out which body each preset belongs to.",
            technical: "Edits Settings_OBody.BodyTypeRegistry, a list of BodyTypeRegistryEntry records describing identity fingerprint files (whose existence proves the body is installed) and ShapeData subfolders (whose OSD/BSD files define the body's slider set). Shipped defaults merge in from InternalData/SliderCatalogs/BodyTypeRegistry.json; user edits are flagged IsUserDefined so they survive updates. Feeds BodySlideGroupClassifier and the Character Viewer's OSD linkage.",
            motivation: "New body mods appear constantly; a user-extensible registry means SynthEBD can classify presets for bodies it did not ship knowledge of, without waiting for a program update.");

        Add("OBody.BodyTypeProfilesMenu",
            layperson: "Opens the editor where each body type gets measurement instructions for the 3D shape analyzer: which points on the mesh to track and what measurements mean which body shape labels.",
            technical: "Edits Settings_OBody.BodyTypeProfiles. Each BodyTypeProfile pairs a body type from the registry with key vertices, measurement definitions, and classification rules used by the 3D-mesh BodySlide classifier. A profile is authored once per body type and reused for every preset sharing that body's mesh topology.",
            motivation: "Slider-value rules cannot see the actual mesh; measuring the morphed 3D shape gives more reliable classification, but requires per-body-type reference points, which is what these profiles record.");

        // ---------- (O/Auto)Body Integration: Misc Settings page ----------

        Add("OBody.SetRaceMenuIni",
            layperson: "Fixes RaceMenu's settings file so that body presets assigned by OBody or AutoBody actually show up in the game. Click it once after installing; a confirmation message appears when it works.",
            technical: "Runs RaceMenuIniHandler.SetRaceMenuIniForBodySlide, which edits the SKEE ini (skee64.ini, or skeevr.ini on VR) in the game Data folder: sets bEnableBodyMorph=1 and bEnableBodyGen=0. The BodyGen menu has the opposite counterpart for morph-based distribution.",
            motivation: "BodySlide-style morphs and BodyGen are mutually exclusive RaceMenu features and the required ini flags are easy to get wrong by hand; this button applies the correct combination in one click.");

        Add("OBody.AutoApplyMissingAnnotations",
            layperson: "When checked, any body preset you have not labeled yourself gets labeled automatically using the Annotator rules each time the patcher runs. The automatic labels are not saved; they are recalculated every run.",
            technical: "Mirror of Settings_OBody.AutoApplyMissingAnnotations. At load and at patch time, presets without manual descriptors pass through the annotation library (tier 1, per-weight descriptors) and the rule-based annotator (tier 2, BodySlideClassificationRules). Non-manual annotations are stripped on save and recomputed, and pre-run validation skips the missing-annotation warning while this is enabled.",
            motivation: "Descriptor-based rules only work if presets carry descriptors. Auto-annotation gives unlabeled presets sensible labels without forcing you to hand-annotate your whole BodySlide library.");

        Add("OBody.OBodyAssignmentMode",
            layperson: "Chooses how your body assignments reach OBody in the game. Native writes them straight into OBody's own settings file; Script uses a small in-game script to apply them instead.",
            technical: "Mirror of Settings_OBody.OBodySelectionMode. Native mode rewrites the npcFormID section of SKSE/Plugins/OBody_presetDistributionConfig.json from Patcher.BodySlideTracker (OBodyWriter.WriteNativeAssignmentDictionary). Script mode instead emits SynthEBD/BodySlideAssignments.json plus a spell/magic-effect/loader-quest Papyrus setup that calls OBody's API per NPC, which requires JContainers.",
            motivation: "Native mode is simpler and lighter at runtime since OBody reads its own config directly; Script mode predates it and remains for setups where writing OBody's config file is not desirable.");

        Add("OBody.OBodyEnableMultipleAssignments",
            layperson: "When checked, SynthEBD gives OBody the full list of body presets that fit each NPC instead of picking just one, and OBody randomly picks one in the game.",
            technical: "Mirror of Settings_OBody.OBodyEnableMultipleAssignments (Native mode only). OBodySelector records every rule-compliant preset for the NPC into Patcher.BodySlideTracker rather than a single selection, and all of them are written to OBody_presetDistributionConfig.json. Consistency for BodySlide selection is bypassed in this mode since the final pick happens in-game.",
            motivation: "Some users prefer OBody's own in-game randomization; this keeps SynthEBD in charge of which presets are allowed per NPC while letting OBody make the final roll.");

        Add("OBody.UseVerboseScripts",
            layperson: "When checked, the game shows a notification whenever a body preset is applied to an NPC. Useful to confirm the system is working; turn it off for normal play.",
            technical: "Mirror of Settings_OBody.bUseVerboseScripts (Script/AutoBody paths only). Sets the VerboseMode global that OBodyWriter wires into the SynthEBDBodySlideScript magic-effect script, enabling in-game debug notifications per assignment. Reset-to-default treats this as a troubleshooting setting.",
            motivation: "Script-based assignment fails silently when a requirement (like JContainers) is missing; the notifications give immediate in-game confirmation while troubleshooting.");

        Add("OBody.AutoBodyAssignmentMode",
            layperson: "Chooses the file format used to hand assignments to AutoBody. INI is recommended; JSON does not currently work in Skyrim VR.",
            technical: "Mirror of Settings_OBody.AutoBodySelectionMode. INI mode writes autoBody/Config/morphs.ini with FormKey=preset lines (OBodyWriter.WriteAssignmentIni). JSON mode writes SynthEBD/BodySlideAssignments.json and applies presets through the Papyrus script system, which requires JContainers - and JContainers does not support VR as of the current build.",
            motivation: "AutoBody accepts either input; the toggle exists so VR users and script-averse setups can use the plain ini path while others can share the JSON pipeline.");

        Add("OBody.RemoveUnusedDescriptors",
            layperson: "Removes leftover shape labels from your body presets. These appear when a preset still carries a label whose definition was deleted; tick the ones listed on the right and click to clean them up.",
            technical: "The list shows descriptor signatures found on loaded BodySlides (male and female) that no longer exist in Settings_OBody.TemplateDescriptors. The command strips the checked signatures from every BodySlide's per-weight descriptor slots. The button and list only appear when orphaned descriptors were detected at load.",
            motivation: "Deleted or renamed descriptor definitions can leave dangling references on presets, which silently never match any rule; this cleanup surfaces and removes them in one step.");

        Add("OBody.SliderCatalogOverrides",
            layperson: "Lets you point SynthEBD at your own slider-category file for a given body type, replacing the built-in one. Only needed if the built-in data misclassifies presets for that body.",
            technical: "Edits Settings_OBody.SliderCatalogOverridePaths, a body-type-name to file-path map. When set and the file exists, SliderCatalogLoader uses that SliderCategories.xml instead of the shipped fallback JSON under InternalData/SliderCatalogs/. Feeds BodySlideGroupClassifier's per-body-type slider categorization.",
            motivation: "Shipped slider catalogs cover common bodies but can lag behind mod updates or miss custom bodies; overrides let users supply corrected catalogs without a SynthEBD release.");

        Add("OBody.SliderCatalogOverrideBodyType",
            layperson: "The name of the body type this override applies to, for example CBBE.",
            technical: "Dictionary key in Settings_OBody.SliderCatalogOverridePaths. Must match the body type name used by the registry/classifier; blank rows are dropped on save.",
            motivation: "Overrides are per body type, so each row needs to name which body's slider catalog it replaces.");

        Add("OBody.SliderCatalogOverridePath",
            layperson: "The location of the replacement slider-category file on your computer. Use Browse to pick it.",
            technical: "Dictionary value in Settings_OBody.SliderCatalogOverridePaths: an absolute path to a SliderCategories.xml. If the file is missing at load time, the classifier falls back to the shipped catalog.",
            motivation: "Pointing at the file in place (for example inside your BodySlide installation) avoids copying data into SynthEBD's folders and keeps the override current when the source mod updates.");

        Add("OBody.BodyTypeFamilies",
            layperson: "Tells the preset classifier which body types are variants of each other, for example that 3BA presets should count as CBBE. Each row maps one main body type to a list of equivalent names.",
            technical: "Edits Settings_OBody.BodyTypeFamilyCompatibility, a map from canonical body-type name to a set of alias names. BodySlideGroupClassifier collapses any alias onto the canonical key during classification, e.g. {\"CBBE\": [\"3BA\", \"3BBB\", \"CBAdvanced\"]}.",
            motivation: "Body-mod ecosystems fork constantly (CBBE vs 3BA vs 3BBB); without family mapping, presets for compatible variants would be classified as belonging to different, incompatible bodies.");

        Add("OBody.BodyTypeFamilyCanonical",
            layperson: "The main body type name you want variants collapsed onto, for example CBBE.",
            technical: "Dictionary key in Settings_OBody.BodyTypeFamilyCompatibility. Whitespace is trimmed and blank rows are dropped on save.",
            motivation: "The classifier needs one canonical name per family so all equivalent presets end up grouped under the body you actually use.");

        Add("OBody.BodyTypeFamilyAliases",
            layperson: "A comma-separated list of body type names that should be treated the same as the main one, for example 3BA, 3BBB, CBAdvanced.",
            technical: "Dictionary value in Settings_OBody.BodyTypeFamilyCompatibility: the CSV is split, trimmed, and stored as a HashSet of alias names that BodySlideGroupClassifier resolves to the canonical body type.",
            motivation: "Listing aliases in one text field keeps family maintenance quick as new body variants appear.");

        Add("OBody.PreviewNpcsByWeight",
            layperson: "The table of NPCs used as 3D preview models when viewing body presets, one male and one female per body-weight slot. SynthEBD fills it in automatically, but you can pick your own.",
            technical: "Edits Settings_OBody.PreviewNpcs (OBodyPreviewNpcSettings), a per-weight-slot FormKey map consumed by the Character Viewer hosted in the BodySlide detail pane. Rows auto-populate with the first installed NPC whose record weight is within +/-5 of the slot; a drift check at load offers to re-resolve NPCs whose weight no longer matches.",
            motivation: "BodySlide presets interpolate between weight 0 and 100 shapes, so previewing a preset accurately requires an NPC at the matching weight; this table pins suitable stand-ins per slot.");

        Add("OBody.PreviewNpcsResetAll",
            layperson: "Refills the whole preview NPC table automatically, replacing every current choice.",
            technical: "Runs VM_OBodyPreviewNpcSettings.ResetAllCommand: re-runs the auto-resolver for every weight row, selecting the first installed NPC of each gender whose weight is within +/-5 of the slot and overwriting existing selections.",
            motivation: "After a load-order change the saved preview NPCs may be gone or reweighted; one click rebuilds the table instead of fixing every row by hand.");

        Add("OBody.PreviewNpcAutoPick",
            layperson: "Automatically picks a suitable NPC for this one row, based on the row's body weight.",
            technical: "Runs the row's AutoMaleCommand/AutoFemaleCommand: selects the first installed NPC of the row's gender whose record weight is within +/-5 of the weight slot, replacing only this row's selection.",
            motivation: "Lets you re-resolve a single stale row (for example after a mod changed that NPC's weight) without resetting the whole table.");

        // ---------- BodyGen Integration ----------

        Add("BodyGen.CurrentFemaleConfig",
            layperson: "Selects which BodyGen settings file is active for female NPCs. The active file is the one the patcher will use when distributing body shapes to women.",
            technical: "Selects the active VM_BodyGenConfig among the loaded female configs; its label is persisted as Settings_BodyGen.CurrentFemaleConfig. During patching, BodyGenSelector draws female morph templates and racial mappings from this config, and the output is written as RaceMenu BodyGen templates.ini/morphs.ini under Meshes/actors/character/BodyGenData/<patch name>.esp.",
            motivation: "Users often keep multiple BodyGen configs (for example different body ecosystems or downloaded rule sets); this dropdown switches between them without deleting anything.");

        Add("BodyGen.CurrentMaleConfig",
            layperson: "Selects which BodyGen settings file is active for male NPCs. The active file is the one the patcher will use when distributing body shapes to men.",
            technical: "Selects the active VM_BodyGenConfig among the loaded male configs; its label is persisted as Settings_BodyGen.CurrentMaleConfig. BodyGenSelector uses it for male NPCs during patching, alongside the female config, when body selection mode is BodyGen.",
            motivation: "Male and female bodies use entirely different morph sets, so BodyGen configs are gendered and each gender gets its own active-config picker.");

        Add("BodyGen.AddNewConfig",
            layperson: "Creates a new, empty BodyGen settings file for this gender and opens it for editing below.",
            technical: "Creates a fresh VM_BodyGenConfig, assigns its Gender, makes it the current and displayed config, and seeds it with a starter template group, a humanoid-race mapping, and a starter combination (VM_SettingsBodyGen.InitializeNewBodyGenConfig). It is saved to its own JSON file with the rest of the settings.",
            motivation: "Gives config authors a working skeleton to build on instead of an empty editor where nothing distributes until several interdependent pieces exist.");

        Add("BodyGen.DisplayedConfig",
            layperson: "Chooses whether the editor below shows the female or the male settings file. This only changes what you are looking at, not what the patcher uses.",
            technical: "Sets VM_SettingsBodyGen.CurrentlyDisplayedConfig to the current config of the chosen gender (DisplayedConfigIsFemale/DisplayedConfigIsMale). The displayed config is purely a UI concern; both genders' current configs are used during patching regardless.",
            motivation: "Both gendered configs are edited in the same panel, so a switch is needed to flip the editor between them.");

        Add("BodyGen.FemalePreviewNpc",
            layperson: "The NPC shown as the 3D preview model when editing female body morph templates.",
            technical: "Mirror of Settings_BodyGen.PreviewNpcFemale (a FormKey). The inline Character Viewer in each female BodyGen template editor loads this NPC's body as the preview target. Stored globally rather than inside the BodyGen JSON so shared configs do not carry a FormKey from the author's load order.",
            motivation: "Morph templates are abstract slider lists; previewing them on a real NPC's body makes it possible to see what a template actually does before distributing it.");

        Add("BodyGen.MalePreviewNpc",
            layperson: "The NPC shown as the 3D preview model when editing male body morph templates.",
            technical: "Mirror of Settings_BodyGen.PreviewNpcMale (a FormKey). The inline Character Viewer in each male BodyGen template editor loads this NPC's body as the preview target. Kept separate from the female pick because male and female rigs need different preview subjects.",
            motivation: "Lets male template authors preview morphs on an appropriate body without changing the female preview setup.");

        Add("BodyGen.FemalePreviewSliderGroup",
            layperson: "The body type name (like CBBE, UNP, or 3BA) used when previewing female morph templates, so the preview knows which slider definitions to load.",
            technical: "Mirror of Settings_BodyGen.PreviewSliderGroupFemale. The template editor's Character Viewer resolves .tri/OSD morph data for the named slider group when deforming the preview body. Must match the body actually installed for the preview to be meaningful.",
            motivation: "The same morph name can exist across body ecosystems with different effects; naming the slider group makes the preview resolve the correct morph data.");

        Add("BodyGen.MalePreviewSliderGroup",
            layperson: "The body type name (like HIMBO or SAM) used when previewing male morph templates, so the preview knows which slider definitions to load.",
            technical: "Mirror of Settings_BodyGen.PreviewSliderGroupMale. Works exactly like the female slider group but for the male template editor's Character Viewer, since male body ecosystems ship their own slider sets.",
            motivation: "Male bodies use different slider ecosystems than female ones, so the preview needs an independent male slider-group setting.");
    }
}

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

        Add("OBody.DescriptorRulesOnly",
            layperson: "Hides this whole descriptor category from the menus where you hand descriptors out to NPCs, while keeping it usable by the automatic labeling rules. Tick it for a category that only exists so other rules can read it - a stepping stone, not a label you would ever assign yourself.",
            technical: "Sets BodyShapeDescriptorShell.IsRulesOnly. VM_BodyShapeDescriptorSelectionMenu skips flagged categories, so they never appear in the pickers used by asset-pack subgroups, BodyGen templates, or per-BodySlide distribution rules. Everything else still sees them: the Body Type Profile editor keeps their rules in the Rules tree, offers their values in DescriptorRef conditions, and the classifier still emits them onto presets, so a Label by Measurements rule can gate on [ShoulderWidth:Wide] exactly as on any other category. Flag state travels with the category through descriptor import/export; an already-present category keeps its own flag on merge rather than adopting the incoming one.",
            motivation: "Aggregator rules read better when the intermediate steps are real categories - Build can say [BicepBulge:Pronounced] instead of repeating a raw measurement threshold - but every such helper category used to land in the descriptor list a config author picks from, burying the dozen labels that actually describe a body. Omitting the category from the catalog instead is worse: the rules still evaluate, but the editor only builds tree nodes for catalog-backed categories, so they become orphans you can neither see nor edit. This flag keeps the category fully editable and hides it only where it would be noise.");

        Add("OBody.AttributeGroupsMenu",
            layperson: "Opens the editor for attribute groups used by this menu. An attribute group is a reusable, named bundle of NPC traits (like factions or classes) that body shape rules can refer to by name.",
            technical: "Edits Settings_OBody.AttributeGroups, the OBody-local copy of the named attribute-group definitions. BodySlide and descriptor rules reference groups by label; resolution goes through NPCAttribute.GetAttributeGroupByLabel, where GeneralSettings.OverwritePluginAttGroups decides whether a same-named General group supersedes this local one.",
            motivation: "Local attribute groups let a shared OBody settings file carry its own group definitions so it works on machines where the recipient never defined them, while the General set still allows centralized management.");

        Add("OBody.MiscSettingsMenu",
            layperson: "Opens the remaining BodySlide options: how assignments are handed to OBody or AutoBody in-game, automatic labeling of unlabeled presets, and settings for the preset classifier and 3D preview.",
            technical: "Displays VM_OBodyMiscSettings: the OBody/AutoBody assignment mode selectors, auto-annotation toggle, verbose-script toggle, slider-catalog overrides, body-type family compatibility map, and the per-weight preview NPC table. All values round-trip to Settings_OBody via CopyIn/DumpViewModelToModel.",
            motivation: "These options affect the whole BodySlide pipeline rather than any single preset, so they live on their own page instead of being repeated per BodySlide.");

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
            layperson: "Opens Auto-Annotation: the rules that label body presets automatically. A rule can look at measurements of the 3D body, at the preset's slider values, or at labels other rules already gave, and every preset category you haven't labeled by hand gets the labels its rules produce.",
            technical: "Edits the Auto-Annotation profile of each body type's active Body Type Rules file (BodyTypeRuleSet.MeasurementProfile; at runtime Settings_OBody.BodyTypeProfiles): key vertices, regions, measurement definitions, and rules whose conditions are Measurement, DescriptorRef or Slider kind, all evaluated in one dependency-ordered pass. Automatic assignments are computed on demand by AutoAnnotationService (when a preset is selected, by the BodySlides Descriptor Filter, and before patching) from the preset's sliders plus its measurement-cache entry, and are never stored. A Manual label owns its (weight slot, category).",
            motivation: "One engine for every kind of rule means a slider threshold, a mesh measurement and a combination of other labels can all describe the same category, and the patcher always sees labels that match the current rules.");

        // ---------- (O/Auto)Body Integration: Misc Settings page ----------

        Add("OBody.SetRaceMenuIni",
            layperson: "Fixes RaceMenu's settings file so that body presets assigned by OBody or AutoBody actually show up in the game. Click it once after installing; a confirmation message appears when it works.",
            technical: "Runs RaceMenuIniHandler.SetRaceMenuIniForBodySlide, which edits the SKEE ini (skee64.ini, or skeevr.ini on VR) in the game Data folder: sets bEnableBodyMorph=1 and bEnableBodyGen=0. The BodyGen menu has the opposite counterpart for morph-based distribution.",
            motivation: "BodySlide-style morphs and BodyGen are mutually exclusive RaceMenu features and the required ini flags are easy to get wrong by hand; this button applies the correct combination in one click.");

        Add("OBody.AutoApplyMissingAnnotations",
            layperson: "When checked, every body preset gets the labels the Auto-Annotation rules give it, for every category you haven't labeled by hand. The automatic labels are never saved; they are worked out again whenever they are needed, so they always follow the current rules.",
            technical: "Mirror of Settings_OBody.AutoApplyMissingAnnotations, the on/off switch for AutoAnnotationService: the BodySlides preset editor shows automatic labels when a preset is selected, the Descriptor Filter and 'remove unannotated' include them, and the patcher computes them into its working copy of the presets before distribution (Patcher.RunPatcher). Categories whose rules need a measurement the preset hasn't been scanned for get no automatic label. Pre-run validation skips the missing-annotation warning while this is enabled.",
            motivation: "Descriptor-based rules only work if presets carry descriptors. Automatic labels give every preset sensible ones without hand-annotating the whole BodySlide library, and computing them on demand keeps them from going stale when rules change.");

        Add("OBody.BodyTypeRuleFiles",
            layperson: "Your Auto-Annotation rules are stored as separate files, one or more per body type, so they can be shared. Each row picks which file a body type uses.",
            technical: "One VM_BodyTypeRuleFileSelection per body type with at least one BodyTypeRuleSet in 'Body Type Rules\\'. The selection persists in Settings_OBody.SelectedRuleFileByBodyType (by file name). SettingsIO_BodyTypeRules projects each active file into Settings_OBody.BodyTypeProfiles at load and folds the editor's state back into the active files on save. Older files' Label by Sliders rules are converted to Slider-condition rules on load (SliderRuleMigration). Switching flushes the editor's current state into the file being left, then reloads that body type from the new file.",
            motivation: "Separate files per body type make a tuned rule set easy to share or swap without touching the rest of the settings.");

        Add("OBody.BodyTypeRuleFilesOpenFolder",
            layperson: "Opens the Body Type Rules folder. Drop rule files you downloaded here, then restart SynthEBD to pick them up.",
            technical: "WinExplorerOpener.OpenFolder(SynthEBDPaths.BodyTypeRulesDirPath). Files are scanned at settings load (SettingsIO_BodyTypeRules.LoadRuleSets); unreadable files are logged and skipped, never overwritten.",
            motivation: "Rule files are meant to be exchanged like asset-pack configs; this is the quickest way to install or back one up.");

        Add("OBody.BodyTypeRuleFileSelect",
            layperson: "Chooses the active rule file for this body type. If the file uses body shape descriptors you don't have, you're asked whether to import them; your existing descriptors are never changed.",
            technical: "Setting VM_BodyTypeRuleFileSelection.SelectedFile calls VM_SettingsOBody.SwitchActiveRuleFile: flush the editor into the previous file, offer missing DescriptorDefinitions (DescriptorUI.MergeInMissingModels, Skip mode; a skip is remembered in Settings_OBody.DeclinedDescriptorImports), then ReplaceProfileForBodyType.",
            motivation: "Swapping files is how you try a different rule set for one body type without losing the current one.");

        Add("OBody.BodyTypeRuleFileName",
            layperson: "The display name of the selected rule file, shown in the list. The file keeps its name on disk.",
            technical: "Edits BodyTypeRuleSet.Name in place; written on the next save. The file name (and therefore the saved selection key) is unaffected.",
            motivation: "Two downloaded files can share a file name; a separate display name lets them be told apart without renaming files on disk.");

        Add("OBody.BodyTypeRuleFileDuplicate",
            layperson: "Copies the selected rule file, including any unsaved edits, and switches this body type to the copy - handy for making your own variant of a shared file.",
            technical: "FlushLabelingStateIntoRuleFile into the source, then SettingsIO_BodyTypeRules.Duplicate: a deep copy named '<Name> (copy)' with a unique file path, added to PatcherState.BodyTypeRuleSets and selected. The copy's measurement profile keeps its Id and Name so it shares the measurement cache. Written on the next save.",
            motivation: "Modifying a shipped or downloaded file in place makes it impossible to tell your edits from the original; duplicating first keeps both.");

        Add("OBody.OBodyAssignmentMode",
            layperson: "Chooses how your body assignments reach OBody in the game. Native writes them straight into OBody's own settings file; Script uses a small in-game script to apply them instead.",
            technical: "Mirror of Settings_OBody.OBodySelectionMode. Native mode writes an OBody NG include file, SKSE/Plugins/OBody/obody_includes/SynthEBD.json, holding only an npcFormID section built from Patcher.BodySlideTracker (OBodyWriter.WriteNativeAssignmentDictionary); OBody merges it into its own config at startup, so the user's OBody_presetDistributionConfig.json is never touched. Script mode instead emits SynthEBD/BodySlideAssignments.json plus a spell/magic-effect/loader-quest Papyrus setup that calls OBody's API per NPC, which requires JContainers.",
            motivation: "Native mode is simpler and lighter at runtime since OBody reads its own config directly; Script mode predates it and remains for setups where writing OBody's config file is not desirable.");

        Add("OBody.OBodyEnableMultipleAssignments",
            layperson: "When checked, SynthEBD gives OBody the full list of body presets that fit each NPC instead of picking just one, and OBody randomly picks one in the game.",
            technical: "Mirror of Settings_OBody.OBodyEnableMultipleAssignments (Native mode only). OBodySelector records every rule-compliant preset for the NPC into Patcher.BodySlideTracker rather than a single selection, and all of them are written to the OBody include file. Consistency for BodySlide selection is bypassed in this mode since the final pick happens in-game.",
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

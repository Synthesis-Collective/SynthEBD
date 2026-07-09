namespace SynthEBD;

// 3-part documentation for the Body Type Profile editor subsystem: the measurement-based
// BodySlide classifier authoring menu (UC_BodyTypeProfileEditor), its satellite windows
// (Window_MeasurementHistogram, Window_RuleDeleteExportPicker), and the Suggest Measurements
// panel. State-dependent diagnostic tooltips in the editor's resource styles (duplicate-name
// washes, resolution-state badges, invalid-reference washes) intentionally keep their inline
// DataTrigger tooltips: their text changes per trigger state, which a single static
// DocTooltip key cannot model.
public static partial class UiDocs
{
    private static void RegisterBodyTypeProfiles()
    {
        // ---------- Profile toolbar ----------

        Add("BodyTypeProfiles.KeyboardShortcuts",
            layperson: "Shows a list of every keyboard shortcut that works in this editor, organized by tab. Most of the power-user save/load/export tricks here are keyboard-only.",
            technical: "Opens a window with a hand-maintained reference of the editor's InputBindings: the per-tab Ctrl+S/Ctrl+L JSON snapshot and patch shortcuts, the CSV exports, clipboard copies, region undo/redo, and the global preset-iteration shortcuts. The text is hard-coded in UC_BodyTypeProfileEditor.xaml.cs (KeyboardShortcuts_Click) and updated alongside the XAML KeyBindings.",
            motivation: "WPF InputBindings are invisible in the UI; without a consolidated list the import/export shortcuts would be undiscoverable.");

        Add("BodyTypeProfiles.DuplicateProfile",
            layperson: "Makes a copy of the selected profile right here in the list, so you can experiment without touching the original.",
            technical: "Runs DoDuplicateProfile: round-trips the profile through DumpToModel for a fully independent deep clone (no shared row VMs, measurement cache, or annotations), assigns a fresh Id, names the copy '<name> - copy' (with an ' (N)' suffix on collision), and selects it for editing.",
            motivation: "Equivalent to Export-then-Import without the disk round-trip - the quickest way to branch a working profile before risky edits.");

        Add("BodyTypeProfiles.ExportProfile",
            layperson: "Saves the selected profile to a JSON file so you can share it or keep a backup.",
            technical: "Serializes SelectedProfile.DumpToModel() to JSON via a save dialog; the suggested filename is the sanitized profile name. Everything the profile owns - key vertices, regions, measurements, rules, annotations - goes into the one file.",
            motivation: "A profile encodes hours of per-body-type authoring; a single-file export makes that work shareable and survivable across installs.");

        Add("BodyTypeProfiles.ImportProfile",
            layperson: "Loads a profile from a JSON file and adds it to the list alongside your existing profiles.",
            technical: "Deserializes a BodyTypeProfile JSON (as written by Export) and appends it to the Profiles collection; a name collision gets the same ' (N)' suffix the Duplicate path uses.",
            motivation: "Lets users install profiles authored by others, or restore backups, without hand-editing the settings file.");

        Add("BodyTypeProfiles.ViewerSplitter",
            layperson: "Drag left or right to change how much space the profile editor and the 3D preview get.",
            technical: "GridSplitter between the tabbed profile pane and the embedded Character Viewer column (ResizeBehavior=PreviousAndNext, ShowsPreview). Layout only - nothing is persisted.",
            motivation: "Vertex picking wants a big viewer while grid editing wants a wide table; the splitter lets each task claim the space temporarily.");

        // ---------- Right rail: preview preset picker ----------

        Add("BodyTypeProfiles.RefreshPresets",
            layperson: "Rebuilds the preset dropdown from the BodySlides menu, picking up presets added or changed since this editor was opened.",
            technical: "Runs RebuildAvailablePresets, repopulating AvailablePresets from VM_SettingsOBody.BodySlidesUI filtered by the Gender selector; FilteredPresets then re-applies the text filter on top.",
            motivation: "The editor snapshots the preset list rather than live-tracking the BodySlides menu, so a manual refresh is needed after changes there.");

        Add("BodyTypeProfiles.PresetFilter",
            layperson: "Type part of a preset name to shrink the Preset dropdown to matching entries.",
            technical: "PresetFilterText drives FilteredPresets, a substring-matched view of AvailablePresets on each preset's Label, re-filtered on every keystroke.",
            motivation: "Installations easily reach hundreds of presets; substring filtering beats scrolling a raw dropdown.");

        Add("BodyTypeProfiles.PreviewNpcOverride",
            layperson: "Pick a specific NPC to model the preview body. Leave it blank to use the automatic stand-in chosen for the current weight.",
            technical: "Sets PreviewNpcOverride (a FormKey). When blank, the per-weight preview NPC configured in OBody Misc Settings is used - the same policy as the BodySlides menu preview.",
            motivation: "The weight-matched defaults are usually right, but sometimes a preset needs checking on a particular character, such as a custom race.");

        // ---------- Key Vertices tab ----------

        Add("BodyTypeProfiles.CapturePicks",
            layperson: "While checked, every vertex you click in the 3D viewer is added to this profile's Key Vertices table automatically.",
            technical: "Mirror of VM_BodyTypeProfile.CapturePicks: key-vertex picks fired by any attached viewer append a new row to KeyVertices. The viewer's Pick Vertex mode must be on for clicks to register as picks at all.",
            motivation: "Authoring dozens of anatomical reference points is far faster by clicking the mesh than by typing vertex indices.");

        Add("BodyTypeProfiles.PickAsCoordinate",
            layperson: "While checked (together with capture), clicked vertices are saved by position instead of by number, so they survive body mods that renumber their meshes.",
            technical: "Captured picks are stored as KeyVertexStrategy.Coordinate rows: the vertex's zeroed-space (sliders-0) position is persisted and matched to the nearest vertex on the current zeroed mesh at evaluation, instead of trusting a raw Explicit index. An existing row converts by switching its Strategy dropdown to Coordinate.",
            motivation: "Explicit indices break when a body variant is rebuilt with different vertex numbering; coordinate anchoring keeps naming the same anatomy across rebuilds.");

        Add("BodyTypeProfiles.CaptureSelectedPicks",
            layperson: "Copies the picks currently highlighted in the viewer's pick list into this profile as Key Vertex rows.",
            technical: "One-shot import that bypasses the 'Capture picks from viewer' toggle: appends the viewer's SelectedPicks as rows, skipping any (shape, vertex index) pair already in the profile, so re-clicking is a safe no-op.",
            motivation: "Lets you click around the viewer exploring freely, then promote only the picks worth keeping, instead of every stray click flowing into the roster.");

        Add("BodyTypeProfiles.ShowPicksInViewer",
            layperson: "Draws this profile's saved Key Vertices as markers on the 3D preview so you can see where they sit.",
            technical: "Sends every row with a shape name and a non-negative vertex index to the active viewer's pick-marker overlay (ShowKeyVerticesInViewer); rows that do not resolve on the current mesh are silently skipped.",
            motivation: "After reloading a saved profile the picks exist only as numbers; re-visualizing them confirms they still land on the intended anatomy.");

        Add("BodyTypeProfiles.AddKeyVertexFromRegion",
            layperson: "Adds a Key Vertex that searches inside one of your saved Regions instead of inside a drawn box. Requires at least one Region on the Regions tab.",
            technical: "Creates a KeyVertexStrategy.Region row pre-pointed at the first available region; the row's Criterion then picks one vertex from the region's member set (box plus added minus removed vertices, resolved in zeroed space). With no regions defined, a notification is shown instead of adding a row that could never resolve.",
            motivation: "Hand-curated regions can trace anatomy no axis-aligned box isolates cleanly; reusing them as key-vertex candidate sets avoids re-authoring the same patch.");

        Add("BodyTypeProfiles.KeyVertexRegionRef",
            layperson: "For a Region-type Key Vertex: which saved Region to search for the winning vertex.",
            technical: "Sets VM_NamedKeyVertex.RegionRefName. At evaluation the row scans the named region's member vertices and applies the row's Criterion, exactly like the BoundingBox strategy but with the candidate set scoped to the region. A red wash means the name no longer matches any row on the Regions tab.",
            motivation: "Referencing regions by name lets one region serve several key vertices and measurements at once.");

        Add("BodyTypeProfiles.KeyVertexCoordinate",
            layperson: "The saved 3D position of this Coordinate-type Key Vertex, in the body's neutral (all-sliders-zero) shape.",
            technical: "Read-only display of CoordX/Y/Z, captured at pick time on the sliders-0 mesh. At evaluation the position is matched to the nearest vertex on the current zeroed mesh and that vertex's deformed position feeds measurements; re-pick with 'Pick as coordinate' enabled to move it.",
            motivation: "Showing the anchor makes it obvious the row is position-based rather than index-based, and offers a sanity check that the stored point is where you think it is.");

        // ---------- Regions tab ----------

        Add("BodyTypeProfiles.RegionViewMode",
            layperson: "Chooses how the selected Region is drawn on the preview body: just its cut outline, or the whole patch as a solid highlight.",
            technical: "Sets RegionViewMode (RegionViewModeKind). End-Cap draws only the cyan cut contour; Solid draws the region as a magenta filled object visible through the body from any angle, with cyan vertices and edges on top. A per-session viewing preference - not persisted.",
            motivation: "The outline is enough while positioning the box; the solid view reveals stray vertices that hand edits included or excluded.");

        Add("BodyTypeProfiles.RegionUndo",
            layperson: "Undoes your last vertex add or remove on the selected Region (Ctrl+Z also works).",
            technical: "Pops the per-region undo stack: one click or one lasso counts as a single action, capped at 50 snapshots (RegionEditHistoryCap). The history belongs to the selected region and clears when you select a different one.",
            motivation: "A lasso can grab dozens of unintended vertices in one stroke; action-level undo makes that recoverable without redrawing the region.");

        Add("BodyTypeProfiles.RegionRedo",
            layperson: "Re-applies the vertex edit you just undid (Ctrl+Y also works).",
            technical: "Pops the per-region redo stack; performing any new edit clears it, since a new action invalidates the redo branch.",
            motivation: "Standard undo/redo pairing keeps experimenting with region membership cheap.");

        Add("BodyTypeProfiles.RegionCapMode",
            layperson: "How the opening of the region is sealed when its volume is computed, which changes what the volume number means.",
            technical: "Sets VM_NamedRegion.CapMode. FlatPlane closes the boundary loop with a flat plane - the volume is tissue protruding past a 'salami cut', with no scoop. AnatomicalFan closes it with a fan following the deformed boundary ring - the volume is what the ring encloses, which can look scooped on a curvy bust.",
            motivation: "Fullness discriminators such as cup size need a stable definition of enclosed volume; the two cap modes trade robustness against anatomical faithfulness.");

        Add("BodyTypeProfiles.RegionDefinedOn",
            layperson: "Which preset and weight were loaded when this Region's box was drawn.",
            technical: "Read-only DefiningPresetLabel / DefiningWeight recordkeeping. The box itself is stored in undeformed (sliders-0) space; these fields only record the authoring context so the same slice can be reloaded for re-editing.",
            motivation: "A box drawn against one preset can look misplaced against another; knowing the original context removes the guesswork when refining it later.");

        Add("BodyTypeProfiles.RegionRotation",
            layperson: "Tilts the Region's box around its center, in degrees, so the cut can follow a feature that is not square to the body.",
            technical: "RotX/RotY/RotZ Euler angles applied about the box center; 0/0/0 keeps the box axis-aligned. The rotated box governs both which vertices are members and the orientation of the cut plane.",
            motivation: "Breast and glute cuts rarely align with the world axes; without rotation the flat cap would slice anatomy at an angle.");

        Add("BodyTypeProfiles.RegionVertexEdits",
            layperson: "Shows how many vertices you have added or removed by hand on top of the box, with a button to throw those edits away.",
            technical: "EditedVertexCount badges the row's curated RegionVertexEdit list, stored in zeroed space. Any edits switch the region's resolve onto the vertex-granular induced patch (box union adds minus removes) instead of the smooth box clip; Clear (ClearEditsCommand) reverts to the plain box. Edit by selecting the row and enabling 'Edit Region Verts' on the viewer toolbar.",
            motivation: "Boxes are fast but blunt; per-vertex curation fixes the stragglers a box cannot isolate, and the badge keeps hand-tuned regions visibly distinct from plain ones.");

        // ---------- Measurements tab ----------

        Add("BodyTypeProfiles.MeasurementRegionRef",
            layperson: "For a RegionVolume measurement: which saved Region's enclosed volume to measure.",
            technical: "Sets VM_MeasurementDefinition.RegionRefName, which only the MeasurementKind.RegionVolume kind reads (the vertex refs A-D are ignored for it). The named region's surface patch is clipped to the box, capped along its boundary loop(s), and integrated per preset by RegionVolumeEvaluator.",
            motivation: "Volume discriminates fullness (for example cup size) where any two-point distance misreads long/narrow versus broad/flat geometry.");

        Add("BodyTypeProfiles.MeasurementNumeratorAxis",
            layperson: "For a Ratio measurement: whether the top of the ratio uses the full 3D distance between points A and B, or only their separation along one axis.",
            technical: "Sets NumeratorAxis on a RatioDistance measurement. The '3D' entry (null) is the legacy full Euclidean length of A-B; X/Y/Z projects the pair onto that axis before dividing. Ignored for all other kinds.",
            motivation: "Axis projection isolates the anatomical direction that matters - waist width rather than a waist diagonal - making ratios sharper discriminators.");

        Add("BodyTypeProfiles.MeasurementDenominatorAxis",
            layperson: "For a Ratio measurement: the same axis choice, but for the bottom pair (points C and D).",
            technical: "Sets DenominatorAxis for the C-D pair of a RatioDistance measurement; null keeps the legacy 3D Euclidean length, X/Y/Z projects onto that axis. Ignored for non-ratio kinds.",
            motivation: "Numerator and denominator often want different axes (bust depth over back width), so each pair gets its own reduction.");

        Add("BodyTypeProfiles.OpenHistogram",
            layperson: "Opens a chart showing how this measurement's value is spread across all of your scanned presets and weights.",
            technical: "Opens Window_MeasurementHistogram over a snapshot of the profile's MeasurementCache for this measurement - one sample per cached (preset, weight) entry - driving a scan first if the cache is stale. The snapshot is frozen; re-open the window after edits or re-scans to see new numbers.",
            motivation: "Rule thresholds are picked off the population's distribution; seeing where presets cluster shows where a cut line actually separates body types.");

        // ---------- Rules tab: descriptor catalog ----------

        Add("BodyTypeProfiles.NewDescriptorCategory",
            layperson: "The category name for a new descriptor, like Shape or Chest. Use an existing name to add another value under it, or a new name to start a category.",
            technical: "NewCategoryInput, read together with NewValueInput by AddDescriptorCommand, which appends a TemplateDescriptor through the editor's AvailableDescriptors (backed by Settings_OBody.TemplateDescriptors).",
            motivation: "Rules can only emit descriptors that exist in the catalog; the inline form saves a trip to the descriptors editor while authoring rules.");

        Add("BodyTypeProfiles.NewDescriptorValue",
            layperson: "The value name for the new descriptor, like Hourglass or Busty, filed under the Category above.",
            technical: "NewValueInput - the Value half of the (Category, Value) pair that AddDescriptorCommand appends to Settings_OBody.TemplateDescriptors.",
            motivation: "Descriptors are two-part labels; split inputs make the category-versus-value structure explicit.");

        Add("BodyTypeProfiles.AddDescriptor",
            layperson: "Adds the Category and Value typed above to the descriptor catalog and selects the new entry in the tree.",
            technical: "AddDescriptorCommand validates the two inputs (CanAddDescriptor) and appends the pair to Settings_OBody.TemplateDescriptors via the editor's AvailableDescriptors, then selects the affected tree branch.",
            motivation: "New body-type distinctions start with a new label; adding it here means rules for it can be authored immediately in the same tab.");

        Add("BodyTypeProfiles.DeleteDescriptorNode",
            layperson: "Removes the selected Category or Value from the descriptor catalog. Refuses while any rule still points at it.",
            technical: "DeleteSelectedTreeNodeCommand, gated by CanDeleteSelectedTreeNode: the node must have no rules producing a descriptor under it - move or delete those rules first. Deletion edits Settings_OBody.TemplateDescriptors.",
            motivation: "Deleting a label out from under live rules would orphan them silently; the guard forces the rules to be dealt with consciously.");

        Add("BodyTypeProfiles.MakeDefaultValue",
            layperson: "Marks this value as the fallback for its category: any preset that matches none of the category's rules gets this label.",
            technical: "TwoWay-bound to VM_RuleTreeValueNode.IsDefault, which routes to the profile's SetDefaultValueForCategory map. One default per category is enforced: once a value is the default, the checkbox is hidden on its siblings until it is unchecked.",
            motivation: "Without a fallback, presets matching no rule end up unlabeled and invisible to descriptor-based distribution; a default guarantees every preset lands somewhere.");

        // ---------- Rules tab: rule editor ----------

        Add("BodyTypeProfiles.AddRule",
            layperson: "Adds a new classification rule. If a category or value is selected in the tree, the rule starts pre-filled with it.",
            technical: "Creates a MeasurementRule seeded from the selected tree node (a Value node fills Category and Value; a Category node fills Category only) with one empty AND group, and appends it to the profile's Rules; the tree and filtered list refresh via the Rules CollectionChanged hook.",
            motivation: "Rules are always authored in the context of the descriptor they emit; pre-filling from the tree selection removes the most error-prone step.");

        Add("BodyTypeProfiles.RuleDescriptorCategory",
            layperson: "The category half of the label this rule hands out when a preset matches it.",
            technical: "Bound to VM_MeasurementRule.DescriptorCategory; the options come from Settings_OBody.TemplateDescriptors via the editor's AvailableDescriptors. Add new entries through the catalog pane on the left or the OBody descriptors editor.",
            motivation: "Tying rules to catalog descriptors keeps rule output consistent with the labels that BodySlide distribution rules actually filter on.");

        Add("BodyTypeProfiles.RuleDescriptorValue",
            layperson: "The value half of the label this rule hands out, chosen from the values defined under the selected category.",
            technical: "Bound to DescriptorValue; the options list is AvailableDescriptorValuesFor(DescriptorCategory) and refilters automatically when the category changes.",
            motivation: "Filtering by category prevents authoring a (Category, Value) pair that does not exist in the catalog.");

        Add("BodyTypeProfiles.RuleGender",
            layperson: "Limits the rule to male or female presets, or fires it for both (Either).",
            technical: "Bound to VM_MeasurementRule.Gender (RuleGender). Either fires regardless of preset gender; Male/Female fire only when the evaluator knows the preset's gender matches (call sites without a known gender run Either rules only). The Either default preserves legacy behavior.",
            motivation: "The same descriptor can need different physical signal per gender - male Powerful must tolerate measurements that female rules would call Chubby, since geometry alone cannot split fat from muscle.");

        Add("BodyTypeProfiles.RuleDraft",
            layperson: "Keeps the rule in try-out mode: this editor still evaluates it so you can calibrate, but the real automatic labeling ignores it until you promote it.",
            technical: "Bound to IsDraft. BodySlideMeasurementEvaluator skips draft rules unless explicitly told to include them; the editor's Preview, Match Presets, and rule-node lists do include drafts so they can be calibrated (Preview shows them in orange). The Promote button clears the flag.",
            motivation: "New thresholds need tuning against the preset population before they may affect distribution; the draft gate makes that experimentation safe.");

        Add("BodyTypeProfiles.BranchDisable",
            layperson: "Temporarily switches this OR-branch off without deleting it - the rule behaves as if the branch were not there.",
            technical: "Bound to VM_AndGatedMeasurementGroup.IsDisabled; the evaluator (MeasurementMath.RuleMatches) skips disabled groups, and the flag round-trips through save/load. Toggling flips the scan cache stale so the matching lists re-derive without the branch's contribution.",
            motivation: "Isolating which OR-branch causes unwanted matches is much faster when branches can be muted and unmuted instead of retyped.");

        Add("BodyTypeProfiles.BranchTempEdits",
            layperson: "Starts a what-if session on this branch: as you edit its conditions, the preset list below shows in green and red which presets would start or stop matching.",
            technical: "StartTempEdit snapshots the branch and its baseline match set (VM_BodyTypeProfile.BeginTempEdit); the rule-node matching list then renders a live conform-diff on every edit - green '+ ' rows newly conform, red '- ' rows no longer conform (RuleMatchDiffState). The button swaps to Save/Discard while the session runs.",
            motivation: "Threshold tuning is guesswork without immediate feedback; the diff shows the real population impact of each edit before you commit it.");

        Add("BodyTypeProfiles.BranchTempSave",
            layperson: "Keeps the changes you made during the what-if session and ends it.",
            technical: "SaveTempEdit accepts the branch's current state, discards the session snapshot, and clears IsTempEditing; the matching list drops its diff coloring.",
            motivation: "An explicit accept step makes ending a preview session deliberate rather than accidental.");

        Add("BodyTypeProfiles.BranchTempDiscard",
            layperson: "Throws away everything you changed during the what-if session, restoring the branch exactly as it was.",
            technical: "DiscardTempEdit restores the branch's conditions and disabled flag from the snapshot taken at session start (RestoreFrom) and ends the session.",
            motivation: "Guaranteed rollback is what makes aggressive experimentation on live rules safe.");

        Add("BodyTypeProfiles.ConditionKind",
            layperson: "What this condition tests: a body measurement against a number, or whether a label applies to the preset - from another rule here, from the Label by Sliders rules, or applied by hand.",
            technical: "Bound to MeasurementConditionKind. Measurement is the classic 'measurement comparator threshold' test; DescriptorRef tests membership in the matched-descriptor set, honoring NOT. That set holds descriptors produced by rules earlier in the topological evaluation order PLUS an external seed (CollectExternalDescriptors): labels the Label by Sliders rules produce for this preset at the evaluated weight - derived live from the current rule set, so slider-rule drafts count without an apply pass - and the preset's stored Manual/Library annotations. Prior Classifier output is excluded so re-runs never read their own results, and a category covered by a seed skips its measurement-side default. The inactive kind's fields stay in memory, so flipping back loses nothing.",
            motivation: "DescriptorRef enables aggregator rules - for example Realism:Unrealistic defined as 'any Unrealistic* descriptor fired' - without duplicating every underlying threshold. Live slider-label seeding lets the two labeling systems compose while you iterate: a slider rule can assign Belly:Muscular from MuscleAbs, and measurement rules for Belly:Chubby/Fat can exclude it, with edits on either side visible to the other immediately.");

        Add("BodyTypeProfiles.ConditionNegate",
            layperson: "Flips the check: the condition passes only if the referenced label did NOT apply.",
            technical: "Bound to Negate on a DescriptorRef condition; the evaluator inverts the membership test, and the live readout's Match / Not Match honors the inversion.",
            motivation: "Exclusion predicates like 'curvy but not unrealistic' would otherwise require authoring mirror-image rules.");

        Add("BodyTypeProfiles.ConditionRefCategory",
            layperson: "The category of the label that this condition checks for. The label can come from another rule here, from the Label by Sliders rules, or from a manual annotation.",
            technical: "Bound to RefCategory; options come from GetSafeDescriptorRefsForCondition, which offers the full descriptor catalog minus any choice that would create a circular dependency between rules referencing each other's output. A descriptor no measurement rule produces is always cycle-safe and resolves at evaluation against the live Label by Sliders rule output and the preset's manual/library annotations.",
            motivation: "Cycle-safe options at the source prevent authoring reference chains the topological evaluator could never order.");

        Add("BodyTypeProfiles.ConditionRefValue",
            layperson: "The value of the label that this condition checks for.",
            technical: "Bound to RefValue; options are the cycle-safe references filtered to the selected RefCategory, refiltered automatically when the category changes.",
            motivation: "Same cycle-safety guarantee as the category picker, narrowed to a concrete label.");

        Add("BodyTypeProfiles.ConditionReadout",
            layperson: "Live result of this condition for the preset shown in the viewer - green means it passes, red means it fails.",
            technical: "ConditionReadout: for Measurement conditions, the named measurement's LiveValue tested with the comparator; for DescriptorRef conditions, 'Match' or 'Not Match' against the previewed preset's matched descriptors - rule matches, materialized category defaults, the preset's live-derived Label by Sliders labels at the previewed weight, and its stored manual/library annotations - honoring NOT. A gray dash means no preset is loaded or the value cannot resolve; the color is driven by ConditionConforms.",
            motivation: "Seeing each condition pass or fail against a live preset pinpoints exactly which clause blocks an expected match.");

        // ---------- Rules tab: matching-presets pane ----------

        Add("BodyTypeProfiles.RuleNodeSort",
            layperson: "Reorders the matching-presets list below: by name, or by any measurement so the most extreme presets rise to the top.",
            technical: "Bound to SelectedRuleNodeSortOption. 'Name' keeps the default (Gender, Preset, Weight) order; any measurement name sorts rows by that metric's cached value descending, with slices missing the value sinking to the bottom. Measurements referenced by the displayed node's rules are listed first in the dropdown.",
            motivation: "Ranking matches by the metric a rule keys on surfaces the borderline cases - the presets that only just cleared a threshold - which is where calibration attention belongs.");

        Add("BodyTypeProfiles.RuleNodeShowMeasurements",
            layperson: "Draws every measurement used by the selected rules as lines on the 3D preview, updating as you switch nodes or edit conditions.",
            technical: "ShowRuleNodeMeasurements pushes the measurements referenced by the rules under the selected tree node (or by the temp-edited rule during a session) into the viewer's measurement-line overlay - the same channel the Measurements tab uses. While on, it overrides the Measurements-grid selection; unchecking restores it.",
            motivation: "Rules are abstract until you see which anatomy their measurements actually span on the loaded preset.");

        Add("BodyTypeProfiles.RuleNodeMatchList",
            layperson: "Presets that currently receive the selected label, with the numbers that got them there. Click a row to load it in the 3D preview; arrow keys step through with live previews.",
            technical: "SelectedNodeMatchingPresets, derived from the profile's scan cache (ScanResults plus MeasurementCache) for the rules under the selected tree node; each row lists every referenced measurement's cached value. Selecting a row sets SelectedNodeMatchRow, which loads that (preset, weight) slice in the viewer. The list stays empty with an inline reason while the cache is stale or absent.",
            motivation: "Stepping through the real population a rule captures closes the loop between editing thresholds and seeing their effect.");

        // ---------- Match Presets tab ----------

        Add("BodyTypeProfiles.VerboseScan",
            layperson: "Makes the scan write extra diagnostic detail to the Status Log. Leave off unless a scan is misbehaving.",
            technical: "Mirror of VerboseScan, read by RunScanAsync: emits the target count, viewer/key-vertex/fingerprint shape comparisons, a first-iteration measurement snapshot, a first-vs-last identity check, and a summary.",
            motivation: "The usual failure mode is a scan that silently matches zero presets; the diagnostics show which stage - shape names, fingerprint, or measurements - went wrong.");

        Add("BodyTypeProfiles.CacheStatus",
            layperson: "How many measurement results are saved on disk for this profile's current body type, so future sessions can skip rescanning.",
            technical: "CacheStatusSummary, refreshed after hydrate, scan, and purge. The disk cache stores measurements (not descriptors) per (preset, gender, weight) under a snapshot keyed by the profile's BodyTypeName; entries invalidate automatically when a preset's sliders change or a measurement / key-vertex definition is edited.",
            motivation: "A full scan deforms and measures every preset at every weight, which is expensive; the cache makes later sessions start warm.");

        Add("BodyTypeProfiles.PurgeCache",
            layperson: "Deletes the saved scan results for this profile's current body type so the next scan starts completely fresh.",
            technical: "PurgeCacheForActiveProfileCommand drops the active BodyTypeName's snapshot from the profile's on-disk cache file and clears the in-memory cache; snapshots for other body types in the same file (for example a dormant BHUNP experiment) are preserved.",
            motivation: "An escape hatch for cache corruption, or for changes the automatic invalidation fingerprint cannot see.");

        Add("BodyTypeProfiles.MatchShowMeasurements",
            layperson: "Draws every measurement used by the rules of the descriptors you have checked above as lines on the 3D preview.",
            technical: "ShowMatchPresetMeasurements pushes the measurements referenced by any rule whose descriptor is checked in the filter into the viewer's measurement-line overlay (the same channel the Measurements grid selection uses), refreshing as the filter selection changes. While on, it overrides the Measurements-tab selection; unchecking restores it.",
            motivation: "When reviewing why filtered presets match, seeing the exact measured spans on the body beats cross-referencing rule text.");

        Add("BodyTypeProfiles.MatchSortMode",
            layperson: "Reorders the results by how strongly each preset matches, or by a raw measurement, instead of alphabetically. Only kicks in when exactly one descriptor value is checked in the filter.",
            technical: "Bound to ScoreSortMode (MarginScoreMode). Match-strength modes score each row against the filtered descriptor's rule: the OR-group with the most slack is chosen and its tightest condition's margin becomes the score, normalized either by the measurement's population standard deviation (reads as 'N standard deviations past the line') or by the threshold (reads as 'N percent past it'). Similarity modes score against a sibling value picked in the 'vs' dropdown, and Measurement mode sorts by a raw cached value. With zero or several descriptors checked, the default (Gender, Preset, Weight) order applies.",
            motivation: "Margin ranking surfaces both the archetypes (deepest matches) and the borderline cases in one glance, which is exactly what threshold calibration needs.");

        Add("BodyTypeProfiles.MatchSimilarityTarget",
            layperson: "For the Similarity sorts: the sibling label to compare against - for example, ranking Hourglass presets by how close they come to also being Rectangle.",
            technical: "Bound to SimilarityTarget; the options are every value in the filtered descriptor's category that has at least one rule, minus the filter's own selected value (comparing to self reduces to the Match-strength sort). Rows are scored against the target's rule instead of the filter's.",
            motivation: "Boundary tuning between adjacent body types needs to see which presets sit near the fence from the other side.");

        Add("BodyTypeProfiles.MatchSortMeasurement",
            layperson: "For the Measurement sort: which measurement's value to rank the list by, largest first.",
            technical: "Bound to SelectedMeasurementForSort. Each surviving row is scored by that measurement's cached value for its (preset, weight); rows whose cache entry lacks the value sink unscored to the bottom. Independent of any rule - it works for measurements no rule references.",
            motivation: "Sorting by a raw metric answers 'which presets have the most of X' directly, which helps decide where a threshold should even sit.");

        Add("BodyTypeProfiles.MatchNameFilter",
            layperson: "Type part of a preset name to hide non-matching rows from the results list.",
            technical: "MatchPresetNameFilter - a case-insensitive substring test on each row's PresetLabel, layered on top of the descriptor, weight, and sort settings and re-applied on every keystroke (an in-memory ScanResults walk, no mesh or GL work).",
            motivation: "Once the descriptor filter still leaves hundreds of rows, a name filter is the fastest path to one specific preset.");

        Add("BodyTypeProfiles.MatchResultsList",
            layperson: "Every preset-and-weight combination that fits the filters above. Click a row to load it in the 3D preview; arrow keys step through with live previews.",
            technical: "MatchingPresets: one VM_PresetScanRow per matching (preset, weight), ordered (Gender, PresetLabel, Weight) unless a score sort is active. Selecting a row (SelectedMatchRow) auto-loads the preset at that row's weight; the blue badge shows the score and its tooltip lists this row's score against every sibling descriptor value.",
            motivation: "Stepping through matches with instant previews is the fastest way to audit whether a rule's population actually looks like the body type it labels.");

        // ---------- Label-then-Suggest: Suggest Measurements panel ----------

        Add("BodyTypeProfiles.SuggestAlgorithm",
            layperson: "Which statistic ranks your measurements by how well they separate the body types you annotated.",
            technical: "Bound to SelectionAlgorithm (MeasurementSelectionAlgorithm): Anova is the one-way ANOVA F-statistic across descriptor-value groups (default); CohenD reports the largest pairwise effect size; InformationGain is the entropy reduction of the best single-threshold split. The choice persists per profile via AnnotatorPreferences.SelectionAlgorithm.",
            motivation: "Different annotation sets favor different statistics - many small groups versus two big ones - so the choice is exposed instead of baked in.");

        Add("BodyTypeProfiles.SuggestRemoveMeasurement",
            layperson: "Drops this measurement from the suggestion list for this category, so rule generation will not use it.",
            technical: "Runs VM_MeasurementSuggestion.RemoveCommand, removing the row from its category group. The curated per-category lists are what the Suggest Rules pass reads (SnapshotByCategory) when synthesizing draft rules.",
            motivation: "Statistically strong measurements can still be anatomically wrong for a category; pruning keeps the generated rules sensible.");

        // ---------- Window: measurement histogram ----------

        Add("BodyTypeProfiles.MeasurementHistogram.GenderFilter",
            layperson: "Show the chart for all samples or just one gender's.",
            technical: "Bound to GenderFilter; the dropdown offers 'All' plus only the genders actually present in the snapshot taken when the window opened. The summary stats line re-derives from the filtered subset, not the full population.",
            motivation: "Male and female presets often form distinct distributions; mixing them can hide exactly the separation you are looking for.");

        Add("BodyTypeProfiles.MeasurementHistogram.BinCount",
            layperson: "How many bars the chart is divided into. Drag to re-bin instantly - more bars suit dense data, fewer suit small samples.",
            technical: "Bound to BinCount (slider range 1-200); every change rebuilds the bins live from the snapshot. A zero-width value range collapses to a single bin so the chart still renders something visible.",
            motivation: "Bin width decides whether real clusters or noise dominate the picture; live re-binning finds the readable granularity fast.");

        Add("BodyTypeProfiles.MeasurementHistogram.PersistBinCount",
            layperson: "Remember this bin count for the next histogram window you open. If several windows are open with this checked, the last one closed wins.",
            technical: "Bound to PersistBinCount; on window close, CommitPersistedSettings writes BinCount and the checkbox state into a process-static store, so the next window opens pre-configured. Resets when SynthEBD restarts - it is deliberately not written to user settings.",
            motivation: "When comparing several measurements back to back, re-dragging the slider to the same value in every window gets old immediately.");

        Add("BodyTypeProfiles.MeasurementHistogram.SaveCsv",
            layperson: "Saves the chart's current bars to a CSV file for spreadsheet analysis.",
            technical: "SaveCsvCommand writes the displayed bins as 'BinIndex,BinStart,BinEnd,Count' rows (edges at F6 precision) via BuildHistogramCsv - byte-identical to the bulk Ctrl+Shift+H export for the same bins. The file reflects the active gender filter and bin count; the button disables when there are no bins.",
            motivation: "External plotting and curve-fitting tools pick up where the built-in chart stops; a stable CSV schema keeps that pipeline simple.");

        // ---------- Window: rules-patch export picker ----------

        Add("BodyTypeProfiles.RuleExport.MarkForDeletion",
            layperson: "Tells the exported patch to delete this rule from whichever profile the patch is later applied to.",
            technical: "Bound to IsMarkedForDeletion; on OK, the rule's Id joins the payload's RulesToDelete list. Independent of the add/edit set - a rule both included and marked resolves in favor of add/edit when the patch is applied (ApplyRulesPatch).",
            motivation: "Sharing rule refinements often means retiring superseded rules on the recipient's side, not just adding new ones.");

        Add("BodyTypeProfiles.RuleExport.ExcludeFromAddEdit",
            layperson: "Removes this rule from the patch's add/update list without leaving the window.",
            technical: "Bound to IsExcludedFromAddEdit; excluded rules stay listed but are filtered out of AddEditModels when the patch payload is built. Only shown on rules pre-seeded from the Rules-tab tree node the export was triggered from.",
            motivation: "The tree-node pre-seed is usually close but not exact; per-rule exclusion refines the set without cancelling and reselecting.");
    }
}

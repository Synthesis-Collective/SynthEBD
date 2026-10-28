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

        Add("BodyTypeProfiles.BodyTypePicker",
            layperson: "Picks which body type you are working on. Each body type has at most one measurement profile at a time: the one inside the rule file currently chosen for it in OBody Misc settings.",
            technical: "Bound to VM_BodyTypeProfileEditor.SelectedBodyType (registry body types plus any body type with a loaded profile), two-way synced with SelectedProfile. A body type whose active BodyTypeRuleSet has no MeasurementProfile selects no profile and enables Create Profile.",
            motivation: "Profiles live in shareable per-body-type rule files, so the editor chooses a body type rather than a free-standing profile; which file supplies the profile is decided in one place (Misc).");

        Add("BodyTypeProfiles.ActiveRuleFile",
            layperson: "The rule file this body type's Auto-Annotation profile is saved into. Change it in OBody Misc settings > Body Type Rules.",
            technical: "VM_OBodyMiscSettings.GetActiveRuleFile(SelectedBodyType): display name and file name of the active BodyTypeRuleSet in 'Body Type Rules\\'. 'none yet' means the body type has no file; one is created on save once something is authored.",
            motivation: "Several rule files can exist for one body type (your defaults plus downloaded variants); showing which one is being edited prevents editing the wrong file.");

        Add("BodyTypeProfiles.CreateProfile",
            layperson: "Starts an empty measurement profile for the selected body type, stored in its active rule file.",
            technical: "Ensures the body type has an active BodyTypeRuleSet (VM_OBodyMiscSettings.EnsureRuleFileForBodyType creates an unsaved one named after the body type if needed), then adds a new BodyTypeProfile via ReplaceProfileForBodyType. Enabled only when the body type has no profile.",
            motivation: "Replaces the old free-form Add: a profile always belongs to exactly one rule file and body type, so creating one is tied to the selected body type.");

        Add("BodyTypeProfiles.RemoveProfile",
            layperson: "Deletes the selected body type's Auto-Annotation profile from its rule file. Your worklist annotations are kept.",
            technical: "Confirms, then ReplaceProfileForBodyType(bodyType, null): the profile VM is dropped (its PresetAnnotations / AnnotatorPrefs are parked and survive the save) and the active BodyTypeRuleSet's MeasurementProfile becomes null when the views are folded back on save.",
            motivation: "Lets you start a body type over without deleting its rule file.");

        Add("BodyTypeProfiles.ProfileName",
            layperson: "The profile's own name, used in log messages and to name its measurement cache file. The rule file's name is set separately in OBody Misc settings.",
            technical: "BodyTypeProfile.Name. MeasurementCacheStore.FilenameFor derives the cache file name from (Name, BodyTypeName, Id), so renaming a profile makes the next scan write a new cache file.",
            motivation: "Kept separate from the rule file's display name so renaming or duplicating a file doesn't orphan an expensive measurement cache.");

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
            layperson: "Which preset and weight were loaded when this Region's box was drawn or last moved. '(Zeroed Sliders)' means it was drawn with 'Show zeroed body' on, so its coordinates are the zeroed body's own.",
            technical: "Read-only DefiningPresetLabel / DefiningWeight recordkeeping. The box itself is stored in undeformed (sliders-0) space; these fields only record the authoring context so the same slice can be reloaded for re-editing. Set on a fresh draw and updated when an edit session confirms a moved box (an untouched box keeps it); '(Zeroed Sliders)' when the box was confirmed with 'Show zeroed body' on, else the preset whose mesh the box was converted from. Selecting a '(Zeroed Sliders)' region reopens its box on the zeroed body so typed coords are stored as-is.",
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
            layperson: "For a RegionVolume measurement: which saved Region's enclosed volume to measure. For a Girth measurement: which saved Region is the band to measure around, like a tape measure. For a TorsoTrace measurement: the bust region (its key vertices A-C are the sternum points).",
            technical: "Sets VM_MeasurementDefinition.RegionRefName, which only the MeasurementKind.RegionVolume and MeasurementKind.Girth kinds read (the vertex refs A-D are ignored for them). RegionVolume: the named region's surface patch is clipped to the box, capped along its boundary loop(s), and integrated per preset by RegionVolumeEvaluator. Girth: only the region's member vertices are read (no caps, so the region need not resolve to a valid volume); RegionGirthEvaluator slices them by zeroed-space height and measures the convex-hull perimeter of each slice's deformed positions in the X-Z plane.",
            motivation: "Volume discriminates fullness (for example cup size) where any two-point distance misreads long/narrow versus broad/flat geometry. Girth is what body-shape systems such as FFIT are defined on (bust, waist and hip circumferences), so their definitions apply without converting volumes.");

        Add("BodyTypeProfiles.MeasurementGirthAggregate",
            layperson: "For a Girth measurement: which slice of the band counts. 'Fullest' takes the largest circumference (bust or hips), 'Narrowest' the smallest (the natural waist), 'Average' the mean over the band.",
            technical: "Sets GirthAggregate on a MeasurementKind.Girth row. The band is cut at zeroed-space heights GirthStep apart (default 1.0 raw unit; editable only in the rules JSON), starting half a step above the band's lowest member; each slice collects the members within 0.35 units of its height on the sliders-0 mesh and measures the convex-hull perimeter of their deformed X-Z positions. Slices with fewer than three vertices or a degenerate hull are skipped; if none remain the measurement is unresolved (RegionNotResolved). GirthAggregate and GirthStep are part of the cache fingerprint for Girth rows only. Selecting the row draws its tape path in the viewer and in Show Spread (RegionGirthEvaluator.MeasuredLoops: the hull loop of the slice Max / Min picks, or every slice Mean averages), over the band tint; the tint follows the skin, the tape path is what is measured.",
            motivation: "A tape measure is placed at the fullest point of the bust and hips and the narrowest point of the waist; taking the max or min over a band finds that level on every preset without hand-placing a key vertex.");

        Add("BodyTypeProfiles.MeasurementTraceMetric",
            layperson: "For a TorsoTrace measurement: what to read from the torso's outline seen from the front, with the bust taken out. 'Stem flare' is how much the torso sides widen between the top of the hips and where the bust starts covering them (1 = perfectly straight sides). 'Stem window height' is how much of that stretch of torso side shows. 'Lat flare' is the waist width divided by the torso width just under the armpits (lower = the waist pinches in more from the back muscles).",
            technical: "Sets TraceMetric on a MeasurementKind.TorsoTrace row (TorsoTraceEvaluator). The body is cut by a curved surface following the torso's own mid-depth (centre-line midpoint of the front and back skin per height, interpolated straight across Y 86-106, 5-sample smoothing), so the bust never enters the cut; the crossing triangles' segment endpoints form the trace, and the torso half-width at a height is half the gap between the innermost points either side of X = 0 (arm loops lie outside). Stem window: bottom = the lowest height still within 3% of the narrowest half-width over Y 78-92 (where the hip flare starts); top = the lowest bust-region vertex more than 1 unit in front of the line through the three sternum key vertices (A-C) whose |X| reaches past the trace edge at its height, or, when the bust covers nothing, the height of the widest-chest key vertex (D); either way capped at Y 95. Stem flare = widest / narrowest half-width inside the window; window height = top - bottom. Lat flare = the narrowest trace width over Y 80-92 (inside |X| < 20) / the mean innermost width at Y 93-96; it reads no key vertices. Heights are raw CBBE 3BA units. Metric, algorithm version and (stem metrics) the four key vertices are part of the cache fingerprint; the trace is cut once per shape per evaluation.",
            motivation: "Telling a Spoon from a Pear or a Bottom Hourglass from a Pear depends on how the torso side runs between the hips and the bust - straight, tapering in from the lats, or hidden by a large bust - which no two-point distance or girth captures. Ported from the offline prototypes fitted to the Shape rule verdicts.");

        Add("BodyTypeProfiles.MeasurementEnabled",
            layperson: "Unchecked keeps this measurement's definition but stops it being computed for every preset and saved to the measurement cache, which saves disk space. The Live column still shows its value for the previewed preset. Rule conditions that use a disabled measurement are highlighted amber and can never match. A measurement ratio can still use a disabled measurement: it is worked out from its definition whenever the ratio needs it, and only the ratio is saved. So you can keep a ratio's parts as named rows without them taking up cache space.",
            technical: "Bound to VM_MeasurementDefinition.Enabled (MeasurementDefinition.Enabled, default true). Disabled rows get no cache fingerprint (MeasurementCacheStore.ComputeAllMeasurementFingerprints), so the scan skips them, hydrate drops their stored values, and the cache save leaves them out; BodySlideMeasurementEvaluator.Evaluate does not evaluate them. Exception: a disabled row read (transitively) by an enabled MeasurementRatio is evaluated on the fly (MeasurementMath.ActiveMeasurementNames vs CachedMeasurementNames): Evaluate computes it, feeds the ratio, then drops it from the result, so rules see exactly the cached set; the scan first tries to derive it from cached terms (MeasurementDerivation step 4) and otherwise evaluates it on the mesh in a partial fill that stores only the ratio. The ratio's fingerprint folds in the operand definitions, so editing a disabled operand still re-measures the ratio. Enabled is not part of the fingerprint, so toggling it never invalidates other values; re-enabling rescans only this measurement.",
            motivation: "Every measurement is stored for every preset at every weight slot, so the cache file grows with each one. Disabling lets you keep an experimental or superseded measurement for later without paying for it on disk.");

        Add("BodyTypeProfiles.MeasurementAngleView",
            layperson: "For an Angle measurement: whether the angle between line A-B and line C-D is measured in full 3D, or as seen from the side, from above, or from the front. Side view is what you see in a profile render.",
            technical: "Sets AngleViewAxis on an Angle measurement. The Angle kind returns the angle in degrees (0..180) between the directions A->B and C->D (atan2 of |u x v| and u . v); with B = C it is the turn at the shared point (0 = straight). '3D' (null) uses the full vectors; 'Side view (along X)' drops their X components before measuring, 'Top view (along Y)' their Y, 'Front view (along Z)' their Z. Degenerate (zero-length) projected lines leave the measurement unresolved. The overlay draws A-B yellow and C-D cyan as the real 3D lines, plus a marker at the pivot C: a white guide continuing A-B's direction (the straight-on reference) and an orange arc from it to C-D, both in the measured plane, so the arc shows which angle is measured (the turn away from straight, not the inner angle) and in which view. Part of the cache fingerprint for Angle rows only.",
            motivation: "Some shape traits are angles rather than distances - how sharply the back of the thigh turns into the glute, for example - and they are usually judged in one view; projecting keeps left-right offsets between the key vertices out of a side-view angle.");

        Add("BodyTypeProfiles.RowDragHandle",
            layperson: "Drag this handle up or down to move the row. Select several rows first to move them together. Reordering is turned off while the grid is sorted by a column header.",
            technical: "gong-wpf-dragdrop on the Key Vertices and Measurements grids, with VM_BodyTypeProfile as the IDropTarget: a drop moves the dragged row(s), kept in their relative order, to the insertion point (VM_BodyTypeProfile.MoveRows, via ObservableCollection.Move). Drags that start on a ComboBox or TextBox are ignored by the library, so the handle is the dependable grab point. Order matters only for duplicate names (the first row wins) and for how the rows are saved; Measurements moves are undoable.",
            motivation: "Grouping related rows (one body part, one measurement family) makes a long list much easier to scan and review.");
        Add("BodyTypeProfiles.UndoMeasurementEdit",
            layperson: "Undoes the last change to the Measurements list: an edit, an added or deleted row, a move, or a loaded file. Shortcut: Ctrl+Z.",
            technical: "VM_BodyTypeProfile.UndoMeasurementEdit. The history keeps up to 100 whole-grid snapshots (rows by reference plus each row's definition fields); changes are recorded after 0.5 s of quiet, so a burst of typing is one step. A restore keeps row identity, replays renames through the normal rename cascade (rule conditions, ratio operands, the measurement cache) and re-evaluates live values once. While a text box has focus, Ctrl+Z goes to the box's own text undo first.",
            motivation: "A misclick on a Kind or reference dropdown can silently change what a measurement reads; one keystroke puts it back.");
        Add("BodyTypeProfiles.RedoMeasurementEdit",
            layperson: "Re-applies a change you just undid. Shortcut: Ctrl+Y (or Ctrl+Shift+Z).",
            technical: "VM_BodyTypeProfile.RedoMeasurementEdit. The redo list is cleared by any new edit.",
            motivation: "Lets you step back and forth to compare before and after.");
        Add("BodyTypeProfiles.MeasurementRatioOperandA",
            layperson: "For a MeasurementRatio: the measurement on top of the fraction. The row's value is this measurement divided by the one in column B. When the row is selected, the viewer draws the top measurement in yellow and the bottom one in cyan, each the way it is drawn on its own (a line for a distance, a shaded patch for a region volume).",
            technical: "MeasurementRefNames[0] of a MeasurementKind.MeasurementRatio row. The ratio is evaluated after every other measurement for the preset (BodySlideMeasurementEvaluator post-pass, MeasurementMath.EvaluateMeasurementRatios), reading the named row's value (first row wins on duplicate names). A ratio may read another ratio; a missing operand, a cycle, a self-reference or a denominator within 1e-6 of zero leaves it unresolved (MissingOperand). Its cache fingerprint folds in the operands' definitions but not their names, so editing an operand re-measures the ratio while renaming one (which updates this cell) does not. Partial-fill scans evaluate the operands too, and a ratio whose operands are already cached is derived without mesh work.",
            motivation: "Some proportions compare things no pair of key vertices can express directly - two region volumes, for example (bust band against hip band, as FFIT-style body shapes do).");

        Add("BodyTypeProfiles.MeasurementRatioOperandB",
            layperson: "For a MeasurementRatio: the measurement on the bottom of the fraction (column A divided by this).",
            technical: "MeasurementRefNames[1] of a MeasurementKind.MeasurementRatio row: the denominator. Same evaluation, failure and fingerprint rules as operand A; a denominator within 1e-6 of zero leaves the ratio unresolved rather than producing a huge or infinite value.",
            motivation: "Dividing by a reference measurement (usually the hips) makes the value comparable across bodies of different overall size.");

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

        Add("BodyTypeProfiles.BranchDescription",
            layperson: "A note on what this OR-branch is for - which presets it is meant to catch and why it exists. It has no effect on matching.",
            technical: "Bound to VM_AndGatedMeasurementGroup.Description; round-trips as AndGatedMeasurementGroup.Description in the rule file and rule JSON exports/patches. Ignored by the evaluator, the rule signature and the cache fingerprints, so editing it never marks results stale.",
            motivation: "Rules grow branches over many revisions; without a note, the reason a branch exists (and whether it is safe to remove) is easily lost.");

        Add("BodyTypeProfiles.RuleDescription",
            layperson: "A note on what this rule as a whole captures - what the descriptor means in terms of the measurements. It has no effect on matching.",
            technical: "Bound to VM_MeasurementRule.Description; round-trips as MeasurementRule.Description (the rule-level OR group) in the rule file and rule JSON exports/patches. Ignored by the evaluator, the rule signature and the cache fingerprints.",
            motivation: "Makes a shared rule file readable without cross-referencing the authoring notes it came from.");

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
            layperson: "What this condition tests: a body measurement against a number, one of the preset's BodySlide slider values against a number, or whether a label applies to the preset - from another rule here or applied by hand.",
            technical: "Bound to MeasurementConditionKind. Measurement compares a measurement value with the threshold; Slider compares a value of the preset's slider (SliderSample: the preset's SliderValues at the evaluated weight), needing no mesh or cache; DescriptorRef tests membership in the matched-descriptor set, honoring NOT. That set holds descriptors produced by rules earlier in the topological evaluation order plus the preset's stored Manual annotations (CollectExternalDescriptors); a manually set category skips its own rules and default. The inactive kinds' fields stay in memory, so flipping back loses nothing.",
            motivation: "All three kinds run in one pass, so one category can be decided by sliders, measurements or other labels - for example Belly:Muscular from the MuscleAbs slider while measurement rules for Belly:Chubby exclude it with a DescriptorRef.");

        Add("BodyTypeProfiles.ConditionNegate",
            layperson: "Flips the check: the condition passes only if the referenced label did NOT apply.",
            technical: "Bound to Negate on a DescriptorRef condition; the evaluator inverts the membership test, and the live readout's Match / Not Match honors the inversion.",
            motivation: "Exclusion predicates like 'curvy but not unrealistic' would otherwise require authoring mirror-image rules.");

        Add("BodyTypeProfiles.ConditionRefCategory",
            layperson: "The category of the label that this condition checks for. The label can come from another rule here or from a manual annotation.",
            technical: "Bound to RefCategory; options come from GetSafeDescriptorRefsForCondition, which offers the full descriptor catalog minus any choice that would create a circular dependency between rules referencing each other's output. Categories flagged Rules only (BodyShapeDescriptorShell.IsRulesOnly) are deliberately INCLUDED here - that flag hides a category from the distribution pickers only, so intermediate categories authored purely to be referenced by other rules remain fully available to this dropdown. A descriptor no rule produces is always cycle-safe and can only be matched by a manual annotation.",
            motivation: "Cycle-safe options at the source prevent authoring reference chains the topological evaluator could never order.");

        Add("BodyTypeProfiles.ConditionRefValue",
            layperson: "The value of the label that this condition checks for.",
            technical: "Bound to RefValue; options are the cycle-safe references filtered to the selected RefCategory, refiltered automatically when the category changes.",
            motivation: "Same cycle-safety guarantee as the category picker, narrowed to a concrete label.");

        Add("BodyTypeProfiles.ConditionReadout",
            layperson: "Live result of this condition for the preset shown in the viewer - green means it passes, red means it fails.",
            technical: "ConditionReadout: for Measurement conditions, the named measurement's LiveValue tested with the comparator; for Slider conditions, the slider value the condition reads (Small, Big, or the value interpolated at the preview weight; Either shows the interpolated value) from the previewed preset including the Sliders tab's unsaved edits, or 'unset' when the preset doesn't set the slider; for DescriptorRef conditions, 'Match' or 'Not Match' against the previewed preset's matched descriptors - rule matches, materialized category defaults and its stored manual annotations - honoring NOT. A gray dash means no preset is loaded or the value cannot resolve; the color is driven by ConditionConforms.",
            motivation: "Seeing each condition pass or fail against a live preset pinpoints exactly which clause blocks an expected match.");

        Add("BodyTypeProfiles.ConditionSliderName",
            layperson: "The BodySlide slider this condition looks at. You can pick one from the list or type any slider name.",
            technical: "Bound to MeasurementCondition.SliderName. The list is VM_BodyTypeProfile.AvailableSliderNames: the body type's registry catalog plus every slider a condition already references, and the names found only in preset XMLs when 'Show preset-only sliders' is on. A preset that doesn't set the slider fails the condition.",
            motivation: "Slider values are the cheapest signal there is - no scan needed - and some traits (muscle definition, for example) show up in sliders more reliably than in mesh measurements.");

        Add("BodyTypeProfiles.ConditionSliderType",
            layperson: "Which of the slider's values the condition tests. Every preset stores two values per slider - one used at NPC weight 0 (Small) and one at weight 100 (Big) - and the game blends between them by weight. 'Small', 'Big' and 'Either' test the stored values and so label every weight the same; 'Interpolated' tests the blended value at each weight, so the label can apply only where it is true.",
            technical: "Sets MeasurementCondition.SliderType (BodySliderType). Small/Big/Either compare the authored endpoint values and are weight-independent; Interpolated compares Small + (Big - Small) * weight/100 at the evaluated weight slot. EqualTo / NotEqualTo compare the slider value rounded to the nearest whole number (halves away from zero); ordered comparators use the exact value.",
            motivation: "A preset is a family of shapes blended by NPC weight; Interpolated conditions let one threshold produce weight-aware labels.");

        Add("BodyTypeProfiles.ShowPresetOnlySliders",
            layperson: "BodySlide preset files often carry slider entries for outfits ('squeeze' sliders and similar) alongside real body sliders. By default the slider lists only show this body type's official sliders; tick this to also list every extra name found in your installed presets.",
            technical: "Session-only VM_BodyTypeProfile.ShowPresetOnlySliders. Off: the Body Type Registry catalog plus any names referenced by existing conditions. On: the union with all names parsed from loaded preset XMLs (SliderNameCatalog). Body types without a catalog always show the full union. Logs\\SliderNameProvenance.txt lists every name with its source.",
            motivation: "One outfit preset collection can inject hundreds of names into a body's slider list, burying the sliders rules are actually written against - but the unusual ones must stay reachable.");

        // ---------- Sliders tab ----------

        Add("AutoAnnotationSliders.SliderPicker",
            layperson: "Picks which slider's numbers appear in the preset table's Low / High / Interp columns, so you can sort every preset by that slider. It also picks the slider the heatmap shows.",
            technical: "Sets VM_AutoAnnotationSliders.SelectedSliderName; VM_SliderPresetRow.Low/High/Interpolated show that slider's authored Small/Big values and its interpolation at the editor's preview weight. Options: the body type's catalog, plus preset-only names when that toggle is on.",
            motivation: "Sorting all presets by the slider a Slider condition tests shows where a proposed threshold lands across your installed presets.");

        Add("AutoAnnotationSliders.PresetFilter",
            layperson: "Type here to narrow the preset table to names containing the text.",
            technical: "Case-insensitive substring filter over the rows built from the editor's preset list (same gender and body type as the right rail).",
            motivation: "Preset collections routinely run to hundreds of entries.");

        Add("AutoAnnotationSliders.ShowPresetOnlySliders",
            layperson: "Also list slider names found only in preset files (mostly outfit 'squeeze' sliders) in the slider picker.",
            technical: "Session-only VM_AutoAnnotationSliders.ShowPresetOnlySliders; reconciles AvailableSliderNames from SliderNameCatalog.",
            motivation: "Keeps the picker focused on body sliders while the unusual names stay reachable.");

        Add("AutoAnnotationSliders.ShowMovedVertices",
            layperson: "Paints, on the 3D preview, the body vertices the picked slider moves, as a heatmap from blue (barely moved) to red (moved most). Pick the slider in the 'Slider' box or by clicking a row in the slider list.",
            technical: "BodyShapeViewerExtension.HighlightSliderMorph on the editor's viewer for ActiveHighlightSlider (the picker's slider, or the clicked readout row), rebuilt on every BodySlideApplied so it follows preset, weight and edit changes; ClearSliderHighlight otherwise.",
            motivation: "Slider names don't always map obviously to a body region; seeing exactly what a slider moves turns rule authoring from guesswork into inspection.");

        Add("AutoAnnotationSliders.PresetList",
            layperson: "The presets of this body type. Click a column header to sort; click a row to show that preset in the 3D view (the same as picking it in the right panel).",
            technical: "Rows from VM_BodyTypeProfileEditor.FilteredPresets. Selecting a row sets the editor's SelectedPreset, which reloads the viewer; the selection follows the right rail's picker the other way too.",
            motivation: "Pairs the sorted numbers with the live 3D preview without leaving the editor.");

        Add("AutoAnnotationSliders.Readout",
            layperson: "The selected preset and the SliderPresets file it comes from. 'unsaved edits' appears once you change a slider.",
            technical: "SelectedPresetLabel and SourceFileDisplay (BodySlideSetting.SourceXmlPath, recorded at import: the last file that defined the preset). IsDirty compares the working copy with the preset's stored slider values.",
            motivation: "Save writes back to that exact file, so it is shown before you save.");

        Add("AutoAnnotationSliders.ShowUnsetSliders",
            layperson: "Also lists the body type's sliders this preset doesn't set, so you can give them a value.",
            technical: "Adds SliderNameCatalog.CatalogSliders(body type) rows not in the preset (shown in italics); typing a value into one adds the slider to the working copy.",
            motivation: "BodySlide leaves sliders at 0 out of the file, so adding one is the only way to set it.");

        Add("AutoAnnotationSliders.ReadoutGrid",
            layperson: "Every slider of the selected preset. Edit the Low (weight 0) or High (weight 100) value and the 3D preview updates right away; changed sliders are shown in bold. Click a row to point the heatmap at that slider.",
            technical: "VM_EditableSliderRow per slider. Edits update VM_AutoAnnotationSliders.WorkingCopy (never the preset itself until Save); a throttled ReapplyPreviewMorph re-applies it to the viewer and refreshes live measurements. While unsaved, the editor previews the working copy (PreviewPresetModel), so Slider conditions and live measurement readouts reflect the edits.",
            motivation: "Lets you test 'what would this preset be labeled if this slider were higher' without leaving SynthEBD or touching the preset file.");

        Add("AutoAnnotationSliders.Save",
            layperson: "Writes your slider changes back into the preset's own BodySlide file. A backup of the file is made first.",
            technical: "BodySlidePresetXmlWriter.WritePreset(SourceXmlPath, ReferencedBodySlide, ...): copies the file to '<file>.<timestamp>.bak', replaces only that preset's SetSlider entries (attributes and Group tags kept), then adopts the values as the preset's SliderValues. The changed slider hash makes the next Match Presets scan re-measure the preset.",
            motivation: "Closes the loop from 'this preset should be a bit different' to an actual preset file BodySlide can build.");

        Add("AutoAnnotationSliders.Revert",
            layperson: "Throws away your unsaved slider changes.",
            technical: "Rebuilds the working copy from the preset's stored slider values and re-applies the preview.",
            motivation: "Makes experimenting with sliders safe.");

        Add("AutoAnnotationSliders.SaveAsName",
            layperson: "The name for the new preset Save As creates.",
            technical: "VM_AutoAnnotationSliders.SaveAsName; defaults to the selected preset's name.",
            motivation: "A new name keeps the original preset intact.");

        Add("AutoAnnotationSliders.SaveAs",
            layperson: "Saves the current slider values as a new preset, into a new BodySlide file or added to an existing one. The new preset appears in your BodySlide list right away; the original preset is left unchanged.",
            technical: "Save dialog (OverwritePrompt off: an existing file gets the preset added, or the same-named preset's sliders replaced); BodySlidePresetXmlWriter.WritePreset carries the source preset's 'set' attribute and Group tags. The new BodySlideSetting is added through VM_BodySlidesMenu.AddSavedPreset to the preview gender's list and selected.",
            motivation: "Lets you build preset variants from inside SynthEBD while judging them against the Auto-Annotation rules.");

        // ---------- Rules tab: matching-presets pane ----------

        Add("BodyTypeProfiles.RuleNodeSort",
            layperson: "Reorders the matching-presets list below: by name, or by any measurement so the most extreme presets rise to the top.",
            technical: "Bound to SelectedRuleNodeSortOption. 'Name' keeps the default (Gender, Preset, Weight) order; any measurement name sorts rows by that metric's cached value descending, with slices missing the value sinking to the bottom. Measurements referenced by the displayed node's rules are listed first in the dropdown.",
            motivation: "Ranking matches by the metric a rule keys on surfaces the borderline cases - the presets that only just cleared a threshold - which is where calibration attention belongs.");

        Add("BodyTypeProfiles.RuleNodeShowMeasurements",
            layperson: "Draws every measurement used by the selected rules as lines on the 3D preview, updating as you switch nodes or edit conditions. A measurement that uses a region (a region volume, or a landmark picked from a region) also shades that region in translucent cyan.",
            technical: "ShowRuleNodeMeasurements pushes the measurements referenced by the rules under the selected tree node (or by the temp-edited rule during a session) into the viewer's measurement-line overlay - the same channel the Measurements tab uses. A node on a category's default value has no rules of its own, so it falls back to the union across every other rule in that category: those are the measurements whose failure to fire is what produced the default. While on, it overrides the Measurements-grid selection; unchecking restores it. Regions those measurements use - a RegionVolume's region, or the region behind a Region-strategy key vertex - are tinted translucent cyan on the visible skin (depth-tested, drawn through a channel separate from the region editor's overlay), as is the case for any measurement shown through that channel, including a Measurements-grid selection.",
            motivation: "Rules are abstract until you see which anatomy their measurements actually span on the loaded preset.");

        Add("BodyTypeProfiles.RuleNodeShowClosestAssignment",
            layperson: "Adds 'Next closest match' to every preset below: the label this preset would get in this category if it did not get the selected one, and how far it sits from that outcome.",
            technical: "ShowRuleNodeClosestAssignment. For each row, every other value in the selected node's category - each value with a gender-eligible rule, plus the category default - is scored the same way the Match Presets badge scores, and the best-scoring one is shown with the gap separating it from the value the row actually holds, in standard deviations. An empty suffix means the category has no scorable rival or the slice has no cached measurements. A negative gap means the rival out-scores the assigned value, which flags a row whose label came from something the scorer cannot project.",
            motivation: "A rule is only as good as its boundaries; seeing which neighbor each match is closest to losing to shows exactly where the next threshold edit should land.");

        Add("BodyTypeProfiles.RuleNodeMatchList",
            layperson: "Presets that currently receive the selected label, with the numbers that got them there. Click a row to load it in the 3D preview; arrow keys step through with live previews.",
            technical: "SelectedNodeMatchingPresets, derived from the profile's scan cache (ScanResults plus MeasurementCache) for the rules under the selected tree node; each row lists every referenced measurement's cached value. Selecting a row sets SelectedNodeMatchRow, which loads that (preset, weight) slice in the viewer. The list stays empty with an inline reason while the cache is stale or absent.",
            motivation: "Stepping through the real population a rule captures closes the loop between editing thresholds and seeing their effect.");

        // ---------- Match Presets tab ----------

        Add("BodyTypeProfiles.VerboseScan",
            layperson: "Makes the scan write extra diagnostic detail to the Status Log. Leave off unless a scan is misbehaving.",
            technical: "Mirror of VerboseScan, read by RunScanAsync: emits the target count, viewer/key-vertex/fingerprint shape comparisons, a first-iteration measurement snapshot, a first-vs-last identity check, and a summary.",
            motivation: "The usual failure mode is a scan that silently matches zero presets; the diagnostics show which stage - shape names, fingerprint, or measurements - went wrong.");

        Add("BodyTypeProfiles.WatchScanLive",
            layperson: "Shows each preset on the character as the scan measures it. Much slower; leave off for normal scans, which measure in the background without moving the preview.",
            technical: "Mirror of WatchScanLive, read by RunScanAsync. Off: HeadlessMeasurementScan captures the scene once (HeadlessBodyDeformer: pre-skinned base meshes and per-slider skinned offset tables for only the shapes the profile references) and evaluates rows in parallel on worker threads, each with its own profile copy, skipping rules (descriptors are re-derived from the cache afterwards). On: every row is applied to the viewer with ApplyBodySlide and read back, one row per UI yield. Both store the same values up to float rounding. If the scene can't supply a headless deformer, the scan falls back to the live path and logs why.",
            motivation: "Linear blend skinning is affine per vertex, so a preset's skinned shape is a skinned base plus a weighted sum of pre-skinned slider offsets - one pass per row instead of a full viewer deform, upload and repaint. Watching remains useful for spotting a preset that deforms oddly.");

        Add("BodyTypeProfiles.CacheStatus",
            layperson: "How many measurement results are saved on disk for this profile's current body type, so future sessions can skip rescanning.",
            technical: "CacheStatusSummary, refreshed after hydrate, scan, and purge. The disk cache stores measurements (not descriptors) per (preset, gender, weight) under a snapshot keyed by the profile's BodyTypeName; entries invalidate automatically when a preset's sliders change or a measurement / key-vertex definition is edited.",
            motivation: "A full scan deforms and measures every preset at every weight, which is expensive; the cache makes later sessions start warm.");

        Add("BodyTypeProfiles.PurgeCache",
            layperson: "Deletes the saved scan results for this profile's current body type so the next scan starts completely fresh.",
            technical: "PurgeCacheForActiveProfileCommand drops the active BodyTypeName's snapshot from the profile's on-disk cache file and clears the in-memory cache; snapshots for other body types in the same file (for example a dormant BHUNP experiment) are preserved.",
            motivation: "An escape hatch for cache corruption, or for changes the automatic invalidation fingerprint cannot see.");

        Add("BodyTypeProfiles.MatchShowMeasurements",
            layperson: "Draws every measurement used by the rules of the descriptors you have checked above as lines on the 3D preview. A measurement that uses a region (a region volume, or a landmark picked from a region) also shades that region in translucent cyan.",
            technical: "ShowMatchPresetMeasurements pushes the measurements referenced by any rule whose descriptor is checked in the filter into the viewer's measurement-line overlay (the same channel the Measurements grid selection uses), refreshing as the filter selection changes. A checked value that is its category's default has no rules of its own, so it falls back to the union across every other rule in that category: those are the measurements whose failure to fire is what produced the default. Hovering any drawn line names its measurement and reports the value currently evaluated against the loaded preset. Regions those measurements use - a RegionVolume's region, or the region behind a Region-strategy key vertex - are tinted translucent cyan on the visible skin (depth-tested, drawn through a channel separate from the region editor's overlay), as is the case for any measurement shown through that channel, including a Measurements-grid selection.",
            motivation: "When reviewing why filtered presets match, seeing the exact measured spans on the body beats cross-referencing rule text.");

        Add("BodyTypeProfiles.MatchShowClosestAssignment",
            layperson: "Adds each preset's 'next closest' label - the one it would have received instead, and how far it sits from that outcome - inline on the results rows, and on every label's line in the row tooltips.",
            technical: "ShowMatchPresetClosestAssignment. Drives two readouts. Inline on the row, beside the score badge: 'Next closest: {Value} ({gap})' for the value that row carries in the filtered category - the best-scoring value it did not receive, and how much margin it would have to give up before that rival took the label. Shown only when the filter pins exactly one descriptor value, since with zero or several checked there is no single subject to rank rivals against. Under a Similarity sort this stays anchored to the filtered value even though the badge scores the 'vs' target, because anchoring it to the target would just name the row's own value back at it. In the hover tooltip, which lists every assigned descriptor with its margin score one category per line, it appends to each of those lines the best-scoring value the row did NOT receive plus the gap between the two. Both use the unit the sort mode implies (standard deviations unless a percent-of-threshold sort is active). Scoring is deferred to container realization / hover, so only rows actually on screen or inspected are computed.",
            motivation: "Knowing a preset is Hourglass says less than knowing it was a hair away from Rectangle - that is the pair whose boundary needs attention.");

        Add("BodyTypeProfiles.MatchSortMode",
            layperson: "Reorders the results by how strongly each preset matches, or by a raw measurement, instead of alphabetically. Only kicks in when exactly one descriptor value is checked in the filter.",
            technical: "Bound to ScoreSortMode (MarginScoreMode). Match-strength modes score each row against the filtered descriptor's rule: the OR-group with the most slack is chosen and its tightest condition's margin becomes the score, normalized either by the measurement's population standard deviation (reads as 'N standard deviations past the line') or by the threshold (reads as 'N percent past it'). Descriptor-reference conditions are scored by tunneling into the referenced descriptor's own rules - the best margin among its gender-eligible rules becomes that condition's margin (sign-flipped for negated references) - so aggregator labels built from references (e.g. Build) rank by real margins instead of a flat zero. A value that is its category's default is scored even without a live rule: its margin is the negation of the best sibling rule's margin (positive exactly when no sibling fires; the magnitude is how far the row sits from the nearest rival), the badge is suffixed '(default)' when that synthesized margin is the number shown, and references to a default value tunnel into it the same way. Groups with no continuous condition carry no margin and leave the row unscored rather than scoring a flat zero. Similarity modes score against a sibling value picked in the 'vs' dropdown, and Measurement mode sorts by a raw cached value. With zero or several descriptors checked, the default (Gender, Preset, Weight) order applies.",
            motivation: "Margin ranking surfaces both the archetypes (deepest matches) and the borderline cases in one glance, which is exactly what threshold calibration needs.");

        Add("BodyTypeProfiles.MatchSimilarityTarget",
            layperson: "For the Similarity sorts: the sibling label to compare against - for example, ranking Hourglass presets by how close they come to also being Rectangle.",
            technical: "Bound to SimilarityTarget; the options are every value in the filtered descriptor's category that has at least one rule, plus the category's default value (scorable through its synthesized default margin even with no rule), minus the filter's own selected value (comparing to self reduces to the Match-strength sort). Rows are scored against the target's rule instead of the filter's.",
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
            technical: "MatchingPresets: one VM_PresetScanRow per matching (preset, weight), ordered (Gender, PresetLabel, Weight) unless a score sort is active. Selecting a row (SelectedMatchRow) auto-loads the preset at that row's weight; the blue badge shows the score (suffixed '(default)' when the row holds the value through the category default rather than a rule of its own). Hovering a row lists every descriptor it was assigned, one category per line in alphabetical order, each with its margin score - plus the runner-up value when Show Closest Assignment is on. A category marked '(N assigned)' had N rules fire at once, which usually means their conditions overlap.",
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

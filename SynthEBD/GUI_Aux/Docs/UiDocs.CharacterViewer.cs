namespace SynthEBD;

// 3-part documentation for the embedded 3D Character Viewer (UC_CharacterViewer): the
// classifier toolbar (wireframe, vertex/box picking, region vertex editing, pick pairing),
// the pick-info panel, the pending-box edit bar, and the viewport axis gizmo. The lighting
// and render-pipeline panels are hosted from CharacterViewer.Rendering and keep their own
// inline tooltips.
public static partial class UiDocs
{
    private static void RegisterCharacterViewer()
    {
        // ---------- Classifier toolbar toggles ----------

        Add("CharacterViewer.Wireframe",
            layperson: "Draws the triangle edges of every loaded mesh over the model, so you can see the actual geometry you are picking vertices on.",
            technical: "Sets VM_CharacterViewer.ShowWireframe, which makes the renderer overlay each loaded mesh's triangle edges. Only offered when the viewer is hosted with classifier controls enabled (the Body Type Profiles editor).",
            motivation: "Key-vertex assignment targets real mesh vertices; the wireframe reveals where vertices actually sit on otherwise smooth-shaded skin.");

        Add("CharacterViewer.PickVertex",
            layperson: "While checked, left-clicking the model drops a marker on the nearest mesh vertex instead of rotating the camera. Used to record key vertices for the body preset classifier.",
            technical: "Toggles IsKeyVertexPickMode: a viewport click ray-picks the closest triangle vertex and raises the same pick event the Body Type Profile editor listens to; re-picking an already-marked vertex on the same shape is a no-op. Camera orbit is suppressed while active because both gestures share left-drag.",
            motivation: "Body type profiles need exact vertex indices as measurement anchors; clicking them on the real mesh is far more reliable than guessing indices.");

        Add("CharacterViewer.PickBox",
            layperson: "While checked, dragging a rectangle across the model defines a small 3D box on the mesh. The classifier later finds the vertex inside that box which best matches the chosen Criterion, so the landmark stays correct on every preset.",
            technical: "Toggles IsBoundingBoxPickMode: the dragged screen rectangle is projected back onto the starting mesh to form a mesh-local AABB, which is parked as the pending box for per-axis editing; Confirm then fires it with the selected criterion through the key-vertex box pick event. Orbit is suppressed while active.",
            motivation: "A fixed vertex index can drift off the intended anatomy as presets deform the mesh; a box plus a selection criterion re-finds the right vertex per body, keeping profiles robust across presets.");

        Add("CharacterViewer.AddVerts",
            layperson: "Turns on 'add vertices' mode for the region row you have selected: left-click a vertex to force it into the region, or drag a lasso to add many at once. Click the button again to return to normal camera control.",
            technical: "Sets IsRegionVertexAddMode (region vertex-edit mode plus additive). Mutually exclusive with Remove Verts - at most one is on, and with both off a left-drag orbits the camera - and it auto-disengages when the pending box is confirmed or cancelled. Edits apply to the currently selected RegionVolume region.",
            motivation: "Box-selected regions sometimes clip or miss anatomy at their edges; hand-adding vertices patches the membership without redrawing the whole box.");

        Add("CharacterViewer.RemoveVerts",
            layperson: "Turns on 'remove vertices' mode for the selected region: left-click a vertex to force it out of the region, or drag a lasso to remove many at once. Removing interior vertices opens a hole in the region.",
            technical: "Sets IsRegionVertexRemoveMode (region vertex-edit mode plus subtractive). Mutually exclusive with Add Verts; with both off a left-drag orbits the camera; auto-disengages when the pending box is confirmed or cancelled. Edits apply to the currently selected RegionVolume region.",
            motivation: "The subtractive half of region cleanup: carve unwanted geometry, such as an arm grazing a torso box, out of a region without pulling the box away from anatomy it should keep.");

        // ---------- Pick tooling ----------

        Add("CharacterViewer.SelectMirror",
            layperson: "For each selected pick (or the most recent pick if none are selected), adds a matching pick on the opposite side of the body.",
            technical: "For each source pick, searches the same mesh for the vertex whose current (post-deformation) position is closest to the X-mirror of the pick's position, and fires the normal pick event for it. Scope comes from the pick-list selection, falling back to the latest pick when nothing is selected.",
            motivation: "Bilateral landmarks such as hips and shoulders are always needed in pairs; mirroring one careful pick beats hand-hunting its counterpart.");

        Add("CharacterViewer.PairAxis",
            layperson: "Chooses the direction 'Pair Across Axis' shoots through the body: X is left-right, Y is up-down, Z is front-back.",
            technical: "Sets ProjectAcrossAxisIndex (0=X, 1=Y, 2=Z) in the model's local axes as shown by the viewport gizmo. Defaults to Z, the front-back axis used for chest-to-spine anchor pairs.",
            motivation: "Different anchor pairs run along different body axes; exposing the axis keeps one pairing button usable for all of them.");

        Add("CharacterViewer.PairAcrossAxis",
            layperson: "Starting from your most recent pick, shoots straight through the body along the chosen Axis and adds a pick where it exits the opposite surface.",
            technical: "Casts a ray from the last pick along the selected axis in both polarities (with the origin nudged off the source surface to avoid self-hits), keeps the farther, genuinely through-the-body hit, and picks the vertex nearest that exit point, firing the standard pick events.",
            motivation: "Paired measurements like NippleFront and UpperSpineBack need both anchors at matching cross-axis coordinates; ray-casting guarantees the alignment instead of eyeballing it.");

        Add("CharacterViewer.ClearPicks",
            layperson: "Removes every pick marker from the model and empties the pick list.",
            technical: "Calls ClearKeyVertexMarkers, which clears the renderer's marker gizmos, the view model's pick bookkeeping, and the pick-info panel rows in one step.",
            motivation: "Pick sessions accumulate markers quickly; a one-click reset beats reloading the viewer to get a clean slate.");

        Add("CharacterViewer.ShowBulgeBins",
            layperson: "Draws the horizontal measurement slices used by the paired Bulge and Pinch criteria, so you can see which slice of the box wins the measurement.",
            technical: "For BulgePair*/PinchPair* criteria, draws one line per occupied Y-bin from its min-X vertex to its max-X vertex; the winning bin (widest for Bulge, narrowest for Pinch) renders cyan and the rest white. Targets the pending box if one is open, otherwise the selected key-vertex row's box; the active Body Type Profile subscribes to the viewer and drives the overlay refresh.",
            motivation: "The paired criteria are otherwise a black box; visualizing the bins shows at a glance why the algorithm chose a slice and whether the box needs adjusting.");

        Add("CharacterViewer.VerboseLog",
            layperson: "Sends the viewer's informational messages (loading, lighting, morph and texture details) to the Status Log. Errors are always logged either way.",
            technical: "Gates every LogVerbose/LogViewerDiagnostic call in VM_CharacterViewer - GL initialization, NPC-load checkpoints, weight morphs, texture overrides - while LogError bypasses the gate. Off by default.",
            motivation: "Viewer chatter would bury the classifier's own trace during profile authoring, but it is invaluable when debugging why a mesh or texture did not load.");

        // ---------- Pick-info panel ----------

        Add("CharacterViewer.PicksList",
            layperson: "The list of every vertex you have picked this session. Click a row to select it; Ctrl- or Shift-click selects several. 'Select Mirror' acts on whatever is selected here.",
            technical: "One row per pick (shape, vertex index, position), in pick order. Selection changes are pushed back to the view model so the corresponding viewport markers highlight and SelectMirrorPicks scopes to the selected rows; with nothing selected, mirroring falls back to the most recent pick.",
            motivation: "With dozens of markers on a body, a textual roster is the only reliable way to identify, select, and operate on specific picks.");

        Add("CharacterViewer.CopyPicksTsv",
            layperson: "Copies all picks to the clipboard as a spreadsheet-friendly table: shape, vertex index, and X/Y/Z position.",
            technical: "Writes a tab-separated block with a 'shape index x y z' header row and one line per pick via Clipboard.SetText. Enabled only while at least one pick exists.",
            motivation: "Pick data often needs to leave the app - for spreadsheets, bug reports, or sharing candidate key vertices with other profile authors.");

        // ---------- Pending-box edit bar ----------

        Add("CharacterViewer.ForceSymmetry",
            layperson: "Locks the pending box so it stays centered on the body: with an axis locked, editing one side automatically moves the opposite side to match.",
            technical: "Sets PendingBoxSymmetry flags. Editing min or max on a locked axis mirrors the opposite side about 0, and changing the lock immediately re-centers each locked axis using the larger side's magnitude. Auto-seeded on capture when the drawn box already straddles an axis with sides within 10 percent of equal.",
            motivation: "Bilateral features such as hips, waist, and shoulders need boxes symmetric about the midline; the lock guarantees that while you fine-tune, instead of hand-balancing two numbers.");

        Add("CharacterViewer.ShowZeroedBody",
            layperson: "Temporarily shows the body with all sliders at zero. Boxes are stored against this zeroed body, so this reveals the exact patch of skin your box will really capture. Untick to bring the current preset back; the box itself does not move.",
            technical: "While on, the body renders with an empty morph set; turning it off re-applies the last preset. Pending-box coordinates live in sliders-0 space, so this previews the ground truth the stored box resolves against. Auto-resets whenever a pending box begins, confirms, or cancels.",
            motivation: "A box drawn over a heavily-morphed preset can enclose different anatomy on the zeroed reference mesh; flipping to zeroed space catches the mismatch before the box is committed.");

        Add("CharacterViewer.ShrinkToCameraHalf",
            layperson: "Cuts the pending box in half toward the camera, along whichever axis you are looking down. Click again to halve it further.",
            technical: "Halves the box's range on the mesh-local axis most aligned with the camera's view direction, keeping the camera-side half; repeated clicks keep halving that side. Available only while a pending box exists.",
            motivation: "Depth is the hard axis to trim with a 2D drag; this carves off far-side geometry - for example an overhanging belly behind a hip box - with one click from a natural viewing angle.");

        Add("CharacterViewer.ConfirmAsDuplicate",
            layperson: "Saves this box as a measurement but keeps it on screen, so you can switch the Criterion and save again - capturing several measurements from one drawn box.",
            technical: "Fires the key-vertex box pick with the IsDuplicate flag set, which forces the profile editor's add-new-row path without consuming an active row-edit session, and intentionally leaves the pending box open for further confirms.",
            motivation: "Related measurements (for example MaxX and BulgePairMinX) often want the identical AABB; reusing the confirmed box is faster and more precise than redrawing it.");

        Add("CharacterViewer.DuplicateAsRegion",
            layperson: "Saves this box as a brand-new region row, even while you are editing an existing region. The box stays on screen, so each click creates another copy.",
            technical: "Fires the region-channel pick with the IsDuplicate flag set, so the profile editor forks a new RegionVolume row instead of updating the currently selected one; the selection and pending box are left untouched, so repeated clicks spawn identical regions.",
            motivation: "Lets one carefully-placed box seed several region variants, whose vertex membership can then be edited differently, without disturbing the region you started from.");

        Add("CharacterViewer.ConfirmAsRegion",
            layperson: "Saves this box as a region instead of a single-vertex measurement. A region measures the volume of the patch of body inside the box - useful for traits like chest fullness or thigh volume.",
            technical: "Fires the region pick channel and clears the pending box; the Criterion is ignored for regions. The box (stored in sliders-0 space) clips a surface patch whose boundary loops are capped into a watertight solid, and the RegionVolume measurement integrates that solid's volume against each preset's deformed vertices. The region appears as a new row on the profile's Regions tab.",
            motivation: "Some body traits are about mass rather than any single landmark's position; volume integration over a stable patch captures them where key-vertex distances cannot.");

        // ---------- Viewport overlays ----------

        Add("CharacterViewer.AxisGizmo",
            layperson: "A small compass showing which way the model's X (red), Y (green), and Z (blue) axes point from the current camera angle. Drag it anywhere convenient.",
            technical: "The three axis lines are recomputed from the camera's view matrix on every rendered frame, so they always reflect the current orbit; dragging repositions the widget on the overlay canvas without affecting the camera, and empty canvas areas pass clicks through to the viewport.",
            motivation: "Box editing and axis pairing constantly reference mesh-local axes; an always-correct orientation reference removes the guesswork after orbiting.");
    }
}

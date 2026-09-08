namespace SynthEBD;

// 3-part documentation for the BodySlide Compare window (Window_BodySlideCompare): the
// per-pane preset / NPC / weight inputs and the cross-pane camera-lock and superimpose
// controls. Opened from the "Compare" button on any OBody-menu CharacterViewer toolbar
// (documented under CharacterViewer.Compare).
public static partial class UiDocs
{
    private static void RegisterBodySlideCompare()
    {
        // ---------- Per-pane inputs ----------

        Add("BodySlideCompare.Gender",
            layperson: "Which gender's BodySlide presets this side offers, and which NPCs its preview-NPC box will offer. The two sides are independent, so you can put a male preset next to a female one.",
            technical: "Sets VM_BodySlideComparePane.PreviewGender, which rebuilds the preset list from VM_BodySlidesMenu.BodySlidesMale / BodySlidesFemale and, on the same throttle as a weight change, re-runs the gendered NPC eligibility scan that feeds the preview-NPC picker.",
            motivation: "Presets and preview NPCs are both per-gender; keeping the setting per-pane means a cross-gender comparison doesn't need two trips through the menu.");

        Add("BodySlideCompare.PresetFilter",
            layperson: "Type here to narrow the preset dropdown below to names containing what you typed.",
            technical: "Case-insensitive substring filter over the pane's preset list, re-applied on every keystroke. The current selection is preserved when it still passes the filter, so typing never blanks the viewer underneath.",
            motivation: "Preset libraries routinely run to hundreds of entries; scrolling an unfiltered dropdown to find one body type is slow.");

        Add("BodySlideCompare.Preset",
            layperson: "The BodySlide preset shown on this side. The list is already narrowed by the Filter box above, and you can type in the dropdown to jump to a name.",
            technical: "Bound to VM_BodySlideComparePane.SelectedPreset over FilteredPresets, with WPF TextSearch on the preset Label. Selecting one applies its sliders to the pane's viewer at the pane's current weight, generation-guarded so rapid changes can't land a stale preset over a newer one.",
            motivation: "Choosing the two presets is the whole point of the window; everything else on the pane exists to make that comparison fair.");

        Add("BodySlideCompare.Weight",
            layperson: "The NPC weight (0-100) this side previews at. Changing it re-shapes the body, and it also narrows the NPC box below to NPCs at that weight.",
            technical: "Sets VM_BodySlideComparePane.Weight, which re-applies the preset's weight interpolation and, on a 400 ms throttle, re-runs PreviewNpcResolver.FindNpcsAtWeight off-thread to rebuild the NPC picker's candidate list. The throttle matters because that scan walks every NPC record in the load order, so dragging the spinner costs one scan rather than one per value. Half of the weight-NPC interlock: this direction restricts which NPCs are offered.",
            motivation: "BodySlide presets interpolate between a weight-0 and a weight-100 shape, so two presets can look alike at one weight and quite different at another - comparing at a chosen weight is essential.");

        Add("BodySlideCompare.PreviewNpc",
            layperson: "The NPC this side is previewed on. The dropdown lists NPCs at the weight set above, but you can also type or paste any other NPC - and doing so moves the Weight box to that NPC's own weight. It fills itself in on open with whoever is being previewed by default.",
            technical: "The picker's CandidateFormKeys is bound to the weight-filtered candidate list, which narrows its suggestions only: the display of the current FormKey still resolves against the full record index, and a typed or pasted parseable FormKey is accepted outright. That escape hatch is what makes the second half of the interlock reachable - setting this reads the record's Weight via PreviewNpcResolver.GetNpcWeight and snaps the pane's Weight to it (rounded), guarded so the change doesn't bounce back and reset the NPC. When nothing is picked the pane resolves a default from the OBody Misc per-weight table at the nearest configured slot and writes it back here, so the box names who is actually on screen rather than sitting empty.",
            motivation: "Restricting the list to the chosen weight keeps the comparison fair, but a preview box that disagreed with the model on screen - or that refused an NPC you named - would be worse than no restriction at all.");

        // ---------- Cross-pane controls ----------

        Add("BodySlideCompare.LockCamera",
            layperson: "Keeps both previews pointing the same way: rotate, zoom or pan either side and the other follows.",
            technical: "Mirrors the five view-defining OrbitCamera properties (azimuth, elevation, distance, target, field of view) between the panes via OrbitCamera.CopyViewFrom, which suppresses the receiving camera's change event so the two cannot echo each other. Because the event is raised from the property setters rather than the mouse handlers, an automatic reframe after an NPC load propagates too.",
            motivation: "Two bodies viewed from even slightly different angles cannot be compared reliably - perspective alone changes apparent proportions.");

        Add("BodySlideCompare.Superimpose",
            layperson: "Draws pane B's model inside pane A's preview, on top of A's own model, so you can see exactly where the two shapes differ. Pane B keeps showing its own view as well.",
            technical: "Loads pane B's NPC and preset as a guest scene inside pane A's viewer. The guest goes through pane A's own mesh builder, texture manager and GL context - GL objects cannot be shared between two GLWpfControls - and is installed into a separate mesh list that the primary scene's bookkeeping never touches. The overlay survives pane A changing its own preset: the primary reload tears the guest meshes down and it re-installs once the new scene commits.",
            motivation: "Side-by-side comparison shows that two bodies differ; overlaying them shows where, which is what you need in order to act on it.");

        Add("BodySlideCompare.SuperimposeStyle",
            layperson: "How the superimposed model is drawn. Translucent shows it as a see-through colored body, Wireframe as coloured outlines only, and Textured draws it fully like the model underneath.",
            technical: "Applied per-mesh with no extra render pass. Translucent sets a flat tint plus sub-1 MaterialAlpha with depth writes off, so it sorts into the alpha-blend pass and never occludes the primary model. Wireframe reuses the missing-texture fallback's draw behavior (solid passes skipped, edges only) with a per-mesh color override, so it does not get mistaken for the green that means a texture failed to decode. Textured leaves the mesh exactly as the texture pass built it.",
            motivation: "Which one reads best depends on the comparison: translucency shows volume differences, wireframe never hides the model underneath, and textured is the honest full-detail view.");

        Add("BodySlideCompare.SuperimposeBodyOnly",
            layperson: "Overlays only the body of pane B, leaving out its head, hair and any worn gear. Usually what you want - two complete models at the same spot mostly obscure each other.",
            technical: "Filters the guest install to the Body part. Changing it re-installs from the retained load result, so it costs a GL upload but no NIF re-parse. Independent of the style setting, so all six style/scope combinations are reachable.",
            motivation: "BodySlide sliders only move the body; heads and hair drawn twice at one origin add nothing to the comparison and a lot of visual noise.");
    }
}

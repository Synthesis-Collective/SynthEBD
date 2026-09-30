namespace SynthEBD;

// 3-part documentation for the annotation queue's Panel window (Window_AnnotationPanel) and the
// queue button that opens it. Back view and HD reuse the Show Spread / BodySlides keys.
public static partial class UiDocs
{
    private static void RegisterAnnotationPanel()
    {
        Add("BodySlides.QueueOpenPanel",
            layperson: "Opens a window showing the queue's bodies side by side, sorted from smallest to largest on a measurement, with buttons under each body to label it on the spot. Best for Categories that are a scale you cut into bands (Butt: Flat / Normal / Round / Large).",
            technical: "Opens Window_AnnotationPanel over the built queue's slices for the target Category (enabled when a queue is built and no scan is running). Same preamble as Show Spread: scans first when the measurement cache is stale or empty, and renders on the editor viewer's NPC or the body type's default preview NPC. Non-modal; the queue stays usable. The panel snapshots the queue when opened, and marks itself stale (editing disabled) when the queue is rebuilt or its target Category changes.",
            motivation: "Judging a continuum one body at a time lets your standard drift between cases. Seeing neighbours side by side, in measured order, keeps the cuts consistent and makes a body that looks out of order stand out.");

        Add("AnnotationPanel.Metric",
            layperson: "The measurement the bodies are sorted by, smallest first (left to right, then top to bottom).",
            technical: "Lists every measurement that can decide the Category (VM_BodyTypeProfileEditor.CollectCategoryMeasurementNames), without Show Spread's Score: a per-value margin gives no single ordering across cells. Opens on the queue's Spread measurement when it is in the list, else the Category's primary measurement (the one its own rules test most). Slices with no value sort last and say so in their caption. Ties sort by preset label (ordinal) then weight, so re-opening shows the same order.",
            motivation: "Sorting is what makes neighbouring bodies comparable: a band boundary becomes a place in the row, and a body that looks out of place points at what the measurement misses.");

        Add("AnnotationPanel.ShowMeasurements",
            layperson: "Draws the lines of the sorting measurement on every image, so you can see exactly what was measured on each body.",
            technical: "Re-renders each image with the metric's measurement overlay (the same BeforeDraw hook as Show Spread: key vertices resolved on that render's own deformed mesh; region-based measurements tint the region cyan). Images with and without lines are cached separately, so toggling back is instant.",
            motivation: "When a body looks out of order, the lines show whether the measurement landed where you expected on that body.");

        Add("AnnotationPanel.ShowValues",
            layperson: "Adds each body's measured value and the label the rules currently give it to the caption. Off by default: judge first, then turn it on to review.",
            technical: "Off: captions show the weight (W##), the alias count, and a no-value note for an unmeasured slice - no numbers and no rule label. On: W## · value · rules: <label>, where the label is the Category's values from the rules alone (VM_BodyTypeProfile.DeriveRuleOnlyDescriptors, annotations re-derived out), computed on first use. Values print at the fewest decimals (3+) that tell the shown values apart. Not persisted.",
            motivation: "Printed numbers and today's labels anchor judgement; the sort already conveys order. Hidden while judging, they are still one click away for checking where the rules and your eye disagree.");

        Add("AnnotationPanel.Source",
            layperson: "Which bodies to show: the queue's own bodies (re-sorted), or N bodies spread evenly from the smallest to the largest across everything the queue could serve. Not available for a worklist queue.",
            technical: "QueueSlices: the built queue's slices (representatives; aliases ride along), whatever policy built them; the panel only decides the order. EvenlySpaced: the same candidate population BuildQueue samples from (Include annotated filter, alias grouping), N slices at sorted ranks floor((i + 0.5) / N * M) over the M with a value (AnnotationPanelLayout.PickEvenlySpaced), ties by label then weight so the same presets come back on re-open. Disabled under the List policy, where the worklist is already a deliberate sample.",
            motivation: "The queue's own sample decides which bodies you judge; the evenly spaced ladder is for drawing band boundaries across the whole population by eye.");

        Add("AnnotationPanel.EvenlySpacedCount",
            layperson: "How many bodies the evenly spaced ladder shows.",
            technical: "N for the EvenlySpaced source (default 24 = one page). When N is at least the number of candidates with a value, every one of them is shown. Changing it re-picks and returns to page 1.",
            motivation: "More rungs place a cut more precisely; fewer give a quicker overview.");

        Add("AnnotationPanel.Paging",
            layperson: "Moves between pages of 24 bodies.",
            technical: "24 cells per page. The current page renders first, then the next page is rendered in the background, so paging forward usually finds its images ready. Images stay cached for the window's lifetime.",
            motivation: "Keeps each page small enough to compare at a glance while still covering a long queue.");

        Add("AnnotationPanel.Cell",
            layperson: "One body at the weight it was measured at. Click the images to load it into the editor's viewer to rotate and inspect.",
            technical: "Same cell as Show Spread: front (or back) and side offscreen renders with one fixed whole-body camera, so sizes are comparable across cells. Clicking calls VM_BodyTypeProfileEditor.LoadSliceInViewer; it does not move the annotation table's selection. The preset label's tooltip lists the alias family the cell's verdict covers.",
            motivation: "The images are the thing being judged; the live viewer is there for a closer look when a thumbnail isn't enough.");

        Add("AnnotationPanel.ValueToggles",
            layperson: "Click a value to label this body with it; click it again to remove it. Saved immediately. A body can carry more than one value.",
            technical: "One toggle per value of the target Category, in the queue's digit-legend order. Each click flips the value in the slice's stored verdict and writes it through VM_AnnotationQueue.WriteVerdict for the representative and every alias: only this Category's descriptors are replaced, siblings are stamped in AliasLabels, and an empty set removes the verdict (and an annotation left with no descriptors). The annotation table's selection does not move; when the written slice is the one selected there, the annotation editor reloads so it does not revert the change. Toggles show what is stored. A slice counts as labelled in the session counters the first time it gets a non-empty verdict here. Disabled when the panel is stale.",
            motivation: "Labelling in place, next to the neighbours you are comparing against, is the point of the panel; a tag set rather than a radio group because a body can legitimately carry two values (decision D23).");
    }
}

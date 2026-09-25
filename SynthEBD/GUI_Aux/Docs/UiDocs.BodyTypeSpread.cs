namespace SynthEBD;

// 3-part documentation for the Show Spread window (Window_BodyTypeSpread) and the two
// Body Type Profile editor buttons that open it (Rules tab, Match Presets tab).
public static partial class UiDocs
{
    private static void RegisterBodyTypeSpread()
    {
        // ---------- Launch buttons (Body Type Profile editor) ----------

        Add("BodyTypeProfiles.ShowSpreadRules",
            layperson: "Opens a window showing, for the Category selected in the tree, what the smallest, typical and largest presets of each value look like - one row per value, rendered side by side.",
            technical: "Opens Window_BodyTypeSpread for the Category of the selected tree node (a Category node or any of its values). Scans first if the measurement cache is stale or empty, and re-derives descriptors from the cache if only the rules changed. Renders use the NPC loaded in this editor's viewer; when none is loaded (or it's the other gender), the default preview NPC for the body type's gender is loaded from the OBody per-weight preview table, at the configured weight nearest the editor's preview weight.",
            motivation: "A threshold is easiest to judge by eye at its edges: seeing each value's extremes and typical members next to each other shows at once whether a boundary sits in the right place.");

        Add("BodyTypeProfiles.ShowSpreadMatchPresets",
            layperson: "Opens the Show Spread window for the one Category you've narrowed the descriptor filter to. Enabled only when the filter's checked values all belong to a single Category.",
            technical: "Same window as the Rules tab's Show Spread, for the single Category spanned by the Match Presets descriptor filter selection (DescriptorFilter.DumpToHashSet). Disabled when the selection is empty or spans several Categories.",
            motivation: "Lets the spread be checked from where rules are usually tuned against the preset list, without switching tabs.");

        // ---------- Window controls ----------

        Add("BodyTypeSpread.Metric",
            layperson: "What the columns are ranked by: one of the measurements this Category's rules use, or Score - how firmly each preset holds its row's value.",
            technical: "Lists every measurement named in the Category's gender-eligible rules and in any rule they reach through descriptor references, in the Measurements tab's order. Score is the Match Presets σ-normalized margin (StdDevNormalized) for the row's own value: soft-AND/OR over the rule's conditions, positive when the preset carries the value, magnitude = distance from the rule boundary. The unassigned row has no value to score. Opens on the single measurement when only one decides the Category, and on Score when several do.",
            motivation: "A Category decided by several measurements can look right on one and wrong on another; switching the metric shows each in turn, and Score folds them into the single number the rules actually decide on.");

        Add("BodyTypeSpread.ShowBack",
            layperson: "Shows each preset from behind instead of from the front. The side view stays.",
            technical: "Switches each cell's first image between orbit azimuth 180 (front) and 0 (back). Images are cached per preset, weight and angle, so switching back is instant once rendered.",
            motivation: "Some shape traits - the glutes, the back, the shoulder blades - are only visible from behind.");

        Add("BodyTypeSpread.IgnoreManualAnnotations",
            layperson: "On (the default): each row shows presets purely by whether the rules put them there, ignoring any label you or a library assigned by hand. Off: rows also include hand-labeled presets, and a preset whose hand label disagrees with the rules is shown in orange (hover it to see both).",
            technical: "On: row membership is re-derived per slice with the preset's stored Manual / Library annotations left out of the classifier seed (VM_BodyTypeProfile.DeriveRuleOnlyDescriptors), so an annotation neither adds a value, nor suppresses the Category default, nor feeds aggregator (DescriptorRef) rules; Label-by-Sliders rule labels still count. Off: membership is the Match Presets set (classifier output plus seeds). A slice is contradicted when an annotation names a value in this Category that the rules-only derivation doesn't produce. Row order is kept across the toggle.",
            motivation: "Judging a threshold needs the presets the rules actually select; hand labels that disagree would blur the rows. Turning it off shows where the rules and your own judgement part ways - often the best hint about which threshold to move.");

        Add("BodyTypeSpread.ShowMeasurements",
            layperson: "Draws the lines of the measurement you're ranking by onto every image (with Score selected, all of this Category's measurements), so you can see exactly what was measured on each preset. A measurement that uses a region (a region volume, or a landmark picked from a region) also shades that region in translucent cyan.",
            technical: "Re-renders each image with an OffscreenRenderRequest.BeforeDraw hook that resolves the measurement's key vertices on that render's own deformed mesh (MeasurementMath.TryResolveKeyVertex, the scan's resolution) and pushes the same segments the live viewer overlay draws (AppendMeasurementLineSegments): yellow for the measured pair or axis leg, white for the other axis legs, cyan for a ratio's denominator. Lines draw through the body, as in the viewer. RegionVolume measurements have no line geometry; they, and measurements with a Region-strategy key vertex, instead tint the region's surface patch (the baked patch evaluated on the deformed mesh, or the member-vertex triangles for a region that is not a valid volume) cyan at 45% opacity, depth-tested so only the visible side is shaded. Images with and without lines are cached separately, so toggling back is instant.",
            motivation: "A number is only as good as the landmarks it's taken between; seeing the lines on the extremes shows at once when a key vertex landed somewhere unexpected on an unusual body.");

        Add("BodyTypeSpread.SortRows",
            layperson: "Puts the rows back in numeric order (smallest to largest) by the selected measurement. The window already opens in this order.",
            technical: "Sorts rows by each row's median on the selected measurement (SpreadStatistics.OrderByMedian); with Score selected, by the Category's primary measurement -- the one its own rules test most often -- since per-row scores aren't comparable across rows. Rows without values and the unassigned row go last. On open, rows are sorted by the primary measurement; aggregator Categories whose rules only reference other descriptors keep the Rules-tab order.",
            motivation: "Rows in threshold order (Skinny, Normal, Thick) read as a progression, making a misplaced boundary easy to spot; alphabetical order scrambles that.");

        Add("BodyTypeSpread.MoveRow",
            layperson: "Moves this row up or down. Your order is kept when you switch metric, until you press Sort rows.",
            technical: "Reorders the row in place (no re-render; images are cached). The order persists across metric, view and Peak-bin changes for as long as the window is open.",
            motivation: "Some Categories have no single natural order, and sometimes the useful comparison is two particular rows side by side.");

        Add("BodyTypeSpread.PeakBins",
            layperson: "How finely the Peak column divides each row's values when looking for the most common range. More bins means a narrower, more precise peak on rows with many presets.",
            technical: "Number of equal-width bins over the row's [min, max], binned exactly like the measurement histogram. Peak shows the preset closest to the centre of the fullest bin (lowest bin on a tie), chosen from that bin's own members.",
            motivation: "On continuous values the mode only exists relative to a bin width, so the width is left adjustable rather than hidden.");

        Add("BodyTypeSpread.Cell",
            layperson: "One preset, at the weight it was measured at, chosen to represent this column's statistic for this row. Click it to load it into the editor's viewer to rotate and inspect.",
            technical: "Min / Max are the lowest / highest slices; Mean the slice nearest the mean; Median the middle slice (the lower of the two middle slices for an even count); Peak the slice nearest the centre of the fullest bin. Each slice keeps its own weight. The caption shows the slice's value and, in brackets, the statistic's own value when it falls between slices. Renders use the offscreen renderer with one fixed whole-body camera, so body sizes are comparable across cells.",
            motivation: "Rendering the actual presets at the statistics - rather than just printing the numbers - lets a threshold be judged by what the bodies look like.");
    }
}

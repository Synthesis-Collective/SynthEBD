namespace SynthEBD;

// 3-part documentation for the Show Sample window (Window_DescriptorSample) and the Rules-tab
// button that opens it. Back view, measurement lines, paging, HD and 3D reuse the Show Spread /
// annotation Panel / BodySlides keys.
public static partial class UiDocs
{
    private static void RegisterDescriptorSample()
    {
        Add("BodyTypeProfiles.ShowSampleRules",
            layperson: "Opens a window showing a random selection of the presets that currently get the value selected in the tree (50 by default), rendered side by side. Select a value, not a Category, to enable it.",
            technical: "Opens Window_DescriptorSample for the selected value node's (Category, Value). Enabled only for a value node while no scan is running. Same preamble as Show Spread: scans first if the measurement cache is stale or empty, re-derives descriptors if only the rules changed, and renders on the editor viewer's NPC or the default preview NPC for the body type's gender. The population is the matching list under the node: the profile's derived descriptors (ScanResults) for the body type's gender, hidden-and-disabled presets excluded, snapshotted at open.",
            motivation: "Show Spread shows a value's edges and typical members; a random sample shows what the value's population really looks like, which is where a rule that catches the wrong kind of body shows up.");

        Add("DescriptorSample.SampleSize",
            layperson: "How many presets to show. If fewer presets have this value, all of them are shown. Changing the number picks a new random selection.",
            technical: "Number of (preset, weight) slices drawn uniformly at random without replacement from the matching population (AnnotationPanelLayout.PickRandom; default 50, capped at the population size). Changing it redraws and returns to page 1. Window-only, not saved.",
            motivation: "A small sample is quick to render and take in; a larger one gives more confidence that what you see is typical.");

        Add("DescriptorSample.Reshuffle",
            layperson: "Picks a new random selection of the same size.",
            technical: "Redraws SampleSize slices from the same population with the window's random generator, then re-applies the current sort. Images already rendered stay cached, so presets that come up again appear immediately.",
            motivation: "One random sample can be unlucky; a few reshuffles show whether a pattern is real.");

        Add("DescriptorSample.OnePerPreset",
            layperson: "When on, each preset appears at most once (at one of its weights, chosen at random), so the selection covers more different presets.",
            technical: "The draw skips a slice whose preset label was already drawn, so at most one weight slot per preset is shown; the sample can then be smaller than Sample size when the population has fewer distinct presets. Off: slices are drawn independently, so a preset can appear at several weights.",
            motivation: "Most presets get a value at every weight, so a slice sample often shows the same preset twice; one per preset spends the sample on variety instead.");

        Add("DescriptorSample.IgnoreManualAnnotations",
            layperson: "Off (the default): shows the same presets the Rules tab lists for this value. On: shows only presets the rules give this value on their own, ignoring any labels you or a library assigned by hand.",
            technical: "Off: membership is the slice's derived descriptors (ScanResults), in which stored Manual / Library annotations seed the classifier as at scan time (they can suppress a Category default or feed DescriptorRef rules). On: membership is re-derived per slice with those annotations left out (VM_BodyTypeProfile.DeriveRuleOnlyDescriptors), as in Show Spread. Changing it redraws.",
            motivation: "Hand labels can change what the rules decide downstream; turning them off shows what the rules alone select.");

        Add("DescriptorSample.Metric",
            layperson: "The measurement the presets are sorted by, smallest first (left to right, then top to bottom). Sorting only changes the order, not which presets were picked. Free lets you drag them into your own order.",
            technical: "Same list as the annotation Panel: Free first, then every measurement that can decide the Category (CollectCategoryMeasurementNames), marked \"(<Category> rule)\", then every other profile measurement. Opens on the Category's primary measurement. Sorting is AnnotationPanelLayout.SortByValue over the drawn sample; slices with no value go last. Also chooses the measurement Show measurements draws and Show values prints.",
            motivation: "In measured order, a preset that looks out of place among its neighbours points at what the rule's measurements miss.");

        Add("DescriptorSample.ShowValues",
            layperson: "Adds each preset's value on the sorting measurement to its caption. If a preset only has this value because of a hand label, the caption also says what the rules alone would give it.",
            technical: "Off: W## (plus a no-value note for an unmeasured slice). On: W## · value, formatted to the fewest decimals that tell the page's values apart; with Ignore Manual Annotations off, a slice whose rule-only derivation (DeriveRuleOnlyDescriptors) does not produce this value also gets 'rules alone: <values>'.",
            motivation: "The numbers show where each body sits relative to the rule's thresholds, and the rules-alone note explains a preset that seems not to belong.");

        Add("DescriptorSample.CopyOrder",
            layperson: "Copies the presets' current order - every page, left to right - to the clipboard, one preset per line.",
            technical: "AnnotationPanelLayout.FormatOrder: a '#' header (Category = Value, profile, gender, sample size, sort mode, time), then 'label | weight' per drawn slice in display order - the annotation queue's plain-text worklist format, so it can be pasted as a List queue.",
            motivation: "A sample worth a closer look can be turned straight into an annotation worklist.");

        Add("DescriptorSample.Cell",
            layperson: "One preset at the weight it was picked at. Click the images to load it into the editor's viewer. Hold Ctrl and turn the mouse wheel to make every preset bigger or smaller.",
            technical: "Same cell as the annotation Panel without the value toggles: front (or back) and side offscreen renders with one fixed whole-body camera, so sizes are comparable across cells. Ctrl + wheel scales every cell by 10% per notch (0.5x to 3x). In Free mode a single click starts a drag, so loading takes a double-click.",
            motivation: "The images are what is being judged; the live viewer is there for a closer look when a thumbnail isn't enough.");
    }
}

namespace SynthEBD;

// 3-part documentation for shell-level chrome (the Run button), the shared named FormKey pickers,
// and the verbose-logging Detailed Report NPC selector embedded in General Settings.
public static partial class UiDocs
{
    private static void RegisterShell()
    {
        Add("Shell.RunButton",
            layperson: "Runs the patcher: every eligible NPC gets assets, body shape, height, and head parts assigned according to your settings, and the output plugin and support files are written. The window switches to the log view so you can watch progress.",
            technical: "Bound to VM_RunButton.ClickRun (a ReactiveCommand). It switches to the log display, dumps all view models to their models, runs PreRunValidation.ValidatePatcherState plus the BodySlide/BodyGen annotation checks, resets the output environment on repeat runs so results overwrite instead of stacking, then executes Patcher.RunPatcher on a background task.",
            motivation: "One explicit commit point runs everything configured elsewhere; validation goes first so a bad setup fails fast with readable errors instead of a half-written patch.");

        Add("Pickers.SearchBox",
            layperson: "Type part of a record's name, editor ID, or FormKey and pick a match from the dropdown. Pasting a complete FormKey (like 013BA3:Skyrim.esm) selects it directly.",
            technical: "Backed by RecordNameIndexer, which builds a background index of winning-override records (Name, EditorID, FormKey) per link cache and scoped record type set. Input is debounced 250 ms and matched culture-aware and case-insensitively; up to 500 suggestions are listed, and any parseable FormKey is accepted even if absent from the index (for example a record from an unloaded plugin).",
            motivation: "Mutagen's stock picker searches only EditorIDs and FormKeys, but users know records by their in-game names; name search plus direct FormKey entry covers both casual and power-user workflows.");

        Add("Pickers.Clear",
            layperson: "Clears the current selection so no record is chosen.",
            technical: "Sets the picker's FormKey dependency property to FormKey.Null, which flows through the two-way binding into the underlying setting.",
            motivation: "Erasing the search text alone would not unset the stored record; an explicit clear button makes emptying the field deliberate and obvious.");

        Add("Pickers.Remove",
            layperson: "Removes this record from the list.",
            technical: "Deletes the entry's FormKey from the multi-picker's bound FormKeys collection (an ObservableCollection of FormKey), updating the underlying setting immediately.",
            motivation: "Multi-record lists like allowed races are edited incrementally; per-row removal fixes a single mistake without retyping the rest.");

        Add("Logging.AllowUnique",
            layperson: "Include NPCs flagged Unique (named, one-of-a-kind characters) among those that receive a detailed log.",
            technical: "DetailedReportNPCSelector.AllowUnique. When false, VerboseLoggingNPCSelector rejects any NPC whose NpcConfiguration flags include Unique, so no unique NPC receives the verbose report.",
            motivation: "Splitting unique from generic NPCs is the quickest way to halve log noise when a problem clearly affects only one of the two populations.");

        Add("Logging.AllowNonUnique",
            layperson: "Include generic, non-unique NPCs (bandits, guards, leveled spawns) among those that receive a detailed log.",
            technical: "DetailedReportNPCSelector.AllowNonUnique. When false, VerboseLoggingNPCSelector rejects NPCs that lack the Unique flag.",
            motivation: "Generic NPC records are numerous and often irrelevant to a problem with named characters; excluding them keeps report output manageable.");

        Add("Logging.AllowedRaces",
            layperson: "If any races are listed here, only NPCs of these races receive the detailed log. Leave the list empty to allow all races.",
            technical: "DetailedReportNPCSelector.AllowedRaces, a set of RACE FormKeys compared directly against the NPC record's Race (no Race Alias translation). When the set is non-empty, an NPC whose race is not in it is rejected.",
            motivation: "Appearance bugs are frequently race-scoped - one race's textures or morphs misbehaving - so filtering by race captures the affected NPCs without logging the rest of the world.");

        Add("Logging.AllowedRaceGroupings",
            layperson: "Pick named race groups from your General Settings instead of listing races one by one. Note: the detailed-log filter currently only honors the explicit race lists, so prefer Allowed Races for now.",
            technical: "Stored as grouping labels in DetailedReportNPCSelector.AllowedRaceGroupings. Unlike the distribution axes, which merge grouping labels into their race lists during preprocessing, VerboseLoggingNPCSelector evaluates only AllowedRaces/DisallowedRaces - so selections here do not yet affect which NPCs get logged.",
            motivation: "Groupings exist to keep race lists short and centrally managed; the control mirrors the standard rule layout, but until the evaluator consumes groupings, explicit races are the reliable filter.");

        Add("Logging.DisallowedRaces",
            layperson: "NPCs of these races never receive the detailed log, even if they pass every other filter.",
            technical: "DetailedReportNPCSelector.DisallowedRaces; an NPC whose Race FormKey appears in the set is rejected by VerboseLoggingNPCSelector regardless of the allowed lists.",
            motivation: "Broad filters sometimes need carve-outs - for example logging all humanoids except the one race you already know is fine.");

        Add("Logging.DisallowedRaceGroupings",
            layperson: "Race groups whose members should never receive the detailed log. Note: the detailed-log filter currently only honors the explicit race lists, so prefer Disallowed Races for now.",
            technical: "Stored as grouping labels in DetailedReportNPCSelector.DisallowedRaceGroupings. As with the allowed side, VerboseLoggingNPCSelector currently evaluates only the explicit race sets and does not expand grouping labels, so these selections do not yet affect logging.",
            motivation: "Intended as the exclusion counterpart to grouping-based selection; until the evaluator consumes groupings, use explicit disallowed races.");

        Add("Logging.AllowedAttributes",
            layperson: "Traits an NPC must have to receive the detailed log - for example a specific class, faction, or name. Leave empty for no requirement; Add New creates a rule.",
            technical: "DetailedReportNPCSelector.AllowedAttributes, matched via AttributeMatcher.MatchNPCtoAttributeList against the NPC record. Attribute-group labels resolve against GeneralSettings.AttributeGroups (this is a General-settings feature, so the General set is the group source). If any attributes are defined and none match, the NPC is not logged.",
            motivation: "Attributes reach properties races cannot - factions, classes, keywords, names - letting you capture logs for exactly the population showing a problem.");

        Add("Logging.DisallowedAttributes",
            layperson: "Traits that exclude an NPC from detailed logging even when everything else matches.",
            technical: "DetailedReportNPCSelector.DisallowedAttributes, evaluated with the same AttributeMatcher call and GeneralSettings.AttributeGroups source as the allowed list; a match rejects the NPC.",
            motivation: "Lets you subtract known-good subgroups (say, a faction already verified) from a broad logging net instead of enumerating everything you do want.");

        Add("Logging.WeightRange",
            layperson: "Only NPCs whose weight value falls inside this range (inclusive, 0 to 100) receive the detailed log.",
            technical: "DetailedReportNPCSelector.WeightRange (NPCWeightRange, default 0-100). VerboseLoggingNPCSelector rejects NPCs whose record Weight is below the lower bound or above the upper bound.",
            motivation: "Body-shape and texture issues often manifest only at weight extremes; a weight window targets exactly those NPCs.");
    }
}

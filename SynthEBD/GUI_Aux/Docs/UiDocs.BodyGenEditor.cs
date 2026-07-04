namespace SynthEBD;

// 3-part documentation for the BodyGen config editor: the per-config header (UC_BodyGenConfig)
// and the per-template morph editor (UC_BodyGenTemplate) with its distribution rules and live
// 3D preview controls. The top-level BodyGen Integration panel is documented in
// UiDocs.BodyShape.cs under the "BodyGen." prefix.
public static partial class UiDocs
{
    private static void RegisterBodyGenEditor()
    {
        // ---------- Config header (UC_BodyGenConfig) ----------

        Add("BodyGenEditor.ConfigGender",
            layperson: "Sets whether this settings file applies to male or female NPCs. A config only ever hands its morphs to NPCs of this gender.",
            technical: "Mirror of BodyGenConfig.Gender. At load, SettingsIO_BodyGen partitions configs into male and female collections by this value, and during patching BodyGenSelector draws morphs for each NPC only from the current config of the matching gender (chosen by the Current Male/Female Config dropdowns).",
            motivation: "Male and female bodies use entirely different slider ecosystems (e.g. CBBE vs HIMBO), so BodyGen databases are authored per gender and the patcher needs to know which population this one targets.");

        // ---------- Template (morph) editor: identity ----------

        Add("BodyGenEditor.MorphName",
            layperson: "The name of this morph. Everything else refers to the morph by this name - Specific NPC Assignments, consistency, and the files SynthEBD writes for the game - so keep it unique within the config.",
            technical: "The template's Label. BodyGenWriter emits each assigned morph as a 'Label=Specs' line in the RaceMenu BodyGen templates.ini and lists the chosen labels per NPC in morphs.ini, so duplicate labels would collide at runtime. Specific NPC Assignments (BodyGenMorphNames) and the consistency file also reference morphs by this string.",
            motivation: "The RaceMenu BodyGen file format keys morphs by name, and readable names keep shared configs and troubleshooting logs legible.");

        Add("BodyGenEditor.Notes",
            layperson: "Free-form notes about this morph, such as what it looks like or where it came from. Purely for your own reference.",
            technical: "Stored as BodyGenTemplate.Notes in the config JSON and round-tripped by the editor; no patcher code reads it and it is not written to any output file.",
            motivation: "Config authors need somewhere to leave reminders and documentation for downstream users without affecting distribution behavior.");

        Add("BodyGenEditor.Specs",
            layperson: "The actual body-morph recipe: a comma-separated list of slider names and values, like 'AnkleSize@0.6, 7B Upper@0.1:0.3'. A single number applies that fraction of the slider; a Low:High pair lets the game roll a random amount in that range for each NPC.",
            technical: "Written verbatim as the value of the 'Label=Specs' line in the RaceMenu BodyGen templates.ini. Grammar: 'Slider@Value' or 'Slider@Low:High' entries separated by commas, where 1.0 equals the slider's full BodySlide morph (slider at 100). The editor live-parses the string with BodyGenSpecsParser, surfaces malformed entries under the preview-weight slider, and previews the result through the BodySlide deformer.",
            motivation: "This string is the morph itself - every other field only decides who receives it. Live parsing and 3D preview catch typos that would otherwise silently produce no morph in game.");

        // ---------- Template editor: grouping and descriptors ----------

        Add("BodyGenEditor.MemberOfGroups",
            layperson: "The morph groups this morph belongs to. Morphs are only ever distributed through groups: the Morph Group Map decides which groups each race draws from, and a morph that belongs to no group is never assigned (the editor outlines it in red).",
            technical: "Sets BodyGenTemplate.MemberOfTemplateGroups. During selection, the config's RacialTemplateGroupMap yields group combinations for the NPC's race, and each combination position is filled by drawing one rule-passing template from that group's members; a combination whose group resolves to zero eligible templates is discarded entirely.",
            motivation: "Groups decouple 'what morphs exist' from 'which races get what', letting one morph serve many racial mappings and letting mappings combine categories (for example one torso morph plus one leg morph per NPC).");

        Add("BodyGenEditor.Descriptors",
            layperson: "Body shape labels (like Build: Chubby) describing what this morph does to the body. Other parts of SynthEBD use them to keep textures and body shapes consistent - for example, a muscular normal map can demand a muscular morph.",
            technical: "Annotates the template with BodyShapeDescriptors defined in this config's Morph Descriptors menu. During validation, the morph's descriptors must satisfy the Allowed/Disallowed BodyGen descriptor rules of the NPC's assigned asset pack and its subgroups, and each descriptor definition's own distribution rules can further reject the morph or add ForceIf priority for matching NPCs.",
            motivation: "Descriptors let asset packs constrain body shape by meaning ('needs a heavy build') instead of naming specific morphs the recipient may not have installed, keeping shared configs interoperable.");

        // ---------- Template editor: race rules ----------

        Add("BodyGenEditor.AllowedRaces",
            layperson: "Races that may receive this morph. Leave blank to allow every race (the other rules still apply).",
            technical: "If non-empty, the NPC's body-shape race (after any race aliasing) must appear in this list or the morph is invalid for that NPC. At the start of a patcher run, BodyGenPreprocessing flattens the Allowed Race Groupings into this list, so the effective whitelist is the union of both fields.",
            motivation: "Race gating keeps shapes authored for one body population off races they were never meant for.");

        Add("BodyGenEditor.AllowedRaceGroupings",
            layperson: "Named bundles of races (like 'Humanoid Playable') that may receive this morph. Easier than listing individual races, and the rule keeps working when the user's groupings include custom races.",
            technical: "Stores race-grouping labels. At patch time BodyGenPreprocessing resolves each label against the effective grouping set - General settings definitions supersede same-labeled config-local ones when Overwrite Plugin Race Groups is enabled - and merges the member races into the flat allowed-race list before selection.",
            motivation: "Groupings let a rule say 'any elf' once and have the meaning maintained centrally, instead of repeating race lists on every morph.");

        Add("BodyGenEditor.DisallowedRaces",
            layperson: "Races that must never receive this morph, even if they pass every other rule.",
            technical: "If the NPC's body-shape race (after any race aliasing) appears in this list the morph is rejected outright; a disallowed match wins over the allowed lists. Disallowed Race Groupings are flattened into this list at patch time.",
            motivation: "A blacklist is the quickest way to carve exceptions out of a broad allow rule, such as allowing all humanoids except one race.");

        Add("BodyGenEditor.DisallowedRaceGroupings",
            layperson: "Named bundles of races that must never receive this morph.",
            technical: "Race-grouping labels resolved the same way as Allowed Race Groupings (General definitions supersede same-labeled local ones when Overwrite Plugin Race Groups is enabled) and merged into the flat disallowed-race list at patch time.",
            motivation: "Excluding a whole family of races by name is less error-prone than enumerating each member race by hand.");

        // ---------- Template editor: attribute rules ----------

        Add("BodyGenEditor.AllowedAttributes",
            layperson: "NPC traits (factions, classes, names, and so on) required for this morph. If any are listed, the NPC must match at least one of them. Attributes marked ForceIf additionally steer the picker toward this morph on matching NPCs.",
            technical: "Evaluated by AttributeMatcher against this config's own Attribute Groups list (General-settings groups supersede same-labeled local ones when Overwrite Plugin Attribute Groups is enabled). ForceIf matches are tallied per template: combinations containing the highest tally are preferred during selection, and a ForceIf match overrides 'Distribute to non-forced NPCs' being unchecked.",
            motivation: "Attributes tie body shape to who the NPC is - warriors can be built powerfully, beggars gaunt - rather than distributing shapes purely at random.");

        Add("BodyGenEditor.DisallowedAttributes",
            layperson: "NPC traits that block this morph. If the NPC matches any of them, the morph is never assigned to that NPC.",
            technical: "Evaluated with the same attribute-group resolution as Allowed NPC Attributes; a single match invalidates the morph for that NPC. ForceIf has no meaning on disallowed attributes.",
            motivation: "Lets a broadly-distributed morph carve out exceptions (for example, keeping exaggerated shapes off elder-class NPCs) without narrowing its allow rules.");

        // ---------- Template editor: distribution gates ----------

        Add("BodyGenEditor.WeightRange",
            layperson: "The NPC weight range (0 to 100, inclusive) this morph may be assigned to. An NPC's weight slider already implies a build; use this to keep heavy-set morphs off weight-0 NPCs.",
            technical: "Compared against the NPC record's Weight value; outside Lower..Upper the morph is invalid. The gate reads the vanilla record weight only - BodyGen morph values themselves are applied as-is rather than blended by weight.",
            motivation: "NPC weight is vanilla's own body-build dial; respecting it keeps assigned shapes coherent with how the game already presents each NPC.");

        Add("BodyGenEditor.AllowRandom",
            layperson: "When checked, this morph joins the normal random pool. When unchecked, only NPCs that match one of its ForceIf attributes or a Specific NPC Assignment can receive it.",
            technical: "Maps to BodyGenTemplate.AllowRandom. The gate is evaluated last in the shared body-shape rule battery, so a ForceIf attribute match (including descriptor-derived ones) still admits the morph while this is off; Specific NPC Assignments bypass the rules entirely.",
            motivation: "Supports 'special' morphs - a distinctive shape reserved for one character or faction - that should never dilute the general random pool.");

        Add("BodyGenEditor.AllowUnique",
            layperson: "Whether NPCs flagged as Unique (mostly named story characters) may receive this morph.",
            technical: "When off, any NPC whose record configuration carries the Unique flag is rejected. Evaluated early in the shared body-shape rule battery, before the race, weight, and attribute checks.",
            motivation: "Users often want generic crowd variety while keeping recognizable named characters on curated shapes, or the reverse; this flag splits the two populations.");

        Add("BodyGenEditor.AllowNonUnique",
            layperson: "Whether ordinary, non-unique NPCs (guards, bandits, and other generic actors) may receive this morph.",
            technical: "When off, any NPC without the Unique record flag is rejected. Complement of Allow Unique NPCs; with both unchecked the morph is only reachable through Specific NPC Assignments, which are checked before either flag.",
            motivation: "The mirror of Allow Unique NPCs: it reserves special shapes for named characters without letting them spread across the generic population.");

        Add("BodyGenEditor.ProbabilityWeighting",
            layperson: "How likely this morph is to be picked compared to the other eligible morphs in the same group. A morph with weighting 2 is drawn about twice as often as one with weighting 1.",
            technical: "The template's relative weight in the random draw: after a group combination is chosen, one morph per group position is selected with probability proportional to ProbabilityWeighting multiplied by any matching Probability Modifier factors. Accepts non-integer values.",
            motivation: "Weighting shifts how common each shape is without banning anything - most NPCs can get average builds while extreme shapes stay rare.");

        Add("BodyGenEditor.ProbabilityModifiers",
            layperson: "Conditional nudges to this morph's odds: each entry pairs an NPC trait with a multiplier. When an NPC matches, this morph's probability is scaled by that factor - above 1 makes it likelier, below 1 rarer.",
            technical: "Each AttributeWeightModifier holds attributes plus a factor; during selection, every modifier whose attributes match the NPC multiplies into the morph's effective probability weighting (multiple matches multiply together). Uses the same attribute-group resolution as the allowed/disallowed attribute rules.",
            motivation: "Sits between a hard allow/disallow and a flat weighting: soldiers can strongly favor muscular morphs while civilians still occasionally roll them.");

        Add("BodyGenEditor.RequiredTemplates",
            layperson: "A list of other morphs this morph is meant to be paired with. Note that the current version of SynthEBD does not enforce this pairing when assigning morphs.",
            technical: "Maps to BodyGenTemplate.RequiredTemplates, retained for zEBD config compatibility (populated when importing zEBD templates). It round-trips through the editor and JSON, but no current selector code evaluates it - assignments are built purely from group combinations and the other rules.",
            motivation: "zEBD used this field to keep interdependent morphs together; SynthEBD preserves it so imported configs survive round-trips, with Morph Group Map combinations now being the supported way to co-assign morphs.");

        // ---------- Template editor: live preview ----------

        Add("BodyGenEditor.PreviewWeight",
            layperson: "Scrubs the 3D preview through the morph's random range: 0 shows every ranged entry at its Low value, 100 at its High value. Entries with a single value do not change.",
            technical: "Drives the BodySlide deformer's weight interpolation over the parsed spec (High maps to weight 100, Low to weight 0). Preview-only: BodyGen output is weight-independent, and for Low:High entries the game's BodyGen runtime rolls a value within the range per NPC.",
            motivation: "A ranged spec describes a family of bodies, not one; scrubbing the slider shows the extremes the game may roll before you commit to the range.");

        Add("BodyGenEditor.PreviewPaneSplitter",
            layperson: "Drag left or right to resize the 3D preview pane.",
            technical: "A GridSplitter between the rule form and the embedded Character Viewer column; it only adjusts this editor's column widths and the position is not persisted.",
            motivation: "Rule editing wants a wide form while morph inspection wants a large viewport; the splitter lets each task claim the space when needed.");
    }
}

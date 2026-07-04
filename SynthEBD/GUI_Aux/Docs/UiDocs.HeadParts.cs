namespace SynthEBD;

// 3-part documentation for the Head Parts menus: the miscellaneous settings panel, the per-type
// head part lists (including the 3D preview controls), and the two distribution-rule editors.
// Parallel fields intentionally use distinct "Part" (UC_HeadPart) and "Category"
// (UC_HeadPartCategoryRules) keys because the two rule sets gate at different scopes: the
// category gate runs once per NPC per head-part type, while part rules filter the individual
// candidates within that type.
public static partial class UiDocs
{
    private static void RegisterHeadParts()
    {
        // --- Misc settings (UC_HeadPartMiscSettings) ---

        Add("HeadParts.PatchingMode",
            layperson: "How the chosen head parts get onto NPCs in the game. Script applies them while you play using a small script; Nif writes them permanently into the NPC's plugin records and face meshes ahead of time.",
            technical: "Sets Settings_Headparts.PatchingMode. Script mode adds a loader quest, an assignment ability, and the SynthEBD_HeadPartScriptActive global, and writes SynthEBD\\HeadPartAssignments.json, which the SynthEBDHeadPartScript Papyrus script applies to actors at runtime. NifEdit mode writes the head-part references into NPC record overrides (or SkyPatcher surrogate records) and bakes the result into FaceGen NIFs during patching.",
            motivation: "The two routes trade compatibility for simplicity: Nif mode needs no runtime scripting and its results exist even before any script fires, while Script mode leaves NPC face records and FaceGen untouched and applies the changes on top of whatever wins your load order.");

        Add("HeadParts.AssociatedBodyGenConfig",
            layperson: "The BodyGen configuration file whose body-shape labels the head-part rules understand for this gender. Pick the config you actually distribute so head parts can be matched to body types. Only shown when the body selection mode is BodyGen.",
            technical: "Sets Settings_Headparts.AssociatedBodyGenConfigNameMale/Female - one tracked config per gender, since BodyGen configs are gender-specific. The tracked config's descriptor vocabulary populates every Allowed/Disallowed BodyGen Descriptors menu at both the category and the individual head-part level, and during patching those descriptors are compared against the NPC's assigned BodyGen morphs.",
            motivation: "Body-shape descriptors are defined inside each BodyGen config, so the head-parts menu needs to know which config's vocabulary its rules should speak; pointing it at the wrong config would offer labels that never match the morphs actually being assigned.");

        // --- Per-type list (UC_HeadPartList) ---

        Add("HeadParts.LockPreviewNpc",
            layperson: "Keeps the same character in the 3D preview while you click through head parts, instead of letting the preview switch to a different NPC when a part's race rules call for one.",
            technical: "When no explicit Preview NPC is picked, the previewer re-resolves a stand-in for each selected head part from its effective allowed races and the preview-NPC mapping in General Settings; locking reuses the last loaded NPC instead. An explicitly picked Preview NPC always wins over the lock.",
            motivation: "Comparing hairs or brows is much easier on a constant face; without the lock, selecting a part with different race rules could silently swap the model out from under the comparison.");

        // --- Individual head part rules (UC_HeadPart) ---

        Add("HeadParts.PartAllowedRaces",
            layperson: "The races that may receive this head part. Leave the list empty to allow every patchable race.",
            technical: "At the start of a run the allowed race groupings are flattened into this list; a non-empty result must contain the NPC's head-parts race (after race-alias substitution) or the part is skipped for that NPC. The patcher also extends the winning head part's ValidRaces FormList so the game accepts it on the assigned race.",
            motivation: "Head parts are usually sculpted for particular head meshes - a human hairstyle clips or floats on Khajiit and Argonian heads - so race gating keeps each part on the races it was made for.");

        Add("HeadParts.PartAllowedRaceGroupings",
            layperson: "Named collections of races (like 'Humanoid' or 'Elven') that may receive this head part, so one checkbox can allow a whole family of races.",
            technical: "Each checked grouping label is expanded into its member races using the General race groupings and merged into this part's Allowed Races list during preprocessing; from then on it gates identically to individually listed races.",
            motivation: "Groupings keep rules short and centrally tunable: allowing 'Elven' reads better and survives new race additions better than enumerating every elf race on every head part.");

        Add("HeadParts.PartDisallowedRaces",
            layperson: "Races that must never receive this head part, even if the allowed lists would otherwise permit them.",
            technical: "Merged with the expanded disallowed race groupings at run start; if the resulting list contains the NPC's head-parts race, the part is invalid for that NPC regardless of the allowed lists.",
            motivation: "An explicit veto is simpler than rebuilding an allowed list minus one race - for example allowing the 'Humanoid' grouping but excluding Elders.");

        Add("HeadParts.PartDisallowedRaceGroupings",
            layperson: "Named collections of races that must never receive this head part.",
            technical: "Each checked grouping label expands into its member races via the General race groupings and merges into this part's Disallowed Races list during preprocessing; a match vetoes the part for that NPC.",
            motivation: "Lets one checkbox veto a whole family of races, and the veto stays correct as the grouping's definition is edited centrally.");

        Add("HeadParts.PartAllowedAttributes",
            layperson: "Traits an NPC must have to receive this head part - for example belonging to a certain faction or class. The NPC needs to match at least one of the attributes listed here.",
            technical: "Evaluated by the attribute matcher against the NPC record: each attribute is a set of sub-attributes that must all match, and the NPC must match at least one attribute in the list (an empty list imposes no restriction). Sub-attributes flagged Force If additionally promote the part: parts with Force If matches are pooled and chosen preferentially, skipping the assign-none roll. Group-type attributes resolve against the General attribute groups.",
            motivation: "Attributes tie appearance to identity - scars for soldiers, well-groomed brows for nobles - instead of purely random distribution.");

        Add("HeadParts.PartDisallowedAttributes",
            layperson: "Traits that block an NPC from receiving this head part. If the NPC matches any attribute listed here, the part is off the table.",
            technical: "Evaluated like the allowed attributes but inverted: matching any listed attribute invalidates this head part for the NPC. Group references use the same General attribute-group resolution; Force If flags have no meaning on the disallowed side.",
            motivation: "Vetoes carve exceptions out of broad rules - a scar set for everyone except priests, for example - without restructuring the allowed lists.");

        Add("HeadParts.PartWeightRange",
            layperson: "The body-weight range (0 to 100, the NPC's weight-slider value) an NPC must fall inside to receive this head part. Both ends are inclusive.",
            technical: "Compares the NPC record's Weight against the Lower and Upper bounds; NPCs outside the range skip this part. The category rules carry their own weight range, and both must pass.",
            motivation: "Lets appearance track physique - gaunt-face parts for weight-0 NPCs or fuller styles for heavy ones - since weight is the game's main body-build axis.");

        Add("HeadParts.PartDistributeToNonForced",
            layperson: "Allows this head part to be handed out randomly. Uncheck it to make the part exclusive: only NPCs you target through Specific NPC Assignments or Force If attributes will ever get it.",
            technical: "Mirror of the part's bAllowRandom flag. When false, the part is dropped from the random candidate pool unless the NPC tallied at least one Force If match for it (the gate runs after attribute matching) or has it specifically assigned.",
            motivation: "Some parts are meant for particular characters - a signature scar or a lore-specific hairstyle - and should never appear on random strangers.");

        Add("HeadParts.PartAllowUnique",
            layperson: "Whether NPCs flagged as unique (mostly named, hand-placed characters) may receive this head part.",
            technical: "Checks the Unique flag in the NPC record's configuration; when unchecked, unique NPCs are skipped for this part.",
            motivation: "Users often want bold or unusual parts kept away from important named characters while still circulating among generic ones - or the reverse.");

        Add("HeadParts.PartAllowNonUnique",
            layperson: "Whether ordinary, non-unique NPCs (guards, bandits, and other generic characters) may receive this head part.",
            technical: "The complement of Allow Unique NPCs: when unchecked, NPCs without the Unique flag are skipped for this part.",
            motivation: "Reserving a part for unique NPCs keeps signature looks special; generic NPCs are numerous, so anything allowed here will be seen often.");

        Add("HeadParts.PartProbabilityWeighting",
            layperson: "How likely this head part is to be picked compared to the other valid parts of the same type. Doubling the number roughly doubles its share; it is a relative weight, not a percentage.",
            technical: "The selector runs a weighted lottery over all rule-valid candidates: each part's chance is its ProbabilityWeighting (times any matched probability modifiers) divided by the summed weights of the pool. Default 1; 0 makes the part effectively never win a random draw.",
            motivation: "Weights shape the distribution without hard rules - keeping common styles common and flashy ones rare while everything stays possible.");

        Add("HeadParts.PartAllowedBodySlideDescriptors",
            layperson: "Body-shape labels the NPC's assigned BodySlide preset must carry for this head part to be allowed. Leave empty to ignore body shape.",
            technical: "Compared against the descriptors annotated on the NPC's assigned BodySlide preset, evaluated at the NPC's weight; the menu's match mode decides whether All, Any, or Shared descriptor categories must match, and an empty list imposes no constraint. Only meaningful when the body selection mode is BodySlide.",
            motivation: "Keeps face and body coherent - for example reserving a jowly face part for NPCs whose assigned preset is annotated as Chubby.");

        Add("HeadParts.PartDisallowedBodySlideDescriptors",
            layperson: "Body-shape labels that block this head part: if the NPC's assigned BodySlide preset carries them, the part is not allowed.",
            technical: "Matched against the assigned preset's descriptors at the NPC's weight using this menu's match mode (default Any: one matching descriptor vetoes). Runs alongside the allowed-descriptor check.",
            motivation: "A veto is the natural direction for mismatches - a delicate hairstyle that should never pair with presets annotated as Muscular, whatever else they are.");

        Add("HeadParts.PartAllowedBodyGenDescriptors",
            layperson: "Body-shape labels the NPC's assigned BodyGen morph must carry for this head part to be allowed. The Male list applies to male NPCs and the Female list to female NPCs.",
            technical: "Compared against the descriptors of the NPC's assigned BodyGen morphs using this menu's match mode; an empty list imposes no constraint. The available labels come from the male/female BodyGen config tracked in the head-parts settings (Associated BodyGen Configuration).",
            motivation: "BodyGen morphs are gendered and their descriptors are defined per config, so gender-specific lists let head parts follow the body types each gender can actually receive.");

        Add("HeadParts.PartDisallowedBodyGenDescriptors",
            layperson: "Body-shape labels that block this head part when the NPC's assigned BodyGen morph carries them. The Male list applies to male NPCs and the Female list to female NPCs.",
            technical: "Matched against the assigned BodyGen morphs' descriptors using this menu's match mode (default Any); a match vetoes the part. Labels come from the tracked male/female BodyGen config.",
            motivation: "Provides the BodyGen-side veto for face/body mismatches, mirroring the disallowed BodySlide descriptors used in BodySlide mode.");

        Add("HeadParts.PartProbabilityModifiers",
            layperson: "Rules that make this head part more or less likely for NPCs with certain traits, without forbidding anything. Each rule multiplies the part's selection weight when its trait matches.",
            technical: "Each modifier holds one attribute and a factor; during the weighted lottery, this part's Distribution Probability Weighting is multiplied by the factor of every modifier whose attribute the NPC matches (multiple matches multiply together). A factor of 0 effectively removes the part for matching NPCs.",
            motivation: "Soft preferences beat hard rules for natural-looking distribution - you can make braids three times likelier for soldiers while civilians still occasionally wear them.");

        Add("HeadParts.DeletePart",
            layperson: "Removes this head part from SynthEBD's settings so it is no longer distributed. The head part itself stays in your load order; only SynthEBD forgets about it.",
            technical: "Deletes this entry from the type's imported head-part list, and the removal persists to the head-parts settings JSON on save. The HDPT record in the source plugin is not touched.",
            motivation: "Imports sweep in whole mods' worth of parts, and not every part is worth distributing; pruning here curates the pool without uninstalling anything.");

        // --- Whole-category rules (UC_HeadPartCategoryRules) ---

        Add("HeadParts.CategoryRestrictToExistingType",
            layperson: "Only give NPCs a new part of this type if they already have one - for example, only swap beards on NPCs that already have facial hair, rather than adding beards to clean-shaven faces.",
            technical: "When enabled, the NPC's existing head parts must include one of this type. An NPC whose only part of the type is one of the vanilla 'none' placeholder records (no brow, no beard, no scar) is treated as not having one.",
            motivation: "NPC authors deliberately left many faces without beards, scars, or brows; restricting to NPCs that already have the feature preserves that intent instead of stamping the feature onto everyone.");

        Add("HeadParts.CategoryAllowedRaces",
            layperson: "The races that may receive any head part of this type. Leave empty to allow every patchable race. Individual head parts can narrow this further with their own race rules.",
            technical: "Race groupings are flattened into this list at run start; a non-empty list must contain the NPC's head-parts race (after race-alias substitution) or the whole type is skipped for that NPC before any individual part is considered.",
            motivation: "A single category-level gate saves repeating the same race restriction on every imported part - for example limiting all Facial Hair to races whose head meshes support it.");

        Add("HeadParts.CategoryAllowedRaceGroupings",
            layperson: "Named collections of races allowed to receive this type of head part, so one checkbox can cover a whole family of races.",
            technical: "Checked grouping labels expand into member races using the General race groupings and merge into the category's Allowed Races during preprocessing, gating identically to individually listed races.",
            motivation: "Keeps the category gate readable and centrally tunable instead of enumerating races on every head-part type.");

        Add("HeadParts.CategoryDisallowedRaces",
            layperson: "Races that must never receive head parts of this type, overriding the allowed lists.",
            technical: "Merged with the expanded disallowed groupings at run start; if the list contains the NPC's head-parts race, the entire type is skipped for that NPC.",
            motivation: "An explicit type-wide veto is simpler than rebuilding allowed lists - for example excluding Elders from all hair replacement.");

        Add("HeadParts.CategoryDisallowedRaceGroupings",
            layperson: "Named collections of races that must never receive head parts of this type.",
            technical: "Checked labels expand via the General race groupings into the category's Disallowed Races during preprocessing; a match vetoes the whole type for the NPC.",
            motivation: "One checkbox vetoes a whole race family, and the veto stays correct as the grouping's definition is centrally edited.");

        Add("HeadParts.CategoryAllowedAttributes",
            layperson: "Traits an NPC must have to receive any head part of this type. The NPC needs to match at least one attribute listed here.",
            technical: "The same matcher as the part-level attributes, applied once for the whole type: the NPC must match at least one listed attribute, where an attribute matches only if all of its sub-attributes match (empty list = unrestricted). Force If matches at this level satisfy the category's Distribute to non-forced NPCs gate rather than promoting any specific part. Group references resolve against the General attribute groups.",
            motivation: "Gates the entire type in one place - for example issuing Scars only to warrior classes - without repeating the rule on every part.");

        Add("HeadParts.CategoryDisallowedAttributes",
            layperson: "Traits that exclude an NPC from receiving any head part of this type.",
            technical: "Matching any listed attribute skips the whole type for the NPC; evaluated with the same General attribute-group resolution as the allowed list. Force If flags have no effect on the disallowed side.",
            motivation: "A single veto - like keeping all Scars off priests and nobles - beats adding the same disallowed attribute to dozens of individual parts.");

        Add("HeadParts.CategoryWeightRange",
            layperson: "The body-weight range (0 to 100) an NPC must fall inside to receive any head part of this type. Both ends are inclusive.",
            technical: "Compares the NPC record's Weight against the bounds before any individual part is considered; parts also carry their own range, and both must pass.",
            motivation: "Useful when a whole type only makes sense for certain builds, sparing you from setting the same range on every part.");

        Add("HeadParts.CategoryDistributeToNonForced",
            layperson: "Allows head parts of this type to be handed out randomly. Uncheck it so the type is only applied to NPCs targeted via Specific NPC Assignments or Force If attributes.",
            technical: "Mirror of the category's bAllowRandom flag. When false, the type is skipped unless the NPC tallied a Force If match on the category's allowed attributes (the gate runs after attribute matching) or a specific assignment applies.",
            motivation: "Lets a category run in opt-in mode - useful while testing, or when a type like Scars should only ever appear on hand-picked NPCs.");

        Add("HeadParts.CategoryAllowUnique",
            layperson: "Whether unique-flagged NPCs (mostly named characters) may receive head parts of this type.",
            technical: "Checks the Unique flag on the NPC record before considering any part of this type; individual parts repeat this check with their own flags.",
            motivation: "Named characters' faces are often curated by mod authors; excluding uniques wholesale here is safer than trusting every part's individual settings.");

        Add("HeadParts.CategoryAllowNonUnique",
            layperson: "Whether ordinary, non-unique NPCs may receive head parts of this type.",
            technical: "When unchecked, NPCs without the Unique flag skip this type entirely; the same check exists per part.",
            motivation: "Restricting a type to unique NPCs keeps it special - generic actors are numerous, so anything allowed here shows up frequently.");

        Add("HeadParts.CategoryDistributionProbability",
            layperson: "The percent chance (0 to 100) that an eligible NPC receives a new head part of this type at all. At 50, about half of eligible NPCs keep their original part.",
            technical: "Stored as the category's RandomizationPercentage (default 50). After the rule filters pass, a single roll decides whether to assign anything; failing the roll records 'randomized to none' in consistency so the NPC stays unchanged on later runs, and parts with Force If matches bypass the roll entirely.",
            motivation: "Replacing the feature on every eligible NPC looks unnatural for types like scars or facial hair; throttling the rate keeps the feature an accent rather than a census.");

        Add("HeadParts.CategoryAllowedBodySlideDescriptors",
            layperson: "Body-shape labels the NPC's assigned BodySlide preset must carry before any head part of this type is considered. Leave empty to ignore body shape.",
            technical: "Checked once for the whole type against the descriptors annotated on the NPC's assigned preset, evaluated at the NPC's weight using this menu's match mode; individual parts then apply their own descriptor rules. Only meaningful when the body selection mode is BodySlide.",
            motivation: "Filters the whole type by body shape in one place - the per-part lists then only need to express the differences between parts.");

        Add("HeadParts.CategoryDisallowedBodySlideDescriptors",
            layperson: "Body-shape labels that block this whole head-part type when the NPC's assigned BodySlide preset carries them.",
            technical: "A match against the assigned preset's descriptors (at the NPC's weight, default mode Any) skips the type for the NPC before individual parts are evaluated.",
            motivation: "One veto here spares adding the same disallowed descriptor to every part of the type.");

        Add("HeadParts.CategoryAllowedBodyGenDescriptors",
            layperson: "Body-shape labels the NPC's assigned BodyGen morph must carry before any head part of this type is considered. The Male list applies to male NPCs and the Female list to female NPCs.",
            technical: "Checked once for the type against the assigned BodyGen morphs' descriptors using this menu's match mode; an empty list imposes no constraint. The label vocabulary comes from the male/female BodyGen config tracked in the head-parts settings (Associated BodyGen Configuration).",
            motivation: "Gates the type by body shape in BodyGen setups, with gendered lists because BodyGen configs and their descriptors are gender-specific.");

        Add("HeadParts.CategoryDisallowedBodyGenDescriptors",
            layperson: "Body-shape labels that block this whole head-part type when the NPC's assigned BodyGen morph carries them. The Male list applies to male NPCs and the Female list to female NPCs.",
            technical: "A match against the assigned morphs' descriptors (default mode Any) skips the type before individual parts are evaluated; labels come from the tracked male/female BodyGen config.",
            motivation: "Mirrors the disallowed BodySlide descriptors for BodyGen users, vetoing face/body mismatches type-wide.");
    }
}

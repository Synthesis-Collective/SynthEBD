# Asset-pack config file format

An asset-pack config is a single JSON file in the SynthEBD instance's `Asset Packs` folder. You can
and should edit it directly with careful JSON edits — run `validate` after every editing session.
All examples below are taken from real, shipping configs.

## Top level

```json
{
  "GroupName": "SynthEBD - BnP 2.1 4K CBBE",      // display name; also the saved file name
  "ShortName": "BnPF4KC",                          // the config's Prefix (see below)
  "ConfigType": "Primary",                         // Primary (one per NPC) or MixIn (layered on top)
  "Gender": "Female",                              // Male or Female; one config covers one gender
  "DisplayAlerts": true,
  "UserAlert": "",                                 // message shown to users at patch time
  "Subgroups": [ ... ],                            // the tree (see below)
  "DefaultRecordTemplate": "000801:Record Templates - 3BA - pamonha.esp",
  "AdditionalRecordTemplateAssignments": [ ... ],  // race-specific templates (see below)
  "AssociatedBodyGenConfigName": "MCSmarties Diverse Races Adaptation",  // optional BodyGen pairing
  "AttributeGroups": [ ... ],                      // local attribute-group definitions (see below)
  "RaceGroupings": [ ... ],                        // local race-grouping definitions
  "DistributionRules": { ... }                     // whole-config rules (same fields as a subgroup's)
}
```

**Prefix (`ShortName`)**: a short acronym for the mod (e.g. `BnP`). Textures are installed to
`textures\<Prefix>\<subpath>`, which is what lets multiple skin mods coexist — they would otherwise
collide at identical vanilla paths. Source paths in the config must match that layout.

## Subgroups

NPCs receive **exactly one subgroup from each enabled top-level subgroup**. Nesting expresses
specialization: a child inherits the constraints implied by its parent chain, and only leaf-ward
paths carry textures. The drafter creates one top-level subgroup per texture type (`HD` Head
Diffuse, `HN` Head Normals, `BD` Body Diffuse, `BN` Body Normals, specular `*Sp`, subsurface `*S`,
hands `Ha*`, etc/TNG types, and `U*` Unknown types for kept unrecognized files).

**An intermediate (grouping) subgroup must earn its level.** A non-leaf subgroup is worth keeping only
when it carries a rule that should gate all its children (e.g. a "Softer Faces" group whose parent
restricts every per-race child to `Must Get Young Face`), or when it parallels such a ruled grouped
sibling so the alternatives stay organized (the "Default" per-race normals beside "Softer Faces").
A *lone* intermediate wrapper that carries **no rules of its own** — typically the drafter's
per-FOMOD-option `CORE`/"Default" folder holding unrelated race leaves (Main, Afflicted, Snow Elf,
Vampire, ...) — is dead nesting: in the review pass, **promote its children up to the parent and
delete the wrapper**. Rule of thumb: if a top-level has exactly one child that has children and that
child has no rules, flatten it; if two or more children are wrappers (or the wrapper carries rules),
the grouping is meaningful — leave it.

**Every enabled top-level subgroup is a required position.** If, for some NPC, a top-level offers
*no* selectable subgroup — because all its subgroups are `Enabled: false`, or every one is gated
away by race/attribute — then **that NPC receives nothing from the entire config** (selection
aborts at the unsatisfiable position; this is intended behavior). Two consequences:

- A top-level you want to *keep but not distribute* (a disabled placeholder for a future feature, or
  an installer "off" choice such as "no schlong") must still contain **one empty, `Enabled: true`,
  `DistributionEnabled: true` "None" subgroup** — a no-op with no `Paths` — so the position is
  always satisfiable. The selection algorithm is happy with an assignable subgroup that points to no
  assets. (This is the same pattern as the SOS/TNG "no schlong management" branch.)
- The **default ("Main") subgroup of each texture type must cover every race that shares the base
  skin** — most importantly **elders** (the Elder race uses the human head/body diffuse, only the
  normal map differs) and, for texture types that have *no* vampire-specific variant (hands,
  complexion, normals, ...), **vampires**. The drafter seeds the default subgroups with the
  `Humanoid Playable Non-Vampire` grouping (the 8 young human races); during review, broaden any
  default whose texture type lacks an elder/vampire sibling — e.g. base it on `Humanoid` and
  `DisallowedRaceGroupings`/`DisallowedRaces` only the races that genuinely have their own variant
  (`Humanoid Young Vampire` for the head, plus Afflicted/Snow Elf). Always confirm with the
  simulator on an elder and a vampire — these are the NPCs that fall through a too-narrow default.

`validate` reports an "unsatisfiable position" error when exactly one top-level fails to cover a race
every other top-level covers (the elder / all-disabled signal). One known benign trigger:
**`NordRaceAstrid` is used by a single *female* NPC (Astrid, the Dark Brotherhood)** — a male config
whose per-race head subgroups list only `NordRace` gets flagged for it. It can never matter for a male
config; either add `NordRaceAstrid` (and `DLC1NordRace`, which *does* have male NPCs) to the relevant
Nord subgroup for completeness, or disregard that one race.

```json
{
  "ID": "HD.M.M",                       // unique, XML-tag-compatible (letters/digits/periods)
  "Name": "Moles",
  "Enabled": true,                      // false = subgroup (and descendants) ignored entirely
  "DistributionEnabled": true,          // false = never random-assigned, but still valid for
                                        //         Specific NPC Assignments and required-linkage
  "Notes": "",
  "AllowedRaces": [],                   // FormKey strings, e.g. "013746:Skyrim.esm"
  "AllowedRaceGroupings": ["Humanoid"], // labels into RaceGroupings (local or General)
  "DisallowedRaces": [],
  "DisallowedRaceGroupings": [],
  "AllowedAttributes": [],              // see Attributes below
  "DisallowedAttributes": [],
  "AllowUnique": true,                  // may go to unique NPCs
  "AllowNonUnique": true,               // may go to non-unique NPCs
  "RequiredSubgroups": ["BD.M.M"],      // if this subgroup is chosen, those IDs must also be chosen
  "ExcludedSubgroups": [],              // ...and those IDs must NOT be chosen
  "AddKeywords": [],                    // keywords written to the NPC's WNAM armor record
  "ProbabilityWeighting": 1.0,          // relative weight vs sibling candidates (0 disables)
  "Paths": [
    {
      "Source": "textures\\BnPF4K_C\\...\\moles\\textures\\actors\\character\\female\\femalehead.dds",
      "Destination": "HeadTexture.Diffuse.GivenPath"
    }
  ],
  "AllowedBodyGenDescriptors": [],      // see Descriptors below
  "AllowedBodyGenMatchMode": "All",
  "DisallowedBodyGenDescriptors": [],
  "DisallowedBodyGenMatchMode": "Any",
  "AllowedBodySlideDescriptors": [],
  "AllowedBodySlideMatchMode": "All",
  "DisallowedBodySlideDescriptors": [ { "Category": "Chest", "Value": "Petite" } ],
  "DisallowedBodySlideMatchMode": "Any",
  "PrioritizedBodySlideDescriptors": [],
  "WeightRange": { "Lower": 0, "Upper": 100 },   // NPC weight gate
  "Subgroups": [ ... ],                 // children
  "TopLevelSubgroupID": ""              // maintained by SynthEBD; leave as loaded
}
```

### Linkage semantics (the #1 source of broken configs)

- `RequiredSubgroups` IDs must live in **other** top-level branches (validation enforces this).
- Required sets are honored transitively during selection. If subgroup A requires B and B requires
  C, choosing A forces B and C. If two required subgroups can never co-exist (e.g. each excludes
  the other, or they demand contradictory descriptors/races), **affected NPCs silently get
  nothing** — this is what the simulator's zero-count output catches.
- Use linkage for visual coherence (face diffuse ↔ body diffuse of the same skin tone so necks
  match; vampire face ↔ vampire body) — exactly the pairings the FOMOD installs together.

## Paths and destinations

`Source` is relative to the game Data folder (`textures\<Prefix>\...`). `Destination` is a record
path on the NPC. The common destinations:

- Head (TextureSet on the head): `HeadTexture.Diffuse.GivenPath`,
  `HeadTexture.NormalOrGloss.GivenPath`, `HeadTexture.BacklightMaskOrSpecular.GivenPath`,
  `HeadTexture.GlowOrDetailMap.GivenPath` (subsurface), `HeadTexture.Height.GivenPath` (complexion
  / detail maps, e.g. `femaleheaddetail_*.dds`).
- Body/hands/feet (skin TextureSets inside the WornArmor's armature, race-matched):
  `WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath`
  — same pattern with `BipedObjectFlag.Hands` / `BipedObjectFlag.Feet` / `BipedObjectFlag.Tail`,
  `.Male.` for male configs, and `NormalOrGloss` / `BacklightMaskOrSpecular` / `GlowOrDetailMap`
  for the other texture types. "Etc" (3BA/BHUNP extra-part) textures use
  `WorldModel.Female.AlternateTextures[Index == N].NewTexture.<slot>.GivenPath` inside the same
  armature selector.
- The same texture file may legitimately appear at several destinations (Skyrim reuses body
  textures for feet, etc.). One subgroup may not list the same Destination twice.

When you keep an unrecognized texture, the drafter leaves `Destination` empty — copy the correct
destination from a sibling subgroup of the same texture type before validating.

## Attributes (and Group-type rules — prefer these)

`AllowedAttributes` / `DisallowedAttributes` entries are AND-of-OR structures: an attribute matches
when **all** of its `SubAttributes` match; a rule matches when **any** attribute in the list does.

```json
{
  "SubAttributes": [
    {
      "Type": "Class",                          // Class | Custom | FaceTexture | Faction | Group |
      "FormKeys": ["01317F:Skyrim.esm"],        // Keyword | Misc | Mod | NPC | Race | VoiceType
      "ForceMode": "Restrict",                  // Restrict | ForceIf | ForceIfAndRestrict
      "Weighting": 1,                           // ForceIf tally weight (tie-breaking strength)
      "Not": false                              // invert the match
    }
  ]
}
```

**Group-type sub-attributes** reference shared, user-maintainable Attribute Groups by label:

```json
{ "Type": "Group", "SelectedLabels": ["Must be Fit"], "ForceMode": "Restrict", "Weighting": 1, "Not": false }
```

When converting a user preference ("fantasy skins only for mages") into rules, **prefer a Group**:
use an existing General-settings group if one fits, or define a new group in the config's local
`AttributeGroups` (so the config stays shareable — recipients get the definition even if their
General settings lack it). Only fall back to loose FormKey lists for one-off cases. `ForceMode`
guidance: `Restrict` gates eligibility; `ForceIf` makes matching NPCs strongly prefer the subgroup
(weighted tally); `ForceIfAndRestrict` does both.

`AttributeGroups` entries look like:

```json
{ "Label": "Must be Fit", "Attributes": [ { "SubAttributes": [ { "Type": "Class", "FormKeys": [...],
  "ForceMode": "Restrict", "Weighting": 1, "Not": false } ] } ] }
```

`RaceGroupings` are simpler: `{ "Label": "Humanoid", "Races": ["013746:Skyrim.esm", ...] }`.
Rules reference both by label; local definitions ship with the config, and the user's General
settings can supersede same-label definitions (default-on toggles).

## Body-shape descriptors

Descriptors pair textures with body shapes. They are `Category: Value` pairs defined in the user's
OBody settings (BodySlide mode) or the paired BodyGen config (BodyGen mode); a config can only
reference descriptors that exist there — `validate` checks this against the *active* body mode.

Standard conventions to apply during review:

- Normal maps with defined muscularity → `DisallowedBodySlideDescriptors: [{ "Category": "Build", "Value": "Chubby" }]`
  (and similar) so muscle shading never lands on heavy body shapes.
- Female torso normals with baked under-bust shading → restrict to busty shapes, e.g.
  `AllowedBodySlideDescriptors: [{ "Category": "Chest", "Value": "Busty" }]` — or disallow petite
  (`{ "Category": "Chest", "Value": "Petite" }`) when the author's shading is mild.
- Match modes: `All` = every listed category must match; `Any` = one suffices; `Shared` = every
  category both sides define must match. Defaults (`Allowed: All`, `Disallowed: Any`) are right for
  almost all cases.
- Exact category/value names vary with the user's descriptor set — read them from the instance's
  OBody settings or the paired BodyGen config rather than assuming.

## Styling heuristics (deriving rules from a texture variant's meaning)

These map a variant subgroup's *semantics* — read from its FOMOD option name / folder, not from one
mod's idiosyncratic words — onto the standard, user-maintained Attribute Groups and Build/Chest
descriptors. They are distilled from shipping configs (BnP Male, BnP CBBE "Piranha's Choice"); apply
them in the finalization pass (see the SKILL's "Apply a distribution style" step). The named groups
(`Must be Fit/Athletic/Muscular`, `Cannot Have Definition`, `Can Get Chubby Morph`, `Must Get Young
Face`, `Must Get AgeNN/RoughNN Face`, `Can Get Mildy Older/Haggard Face`, `Must Get Face Freckles`,
`Can Be Dirty`, `Must Be Dirty`, `Cannot Have Scars`, `Magic Users`, `Has CotR Head`, ...) are the
default Attribute Groups the drafter ships; rules referencing them stay inert until the user has
populated those groups with NPC criteria, which is expected.

| Variant meaning | Rule |
|---|---|
| **Muscular / defined body normal** (lean, toned, sporty, boxer, vigorexia, "veiny") | `AllowedAttributes` Group `Must be Fit`/`Must be Athletic`/`Must be Muscular` as **ForceIfAndRestrict**, weight scaling with intensity (Fit≈1, Athletic≈2, Muscular≈3); `DisallowedBodySlideDescriptors Build=Chubby`; the strongest also `PrioritizedBodySlideDescriptors Build=Powerful`. |
| **Plain / smooth / less-muscular body normal** (the non-athletic default) | `DisallowedAttributes` Group `Must be Fit, Must be Athletic, Must be Muscular` (Restrict) and/or `AllowedAttributes` Group `Cannot Have Definition` (ForceIf) so built NPCs never get it. |
| **Chubby / heavy / strongman body normal** | `AllowedAttributes` Group `Can Get Chubby Morph` (Restrict); `AllowedBodySlideDescriptors Build=Chubby`. |
| **Younger / softer face** (diffuse or normal) | `AllowedAttributes` Group `Must Get Young Face` (ForceIfAndRestrict). |
| **Aged complexion** (`maleheaddetail_age40/age50/rough01...`) | the matching `Must Get AgeNN/RoughNN Face` group (ForceIfAndRestrict). The drafter usually assigns these automatically from the file name — verify and leave as-is. |
| **Older / haggard face normal** | `AllowedAttributes` Group `Can Get Mildy Older Face` / `Can Get Haggard Face` (Restrict). |
| **Dirt overlay** (`+dirt`) | `AllowedAttributes` Group `Can Be Dirty` (Restrict) OR `Must Be Dirty` (ForceIfAndRestrict). |
| **Scar / blood overlay** (`+scars`) | `DisallowedAttributes` Group `Cannot Have Scars` (Restrict). |
| **Freckles overlay** | `AllowedAttributes` Group `Must Get Face Freckles`. |
| **Fantasy / saturated race skin** (vivid elf/orc tones) | `AllowedAttributes` Group `Magic Users` (Restrict) — the author intends them for mages/fantasy NPCs (confirm with the user). |
| **Genital / body size mesh** (SOS Smurf/Regular/VectorPlexus Muscular) | the muscular size → Group `Must be Muscular` (Restrict/ForceIf); the smallest → `Cannot Have Definition`. |
| **Head-mesh-replacer variant** (Charmers of the Reach, High Poly Head, ...) | gate the whole branch by the replacer's NPC group, e.g. `+ATTR Has CotR Head` on the replacer branch and `-ATTR Has CotR Head` on every sibling branch. |

ForceMode guidance: **ForceIfAndRestrict** = "for these NPCs and only these" (young faces, aged
complexions). **Restrict** = eligibility gate without forcing (fantasy skins, chubby). **ForceIf** =
prefer-for-these without excluding others. A **Disallowed** group with Restrict = "never for these"
(scars on `Cannot Have Scars`; muscle shading on the smooth normal). `Weighting` raises a ForceIf's
tally so it wins when several themed variants compete (use 2–3 for athletic/muscular). The default
("Smooth"-style) variant of an axis should be the one with no positive ForceIf so it remains the
fallback for NPCs that match no theme.

Know what ForceIf actually does: a matched ForceIf does not nudge the odds, it **wins** — among the
candidates for a position, a subgroup an NPC matches by ForceIf is selected over all non-ForceIf
candidates, so a matched NPC effectively gets it ~100% of the time (e.g. ForceIf'ing the elf/orc
races onto a "Hairless" body diffuse makes elves and orcs essentially always hairless, while races
with no ForceIf split evenly across the axis). When you want a genuine *probability* lean rather than
a hard win, use `ProbabilityWeighting` on the siblings instead of ForceIf.

Caveat to flag in the rules report: a `Restrict`/`ForceIfAndRestrict` rule tied to an Attribute
Group that is **empty** in the user's settings makes that subgroup match nobody, so it stops
distributing until the group is populated. Never put such a rule on the *only* subgroup that covers
some race (it would resurrect the unsatisfiable-position problem) — keep an unrestricted default in
each top-level.

## Record templates

`DefaultRecordTemplate` points at an NPC in a Record Templates plugin (loaded from the instance's
`Record Templates` folder); SynthEBD copies missing record structure (e.g. custom AlternateTextures
slots) from that template when patching. `AdditionalRecordTemplateAssignments` override the
template per race (Khajiit/Argonian beast templates are standard):

```json
{ "Races": ["013745:Skyrim.esm", "088845:Skyrim.esm"],
  "TemplateNPC": "000803:Record Templates - 3BA - pamonha.esp",
  "AdditionalRacesPaths": [
    "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces", ...
  ] }
```

If a config uses a destination path that no shipped record template provides, the author must
distribute a **custom Record Templates .esp** containing an NPC where that path exists, and the
manifest must install it. The drafter handles the standard cases automatically (default gender
templates; 3BA/BHUNP via `--etc-body`; TNG for male mods).

## Whole-config DistributionRules

The top-level `DistributionRules` object carries the same rule fields as a subgroup (races,
attributes, `ProbabilityWeighting`, descriptors, `WeightRange`, ...) and gates the whole config —
use it when the entire texture set is e.g. female-orc-only, or to weight one config against others
in the user's rotation.

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

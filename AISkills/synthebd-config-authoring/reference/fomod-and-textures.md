# Texture classification and FOMOD interpretation

## Skyrim skin-texture conventions

File-name suffixes identify the texture type (the drafter categorizes by exact known file names):

| Suffix | Type | Destination slot |
|---|---|---|
| *(none)* (`femalebody_1.dds`, `femalehead.dds`) | Diffuse (color) | `Diffuse` |
| `_msn` | Model-space normal map (shape/muscle detail) | `NormalOrGloss` |
| `_sk` | Subsurface tint | `GlowOrDetailMap` |
| `_s` | Specular | `BacklightMaskOrSpecular` |
| `femaleheaddetail_*` | Complexion / age detail (head only) | `HeadTexture.Height` |

Standard file sets: `femalebody_1` / `malebody_1`, `femalehands_1`, `femalehead`, plus per-type
suffix variants. Race-specific folders hold race-locked variants: `actors\character\female\`
(Nord/default), `bretonfemale`, `darkelffemale`, `highelffemale`, `imperialfemale`, `femaleorc`,
`redguardfemale`, `woodelffemale`, etc. The drafter understands these and auto-applies race rules
(`--no-auto-rules` disables that). Vanilla quirk: the "default" `female`/`male` folder doubles as
the **Nord** texture — the drafter renames such subgroups to "Nord" automatically.

Special families the drafter detects:

- **Etc textures** (`femalebody_etc*`): 3BA/BHUNP-specific body-part textures → `--etc-body` is
  required so the matching record templates get applied.
- **TNG textures**: The New Gentleman male textures → TNG record templates applied automatically.
- **Tint masks** (`tintmasks\...`): NOT distributable (they bake into FaceGen at game-build time) —
  always ignore them when drafting.
- **Wood Elf normals**: if a female mod lacks explicit Wood Elf head normals, the drafter reuses the
  High Elf ones (vanilla behavior parity).
- Body diffuse/normal/specular/subsurface textures are automatically replicated to the feet (and
  tail where relevant) destinations — that is why body subgroups carry several `Paths`.

## Schlong distribution (SOS / TNG / SOS Light) — always handle this way

Male skin mods built for **SOS (Schlongs of Skyrim)** or **TNG (The New Gentleman)** ship genital
textures (`malegenitals_*`). SOS/TNG add the schlong armor-addon record **dynamically at runtime
via script**, so that record does not exist at patch time. This makes the schlong
**all-or-nothing**: SynthEBD either adds the *entire* record (mesh **and** textures, via the record
template's slot-52 armature — `BipedObjectFlag 4194304`) or it adds nothing. A texture-only
approach is impossible: if no record is added, SOS/TNG applies its own default schlong with default
textures, producing a groin/neck seam against the mod's body skin. Therefore **the NIFs must be
distributed alongside the genital textures**, and you should distribute the size variants rather
than leaning on the template's single default nif.

Unless the user's prompt says otherwise, handle a TNG/SOS texture mod with these patterns (verified
against the shipping BnP Male configs):

- **TNG**: `DefaultRecordTemplate` → `…:Record Templates - The New Gentleman.esp` (the drafter
  applies this automatically when it detects TNG textures), plus the standard beast-race
  `AdditionalRecordTemplateAssignments`. Build full schlong top-levels — diffuse / normal /
  subsurface / specular on `WornArmor.Armature[…HasFlag((BipedObjectFlag)4194304)…].SkinTexture.Male.*`
  and a **mesh** top-level on `…WorldModel.Male.File` — each with the three size children
  **Smurf Average / VectorPlexus Muscular / VectorPlexus Regular**, cross-linked via
  `RequiredSubgroups` so each NPC gets one coherent size (matching mesh + size-specific normal).
  The meshes are TNG's own (`meshes\actors\character\character assets\TNG\{c,m,r}_genitals_1.nif`),
  a TNG dependency the skin mod does not ship.
- **SOS Full**: use the **same TNG record template**, but distribute **no schlong subgroups** — SOS's
  runtime owns the schlong, and the template's default nif is incorporated wholesale.
- **SOS Light**: use the **base** template (`Record Templates.esp`); SOS Light reallocates the schlong
  onto the **Feet** slot, so distribute Feet subgroups (`FD`/`FN`/`FS`) rather than slot-52 schlong
  subgroups.

Because the TNG meshes live under `meshes\…` (not `textures\<Prefix>\…`), they will not resolve in
`validate` unless TNG is installed — stage dummy `.nif` files at those paths in an extra
`--asset-root` to validate cleanly — and they must be listed in the manifest's
`IgnoreMissingSourceFiles` so `verify-install` does not flag them as missing from the package.

## Reading a FOMOD (`fomod\ModuleConfig.xml`)

Most texture mods ship a FOMOD installer whose script encodes exactly how the author intends the
textures to combine — parse it **before** drafting. Watch the encoding: these files are frequently
**UTF-16** (convert before XML-parsing; a `Read` that shows interleaved spaces/NULs means UTF-16).

Structure:

```xml
<installSteps>
  <installStep name="Face normals (humans)">
    <optionalFileGroups>
      <group name="Younger or older" type="SelectExactlyOne">
        <plugins>
          <plugin name="V1.7Mature">
            <description>...fits more mature characters...</description>
            <files><folder source="options\face normal\1.7old\textures" destination="textures"/></files>
            <conditionFlags><flag name="something">On</flag></conditionFlags>
          </plugin>
          ...
```

### Translation rules

1. **`installStep` + `SelectExactlyOne` group → sibling subgroups.** What is an either/or choice
   for a single player becomes a set of co-existing variants for NPCs: every option of the step
   becomes one subgroup under the matching texture-type top level, all distribution-enabled, so
   different NPCs get different variants. (`SelectAll` intro steps that install the base `textures`
   folder are the common/base files, not variants.)
2. **`<folder source>` → which files belong to which option.** The source path (e.g.
   `options\face normal\1.7old\textures`) maps each option name onto a folder subtree in your
   working folder — use it to name the drafted subgroups after the **option names**
   (`V1.7Mature` → "Mature (1.7)"), which are nearly always better than raw folder words.
3. **A plugin with MULTIPLE `<folder source>` entries is a composite option** — e.g. "Young purple"
   installing both a body-color folder and a young-head-normal folder. Decompose composites into
   their orthogonal axes (color subgroups × age subgroups) instead of copying the FOMOD's
   combinatorial options; the axes already exist as separate top-level texture types.
4. **`conditionFlags` + `<visible><flagDependency .../></visible>` chains → structure and linkage
   hints.** A step that only appears when an earlier option set a flag (e.g. choosing body skin
   "Dark" reveals a "±tan lines" step) means those later options are *refinements* of the earlier
   choice: nest them under the corresponding subgroup, or link them via `RequiredSubgroups` when
   they live in a different texture-type branch.
5. **Descriptions carry rule intent.** Authors write things like "sun loving Redguards", "a strong
   Orc", "fits more mature characters", or name muscle tiers (Vigorexia/Boxer/Sporty) and chest
   sizes (big/small) in the option text. Mine these for:
   - race affinities → `AllowedRaces`/`AllowedRaceGroupings` (or ForceIf attributes when the author
     says "fits X" rather than "only for X") — note the FOMOD often locks a skin to one race purely
     for the player; for NPC distribution, confirm with the user whether to keep it race-locked or
     distribute more broadly;
   - muscularity → body-shape descriptor pairings (see `config-format.md`);
   - age ("Mature"/"old") → consider elder-oriented rules or simply better names.
6. **Identical files across option folders** are FOMOD packaging convenience — exactly what the
   scan verb's duplicate groups catch. The keeper heuristic (least race-specific, shortest path)
   almost always matches the FOMOD's "base" copy.

### Worked micro-example

FOMOD: step "Body skin selection" (SelectExactlyOne: Dark→flag `redguard`, Frostnip→flag `nord`,
...), conditional step "Dark skin selection" (±tanline) visible only when `redguard` is on.

Resulting config shape: under Body Diffuse, sibling subgroups `Dark` and `Frostnip` (et al.), with
`Dark`'s children `Tan Lines` and `No Tan Lines`; the description "sun loving Redguards" suggests
(ask the user) `AllowedRaceGroupings` or a Redguard ForceIf on `Dark`, while `Frostnip`'s "snow
loving Nord" suggests the same for Nords. The matching face-diffuse variants (if the FOMOD pairs
them via flags) get `RequiredSubgroups` links so faces and bodies stay consistent.

---
name: synthebd-config-authoring
description: Create, update, validate, simulate, and package SynthEBD asset-pack config files from Skyrim texture mods using SynthEBD.CLI. Use when the user wants a SynthEBD config drafted from a texture mod, an existing config updated for a new mod version, distribution rules added, or a config packaged for distribution.
---

# SynthEBD Config Authoring

SynthEBD assigns appearance assets (almost always **skin textures**) to Skyrim NPCs through *asset-pack
config files* — JSON documents that organize a texture mod's files into a tree of **subgroups** with
distribution rules. At patch time, each NPC receives one subgroup from each enabled top-level subgroup
(one head diffuse, one body normal, ...), gated by races, attributes, probability weights, and
body-shape descriptors.

## Your role

SynthEBD already automates the mechanical work: its Config Drafter turns a folder of textures into a
draft config, and `SynthEBD.CLI` exposes that pipeline headlessly. **You are not here to re-implement
it — you are here to add the judgment the algorithm lacks.** After every draft, ask yourself:

1. **Are subgroups appropriately named?** Judge each name against the file name, the folder path, and
   the corresponding FOMOD option (see `reference/fomod-and-textures.md`). The drafter names from
   folder words; the FOMOD's option names and descriptions are usually better.
2. **Are subgroup linkages handled correctly?** `RequiredSubgroups` must tie textures that belong
   together (e.g. a face diffuse and the matching body diffuse so necks don't seam) and must never
   form impossible combinations — mutually-incompatible required sets silently prevent NPCs from
   receiving anything. The FOMOD's flag dependencies tell you which options the author couples.
3. **Does everything distribute?** Run the simulator on representative NPCs (different races, genders,
   vampires, elders). Every subgroup should be assigned at least sometimes — a zero-count subgroup is
   either deliberately disabled, restricted away (fine if intended), or a bug (usually linkage).

Additionally, **elicit the user's distribution preferences** ("the Fantasy Dunmer textures should only
go to mage NPCs") and convert them into rules — preferring **Group-type attributes** (which reference
shared, user-editable Attribute Groups like "Must be Fit") over hard-coded FormKey lists whenever a
suitable group exists or can be defined. See `reference/config-format.md`.

## Prerequisites

- **This skill requires a locally-running agent** with access to the user's filesystem and the
  ability to run `SynthEBD.CLI.exe` on their Windows machine (e.g. Claude Code CLI / IDE extension,
  Codex CLI, Gemini CLI). If you are in a cloud/sandboxed environment that cannot see the user's
  local drives or run the CLI (the Claude desktop app's analysis tool, claude.ai, and similar),
  **stop and tell the user to re-run the request in a locally-running agent such as Claude Code** —
  do not improvise a config from files you cannot see and cannot validate. An unvalidated,
  invented-path config is exactly the broken artifact this skill exists to prevent.
- **SynthEBD.CLI.exe** ships alongside SynthEBD.exe. Run it from the install folder, or pass
  `--synthebd-path <SynthEBD install dir>` (it honors the instance's portable-settings redirect), or
  `--settings-root <dir>` to pin a settings tree directly. All verbs except `package`/`archive-*`
  build a real game environment, so a Skyrim installation must be present (override with
  `--data-folder` / `--skyrim-version` if detection fails).
- Always pass `--json` and parse stdout; all logging goes to stderr. Exit codes: 0 = success,
  1 = findings (validation errors / an NPC that gets nothing), 2 = fatal.
- Full flag reference: `reference/cli.md`.

## The standard workflow (new config)

Work in **one extraction working folder**, laid out like a mod, fully independent of any mod manager:

```
<workfolder>\textures\<Prefix>\<contents of archive 1>
<workfolder>\textures\<Prefix2>\<contents of archive 2>   (only if archives collide or for findability)
```

1. **Gather context, then inventory the downloads.** Ask the user for the mod page description —
   it often reveals the textures' theme/tone (useful for rule selection) and sometimes how
   different textures are meant to pair; be open to being told it is missing or unhelpful. A full
   texture set often spans several archives (base mod, updates, hotfixes, option packs). For each,
   catalogue without extracting: `SynthEBD.CLI archive-list --archive <file> --json` (or list the
   folders if the user already extracted them). Then work out how the archives/folders **relate**
   before extracting anything:
   - If two archives contain identical internal paths, they MUST get different prefixes — and ask
     the user whether one supersedes the other.
   - **Resolution sets (1K/2K/4K...)**: identify which archives/folders belong to which resolution.
     Each resolution becomes its **own config file** (see step 10). Watch for the plausible
     complications: a base resolution (e.g. 2K) with *partial* upgrade/downgrade packs (4K or 1K
     options covering only a subset of textures), or a core mod at one resolution whose
     fix/adjustment file (e.g. a more-correct subsurface fix) only exists at a different
     resolution. In those cases one config legitimately combines sources from several
     archives/folders — make sure the user knows and agrees with the combination plan before
     drafting. Whenever the grouping is not unambiguous, ask.
2. **Read the FOMOD first.** If an archive contains `fomod\ModuleConfig.xml`, parse it before
   anything else — it documents which textures belong together, the author's intended variants, and
   race/body hints. It is often UTF-16: convert before parsing. Mapping rules in
   `reference/fomod-and-textures.md`.
3. **Extract** each archive into its prefix folder:
   `SynthEBD.CLI archive-extract --archive <file> --dest <workfolder>\textures\<Prefix>`.
   Keep prefixes short (path-length limits matter — see `reference/packaging-and-updating.md`).
4. **Scan:** `SynthEBD.CLI scan --root <workfolder> --json`. This reports:
   - **Unmatched textures** — files the drafter doesn't recognize. Typical: tint masks (NOT
     distributable — they bake into FaceGen; ignore them) and complexion variants like
     `femaleheaddetail_vamp.dds` (worth keeping). **Ask the user** about anything you cannot classify.
   - **Duplicate groups** — byte-identical files the author packaged under multiple paths (a FOMOD
     convenience). **Ask the user** whether to de-duplicate (saves VRAM; the suggested keeper is the
     least race-specific, shortest path). Default to `replace` mode unless they object.
5. **Draft:**
   ```
   SynthEBD.CLI draft --root <workfolder> --name "<Mod Name> <variant>" --prefix <Prefix>
       --multiplet-mode replace
       --keep-unmatched "<path from scan>" ... (or --ignore-all-unmatched / --keep-all-unmatched)
       [--etc-body 3BA|BHUNP] [--no-auto-linkage] --json
   ```
   - If the mod has "etc" body textures the CLI refuses to guess: **ask the user** which body family
     their mod targets (3BA for CBBE-based, BHUNP for UNP-based) and pass `--etc-body`.
   - For very large/complex mods consider `--no-auto-linkage` and author the linkage yourself in the
     review pass — bad auto-linkage is the #1 cause of NPCs receiving nothing.
   - Gender is auto-detected from file names. TNG (The New Gentleman) record templates are applied
     automatically for male mods containing TNG textures.
6. **Review pass (your main job).** Open the saved config JSON and check, against the FOMOD and
   folder names: subgroup names, distribution toggles, `RequiredSubgroups`/`ExcludedSubgroups`,
   destinations of kept unknown textures (drafted with an empty `Destination` — copy from a sibling
   subgroup of the same texture type), and descriptor pairings (muscular normals should disallow
   chubby; under-bust-shaded torso normals should require busty — see `reference/config-format.md`).
   Keep subgroup IDs unique and XML-tag-compatible (alphanumeric + period, no spaces).
7. **Validate:** `SynthEBD.CLI validate --config "<name>" --asset-root <workfolder> --json`.
   `--asset-root` lets sources resolve from the working folder before the mod is installed/activated.
   Fix everything it reports; re-run until clean.
8. **Simulate:** `SynthEBD.CLI simulate --config "<name>" --npc Hulda --npc Belethor --npc <other
   races/genders/vampires/elders> --repetitions 100 --json` (drop to 2–3 repetitions for very large
   fresh drafts). Zero-count subgroups carry a log-derived explanation; for stubborn failures add
   `--full-report-dir <dir>` and read the per-NPC XML. Iterate steps 6–8 until distribution matches
   intent. **Cover the failure-prone races, not just the base ones** — a `TotalAssignments` of 0 for
   any NPC means a top-level position had no selectable subgroup (see config-format.md). For male
   mods a good fixed sample is each base race plus a **vampire** (e.g. `DLC1Vingalmo`), an **elder**
   (`Esbern` / `013358:Skyrim.esm`), an **afflicted** (`064A42:Skyrim.esm`), a **snow elf**
   (`DLC1Gelebor`), and a **beast race** (`Kharjo`/`Madesi` — expected to get nothing for a
   human/elf mod, which is correct). Vampires and elders are the classic fall-through cases.
9. **Apply user preferences.** Now ask the user about restrictions and pairings the files alone can't
   tell you (which variants for which NPC kinds, anything to toggle off by default), encode them as
   rules, and re-simulate.
9b. **Apply a distribution style (finalization).** Once distribution is correct, give the variants
   *intent* instead of pure randomness, following the heuristics in `reference/config-format.md`
   ("Styling heuristics") which map each variant's meaning (muscular vs smooth body normal, younger
   vs aged face, dirt/scar overlays, fantasy skins, schlong size, head-replacer branches, ...) onto
   the standard Attribute Groups and Build descriptors. Run it as a **propose → approve → implement**
   loop, and codify it the same way every time:
   0. **Show the user the options before proposing rules.** List each variant axis with the FOMOD's
      own option descriptions, and — when the FOMOD ships preview images (`image assets\...`, referenced
      from `ModuleConfig.xml`) — open them side by side so the user can decide from what they actually
      look like: `python scripts/preview_montage.py "<FOMOD>\image assets"` (or pass a
      `--manifest items.json` of `{path,label,desc}` to caption each with its option text). Discuss,
      don't assume.
   1. For each variant subgroup, derive the proposed rule(s) from its semantics.
   2. **Write the user a report**: every proposed rule, the reasoning, and the caveat that rules tied
      to an Attribute Group are inert until the user populates that group (and must never be the only
      subgroup covering a race — keep an unrestricted default per top-level).
   3. Get approval or revisions; reach consensus before editing.
   4. Implement the agreed rules in the config JSON, then **re-`validate` and re-`simulate`** — confirm
      the race-coverage check still passes and no subgroup was zeroed by an over-restrictive rule on
      an empty group.
10. **Variants.** Two different kinds, handled differently:
    - **Stylistic variants** (e.g. "Default" vs "No Bronze Shine" subsurface): do NOT delete
      subgroups — duplicate the whole config file (adjusting `GroupName`) or toggle
      `DistributionEnabled: false` on the variant subgroups. Disabled-not-deleted is the
      maintainability convention.
    - **Resolution variants (1K/2K/4K...)**: the exception to the rule above — make **one config
      file per resolution**, each referencing only that resolution's texture paths, with the
      resolution in the config's name (e.g. "... 4K CBBE"); the manifest's Options tree then lets
      the installing user pick their resolution. When the user pointed you at a project root
      containing every resolution, do not fold them into one config. Sibling resolution configs are
      usually a search-and-replace on the Source paths of a copied config — except where the mod's
      partial upgrade packs / off-resolution fixes (step 1) force a deliberate, user-approved mix.
11. **Package** (when the user wants to distribute): see `reference/packaging-and-updating.md`, then
    `SynthEBD.CLI package --staging <stagingFolder> --json`, and finally
    `SynthEBD.CLI verify-install --archive <packaged.7z> --downloads <folder with the mod archives>
    --json` to prove every installer selection chain installs correctly against the real dependency
    archives. **Before writing the manifest, ask the user for each dependency archive's mod-page URL
    and its exact Nexus download display name** — these populate each `DownloadInfo` entry's `URL`,
    `ModPageName`/`ModDownloadName`, and `ExpectedFileName`, which is what lets the installer point
    users at the right file and auto-match archives they already downloaded. (`ExpectedFileName`
    should match the archive on disk; if unsure, read it from the `--downloads` folder.)

For **updating** an existing config to a new mod version, see `reference/packaging-and-updating.md`.

## Questions to ask the user — checklist

| When | Ask |
|---|---|
| Before starting | What does the mod page description say? (Theme/tone informs rules and texture pairings; "no description" is a fine answer.) |
| Multiple archives downloaded | Which are base / update / hotfix / options? Does any supersede another? |
| Multiple resolutions present | Which archives/folders belong to which resolution set? (One config per resolution.) Confirm any plan that mixes resolutions in one config (partial upgrade packs, off-resolution fixes). |
| Scan reports duplicate groups | De-duplicate identical textures to save VRAM? (Recommend yes, `replace` mode.) |
| Scan reports unmatched textures | Keep or ignore each (tint masks: ignore; complexions: usually keep)? |
| Draft reports etc body textures | Is this mod for a CBBE-family (3BA) or UNP-family (BHUNP) body? |
| Male mod with genital (`malegenitals_*`) textures | Which schlong system(s) — SOS Full, TNG, SOS Light? One config per system. Distribute the schlong as mesh+textures (all-or-nothing); see the schlong section in `reference/fomod-and-textures.md`. |
| After draft | Any variants they want restricted (e.g. "fantasy skins only on mages"), weighted, or disabled by default? |
| Normal maps with defined muscle/body traits | Confirm descriptor pairings (muscular → disallow chubby; under-bust shading → require busty). |
| At packaging time | Bundle the default female BodyGen configs? (Shareable, optional, somewhat dated — user's call.) |
| At packaging time | For each dependency archive: its mod-page URL and exact Nexus download display name (for the manifest's `DownloadInfo` URL / `ModDownloadName` / `ExpectedFileName`). |
| Before the styling pass | Confirm the proposed distribution-style rules (the report from step 9b) — approve or revise. |
| Custom destination paths beyond the defaults | A custom Record Templates .esp is required — does one exist, or must the user provide one? |

## Reference files

- `reference/cli.md` — every CLI verb, flag, output schema, and exit code.
- `reference/config-format.md` — the asset-pack JSON format: subgroups, rules, attributes (incl.
  Group-type), descriptors, record templates, distribution rules.
- `reference/fomod-and-textures.md` — Skyrim texture classification and how to translate a FOMOD
  ModuleConfig.xml into subgroups, linkage, and rules.
- `reference/packaging-and-updating.md` — Manifest.json, packaging conventions, BodyGen bundling,
  record templates, path-length limits, and the config-update workflow.
- `scripts/inspect_config.py` — a vetted, read-only config inspector (subgroup tree with
  enabled/distribution flags, race rules, links, attributes, descriptors, and shortened
  source/destination per path). Prefer it over writing ad-hoc inspection scripts:
  `python scripts/inspect_config.py "<config>.json" [--paths] [--rules] [--grep <substr>]`.

**Reducing permission prompts (local agents).** Inspecting configs and running the CLI repeatedly
generates a permission prompt per command unless allow-listed. To streamline, the user can add to
their project `.claude/settings.local.json` `permissions.allow`: `"Bash(python:*)"` (and `python3`)
for the inspector/JSON work, and a rule for the CLI by its absolute path,
e.g. `"Bash(<SynthEBD install>/SynthEBD.CLI.exe:*)"`. Invoke the CLI by that absolute path as the
first token (avoid `cd … &&` prefixes and shell variables, which defeat prefix matching). The user
accepts that `python:*` permits arbitrary scripts — that is the intended trade-off for speed.

## Hard rules

- Trust the local code, configs, and these references over web sources; SynthEBD is niche and online
  information is frequently wrong or outdated.
- Never leave a drafted config unvalidated: `validate` then `simulate` is the definition of done.
- Never delete subgroups to express a stylistic variant — disable distribution instead. The one
  exception is texture **resolution**: resolutions are separate per-resolution config files chosen
  through the installer, not subgroups within one config.
- Ask rather than guess whenever a decision changes what ships (de-dup, body family, kept unknowns,
  preference rules).
- For male mods with genital textures, distribute the schlong as a whole record (mesh **and**
  textures) — it is all-or-nothing, never texture-only. Follow the SOS Full / TNG / SOS Light
  patterns in `reference/fomod-and-textures.md` unless the user specifies otherwise.

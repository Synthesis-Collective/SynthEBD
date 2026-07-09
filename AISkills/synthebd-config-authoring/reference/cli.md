# SynthEBD.CLI reference

`SynthEBD.CLI.exe` ships next to `SynthEBD.exe`. Results print to **stdout** (pass `--json` for
machine-readable output); all logging streams to **stderr** — never mix them up when capturing.

```
SynthEBD.CLI <verb> [options]
```

## Common options (all environment-backed verbs)

| Option | Meaning |
|---|---|
| `--synthebd-path <dir>` | SynthEBD install folder whose settings to load (default: the CLI's own folder). Honors the instance's portable-settings redirect, like the GUI. |
| `--settings-root <dir>` | Operate directly on a settings tree (the folder containing `Settings\`, `Asset Packs\`, `Record Templates\`, ...). Overrides `--synthebd-path`. |
| `--data-folder <dir>` | Override the game Data folder used to build the environment. |
| `--skyrim-version <v>` | Override the Skyrim release (`SkyrimSE`, `SkyrimSEGog`, `SkyrimVR`, ...). |
| `--output-mod <name>` | Override the output plugin name excluded from the load order. |
| `--json` | Emit JSON to stdout. |

Exit codes everywhere: **0** success · **1** findings (validation errors; an NPC with zero
assignments) · **2** fatal (bad arguments, environment/settings failure, config/NPC not found).

`validate`, `scan`, `draft`, and `simulate` need a resolvable game environment (Skyrim install).
`package`, `archive-list`, and `archive-extract` run without one.

---

## validate

Runs the same checks as the GUI's per-config Validate button: required fields, duplicate subgroup
IDs, required/excluded subgroup references, body-shape descriptor existence (for the active body
mode), source-file existence, and destination record paths resolving to a string on a record
template.

```
SynthEBD.CLI validate [--config <name>]... [--asset-root <dir>]... [--json]
```

- `--config` — GroupName or file name (no extension); repeatable; default = all installed configs.
- `--asset-root` — extra root(s) probed for `Source` files, e.g. the extraction working folder.
  Without it, sources must exist in the game Data folder (or BSAs) — which they won't for a config
  drafted from a not-yet-installed working folder.

JSON shape:

```json
{ "Command": "validate", "AllValid": false, "BodySelectionMode": "BodySlide",
  "DataFolder": "...", "ExtraAssetRoots": ["..."],
  "Configs": [ { "Name": "...", "IsValid": false, "Errors": ["..."] } ] }
```

Note: descriptor checks depend on the instance's active body mode (`BodySelectionMode`) — BodyGen
descriptors are only validated in BodyGen mode, BodySlide descriptors in BodySlide mode.

---

## scan

Pre-draft analysis of a working folder: categorizes every `.dds`, lists unmatched textures, and
finds byte-identical duplicate groups (file-name grouping + MD5) with a suggested keeper each.

```
SynthEBD.CLI scan --root <workfolder> [--root ...] [--roots-have-prefix] [--json]
```

- Default layout: each `--root` is a mod-style folder containing `textures\<Prefix>\...`.
  `--roots-have-prefix` instead treats each root as a `...\Textures\<Prefix>` folder itself.
- Duplicate hashing reads every texture once; progress streams to stderr.

JSON shape:

```json
{ "Command": "scan", "Roots": ["..."], "TotalDdsFiles": 216, "CategorizedCount": 200,
  "UnmatchedTextures": ["textures\\BnP2K\\...\\femaleheaddetail_vamp.dds"],
  "DuplicateGroups": [ { "FileName": "femalehands_1.dds (Group 1)",
      "Files": [ { "Path": "textures\\...\\+tanline\\...\\femalehands_1.dds", "IsKeeper": true },
                 { "Path": "textures\\...\\-tanline\\...\\femalehands_1.dds", "IsKeeper": false } ] } ] }
```

Paths in the report are root-relative; feed them back verbatim to `draft`'s `--keep-unmatched` /
`--keeper`.

---

## draft

The GUI Config Drafter, headless: creates a new asset-pack config from the working folder and saves
it into the instance's `Asset Packs` folder (or `--out <file>`).

```
SynthEBD.CLI draft --root <workfolder> --name "<GroupName>" --prefix <Prefix>
    [--multiplet-mode replace|ignore|none] [--keeper <path>]...
    [--keep-unmatched <path>]... | --keep-all-unmatched | --ignore-all-unmatched
    [--etc-body 3BA|BHUNP]
    [--no-auto-names] [--no-auto-rules] [--no-auto-linkage]
    [--out <file>] [--json]
```

Behavior to know:

- **Unmatched textures**: if any exist and no disposition flag is given, the draft fails (exit 2)
  and lists them — decide (with the user) and re-run. Kept unknowns land in `Unknown *` top-level
  subgroups **with an empty `Destination`** that you must fill in afterward (copy from a sibling
  subgroup of the same texture type), or the config will not validate.
- **Duplicates**: `replace` (default) re-points all subgroups at each group's keeper; `ignore`
  skips non-keepers entirely; `none` skips the duplicate check. `--keeper <path>` overrides a
  group's suggested keeper.
- **`--etc-body`**: required (exit 2 otherwise) when the mod contains `femalebody_etc` textures;
  applies the 3BA or BHUNP record-template set (`Record Templates - 3BA|BHUNP - pamonha.esp`).
  TNG textures auto-apply the TNG template set; both set gender-appropriate beast-race templates.
- **Auto-toggles** (all default on, matching the GUI): names (subgroup naming from file/folder
  names), rules (race/attribute rules inferred from names), linkage (same-named subgroups in
  different top-levels become mutually Required). Linkage is the risky one on complex mods.
- Gender is detected from file names and sets the default record templates automatically.

JSON shape (abridged):

```json
{ "Command": "draft", "SavedTo": "...\\Asset Packs\\<name>.json", "GroupName": "...",
  "Prefix": "...", "DetectedGender": "Female",
  "DefaultRecordTemplate": "000801:Record Templates - 3BA - pamonha.esp",
  "HasEtcTextures": true, "HasTNGTextures": false, "AppliedTemplateSet": "3BA",
  "TotalDdsFiles": 216, "CategorizedCount": 200, "UnmatchedKept": 1, "UnmatchedIgnored": 15,
  "MultipletMode": "Replace", "DuplicateGroups": 5,
  "TopLevelSubgroups": [ { "Id": "HD", "Name": "Head Diffuse", "TotalSubgroups": 35, "TotalPaths": 23 } ] }
```

---

## simulate

The GUI Distribution Simulator, headless: runs the production selection pipeline N times per NPC
(consistency disabled so draws are independent) and tallies assignments.

```
SynthEBD.CLI simulate --npc <FormKey|EditorID> [--npc ...]
    [--config <name>]... [--repetitions <n>] [--full-report-dir <dir>] [--json]
```

- `--npc` accepts a FormKey (`013BA3:Skyrim.esm`) or an EditorID (`Hulda`), resolved against
  winning overrides.
- `--config` pins the simulated **Primary** configs (the GUI workflow of deselecting all others);
  default = the configs currently selected in the instance's Textures and Meshes settings.
- Pick NPCs that cover the config's intended audience: several races, both vanilla and edge cases
  (vampires, elders, Afflicted) — distribution failures are usually race- or linkage-specific.
- Subgroups with `Count: 0` include an `Explanation` line extracted from the verbose log (the GUI's
  "Why?" button). For deeper debugging, `--full-report-dir` writes each NPC's complete verbose
  report as XML — search it for the failing subgroup IDs.

JSON shape (abridged):

```json
{ "Command": "simulate", "Repetitions": 100, "SimulatedConfigs": ["..."], "AllNpcsAssigned": true,
  "Npcs": [ { "Npc": "Hulda | Hulda | 013BA3:Skyrim.esm", "FormKey": "013BA3:Skyrim.esm",
      "Gender": "Female", "TotalAssignments": 100,
      "AssetPackCounts": [ { "Name": "...", "Count": 100 } ],
      "SubgroupCounts": [ { "GroupName": "...", "Subgroups": [
          { "Id": "HD.Default", "Name": "...", "Count": 42, "Explanation": null } ] } ],
      "FullReportPath": null, "FailureReason": null } ] }
```

Exit 1 when any NPC ends with zero assignments or a failure reason (e.g. no gender-matching config).

---

## package

Validates a staged config folder against its `Manifest.json`, then 7-zips the folder contents
(manifest at archive root) into a distributable archive. No game environment needed.

```
SynthEBD.CLI package --staging <dir> [--out <file>] [--json]
```

Checks: `ConfigPrefix` present; every asset-pack/record-template/BodyGen path referenced by the
manifest (root fields and the whole Options tree) exists in staging; every referenced asset pack
parses; every pack's file-type `Source` paths carry the manifest's `ConfigPrefix` (or a
`DownloadInfo.ExtractionSubPath`) as their second folder. Validation failures exit 1 and skip
archive creation. Default output: `<ConfigName>.7z` next to the staging folder.

---

## verify-install

Verifies that a packaged (or staged) config archive installs correctly for **every possible
installer selection**, without running the interactive installer. Enumerates all selection chains
through the manifest's Options tree (each top-level option is a sequential wizard step; every
root-to-leaf path through its sub-options is one chain), unions each chain's resources exactly as
the installer's finalize step does, and checks per chain:

- the referenced config / record-template / BodyGen files exist in the package and the configs parse;
- every dependency archive the chain requires is present in `--downloads` (matched by
  `DownloadInfo.ExpectedFileName`);
- every config `Source` path resolves to a file the simulated install tree would provide — the
  package's own contents plus each dependency archive's catalogued contents routed to
  `<ExtractionSubPath-or-ConfigPrefix>\<entry>`, using the installer's own prefix-stripping logic.
  No live game data is involved; this is pure path verification.

```
SynthEBD.CLI verify-install (--archive <file.7z> | --staging <dir>) [--downloads <dir>] [--json]
```

Run it after `package` (and after any manifest edit) with the original texture-mod archives in the
`--downloads` folder. Exit 0 = all chains verified; 1 = at least one chain has missing files,
missing archives, or parse failures; 2 = fatal.

JSON shape (abridged):

```json
{ "Command": "verify-install", "ConfigName": "...", "ConfigPrefix": "...", "AllChainsValid": false,
  "Chains": [ { "Selection": "Step 1: CBBE > 4K", "IsValid": false,
      "InstalledConfigs": ["CBBE\\4K\\....json"], "InstalledRecordTemplates": ["..."],
      "InstalledBodyGenConfigs": [], "RequiredDownloads": ["BnP female skin 4k....7z"],
      "Errors": [], "MissingSourceFiles": ["textures\\BnP\\... (expected in archive set at: BnP\\...)"] } ] }
```

---

## archive-list / archive-extract

Bundled 7-Zip wrappers (7z, zip, and rar all supported); no game environment needed.

```
SynthEBD.CLI archive-list --archive <file> [--json]
SynthEBD.CLI archive-extract --archive <file> --dest <dir> [--json]
```

Use `archive-list` to catalogue a texture mod before extraction (check for `fomod\ModuleConfig.xml`,
estimate option structure, detect path collisions between archives) and `archive-extract` to build
the working folder (`--dest <workfolder>\textures\<Prefix>`).

---

## ui-screenshot

Automated visual-QA harness for the SynthEBD GUI itself - not part of the config-authoring
workflow, but useful when verifying how settings or configs render in the real UI.

```
SynthEBD.CLI ui-screenshot --synthebd-path <SynthEBD build folder> --out <dir>
                           [--theme <name> ...] [--mode <Use|Customize|Troubleshoot> ...]
                           [--menu <name>] [--expand-expanders] [--scroll-to <dockey>]
                           [--width N --height N]
```

Boots the real MainWindow against the real settings/environment, walks every nav menu (or just
`--menu`), and writes one PNG per menu to `<out>\<theme>\<mode>\NN-<menu>.png`. Repeatable
`--theme`/`--mode` flags sweep the theme and progressive-disclosure matrices in one session.
It never writes settings back. Note that the harness renders the SynthEBD assemblies and theme
files from the CLI's own folder, so rebuild the CLI to screenshot fresh UI changes.

`--scroll-to <dockey>` scrolls the element carrying that `DocTooltip.Key` (e.g.
`General.AttributeGroups`) to the top of its ScrollViewer before capturing, so below-the-fold
controls on long settings pages land in the shot. It is applied after `--expand-expanders`, so
content that only exists inside an expander is addressable. Mind the disclosure mode: a key on a
row gated above the current `--mode` has no visual to scroll to.

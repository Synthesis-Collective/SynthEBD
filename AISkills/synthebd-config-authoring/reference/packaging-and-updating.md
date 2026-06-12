# Packaging configs and updating them for new mod versions

## Packaging for distribution

A distributable config archive contains a `Manifest.json` at its root that drives SynthEBD's config
installer. Importantly, the archive normally contains **only** the config files, record templates,
and (optionally) BodyGen configs — **not the textures**: users download the original texture mod
themselves, and the manifest's `DownloadInfo` entries tell the installer which archives to ask for.

### Staging layout

```
staging\
├── Manifest.json
├── configs\<Config A>.json, <Config B>.json, ...
├── record templates\<Custom Record Templates>.esp        (only if needed)
└── bodygen configs\<BodyGen Config>.json                  (only if bundled)
```

Then: `SynthEBD.CLI package --staging <staging> [--out <file.7z>] --json` — it validates the
manifest against the staged files (and each config's prefixes) before creating the archive.

### Manifest.json

```json
{
  "ConfigName": "BnP - Female Skin",
  "ConfigDescription": "Installs textures from Bits & Pieces Female Skin",
  "ConfigPrefix": "BnP_Female",
  "Version": 1,
  "InstallationMessage": "shown to the user during installation",
  "Options": [
    {
      "Name": "CBBE",
      "Description": "...",
      "OptionsDescription": "Choose a resolution",
      "Options": [
        { "Name": "4K",
          "AssetPackPaths": ["CBBE\\4K\\SynthEBD - BnP 4K CBBE.json"],
          "RecordTemplatePaths": ["CBBE\\Record Templates - 3BA - pamonha.esp"],
          "BodyGenConfigPaths": ["CBBE\\MCSmarties Diverse Races Adaptation.json"],
          "DownloadInfo": [
            { "ModPageName": "Bits and Pieces", "ModDownloadName": "BnP female skin 4k (CBBE...)",
              "URL": "https://www.nexusmods.com/...", "ExpectedFileName": "BnP female skin 4k....7z",
              "ExtractionSubPath": "" } ] }
      ]
    }
  ],
  "AddPatchableRaces": [],
  "IgnoreMissingSourceFiles": []
}
```

- `ConfigPrefix` **must** equal the second folder of every file-type Source path in the packaged
  configs (`textures\<ConfigPrefix>\...`); a `DownloadInfo.ExtractionSubPath` declares an alternate
  prefix for that particular dependency archive. The `package` verb enforces this.
- The `Options` tree is the installer wizard: each level is one sequential choice; the chosen
  chain's resources are unioned. Use it for body family (CBBE/UNP), resolution (4K/2K), and
  male-mod variants (SOS/TNG) — mirror how the existing configs on Nexus structure theirs.
- `DownloadInfo` per option lists the texture archives the user must supply; `ExpectedFileName`
  lets the installer auto-match files the user already downloaded.
- `IgnoreMissingSourceFiles` suppresses missing-file warnings for paths that are legitimately
  optional (e.g. an optional hotfix's textures).
- `AddPatchableRaces` offers to add custom races to the user's Patchable Races list at install.

### Packaging questions for the user

- **BodyGen configs**: most existing config packages offer the default female BodyGen configs as an
  install option. They are shareable but somewhat dated (BodySlide/OBody is the modern path) —
  bundle them only if the user wants to.
- **Custom record templates**: required whenever a config writes to a destination path the shipped
  templates don't provide; the .esp must contain an NPC where the path exists, and every option
  that needs it must list it in `RecordTemplatePaths`.
- **Variants**: ship variants as separate config JSONs selected via the Options tree, or as one
  config with variant subgroups disabled by default (`DistributionEnabled: false`) — never as
  deleted content. Document the choice in `ConfigDescription`/`InstallationMessage`.

## Updating a config for a new mod version

Preconditions (matter for correctness):

- The installed config must currently pass `validate`.
- Path-length limits: when virtualized through MO2, paths beyond ~220 characters break — keep mod
  folder names and prefixes short. If the config was installed with the "modified to comply with
  the path length limit" warning, source paths were renamed at install and naive comparisons
  against the original mod will mislead.

Workflow (CLI-first equivalent of the GUI's "Update from mod"):

1. Extract the **new** mod version's archives into a fresh working folder
   (`<newwork>\textures\<Prefix>\...`, same prefixes as the installed config uses). Do not activate
   it in any mod manager.
2. `scan --root <newwork> --json`, and hash-compare old vs new yourself: for each `Source` path in
   the config, check whether the same relative path exists in the new working folder and whether
   its content changed (PowerShell `Get-FileHash` on both sides; MD5 is fine). Classify each config
   path as **identical** (no action), **changed in place** (no action — same path, new content),
   **missing from the new mod** (find its replacement or remove/disable the subgroup), and each new
   file as **new content** (decide whether to add a subgroup for it).
3. Beware the trap the GUI docs call out: authors often repackage *both* identical copies and the
   actually-updated texture under different option folders — a changelog saying "updated elder
   normals" means you should locate the new file even if the old path still exists unchanged.
   Always eyeball the new mod's folder/FOMOD structure, not just file diffs.
4. Apply the changes as JSON edits (`Source` path swaps; new subgroups for new variants — follow
   the existing tree's ID/naming conventions; `DistributionEnabled: false` rather than deletion for
   variants that became redundant). Also de-duplicate any newly identical textures across options
   (point all subgroups at one copy) to save VRAM.
5. `validate --config <name> --asset-root <newwork> --json` until clean, then `simulate` the usual
   NPC set and compare counts against expectations.
6. If the mod's *meshes* changed too (new `_1.nif` files etc.), those can be distributed as
   subgroups as well — set their destinations by analogy with existing mesh-distributing configs,
   and confirm intent with the user (meshes are the exception, not the rule).

The GUI alternative (Textures and Meshes → "Update from mod") automates the hash comparison with
menus for 100%-identical / predicted / failed predictions / deprecated files — point the user there
if they prefer doing the update interactively; the underlying decisions are the same.

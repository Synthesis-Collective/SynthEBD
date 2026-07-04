namespace SynthEBD;

// 3-part documentation for the config-distribution tool windows: the Config Drafter, the Config
// Packager (including its per-download rows), the installer's Download Coordinator page, and the
// Custom Game Installation (custom environment) dialog.
public static partial class UiDocs
{
    private static void RegisterDistributionTools()
    {
        // ----- Config Drafter (Window_ConfigDrafter) -----

        Add("Drafter.AutoAssign",
            layperson: "While drafting, SynthEBD can automatically handle three kinds of busywork for you: readable subgroup names, basic distribution rules, and links between matching subgroups. These checkboxes turn each convenience on or off.",
            technical: "The Names, Rules, and Linkage flags (AutoApplyNames/AutoApplyRules/AutoApplyLinkage, all default on) are passed into ConfigDrafter.DraftConfigFromTextures, where they gate ReplaceTextureNamesRecursive, AddRulesBySubgroupNameRecursive, and LinkSubgroupsByName while the subgroup tree is generated.",
            motivation: "The drafter's guesses are right for typically organized texture mods and save hours of manual editing, but authors of unusually structured mods can switch each automation off instead of undoing its work afterward.");

        Add("Drafter.AutoAssignNames",
            layperson: "Renames the generated subgroups after the textures they actually contain, instead of keeping the raw folder names from the mod archive.",
            technical: "Enables ReplaceTextureNamesRecursive: subgroups holding a single recognized texture are renamed via the file-name-to-subgroup-name map (plus head-normal folder rules), redundant 'male'/'female' qualifiers are stripped, and bare 'Male'/'Female' subgroups are renamed to 'Nord' since they nearly always hold the default Nord textures.",
            motivation: "Mod archives are organized for humans installing files, not for distribution logic; naming subgroups by texture identity makes the drafted config readable and reviewable at a glance.");

        Add("Drafter.AutoAssignRules",
            layperson: "Adds basic distribution rules based on subgroup names - for example, a subgroup named after a race is restricted to NPCs of that race, and 'Vampire' subgroups are restricted to vampire races.",
            technical: "Enables AddRulesBySubgroupNameRecursive: racial subgroup names map to Allowed Races (including elder/vampire counterparts where relevant), 'Vampire' subgroups derive the parent race's vampire analogue and exclude it from sibling subgroups, 'Main' receives the playable-non-vampire race grouping, and 'maleold'/'femaleold' source textures add the Elder races.",
            motivation: "Skin mods are conventionally organized by race and variant; encoding those obvious restrictions automatically gives the drafted config sensible distribution out of the box instead of offering every texture to every NPC.");

        Add("Drafter.AutoAssignLinkage",
            layperson: "Links subgroups that share a name across texture categories so they are distributed together - an NPC that receives a particular diffuse variant also receives its matching normal map.",
            technical: "Enables LinkSubgroupsByName, which adds required-subgroup relationships between same-named subgroups under different top-level categories (skipping race/Main/Vampire names, which the allowed-race rules already handle). It runs after all subgroup IDs are finalized.",
            motivation: "Texture variants are authored as matched sets; without linkage the patcher could pair one variant's diffuse with another's normal or specular map, producing mismatched skin detail.");

        Add("Drafter.CheckDuplicates",
            layperson: "Scans the selected folders for identical copies of the same texture stored in several places and lists each group of copies so you can keep just one. Mod authors often ship such copies to make FOMOD installers easier to build.",
            technical: "MD5-hashes every found texture on a background thread (the progress bar tracks file-name groups), groups byte-identical files that share a file name, and fills the Duplicate Textures panel; within each group the least path-specific copy is pre-chosen as the keeper.",
            motivation: "Drafting duplicates as-is creates redundant subgroups with erroneously restrictive folder-derived rules; finding the copies first keeps the drafted config layout simple.");

        Add("Drafter.ShowDuplicatesList",
            layperson: "Opens a popup listing the duplicate texture groups as plain text you can copy and refer to later - for example while deleting the redundant files from the mod itself.",
            technical: "Formats the duplicate groups as text - each group's file name followed by its paths, with the kept (unchecked) copies annotated '[KEEP]' - and shows it in a notification window with a copy button.",
            motivation: "The duplicate review can cover hundreds of files; a copyable record preserves which copies were kept and removed after the panel's contents are gone.");

        Add("Drafter.TexturesBodyType",
            layperson: "SynthEBD found optional 'etc' female body textures (femalebody_etc) in this mod. Pick which body mod the textures were made for (CBBE 3BA or BHUNP) so the drafted config uses the matching record template.",
            technical: "Shown when DraftConfigFromTextures detects etc-type textures. After the drafter window closes, the config editor calls ApplyCustomRecordTemplate to point the config's female default and additional-races template slots at the corresponding shipped record template plugin (Record Templates - 3BA or - BHUNP).",
            motivation: "Etc textures are applied through alternate-texture slots on the worn armor supplied by the record template, and the 3BA and BHUNP templates wire those slots differently; the choice must match the body the textures were authored for.");

        Add("Drafter.RemoveDuplicates",
            layperson: "Applies your duplicate decisions: the checked copies are dropped from the import, and in Replace mode anything that referenced them is pointed at the single unchecked copy you kept.",
            technical: "In Ignore Non-Primary mode the checked paths are added to the drafter's ignore list and skipped at draft time. In Replace With Primary mode each group must have exactly one unchecked keeper; the checked paths are recorded as multiplets whose subgroup path references are replaced with the keeper's path after the subgroups are built.",
            motivation: "Ignore merely leaves the copies out of the drafted config, while Replace consolidates references onto one file so the redundant copies can be deleted from the mod entirely; this button commits either decision before drafting.");

        Add("Drafter.UncategorizedSelectAll",
            layperson: "Checks every texture in this list so all of them are imported (as Uncategorized) when you draft.",
            technical: "Sets IsSelected on every unmatched-texture row. At draft time the selected uncategorized textures are imported into Uncategorized subgroups, while deselected ones are added to the ignore list and left out of the config.",
            motivation: "Bulk selection beats clicking dozens of checkboxes when you want to keep everything and sort it into the proper subgroups in the config editor afterward.");

        Add("Drafter.UncategorizedSelectNone",
            layperson: "Unchecks every texture in this list so none of the unrecognized files are imported when you draft.",
            technical: "Clears IsSelected on every unmatched-texture row; at draft time the deselected paths are added to the drafter's ignore list and excluded from the generated config.",
            motivation: "Unrecognized files are often previews, thumbnails, or leftovers rather than distributable textures; clearing the whole list at once is the fast path when it is mostly junk.");

        // ----- Config Packager (Window_ConfigPackager) -----

        Add("Packager.RootDirectory",
            layperson: "The folder that acts as the top level of the config archive you are packaging. Every file path in this window is stored relative to it, and SynthEBD checks that the files exist inside it.",
            technical: "Sets VM_Manifest.RootDirectory. File pickers start there and store their selections relative to it, and every referenced asset-pack/BodyGen/record-template path is revalidated whenever it changes: green border when the root plus the relative path exists on disk, red when it does not.",
            motivation: "The installer resolves manifest paths against the archive root at install time; authoring against the same root catches broken references (red borders) before the pack ships.");

        Add("Packager.Export",
            layperson: "Saves everything in this window to a Manifest.json file in a folder you choose. Place that file at the top level of your config archive so SynthEBD's installer can read it.",
            technical: "Serializes the packager state - the config metadata plus the full option tree - into a Version 1 Manifest model and writes Manifest.json into the selected folder.",
            motivation: "Manifest.json is what turns an ordinary archive into an installable SynthEBD config pack: the installer reads it to present options, route files, and request the required downloads.");

        Add("Packager.Import",
            layperson: "Opens an existing Manifest.json for editing - for example to update a config pack you released earlier.",
            technical: "Parses the chosen Manifest.json into the editor. Legacy Version 0 manifests (single-root format) have their top-level fields folded into a synthesized Root node with default file-extension mappings; if no root directory is set yet, the manifest's own folder is adopted as the root directory.",
            motivation: "Lets authors revise published packs, or upgrade old-format manifests to the current option-tree format, without rebuilding the whole tree by hand.");

        // ----- Packager option editor (UC_PackagerOption) -----

        Add("Packager.OptionName",
            layperson: "The name of this install option, as users will see it when the installer asks them to choose.",
            technical: "Sets Manifest.Option.Name, used as the node label in the packager's tree and as the option's title in the installer's selection wizard.",
            motivation: "Users choose between sibling options by name during installation, so the name should state the choice plainly (for example a body type or texture resolution).");

        Add("Packager.OptionDescription",
            layperson: "A short description of this option, shown to the user in the installer to explain what picking it means.",
            technical: "Sets Manifest.Option.Description, carried into the installer wizard's option display alongside the option's name.",
            motivation: "A name alone rarely conveys the tradeoffs; the description lets the author explain requirements or differences before the user commits to a branch.");

        Add("Packager.DestinationModFolder",
            layperson: "The name of the mod folder the installer creates in the user's mod manager (inside MO2's mods folder or Vortex's staging folder) to hold this config's assets. Leave it blank to use the folder name from the parent option.",
            technical: "Sets Manifest.Option.DestinationModFolder. For mod-manager users the installer copies assets into that folder under the current installation folder and writes the installation token file there; a non-blank value overrides the one inherited from the parent option, and if no chosen option supplies one the installer falls back to a default name and warns the user to rename it.",
            motivation: "Mod-manager users need the shipped assets to arrive as a normal named mod they can enable, reorder, and delete; per-option overrides let different branches install into distinct mod folders.");

        Add("Packager.DirectoryRouting",
            layperson: "Tells the installer which Data subfolder each file type belongs in - for example routing '.dds' files into the 'textures' folder and '.nif' files into 'meshes'.",
            technical: "Edits Manifest.Option.FileExtensionMap (file extension to folder name, case-insensitive). At install time the destination of each asset is built from this map first, falling back to the patcher's trim-path settings; files with unmapped extensions install at the mod or Data root.",
            motivation: "Config archives are free to organize files however the author likes; the routing map guarantees they land in the Data subfolders that the game and the config's source paths actually expect.");

        Add("Packager.SubOptionsPrompt",
            layperson: "The question the installer shows when asking the user to pick between this option's sub-options - for example 'Which texture resolution do you want?'.",
            technical: "Sets Manifest.Option.OptionsDescription. When the install wizard reaches this option's selection step, the prompt is displayed above the list of its child options.",
            motivation: "Each level of option nesting becomes one wizard step; a clear prompt tells users what dimension they are choosing at that step instead of leaving them to infer it from the option names.");

        Add("Packager.AssetConfigFiles",
            layperson: "The SynthEBD asset config file(s) (.json) installed when the user picks this option - the main payload of the pack.",
            technical: "Manifest.Option.AssetPackPaths: asset-pack .json paths stored relative to the root directory. When the user's chosen chain includes this option, the installer validates each config and installs it into the settings Asset Packs folder.",
            motivation: "Attaching config files per option lets one archive serve several audiences (male and female versions, different body support) while installing only the configs matching the user's choices.");

        Add("Packager.BodyGenConfigFiles",
            layperson: "BodyGen morph config files (.json) installed when the user picks this option. Only needed if the pack distributes BodyGen morphs.",
            technical: "Manifest.Option.BodyGenConfigPaths: BodyGen config .json paths relative to the root directory, moved into the settings BodyGen configs folder at install time; files already present are skipped with a warning.",
            motivation: "Asset configs whose distribution logic depends on BodyGen morphs need their companion BodyGen config on the user's machine; bundling it in the same option keeps the pair together.");

        Add("Packager.RecordTemplatePlugins",
            layperson: "Extra record template plugins (.esp) installed with this option. Record templates are the donor NPC records SynthEBD copies body and armature setups from.",
            technical: "Manifest.Option.RecordTemplatePaths: plugin paths relative to the root directory, moved into the settings Record Templates folder at install time; files already present are skipped.",
            motivation: "Configs made for non-default bodies (3BA, BHUNP, TNG, and similar) reference template records that do not ship with SynthEBD; including the plugin makes the config work out of the box.");

        Add("Packager.IgnoreMissingSourceFiles",
            layperson: "File paths this config refers to that are supposed to come from another mod the user installs separately, so the installer should not complain when they are missing from this archive.",
            technical: "Manifest.Option.IgnoreMissingSourceFiles. During installation every asset path referenced by the installed configs is checked against the extracted archive contents; paths on this list are exempted from the missing-file report (case-insensitive comparison).",
            motivation: "Configs often point at assets from a base mod the author cannot redistribute; listing those paths prevents false 'missing source file' alarms without disabling the check for genuinely packaged files.");

        Add("Packager.ModDownloads",
            layperson: "Mods the user must download themselves when they pick this option, usually because you are not allowed to redistribute them. The installer shows each entry and asks the user to point at the downloaded file.",
            technical: "Manifest.Option.DownloadInfo: one entry per external dependency archive. The installer's Download Coordinator page collects a local file for each entry, then extracts every archive into the install staging tree under the config prefix (or the entry's Override Prefix) before assets are routed.",
            motivation: "Most texture mods cannot legally be bundled; download entries let a pack stay lightweight and legal while the installer still assembles a complete installation from the user-supplied archives.");

        // ----- Packager download entry (UC_DownloadInfoContainer) -----

        Add("Packager.DownloadModPageName",
            layperson: "The name of the mod or web page the user needs to visit, shown as the heading for this download during installation.",
            technical: "Sets DownloadInfoContainer.ModPageName, displayed as the bold title of this dependency's row in the installer's Download Coordinator.",
            motivation: "A recognizable page name lets users confirm they are fetching the right mod before following the link to an external site.");

        Add("Packager.DownloadUrl",
            layperson: "The web address of the page where the user downloads this mod.",
            technical: "Sets DownloadInfoContainer.URL. The Download Coordinator renders it as a clickable hyperlink with an adjacent Copy button.",
            motivation: "Linking straight to the correct page removes the biggest failure point of manual downloads: hunting for, and mis-picking, the right mod page.");

        Add("Packager.DownloadLinkText",
            layperson: "The exact name of the download link or file entry the user should click on that page - useful when a page offers several files.",
            technical: "Sets DownloadInfoContainer.ModDownloadName, shown on the dependency row's 'Download file' line and quoted in the installer's error messages when the archive path is left blank or invalid.",
            motivation: "Mod pages commonly host main, optional, and legacy files; quoting the exact link text steers users to the specific file the config was built against.");

        Add("Packager.DownloadExpectedFileName",
            layperson: "The exact file name (with extension) the download will have on disk, for example 'Some Skin 4K-12345-1-0.7z'.",
            technical: "Sets DownloadInfoContainer.ExpectedFileName. The Download Coordinator displays it, and its Auto-Search feature fills in archive paths by matching this exact name against the files in a chosen folder.",
            motivation: "An exact expected name lets the installer locate downloads automatically and lets users verify they grabbed the right file and version.");

        Add("Packager.DownloadOverridePrefix",
            layperson: "A different path prefix for files extracted from this one download, used instead of the pack's main Config Prefix. It must exactly match the prefix used in the config file's source paths; leave it blank to use the main prefix.",
            technical: "Sets DownloadInfoContainer.ExtractionSubPath. At install time this archive is extracted under this prefix instead of Manifest.ConfigPrefix, and destination-path resolution treats it as an additional valid prefix - so it must match the prefix in the corresponding config's Source paths exactly.",
            motivation: "When one pack draws assets from several mods, per-archive prefixes keep each mod's files in their own subtree and let config paths address them unambiguously.");

        // ----- Config installer Download Coordinator (UC_DownloadCoordinator) -----

        Add("Installer.AutoSearchDownloadsFolder",
            layperson: "Pick the folder where you saved the downloads (for example your browser's Downloads folder) and SynthEBD fills in every required file it can find there by name.",
            technical: "Opens a folder picker and, for each entry that still lacks a path, fills it in when a file matching the entry's expected file name exists in that folder; the Search Subfolders checkbox makes the scan recurse into subdirectories.",
            motivation: "Config packs can require many archives; matching them against one folder in a single pass is much faster than browsing to each file individually.");

        // ----- Custom Game Installation dialog (Window_CustomEnvironment) -----

        Add("CustomEnv.SkyrimRelease",
            layperson: "The edition of the game this installation is (Special Edition, GOG, VR, Enderal, and so on). Pick the one that matches the executable you are selecting.",
            technical: "Sets the Mutagen SkyrimRelease used to build the trial game environment via GameEnvironment.Typical.Builder; changing it immediately rebuilds and revalidates the environment against the selected data folder.",
            motivation: "Different releases live in different install paths and read plugins differently; with the wrong release selected, environment creation fails or targets the wrong game.");

        Add("CustomEnv.GamePath",
            layperson: "The location of your game's .exe file. Use Search to select the executable if your game is not where SynthEBD expected; SynthEBD reads your load order from the 'data' folder next to it.",
            technical: "Search opens an executable picker, and the chosen file's folder plus 'data' becomes the trial environment's target data folder. The dialog pre-seeds the path by probing for SkyrimSE.exe/TESV.exe/SkyrimVR.exe next to the previously known data folder, and every change rebuilds the trial environment and updates the status line.",
            motivation: "This dialog only appears when automatic detection failed, so pointing SynthEBD at the real executable is the recovery path for custom Steam libraries and other unusual installs.");

        Add("CustomEnv.LoadOrder",
            layperson: "The plugin list SynthEBD found using your selected game path - what it will treat as your load order. Check that it matches what your mod manager shows before pressing OK.",
            technical: "Lists the plugin file names from the validated trial environment's listed order (enabled and existing plugins only; the SynthEBD output plugin is not part of this trial build). The panel appears only once an environment builds successfully with a non-empty load order.",
            motivation: "An environment can build successfully against the wrong install or outside the mod manager; eyeballing the load order catches that immediately instead of after a confusing patch run.");
    }
}

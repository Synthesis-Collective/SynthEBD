namespace SynthEBD;

// 3-part documentation for the General Settings menu and the embedded Environment panel.
public static partial class UiDocs
{
    private static void RegisterGeneral()
    {
        Add("General.OutputDataFolder",
            layperson: "The folder where SynthEBD writes everything it generates: the plugin, meshes, textures, and supporting files. Leave it empty to write directly into the game's Data folder. Use Select to pick a folder and Clear to return to the default.",
            technical: "Sets GeneralSettings.OutputDataFolder, which feeds SynthEBDPaths.OutputDataFolder; when empty, the resolved game Data folder is used instead. All patcher outputs are rooted here: the output plugin(s), copied asset files, baked FaceGen NIFs, EBD script JSON, and SkyPatcher ini directives.",
            motivation: "Mod-manager users usually want SynthEBD's output isolated in its own mod folder (for example an empty MO2 mod) instead of dumped loose into Data, so the whole patch can be enabled, disabled, or deleted as one unit.");

        Add("General.Theme",
            layperson: "Changes the color scheme of the SynthEBD window. Pick a theme from the list and the interface recolors immediately.",
            technical: "Selects among the loose .xaml resource dictionaries in the Themes folder next to SynthEBD.exe; ThemeManager.ApplyTheme swaps the active dictionary at runtime and the choice persists as GeneralSettings.ThemeName. Dropping your own theme file into that folder adds it to this list.",
            motivation: "Purely cosmetic: it lets users match the patcher to their preferred light or dark look, and lets theme authors ship custom palettes without rebuilding the application.");

        Add("General.ApplyTexturesAndMeshes",
            layperson: "Turns SynthEBD's main feature - giving NPCs new skin textures and meshes from your installed config files - on or off for the next patcher run.",
            technical: "Master switch for the Assets axis (GeneralSettings.bChangeMeshesOrTextures), the same flag as the Dashboard's Asset Patching toggle. When enabled the patcher assigns an asset combination per NPC and emits it as WornArmor/HeadTexture overrides, FaceGen edits, EBD script data, or SkyPatcher directives depending on the patching mode.",
            motivation: "Lets you exclude asset patching from a run (for example to only re-roll heights or body shapes) without uninstalling or reconfiguring anything.");

        Add("General.ApplyBodyShapes",
            layperson: "Chooses whether NPCs get varied body shapes, and which body system provides them. Pick BodyGen or BodySlide, or None to leave bodies alone. With BodySlide you also choose whether OBody or AutoBody applies the presets in the game.",
            technical: "Sets GeneralSettings.BodySelectionMode (None, BodyGen, or BodySlide). BodyGen distributes RaceMenu BodyGen morph templates; BodySlide distributes BodySlide presets applied at runtime through the selector chosen in the second dropdown (GeneralSettings.BSSelectionMode: OBody or AutoBody).",
            motivation: "BodyGen and the OBody/AutoBody ecosystems are alternative body-morphing frameworks; this control picks the one that matches the mods you actually have installed.");

        Add("General.ApplyHeightChanges",
            layperson: "Turns height randomization on or off. When on, NPCs get slightly different heights based on your height settings instead of everyone in a race being exactly the same size.",
            technical: "Master switch for the Height axis (GeneralSettings.bChangeHeight). When enabled, the HeightPatcher applies per-race height distributions from the selected height configuration file, written as NPC record overrides or as SkyPatcher directives depending on the height patching mode.",
            motivation: "Vanilla Skyrim gives every member of a race an identical scale; small height variation is a cheap immersion win, and the switch lets you skip it if another mod already manages heights.");

        Add("General.ApplyHeadParts",
            layperson: "Turns randomized head features (hair, eyes, brows, scars, and similar) on or off. Enabling it shows a warning because it can change the faces of NPCs whose faces were hand-sculpted by mod authors.",
            technical: "Master switch for the Head Parts axis (GeneralSettings.bChangeHeadParts). Distributes head parts imported per category (hair, eyes, brows, facial hair, scars, misc), applied via Papyrus scripts or baked into FaceGen NIFs depending on the head-part patching mode.",
            motivation: "Extends appearance variety beyond textures to the face itself. It ships as its own toggle because the feature is experimental for NPCs with custom face sculpts.");

        Add("General.HeadPartsExcludeCustomHeads",
            layperson: "Tries to detect NPCs whose faces were hand-made by mod authors and skips giving them new head features, so those faces are not accidentally changed. The detection is not perfect.",
            technical: "Mirror of GeneralSettings.bHeadPartsExcludeCustomHeads. HeadPartSelector.BlockNPCWithCustomFaceGen compares the winning NPC override's FaceMorph, FaceParts, and HeadParts against the base record; any difference marks the NPC as custom-faced and blocks head-part assignment for it. The prediction is heuristic and not 100 percent accurate.",
            motivation: "Follower and overhaul mods often ship carefully sculpted faces that a random hair or brow swap would ruin; this guard preserves most of them while keeping head-part randomization active for everyone else.");

        Add("General.EnableConsistency",
            layperson: "Remembers what each NPC got last time so they keep the same look between patcher runs. Turn it off when you want a complete reshuffle.",
            technical: "Mirror of GeneralSettings.bEnableConsistency (the same flag as the Dashboard's Consistency toggle). When enabled, each selector first tries the NPC's stored assignment from Consistency.json before rolling a new one, so re-running the patcher preserves prior outcomes where still valid.",
            motivation: "Without consistency, every run reshuffles every NPC and re-patching mid-playthrough changes familiar faces. Disable it deliberately when you want everything re-rolled.");

        Add("General.AutoSplitOutput",
            layperson: "If the generated plugin would depend on more files than Skyrim allows, SynthEBD automatically splits it into several plugins. Normal-sized patches are unaffected. If a split happens, every resulting plugin must be enabled in your load order.",
            technical: "Mirror of GeneralSettings.AutoSplitOutput. When the output plugin would exceed Skyrim's 255-master limit, the output stage splits it into SynthEBD.esp, SynthEBD_2.esp, and so on. It only triggers on overflow and applies to standalone output only (Synthesis manages its own output plugin).",
            motivation: "Very large load orders with many appearance sources can push a single patch plugin past the master limit, which would make it unloadable; automatic splitting keeps the run from failing.");

        Add("General.ExcludePlayerCharacter",
            layperson: "Keeps SynthEBD from changing your own character. Leave this checked unless you really want the patcher to modify the player.",
            technical: "Mirror of GeneralSettings.ExcludePlayerCharacter. During the per-NPC patching loop the Player record (Skyrim.esm Player NPC) is skipped entirely, and the vanilla-body-path setter honors the same exclusion.",
            motivation: "Player appearance is normally managed in-game (for example with RaceMenu), and patcher edits to the Player record can cause unexpected results, so the player is excluded by default.");

        Add("General.ExcludePresets",
            layperson: "Keeps SynthEBD from changing the hidden template characters that power the face choices in the race menu. Leave this checked.",
            technical: "Mirror of GeneralSettings.ExcludePresets. The patcher skips any NPC whose EditorID contains 'Preset' - the chargen preset records that supply the premade faces in character creation.",
            motivation: "Preset NPCs are not actors in the world; patching them only alters the faces offered during character creation and wastes assignments, so they are excluded by default.");

        Add("General.ExcludePartiallySkinnedNpcs",
            layperson: "Skips NPCs that look human on paper but actually use a creature-style body, which would badly scramble texture assignment. Leave this checked.",
            technical: "Mirror of GeneralSettings.bFilterNPCsByArmature. Before assignment, the patcher inspects each NPC's WornArmor skin armature and skips NPCs whose skin lacks the separate torso, hands, and feet addons expected for humanoids - for example a mod-added Draugr that uses the Nord race with a single unipiece skin.",
            motivation: "Some mods add NPCs with humanoid races but creature skins; assigning humanoid texture sets to a unipiece body severely scrambles SynthEBD's texture distribution, so such NPCs are blocked by default.");

        Add("General.LoadSettingsFromPortableFolder",
            layperson: "Makes SynthEBD read and save its settings in a folder you choose instead of next to SynthEBD.exe. Useful for keeping settings inside your mod manager's profile.",
            technical: "Mirror of PatcherSettingsSource.UsePortableSettings, stored in the small settings-source JSON next to the executable. When enabled and the portable folder is valid, PatcherSettingsSourceProvider resolves it as the active settings root, so all settings JSON files load from and save to it instead of the default root (the app directory, or the Synthesis extra-settings folder when run under Synthesis).",
            motivation: "Lets settings live inside a mod-manager-managed folder (for example Data\\SynthEBD in an MO2 mod) so they travel with profiles, survive application updates, and can be shared between machines.");

        Add("General.PortableSettingsFolder",
            layperson: "The folder that holds your portable settings. It must be named SynthEBD. Use Search to pick it and Clear to remove it.",
            technical: "Mirror of PatcherSettingsSource.PortableSettingsFolder. The folder picker rejects any directory not named 'SynthEBD'; when the path is set and exists, it becomes the active settings root for all settings IO.",
            motivation: "The fixed folder name keeps the portable root recognizable (typically Data\\SynthEBD) and prevents accidentally pointing the patcher's settings at an arbitrary directory.");

        Add("General.AppearanceMergePatcher",
            layperson: "Tells SynthEBD if you use a separate tool (EasyNPC or NPC Plugin Chooser 2) to decide which mod supplies each NPC's face. This helps SynthEBD know where each NPC's look really comes from.",
            technical: "Sets GeneralSettings.AppearanceMergerType (None, EasyNPC, or NPC2). At the start of a patcher run the matching profile parser is initialized from the EasyNPC profile or NPC2 token file, letting SynthEBD identify the plugin providing each NPC's appearance instead of assuming the winning override.",
            motivation: "Appearance mergers rewrite which mod supplies each NPC's face; without this hint SynthEBD could attribute an NPC's appearance to the wrong plugin and base its decisions on the wrong face data.");

        Add("General.EasyNpcProfilePath",
            layperson: "The exported profile file from EasyNPC that lists which mod you chose for each NPC's appearance. In EasyNPC, open the Profile tab and click the disk icon at the top right to create it.",
            technical: "Sets GeneralSettings.EasyNPCprofilePath, the .txt profile export that maps each NPC to its chosen default and appearance plugin. The EasyNPC profile parser reads it at the start of a patcher run to resolve each NPC's appearance source.",
            motivation: "SynthEBD needs to know which plugin EasyNPC chose per NPC so its assignments track the face each NPC actually wears; the exported profile provides exactly that mapping.");

        Add("General.Npc2TokenPath",
            layperson: "The NPC_Token.json file created by NPC Plugin Chooser 2, which records your per-NPC appearance choices. It is typically in your Data folder or your mod output folder.",
            technical: "Sets GeneralSettings.NPC2TokenPath, pointing at the NPC_Token.json emitted by NPC Plugin Chooser 2. The NPC2 profile parser reads it at the start of a patcher run to identify the appearance-providing plugin per NPC.",
            motivation: "Like the EasyNPC profile, the token file tells SynthEBD which mod actually supplies each NPC's face after merging, keeping asset assignment aligned with what is in the game.");

        Add("General.LinkNpcsWithSameName",
            layperson: "Makes different records of the same character receive the same look, so a character who appears in several forms stays recognizable as one person.",
            technical: "Mirror of GeneralSettings.bLinkNPCsWithSameName. NPCs flagged Unique that share a name and gender are tracked through UniqueNPCData; the first-patched member's assets, body shape, height, and head parts are reused for the others across all selectors.",
            motivation: "Vanilla and mods duplicate named characters across records (quest copies, battle variants); without linking, each copy would roll a different appearance and break the illusion that they are the same person.");

        Add("General.LinkedNpcNameExclusions",
            layperson: "Names that should NOT be treated as one character even when several NPCs share them. Generic names like 'Bandit' belong here so all bandits do not end up looking identical.",
            technical: "Mirror of GeneralSettings.LinkedNPCNameExclusions, a name list consulted by the same-name linking logic; an NPC whose name matches an entry is not treated as a valid linked unique, so each record rolls its appearance independently.",
            motivation: "Unique-flagged NPCs sometimes carry generic shared names; without exclusions, every 'Courier' or 'Nord' would be forced to share a single appearance.");

        Add("General.LinkedNpcGroups",
            layperson: "Hand-made groups of NPC records that all represent the same character and should share one look. SynthEBD ships with groups for characters like Ulfric Stormcloak, who exists as several records.",
            technical: "Mirror of GeneralSettings.LinkedNPCGroups. Each group lists NPC FormKeys plus a designated Primary; during patching the Primary's assignments propagate to the other members regardless of name matching.",
            motivation: "Same-name linking cannot catch records with differing names or non-unique flags (for example corpse or flashback versions of a character); explicit groups cover those cases.");

        Add("General.LinkedNpcGroupName",
            layperson: "A label for this group so you can recognize it in the list. It has no effect on patching.",
            technical: "Sets LinkedNPCGroup.GroupName, a display-only string persisted with the group definition.",
            motivation: "Groups are defined by FormKeys, which are unreadable at a glance; the name keeps the list manageable.");

        Add("General.LinkedNpcGroupPrimary",
            layperson: "The main version of the character in this group. Whatever look this NPC receives is copied to the other members of the group.",
            technical: "Sets LinkedNPCGroup.Primary. The Primary member's assignments (assets, body shape, height, head parts) are recorded and then applied to the remaining FormKeys in the group.",
            motivation: "One member must serve as the source of truth; designating the primary (usually the character's main record rather than a scripted copy) makes the propagation deterministic.");

        Add("General.PatchableRaces",
            layperson: "The list of races SynthEBD is allowed to change. NPCs of races not in this list are left alone. The defaults cover the playable races plus their vampire and variant forms.",
            technical: "Mirror of GeneralSettings.PatchableRaces, a list of RACE FormKeys. An NPC's effective race for each axis (after Race Alias substitution) must appear in this list for that axis to patch it. Defaults include the vanilla playable races, their vampire forms, Elder, Afflicted, Snow Elf, and the beast races.",
            motivation: "Restricting patching to known humanoid races keeps SynthEBD from misassigning humanoid textures to creatures, while still letting users add custom races from mods.");

        Add("General.RaceAliases",
            layperson: "Rules that make SynthEBD treat one race as another - for example, treating a mod's custom Nord race as the regular Nord race so those NPCs get patched too.",
            technical: "Mirror of GeneralSettings.RaceAliases. Each alias redirects a source RACE FormKey to an alias race when resolving an NPC's effective race, scoped by sex (male/female) and by axis (assets, BodyGen, height, head parts). Defaults ship for the Afflicted and the Charmers of the Reach races.",
            motivation: "Mods frequently introduce custom races that are functionally copies of vanilla ones; aliases let those NPCs participate in distribution without every config file needing to know about the custom race.");

        Add("General.RaceGroupings",
            layperson: "Named collections of races (like 'Humanoid' or 'Elven') that distribution rules can refer to by name instead of listing every race individually. This menu manages your central set.",
            technical: "Mirror of GeneralSettings.RaceGroupings, the main/General set of named RaceGrouping definitions referenced by label from distribution rules. Config files carry their own local copies so they work when shared; the Override toggle inside this menu decides whether a General grouping supersedes a same-labeled local one.",
            motivation: "Groupings keep distribution rules short and shareable: a config can say 'Elven' rather than enumerating races, and users can centrally adjust what 'Elven' means for everything that references it.");

        Add("General.OverwritePluginRaceGroups",
            layperson: "When checked, if a config file defines a race group with the same name as one in this menu, your version here wins. Uncheck it to let each config's own definition apply instead.",
            technical: "Mirror of GeneralSettings.OverwritePluginRaceGroups. When true, a General race grouping supersedes a same-labeled grouping defined locally in an asset pack or other config file; when false, the local definition is used.",
            motivation: "Downloaded configs ship their own grouping definitions so they work out of the box; this toggle decides whether centralized management (General wins) or config autonomy (local wins) takes precedence on a name collision.");

        Add("General.AttributeGroups",
            layperson: "Named bundles of NPC traits (like 'Must be Muscular') that distribution rules can reference by name. Editing a group here changes every rule that uses it. This menu manages your central set.",
            technical: "Mirror of GeneralSettings.AttributeGroups, the main/General set of named NPCAttribute lists referenced by Group-type attributes in asset-pack subgroup, BodyGen, OBody, and head-part rules. Label resolution goes through NPCAttribute.GetAttributeGroupByLabel and honors the adjacent Override toggle.",
            motivation: "Groups let many rules share one trait definition: config authors can gate subgroups on a label like 'MustBeFit' while users retune what that label means in a single place.");

        Add("General.OverwritePluginAttributeGroups",
            layperson: "When checked, if a config file defines an attribute group with the same name as one in this menu, your version here wins. Uncheck it to let each config's own definition apply instead.",
            technical: "Mirror of GeneralSettings.OverwritePluginAttGroups, consulted by NPCAttribute.GetAttributeGroupByLabel: when true, a matching General attribute group is returned; when false, the config's local set resolves the label. At load time, General groups are also copied into each config's local set for any label it lacks.",
            motivation: "Shared configs must be able to ship the groups they rely on, while users need central control over group meanings; this toggle picks which side wins when the same label is defined in both places.");

        Add("General.VerboseModeConflictNpcs",
            layperson: "Writes a detailed log explaining what happened for any NPC where something went wrong or the chosen assets and body shape could not agree. Useful when investigating odd results.",
            technical: "Mirror of GeneralSettings.bVerboseModeAssetsNoncompliant. Generates the full per-NPC verbose operation report (selection steps, rule filtering) only for NPCs that hit an error or an assets/body-shape conflict during selection.",
            motivation: "Full logging for every NPC is enormous; logging just the problem cases captures the information needed for troubleshooting at a manageable size.");

        Add("General.VerboseModeAllNpcs",
            layperson: "Writes the detailed log for every single NPC. The log file gets very large, so only use this when you really need it.",
            technical: "Mirror of GeneralSettings.bVerboseModeAssetsAll. Forces the per-NPC verbose operation report for all patched NPCs regardless of outcome.",
            motivation: "Sometimes the NPC you need to inspect is not one that erred; blanket logging guarantees coverage at the cost of log size and patching speed.");

        Add("General.VerboseModeSpecificNpcs",
            layperson: "Writes the detailed log only for the NPCs you pick here. This is usually the best way to investigate one problem character.",
            technical: "Mirror of GeneralSettings.VerboseModeNPClist, a list of NPC FormKeys; the NPCs in it receive the full verbose operation report even when the global verbose modes are off.",
            motivation: "Targets diagnostics precisely: you get exhaustive detail for a problem NPC without paying the cost of logging thousands of others.");

        Add("General.VerboseModeDetailedAttributes",
            layperson: "Makes the detailed logs show readable attribute names instead of raw record IDs. It noticeably slows down patching, so leave it off unless you are actually reading the logs.",
            technical: "Mirror of GeneralSettings.VerboseModeDetailedAttributes, passed into the AttributeMatcher calls throughout the selectors; when true, attribute log strings resolve FormKeys to record names via the link cache, which significantly slows the per-NPC loop.",
            motivation: "Raw FormKeys are hard to read when auditing why a rule matched or failed; name resolution trades speed for readable diagnostics.");

        Add("General.VerboseModeAdvancedSelector",
            layperson: "Lets you describe which NPCs should get detailed logs using rules (race, traits, uniqueness, weight) instead of picking them one by one.",
            technical: "Mirror of GeneralSettings.bUseDetailedReportSelection, which activates the DetailedReportNPCSelector: allowed/disallowed races and race groupings, allowed/disallowed attributes, unique/non-unique status, and a weight range, evaluated per NPC to opt it into verbose logging. As a General-level feature, its attribute groups resolve against the General set.",
            motivation: "When a problem affects a class of NPCs (for example all female Orcs above a certain weight) rather than known individuals, rule-based selection captures the right logs without hand-picking FormKeys.");

        Add("General.DisableValidation",
            layperson: "Skips the safety checks SynthEBD normally runs before patching. Missing files will no longer stop the run, but they will cause problems in the game, such as NPCs turning blue. Only turn this on if you know why you need it.",
            technical: "Mirror of GeneralSettings.bDisableValidation. Bypasses the pre-run validation stage that checks config integrity and that referenced asset files exist. Skyrim itself still needs those files, so enabling this trades early errors for in-game symptoms (missing textures, possible Papyrus script issues).",
            motivation: "Mainly for config file authors who want to test or share distribution logic without downloading the large texture mods a config references.");

        Add("General.CloseArchiveExtractor",
            layperson: "Closes the archive-extraction window automatically when installing config files finishes. Uncheck it to keep the window open so you can read what happened if an installation misbehaves.",
            technical: "Mirror of GeneralSettings.Close7ZipWhenFinished. SynthEBD shells out to its bundled 7-Zip to extract config and mod archives during installation; this flag controls whether that console window closes on completion.",
            motivation: "Normally the extractor window is noise, but when extraction fails its output is the primary clue, so troubleshooting requires keeping it open.");

        Add("General.DoNotMergeInRecordsFrom",
            layperson: "When SynthEBD runs in the mode that avoids depending on other mods, it copies NPCs and their linked records into its own plugin. Mods listed here are never copied. The base game files are listed by default.",
            technical: "Mirror of GeneralSettings.BlockedModsFromImport. In Attempt to Avoid Override (SkyPatcher) mode, SurrogateNPCProvider deep-copies NPCs and their dependency records into the output mod so the patch gains no new masters; records originating from the listed ModKeys are left as references instead of being duplicated.",
            motivation: "Merging in vanilla master records would pointlessly bloat the patch - everyone has Skyrim.esm - so universal masters are excluded by default, and users can add other mods that every consumer of their patch is guaranteed to have.");

        Add("General.NifPreviewOptions",
            layperson: "Chooses which NPCs stand in as the 3D preview model when you look at textures and head parts in the character viewer. You can set one male and one female NPC per race, plus a default pair.",
            technical: "Mirror of GeneralSettings.PreviewNpcs (NifPreviewNpcSettings). The 3D Character Viewer resolves its preview stand-in per patchable race and gender, falling back to the Default row when a race is unmapped.",
            motivation: "Previews need a real NPC to source race, skeleton, and FaceGen data; letting users pick representative NPCs makes previews match the bodies and races they actually care about.");

        Add("General.RenderCacheMode",
            layperson: "Controls how much RAM the 3D character preview may use to cache decoded textures and geometry. \"% Free RAM\" (the default) sizes itself to free memory -- the number beside it is the total share of free RAM the caches may use (85 by default; raise to cache more, lower to leave RAM for other work). \"Fixed RAM\" caps it at the number of GB you set; \"Disabled\" caches nothing (lowest memory, slowest previews).",
            technical: "Mirrors GeneralSettings.CacheMode (RenderCacheMode), CacheFreeRamPercent, and CacheFixedBudgetGB. The SynthEbdSettingsAdapter forwards CacheMode and FreeRamCachePercent live and exposes FixedCacheBudgetBytes (GB * 1024^3) to CharacterViewer.Rendering, whose CharacterPreviewCache and NifMeshBuilder re-poll the budget each cycle. In % Free RAM mode the three caches keep a fixed 75:9:1 ratio (pixel/mesh/cubemap, calibrated from a 50-NPC measurement) and CacheFreeRamPercent scales their collective total AND their upper ceiling (the same share applied to total RAM) -- one source of truth, no separate per-cache cap. 85 gives each cache its calibrated fraction. \"Fixed RAM\" applies those ratios to the GB pool instead.",
            motivation: "Large batch mugshot runs can accumulate significant decode-cache memory; letting users choose an auto (with a tunable share), fixed, or off strategy trades RAM against preview speed to fit their machine.");

        Add("Environment.OutputName",
            layperson: "The file name of the plugin SynthEBD creates (without the .esp extension). Most users can leave it as SynthEBD.",
            technical: "Sets the standalone environment provider's OutputModName; the output mod is created as <name>.esp and changing the name rebuilds the Mutagen game environment. The load-order builder excludes the output plugin itself plus any plugin mastered (directly or transitively) to a previous version of it, so SynthEBD never patches against its own stale output.",
            motivation: "A custom name keeps multiple SynthEBD outputs distinguishable (for example per mod-manager profile), and the automatic exclusion makes renaming safe.");

        Add("Environment.SkyrimRelease",
            layperson: "Which edition of the game you are patching. Pick the one you actually play so SynthEBD finds the right installation and reads the right load order.",
            technical: "Sets the Mutagen SkyrimRelease (for example SkyrimSE, SkyrimSEGog, SkyrimVR, EnderalSE) used to build the game environment via GameEnvironment.Typical.Builder. It determines default install and Data folder detection and how plugins are interpreted; changing it rebuilds the environment and reloads the load order.",
            motivation: "Different releases live in different install paths and have format differences; with the wrong release selected, SynthEBD either fails to build an environment or reads the wrong game's load order.");

        Add("Environment.GameDataDirectory",
            layperson: "The game's Data folder that SynthEBD reads your mods from. Leave it empty to let SynthEBD find it automatically; set it if your game is installed somewhere unusual.",
            technical: "Sets DataFolderPath on the environment provider. When set, the Mutagen environment builder targets it (WithTargetDataFolder); when cleared, the default detected install is used and the resolved path is written back here. If the resulting environment is invalid (its listed order contains only the output plugin), SynthEBD prompts for a custom environment.",
            motivation: "Auto-detection covers standard installs, but custom Steam library locations or unusual setups need an explicit path - and everything SynthEBD does depends on reading the correct load order.");

        Add("General.RaceAliasRace",
            layperson: "The race to redirect: NPCs of this race are treated as if they were the Alias Race below, for the sexes and features ticked in this rule.",
            technical: "Sets RaceAlias.Race, the source RACE FormKey of the alias rule. During patching the alias handler swaps the NPC's effective race to RaceAlias.AliasRace per axis according to the rule's male/female and apply-to flags - NPCInfo derives its AssetsRace, HeightRace, and the other per-axis races through it - and racial height patching redirects its RACE record lookup the same way.",
            motivation: "Mods often add custom races that are functional copies of vanilla ones; aliasing lets their NPCs receive the distributions written for the vanilla race without every config file having to list the custom race.");

        Add("General.ShowTooltips",
            layperson: "Shows or hides explanation popups like this one throughout SynthEBD.",
            technical: "Mirror of GeneralSettings.bShowToolTips, forwarded to TooltipController.Instance.DisplayToolTips; every tooltip's ToolTipService.IsEnabled - the rich three-part tooltips and the remaining legacy inline ones alike - is bound to that flag.",
            motivation: "Tooltips are training wheels: essential while learning the patcher, distracting once you know it. One global switch clears them everywhere.");
    }
}

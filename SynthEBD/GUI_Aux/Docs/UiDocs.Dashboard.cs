namespace SynthEBD;

// 3-part documentation for the Dashboard tiles and the main-window shell chrome.
public static partial class UiDocs
{
    private static void RegisterDashboard()
    {
        Add("Dashboard.Environment",
            layperson: "Shows whether SynthEBD found your Skyrim installation and how many plugins it loaded. Click to open General Settings, where the game folder can be changed.",
            technical: "Reflects the Mutagen game environment: the detected Skyrim release, the resolved Data folder, and the enabled load order behind the link cache. An environment is invalid when its listed order contains only SynthEBD's own output plugin.",
            motivation: "Everything SynthEBD does starts from reading your load order; if this tile is not green, no other module can work, so it is surfaced first.");

        Add("Dashboard.EnvironmentModManager",
            layperson: "SynthEBD writes its output files (meshes, textures, FaceGen, scripts). If you use MO2 or Vortex, tell SynthEBD here so those files land in a mod folder your manager controls instead of loose in the game's Data folder. Green means the configured folder exists, red means it is set but missing, yellow means no manager is selected. Configure it in the Mod Manager Integration menu.",
            technical: "Reflects Settings_ModManager.ModManagerType and the configured target path (the MO2 mod folder or the Vortex staging folder). Generated output is installed under that path; the color shows whether the path currently exists on disk (green = Ok), is set but absent/blank (red = Error), or no manager is configured so output goes straight to the Data folder (yellow = Warning). The value is edited on the Mod Manager Integration tab and re-read whenever the Dashboard is shown.",
            motivation: "MO2 and Vortex do not track files written directly into the Data folder, so SynthEBD's output would be invisible to them and awkward to remove; pointing SynthEBD at the manager's mod/staging folder makes the generated files show up as a normal managed mod.");

        Add("Dashboard.AssetPatching",
            layperson: "Gives NPCs new looks (skin textures and meshes) from the config files you have installed. The switch turns the whole feature on or off; click the tile to choose config files and options.",
            technical: "Master switch for the Assets distribution axis (GeneralSettings.bChangeMeshesOrTextures). When enabled, the patcher flattens the selected asset packs and assigns texture/mesh combinations per NPC, written as WornArmor/HeadTexture overrides, FaceGen edits, EBD script data, or SkyPatcher directives depending on mode.",
            motivation: "Asset distribution is SynthEBD's core feature - the successor of zEBD's controlled randomization of NPC appearance. The switch exists so you can temporarily exclude assets from a patch run without uninstalling anything.");

        Add("Dashboard.BodyShape",
            layperson: "Gives NPCs varied body shapes. The switch turns body-shape assignment on or off, and the dropdown picks the system - BodyGen morphs or BodySlide presets (plus how BodySlide is applied). Click the tile to configure which shapes go to which NPCs.",
            technical: "The switch wraps GeneralSettings.BodySelectionMode (Off = None; On restores the chosen system). The inline dropdowns bind to LastBodySelectionMode (BodyGen or BodySlide) and, for BodySlide, BSSelectionMode (OBody / AutoBody); changing the system while the switch is on applies immediately, while off it just sets what the switch will restore. None is intentionally absent from the dropdown - the switch owns on/off - but remains in the enum for backwards compatibility. The tile opens the menu matching the selected system.",
            motivation: "Uniform bodies make NPCs feel cloned; distributing shapes adds variety. Both BodyGen and OBody/AutoBody ecosystems are widely used, so SynthEBD supports either as the body-shape backend - now selectable right on the tile instead of in General Settings.");

        Add("Dashboard.Headparts",
            layperson: "Randomizes NPC hair, eyes, brows, scars and similar head features. The switch turns the feature on or off.",
            technical: "Master switch for the head-part axis (GeneralSettings.bChangeHeadParts). Distributes head parts imported per category (hair, eyes, brows, facial hair, scars, misc), applied via Papyrus scripts or baked into FaceGen NIFs depending on the patching mode.",
            motivation: "Extends appearance variety beyond textures to the face itself. It is experimental for NPCs with custom face sculpts, which is why it ships as a separate toggle rather than part of asset patching.");

        Add("Dashboard.Height",
            layperson: "Gives NPCs slightly different heights instead of everyone being exactly the same size. The switch turns height randomization on or off.",
            technical: "Master switch for the height axis (GeneralSettings.bChangeHeight). Applies per-race height distributions from the selected height configuration file, optionally without overriding NPC records (SkyPatcher-style).",
            motivation: "Vanilla Skyrim gives every member of a race an identical scale; small height variation is a cheap, immersive diversity win.");

        Add("Dashboard.Destandalone",
            layperson: "Forces patched NPCs to use your own body meshes (the vanilla body path) instead of bodies shipped inside config files. Click the tile for details and options.",
            technical: "Mirror of TexMeshSettings.bForceVanillaBodyMeshPath: rewrites patched NPCs' worn-armor armature to the vanilla body mesh paths (with optional UBE preservation), via WornArmor overrides or SkyPatcher skin directives. Runs independently of Asset Patching; when both are active, config-assigned meshes are exempted from the rewrite.",
            motivation: "Config files ship pre-built standalone bodies so they work everywhere, but if you build your own BodySlide output you usually want YOUR body on every NPC - this 'de-standalones' the patch.");

        Add("Dashboard.SpecificAssignments",
            layperson: "Shows how many NPCs you have given hand-picked looks that override the random assignment. Click the tile to add or edit them.",
            technical: "Count of NPC records in Specific NPC Assignments.json. During patching these are consulted per NPC before random selection and force the chosen assets, body shape, height, or head parts.",
            motivation: "Randomization is the point of SynthEBD, but there are always a few NPCs you want to look exactly one way; specific assignments make those deterministic while everything else stays random.");

        Add("Dashboard.Consistency",
            layperson: "Remembers what each NPC got last time so they keep the same look between patcher runs. The switch turns this memory on or off.",
            technical: "Mirror of GeneralSettings.bEnableConsistency. When enabled, selectors first try each NPC's stored assignment from Consistency.json (seeded per NPC) before rolling new ones, so re-running the patcher preserves prior outcomes where still valid.",
            motivation: "Without consistency, every patch run reshuffles every NPC and mid-playthrough re-patches change familiar faces. Turn it off deliberately when you WANT a full reshuffle.");

        Add("Dashboard.BlockList",
            layperson: "Shows how many NPCs and mods you have excluded from patching. Click the tile to manage the exclusions.",
            technical: "Counts of BlockedNPC and BlockedPlugin entries in BlockList.json. Each entry can block individual axes (assets, body shape, height, head parts, vanilla body path) for one NPC or every NPC from a plugin.",
            motivation: "Some NPCs and follower mods ship carefully crafted appearances that patching would break; the block list carves them out without disabling whole features.");

        Add("Dashboard.PowerToggle",
            layperson: "Turns this module on or off for the next patcher run.",
            technical: "Bound two-way to the module's enable flag in the settings model - the same flag the detailed menus expose - so flipping it here and there is equivalent.",
            motivation: "Lets you see and control the whole patch plan from one screen without hunting through menus.");

        Add("Shell.UseMode",
            layperson: "Shows only what's needed to install content and run the patcher. Great when you just want results.",
            technical: "Sets the global UiDisplayMode to Use: menus collapse to browsing/installation controls; distribution rules and diagnostics are hidden (settings keep their values - only visibility changes).",
            motivation: "SynthEBD has a lot of depth most users never need; hiding it by default keeps the first-run experience approachable.");

        Add("Shell.CustomizeMode",
            layperson: "Also shows the customization options: distribution rules, groupings, and integrations.",
            technical: "Sets the global UiDisplayMode to Customize, revealing rule editors (races, attributes, linked NPCs), patching-mode selectors, and the Config Editor toolset.",
            motivation: "The middle level for users who author or tune distribution behavior without wading through diagnostic switches.");

        Add("Shell.TroubleshootMode",
            layperson: "Also shows diagnostic and compatibility switches. Don't change these unless you know what you're doing or have been instructed to.",
            technical: "Sets the global UiDisplayMode to Troubleshoot, revealing verbose logging modes, validation bypass, exclusion heuristics, and record-import controls.",
            motivation: "These switches solve rare problems but cause confusing behavior when flipped casually, so they sit behind an extra disclosure level with a one-time warning.");
    }
}

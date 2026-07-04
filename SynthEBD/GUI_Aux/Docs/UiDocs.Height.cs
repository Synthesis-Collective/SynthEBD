namespace SynthEBD;

// 3-part documentation for the Height Settings menu and the embedded height-configuration editor.
public static partial class UiDocs
{
    private static void RegisterHeight()
    {
        Add("Height.ChangeNpcHeights",
            layperson: "Gives each NPC a slightly randomized individual height. Turn it off to leave every NPC's personal height untouched (racial base heights can still be patched via the toggle below).",
            technical: "Mirror of HeightSettings.bChangeNPCHeight. When disabled, HeightPatcher.AssignNPCHeight returns without assigning anything, so no per-NPC height multiplier is rolled or written; racial height patching (Change Base Race Heights) is unaffected.",
            motivation: "Per-NPC variation is the core of the height axis, but users who only want to retune racial base heights - or who let another mod manage NPC scale - can switch it off without disabling the whole module.");

        Add("Height.ChangeRaceHeights",
            layperson: "Also updates each race's overall base height (male and female) to the values in the active height configuration. An NPC's final size is its race's base height multiplied by its personal height.",
            technical: "Mirror of HeightSettings.bChangeRaceHeight, gating HeightPatcher.AssignRacialHeight: RACE records listed in a height group receive overrides setting Height.Male/Height.Female to the group's base values, honoring Race Aliases and Patchable Races and skipping races whose values already match to avoid ITM records.",
            motivation: "In-game scale is racial base height times NPC height, so shifting a whole race taller or shorter requires editing the RACE record; this toggle exists for users who want individual variation without changing racial averages.");

        Add("Height.OverwriteNonDefault",
            layperson: "When checked, NPCs whose height was deliberately set to something other than the standard 1.0 get re-randomized like everyone else. Uncheck it to preserve those hand-set heights.",
            technical: "Mirror of HeightSettings.bOverwriteNonDefaultNPCHeights. When false, AssignNPCHeight skips any NPC whose record Height is not exactly 1, leaving the authored value in place.",
            motivation: "A non-default height is usually an intentional authoring choice - a towering chieftain or an unusually short character - and re-rolling it would undo that intent, so preserving such NPCs is offered as an option.");

        Add("Height.SkyPatcherMode",
            layperson: "Applies NPC heights through SkyPatcher configuration files instead of editing NPC records in the output plugin. Helps when patching would otherwise fail with a Too Many Masters error. Requires the SkyPatcher mod in your load order.",
            technical: "Mirror of HeightSettings.bApplyWithoutOverride. In this mode HeightPatcher.ApplyHeight emits a per-NPC 'height=' ini directive via SkyPatcherInterface instead of calling outputMod.Npcs.GetOrAddAsOverride and setting Height, so height patching adds no NPC overrides (and no extra masters) to the output plugin.",
            motivation: "Height-only NPC overrides can drag hundreds of plugins in as masters of the output patch; delegating the edit to SkyPatcher keeps the plugin lean and avoids the 255-master limit.");

        Add("Height.ActiveConfig",
            layperson: "Chooses which installed height configuration the patcher uses. Only one configuration is active at a time; its height groups are shown below for editing.",
            technical: "Selects among AvailableHeightConfigs (JSON files loaded from the Height Configurations settings folder) and persists by label as HeightSettings.SelectedHeightConfig; at run time the patcher resolves the config whose Label matches and feeds its HeightAssignments to the HeightPatcher.",
            motivation: "Height configurations are standalone, shareable JSON files, so several can be installed side by side; this dropdown decides which one actually drives a patcher run.");

        Add("Height.CreateConfig",
            layperson: "Starts a brand new, empty height configuration. Give it a name, add height groups, and it becomes selectable as the active configuration.",
            technical: "AddHeightConfig adds a fresh VM_HeightConfig (labeled 'New Height Configuration') to AvailableHeightConfigs and selects it; saving writes it as <label>.json into the Height Configurations settings folder via SettingsIO_Height.",
            motivation: "Shipped configurations cannot know about custom races or personal taste; creating configs in-app lets users author and share their own height distributions.");

        Add("Height.DeleteConfig",
            layperson: "Removes the currently selected height configuration from the list, and offers to delete its file from disk as well.",
            technical: "DeleteCurrentHeightConfig runs FileDialogs.ConfirmFileDeletion on the config's source path, then removes it from AvailableHeightConfigs and selects the first remaining configuration.",
            motivation: "Without cleanup, abandoned or experimental configurations pile up in the active-configuration list; deletion keeps the choice meaningful and the settings folder tidy.");

        Add("Height.GroupName",
            layperson: "A name for this height group, such as 'Humans' or 'Elves'. Purely a label to keep groups recognizable.",
            technical: "Sets HeightAssignment.Label. Display-only: it is persisted with the configuration and used in error messages (for example when a height value fails to parse), but plays no role in matching NPCs.",
            motivation: "A configuration usually contains several race groups; without labels they would only be distinguishable by their race lists.");

        Add("Height.GroupRaces",
            layperson: "The races this height group applies to. An NPC uses the first group that contains its race; races not covered by any group are left with their existing heights.",
            technical: "HeightAssignment.Races, a set of RACE FormKeys. HeightPatcher matches the NPC's height race (after Race Alias translation) with HeightAssignments.FirstOrDefault, and AssignRacialHeight uses the same lists to locate each RACE record's base values, so a race listed in two groups is only served by the first.",
            motivation: "Height is naturally race-scoped - elves trend taller than humans - and grouping races that share values avoids duplicating identical rows per race.");

        Add("Height.DistributionMode",
            layperson: "How random heights are spread inside the allowed range: 'uniform' makes every height in the range equally likely, while 'bellCurve' clusters NPCs near average with rare extremes.",
            technical: "HeightAssignment.DistributionMode (DistMode). Uniform draws uniformly within [1 - range, 1 + range]; bellCurve performs a Box-Muller normal draw with mean 1 and standard deviation range/3, clamped to the same bounds. The 'Set All Distribution Modes To' control applies one mode to every group at once.",
            motivation: "Real populations are bell-shaped - most people sit near average height - so bellCurve reads more natural, while uniform maximizes visible variety; the choice is left per group.");

        Add("Height.BaseHeight",
            layperson: "The overall body scale for this sex of these races, where 1.0 is standard size. This value is written to the race itself; individual NPCs then vary around it by the +/- range.",
            technical: "HeightAssignment.HeightMale / HeightFemale, parsed from the text box into a float on save. Applied by AssignRacialHeight as the RACE record's Height.Male / Height.Female override when Change Base Race Heights is enabled; the per-NPC multiplier is rolled independently around 1.0.",
            motivation: "Separating the racial baseline from individual variation lets a configuration make an entire race taller or shorter while the +/- range controls diversity within it.");

        Add("Height.HeightRange",
            layperson: "How much individual NPCs of this sex may deviate from standard: each rolls a personal height between 1 minus and 1 plus this value. The default 0.02 gives subtle, realistic variety.",
            technical: "HeightAssignment.HeightMaleRange / HeightFemaleRange. AssignNPCHeight bounds the random draw to [1 - range, 1 + range]; in bellCurve mode the range acts as three standard deviations. A consistency height outside the current bounds is discarded and re-rolled.",
            motivation: "The NPC height multiplies the racial base, so even small ranges are visible in-game; a bounded per-sex range breaks up identical crowds without producing comical giants or dwarfs.");
    }
}

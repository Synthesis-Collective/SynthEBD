using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Pre-pass for OBody/BodySlide body-shape patching: resolves race groupings into flat allowed/disallowed race
/// lists on BodySlide presets and descriptor rules, and exposes the NPC eligibility check. Run once at the start
/// of OBody patching.
/// </summary>
public class OBodyPreprocessing
{
    private readonly PatcherState _patcherState;
    public OBodyPreprocessing(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }
    /// <summary>
    /// Resolves the allowed/disallowed race groupings on each male and female BodySlide preset into flat race
    /// lists (merged with general-settings groupings). Mutates the settings.
    /// </summary>
    public void CompilePresetRaces(Settings_OBody oBodySettings)
    {
        foreach (var preset in oBodySettings.BodySlidesMale)
        {
            preset.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(preset.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, preset.AllowedRaces);
            preset.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(preset.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, preset.DisallowedRaces);
        }
        foreach (var preset in oBodySettings.BodySlidesFemale)
        {
            preset.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(preset.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, preset.AllowedRaces);
            preset.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(preset.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, preset.DisallowedRaces);
        }
    }

    /// <summary>
    /// Resolves the allowed/disallowed race groupings on each flattened template descriptor's associated rules
    /// into flat race lists. Mutates the settings.
    /// </summary>
    public void CompileRulesRaces(Settings_OBody oBodySettings)
    {
        foreach (var descriptor in oBodySettings.TemplateDescriptors.Flatten())
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }

    /// <summary>
    /// Returns false for NPCs that inherit traits from a template (their body shape comes from the template, so
    /// a BodySlide should not be assigned); true otherwise.
    /// </summary>
    public bool NPCIsEligibleForBodySlide(INpcGetter npc)
    {
        if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits) && npc.Template != null && !npc.Template.FormKey.IsNull)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
}
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class OBodyPreprocessing
{
    private readonly PatcherState _patcherState;
    public OBodyPreprocessing(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }
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

    public void CompileRulesRaces(Settings_OBody oBodySettings)
    {
        foreach (var descriptor in oBodySettings.TemplateDescriptors)
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }

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
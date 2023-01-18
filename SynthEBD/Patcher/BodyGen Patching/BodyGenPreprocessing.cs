using Mutagen.Bethesda.Synthesis;

namespace SynthEBD;

public class BodyGenPreprocessing
{
    private readonly PatcherState _patcherState;
    public BodyGenPreprocessing(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }

    /// <summary>
    /// Initializes the Compiled(Dis)AllowedRaces property in BodyGenConfigs by merging their AllowedRaces and AllowedRaceGroupings
    /// </summary>
    /// <param name="bodyGenConfigs"></param>
    public void CompileBodyGenRaces(BodyGenConfigs bodyGenConfigs)
    {
        foreach (var config in bodyGenConfigs.Male)
        {
            CompileBodyGenConfigRaces(config);
            CompileBodyGenDescriptorRaces(config);
        }
        foreach (var config in bodyGenConfigs.Female)
        {
            CompileBodyGenConfigRaces(config);
            CompileBodyGenDescriptorRaces(config);
        }
    }

    /// <summary>
    /// Initializes the Compiled(Dis)AllowedRaces property in BodyGenConfig classes by merging their AllowedRaces and AllowedRaceGroupings
    /// </summary>
    /// <param name="bodyGenConfig"></param>
    private void CompileBodyGenConfigRaces(BodyGenConfig bodyGenConfig)
    {
        var raceGroupings = GetRaceGroupings(bodyGenConfig);
        foreach (var template in bodyGenConfig.Templates)
        {
            template.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, raceGroupings, template.AllowedRaces);
            template.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, raceGroupings, template.DisallowedRaces);
        }
    }

    private void CompileBodyGenDescriptorRaces(BodyGenConfig bodyGenConfig)
    {
        var raceGroupings = GetRaceGroupings(bodyGenConfig);
        foreach (var descriptor in bodyGenConfig.TemplateDescriptors)
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, raceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, raceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }
    private List<RaceGrouping> GetRaceGroupings(BodyGenConfig config)
    {
        var output = new List<RaceGrouping>();

        var mainGroupingLabels = _patcherState.GeneralSettings.RaceGroupings.Select(x => x.Label);
        if (_patcherState.GeneralSettings.OverwritePluginRaceGroups)
        {
            var toOverwrite = new List<RaceGrouping>();
            foreach (var grouping in config.RaceGroupings.Where(x => mainGroupingLabels.Contains(x.Label)))
            {
                var overwriteGrouping = _patcherState.GeneralSettings.RaceGroupings.Where(x => x.Label == grouping.Label).First();
                output.Add(new RaceGrouping() { Label = overwriteGrouping.Label, Races = new(overwriteGrouping.Races) });
            }
        }

        foreach (var grouping in config.RaceGroupings)
        {
            if (!output.Select(x => x.Label).Contains(grouping.Label))
            {
                output.Add(grouping);
            }
        }
        return output;
    }
}
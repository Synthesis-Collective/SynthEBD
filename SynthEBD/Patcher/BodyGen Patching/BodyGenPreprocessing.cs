using Mutagen.Bethesda.Synthesis;

namespace SynthEBD;

/// <summary>
/// Pre-pass that prepares BodyGen configs before assignment: resolves race groupings into flat allowed/disallowed
/// race lists on templates and descriptor rules, and links each template back to its parent config. Run once at
/// the start of BodyGen body-shape patching.
/// </summary>
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

    /// <summary>
    /// Resolves the allowed/disallowed race groupings on each flattened template descriptor's associated rules
    /// into flat race lists, using the config's effective race groupings. Mutates the config.
    /// </summary>
    private void CompileBodyGenDescriptorRaces(BodyGenConfig bodyGenConfig)
    {
        var raceGroupings = GetRaceGroupings(bodyGenConfig);
        foreach (var descriptor in bodyGenConfig.TemplateDescriptors.Flatten())
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, raceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, raceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }
    /// <summary>
    /// Computes the effective race groupings for a config: when OverwritePluginRaceGroups is set, same-labeled
    /// groupings from general settings replace the config's own; then any remaining config-local groupings whose
    /// labels are not already present are appended. Returns the merged list.
    /// </summary>
    private List<RaceGrouping> GetRaceGroupings(BodyGenConfig config)
    {
        var output = new List<RaceGrouping>();

        var mainGroupingLabels = _patcherState.GeneralSettings.RaceGroupings.Select(x => x.Label).ToArray();
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

    /// <summary>
    /// Sets the ParentConfig back-reference on every template of every male and female BodyGen config.
    /// </summary>
    public void LinkTemplatesToParentConfigs(BodyGenConfigs bodyGenConfigs)
    {
        foreach (var cfg in bodyGenConfigs.Male)
        {
            LinkTemplatesToParentConfig(cfg);
        }
        foreach (var cfg in bodyGenConfigs.Female)
        {
            LinkTemplatesToParentConfig(cfg);
        }
    }

    /// <summary>
    /// Sets the ParentConfig back-reference on every template of a single BodyGen config.
    /// </summary>
    private void LinkTemplatesToParentConfig(BodyGenConfig bodyGenConfig)
    {
        foreach (var template in bodyGenConfig.Templates)
        {
            template.ParentConfig = bodyGenConfig;
        }
    }
}
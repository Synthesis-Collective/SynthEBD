namespace SynthEBD;

public class BodyGenPreprocessing
{
    /// <summary>
    /// Initializes the Compiled(Dis)AllowedRaces property in BodyGenConfigs by merging their AllowedRaces and AllowedRaceGroupings
    /// </summary>
    /// <param name="bodyGenConfigs"></param>
    public static void CompileBodyGenRaces(BodyGenConfigs bodyGenConfigs)
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
    private static void CompileBodyGenConfigRaces(BodyGenConfig bodyGenConfig)
    {
        foreach (var template in bodyGenConfig.Templates)
        {
            template.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, bodyGenConfig.RaceGroupings, template.AllowedRaces);
            template.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, bodyGenConfig.RaceGroupings, template.DisallowedRaces);
        }
    }

    private static void CompileBodyGenDescriptorRaces(BodyGenConfig bodyGenConfig)
    {
        foreach (var descriptor in bodyGenConfig.TemplateDescriptors)
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, bodyGenConfig.RaceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, bodyGenConfig.RaceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }
}
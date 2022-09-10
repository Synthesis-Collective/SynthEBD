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
            template.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, PatcherSettings.General.RaceGroupings, template.AllowedRaces);
            template.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, PatcherSettings.General.RaceGroupings, template.DisallowedRaces);
        }
    }

    private static void CompileBodyGenDescriptorRaces(BodyGenConfig bodyGenConfig)
    {
        foreach (var descriptor in bodyGenConfig.TemplateDescriptors)
        {
            descriptor.AssociatedRules.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.AllowedRaceGroupings, PatcherSettings.General.RaceGroupings, descriptor.AssociatedRules.AllowedRaces);
            descriptor.AssociatedRules.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(descriptor.AssociatedRules.DisallowedRaceGroupings, PatcherSettings.General.RaceGroupings, descriptor.AssociatedRules.DisallowedRaces);
        }
    }

    public static void FlattenGroupAttributes(BodyGenConfigs bodyGenConfigs)
    {
        foreach (var config in bodyGenConfigs.Male)
        {
            FlattenGroupSubAttributes(config);
        }
        foreach (var config in bodyGenConfigs.Female)
        {
            FlattenGroupSubAttributes(config);
        }
    }

    private static void FlattenGroupSubAttributes(BodyGenConfig bodyGenConfig)
    {
        foreach (var template in bodyGenConfig.Templates)
        {
            template.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(template.AllowedAttributes, bodyGenConfig.AttributeGroups);
            template.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(template.DisallowedAttributes, bodyGenConfig.AttributeGroups);
        }

        foreach (var descriptor in bodyGenConfig.TemplateDescriptors)
        {
            descriptor.AssociatedRules.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(descriptor.AssociatedRules.AllowedAttributes, bodyGenConfig.AttributeGroups);
            descriptor.AssociatedRules.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(descriptor.AssociatedRules.DisallowedAttributes, bodyGenConfig.AttributeGroups);
        }
    }
}
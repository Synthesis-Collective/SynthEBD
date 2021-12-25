using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
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
            }
            foreach (var config in bodyGenConfigs.Female)
            {
                CompileBodyGenConfigRaces(config);
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

        public static void FlattenGroupAttributes(BodyGenConfigs bodyGenConfigs)
        {
            foreach (var config in bodyGenConfigs.Male)
            {
                FlattenTemplateAttributes(config);
            }
            foreach (var config in bodyGenConfigs.Female)
            {
                FlattenTemplateAttributes(config);
            }
        }

        private static void FlattenTemplateAttributes(BodyGenConfig bodyGenConfig)
        {
            foreach (var template in bodyGenConfig.Templates)
            {
                template.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(template.AllowedAttributes, bodyGenConfig.AttributeGroups);
                template.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(template.DisallowedAttributes, bodyGenConfig.AttributeGroups);
            }
        }
    }
}

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
            foreach (var rule in bodyGenConfig.DescriptorRules)
            {
                rule.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(rule.AllowedRaceGroupings, PatcherSettings.General.RaceGroupings, rule.AllowedRaces);
            }
        }

        public static void ImplementDescriptorRules(BodyGenConfigs bodyGenConfigs)
        {
            foreach (var bodyGenConfig in bodyGenConfigs.Male)
            {
                ImplementDescriptorRules(bodyGenConfig);
            }
            foreach (var bodyGenConfig in bodyGenConfigs.Female)
            {
                ImplementDescriptorRules(bodyGenConfig);
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

            foreach (var rule in bodyGenConfig.DescriptorRules)
            {
                rule.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(rule.AllowedAttributes, bodyGenConfig.AttributeGroups);
                rule.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(rule.DisallowedAttributes, bodyGenConfig.AttributeGroups);
            }
        }

        private static void ImplementDescriptorRules(BodyGenConfig bodyGenConfig)
        {
            foreach (var template in bodyGenConfig.Templates)
            {
                foreach (var descriptor in template.BodyShapeDescriptors)
                {
                    var rules = bodyGenConfig.DescriptorRules.Where(x => x.DescriptorSignature == descriptor.Signature).FirstOrDefault();
                    if (rules != null)
                    {
                        InsertDescriptorRules(template, rules);
                    }
                }
            }
        }

        private static void InsertDescriptorRules(BodyGenConfig.BodyGenTemplate template, BodyShapeDescriptorRules rules)
        {
            // merge properties between current subgroup and rules
            if (rules.AllowRandom == false) { template.AllowRandom = false; }
            if (rules.AllowUnique == false) { template.AllowUnique = false; }
            if (rules.AllowNonUnique == false) { template.AllowNonUnique = false; }
            template.ProbabilityWeighting *= rules.ProbabilityWeighting;

            //handle DisallowedRaces first
            template.DisallowedRaces.UnionWith(rules.DisallowedRaces);

            // if both rules AllowedRaces and template AllowedRaces are not empty, get their intersection
            if (rules.AllowedRaces.Count > 0 && template.AllowedRaces.Count > 0)
            {
                template.AllowedRaces.IntersectWith(rules.AllowedRaces);
            }

            // now trim disallowedRaces from allowed
            if (template.AllowedRaces.Any())
            {
                template.AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(template.AllowedRaces, template.DisallowedRaces);
                // if there are now no more AllowedRaces, the current tempalate shouldn't be distributed
                if (template.AllowedRaces.Count == 0) 
                { 
                    template.AllowRandom = false;
                }
            }

            // Attribute Merging
            template.AllowedAttributes = NPCAttribute.InheritAttributes(rules.AllowedAttributes, template.AllowedAttributes);
            template.DisallowedAttributes = NPCAttribute.InheritAttributes(rules.DisallowedAttributes, template.DisallowedAttributes);

            // Weight Range
            if (rules.WeightRange.Lower > template.WeightRange.Lower) { template.WeightRange.Lower = rules.WeightRange.Lower; }
            if (rules.WeightRange.Upper < template.WeightRange.Upper) { template.WeightRange.Upper = rules.WeightRange.Upper; }
        }
    }
}

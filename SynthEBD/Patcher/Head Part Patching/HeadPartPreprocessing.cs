using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartPreprocessing
    {
        public static void FlattenGroupAttributes(Settings_Headparts headPartSettings)
        {
            foreach (var typeSetting in headPartSettings.Types.Values)
            {
                typeSetting.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(typeSetting.AllowedAttributes, PatcherSettings.General.AttributeGroups);
                typeSetting.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(typeSetting.DisallowedAttributes, PatcherSettings.General.AttributeGroups);

                foreach (var headpartSetting in typeSetting.HeadParts)
                {
                    headpartSetting.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(headpartSetting.AllowedAttributes, PatcherSettings.General.AttributeGroups);
                    headpartSetting.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(headpartSetting.DisallowedAttributes, PatcherSettings.General.AttributeGroups);
                }
            }
        }

        public static void CompilePresetRaces(Settings_Headparts headPartSettings)
        {
            foreach (var typeSetting in headPartSettings.Types.Values)
            {
                typeSetting.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(typeSetting.AllowedRaceGroupings, PatcherSettings.General.RaceGroupings, typeSetting.AllowedRaces);
                typeSetting.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(typeSetting.DisallowedRaceGroupings, PatcherSettings.General.RaceGroupings, typeSetting.DisallowedRaces);

                foreach (var headpartSetting in typeSetting.HeadParts)
                {
                    headpartSetting.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(headpartSetting.AllowedRaceGroupings, PatcherSettings.General.RaceGroupings, headpartSetting.AllowedRaces);
                    headpartSetting.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(headpartSetting.DisallowedRaceGroupings, PatcherSettings.General.RaceGroupings, headpartSetting.DisallowedRaces);
                }
            }
        }

        public static void ConvertBodyShapeDescriptorRules(Settings_Headparts headPartSettings)
        {
            foreach (var typeSetting in headPartSettings.Types.Values)
            {
                foreach (var headpartSetting in typeSetting.HeadParts)
                {
                    headpartSetting.AllowedBodySlideDescriptorDictionary = DictionaryMapper.BodyShapeDescriptorsToDictionary(headpartSetting.AllowedBodySlideDescriptors);
                    headpartSetting.DisallowedBodySlideDescriptorDictionary = DictionaryMapper.BodyShapeDescriptorsToDictionary(headpartSetting.DisallowedBodySlideDescriptors);
                }
            }
            
        }
    }
}

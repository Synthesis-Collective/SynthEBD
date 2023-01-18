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
        private readonly PatcherState _patcherState;
        public HeadPartPreprocessing(PatcherState patcherState)
        {
            _patcherState = patcherState;
        }
        public void CompilePresetRaces(Settings_Headparts headPartSettings)
        {
            foreach (var typeSetting in headPartSettings.Types.Values)
            {
                typeSetting.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(typeSetting.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, typeSetting.AllowedRaces);
                typeSetting.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(typeSetting.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, typeSetting.DisallowedRaces);

                foreach (var headpartSetting in typeSetting.HeadParts)
                {
                    headpartSetting.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(headpartSetting.AllowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, headpartSetting.AllowedRaces);
                    headpartSetting.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(headpartSetting.DisallowedRaceGroupings, _patcherState.GeneralSettings.RaceGroupings, headpartSetting.DisallowedRaces);
                }
            }
        }

        public void ConvertBodyShapeDescriptorRules(Settings_Headparts headPartSettings)
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

        public void CompileGenderedHeadParts(Settings_Headparts headPartSettings)
        {
            foreach (var typeSetting in headPartSettings.Types.Values)
            {
                typeSetting.HeadPartsGendered[Gender.Male].UnionWith(typeSetting.HeadParts.Where(x => x.bAllowMale).ToHashSet());
                typeSetting.HeadPartsGendered[Gender.Female].UnionWith(typeSetting.HeadParts.Where(x => x.bAllowFemale).ToHashSet());
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class OBodySelector
    {
        public static string SelectBodySlidePreset(NPCInfo npcInfo, out bool selectionMade, Settings_OBody oBodySettings, SubgroupCombination assetCombination, AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag statusFlags)
        {
            selectionMade = false;
            string selectedPreset = null;


            return selectedPreset;
        }

        public static bool CurrentNPCHasAvailablePresets(NPCInfo npcInfo, Settings_OBody oBodySettings)
        {
            List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
            switch (npcInfo.Gender)
            {
                case Gender.male: currentBodySlides = oBodySettings.BodySlidesMale; break;
                case Gender.female: currentBodySlides = oBodySettings.BodySlidesFemale; break;
            }

            if (!currentBodySlides.Any())
            {
                return false;
            }
            
            else
            {
                foreach (var slide in currentBodySlides)
                {
                    if ((!slide.AllowedRaces.Any() || slide.AllowedRaces.Contains(npcInfo.BodyShapeRace)) && !slide.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
                    {
                        return true;
                    }
                }
            }

            Logger.LogReport("No BodySlide presets are available for this NPC.", false, npcInfo);

            return false;
        }

    }
}

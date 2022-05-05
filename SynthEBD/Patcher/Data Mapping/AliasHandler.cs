using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class AliasHandler
    {
        public static FormKey GetAliasTexMesh(FormKey npcRaceFormKey)
        {
            var alias = PatcherSettings.General.RaceAliases.Where(x => x.bApplyToAssets && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

            if (!alias.IsNull)
            {
                return alias;
            }
            else
            {
                return npcRaceFormKey;
            }
        }

        public static FormKey GetAliasBodyGen(FormKey npcRaceFormKey)
        {
            var alias = PatcherSettings.General.RaceAliases.Where(x => x.bApplyToBodyGen && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

            if (!alias.IsNull)
            {
                return alias;
            }
            else
            {
                return npcRaceFormKey;
            }
        }

        public static FormKey GetAliasHeight(FormKey npcRaceFormKey)
        {
            var alias = PatcherSettings.General.RaceAliases.Where(x => x.bApplyToHeight && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

            if (!alias.IsNull)
            {
                return alias;
            }
            else
            {
                return npcRaceFormKey;
            }
        }
    }
}

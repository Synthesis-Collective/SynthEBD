using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class Converters
    {
        public static FormKey RaceEDID2FormKey(string EDID)
        {
            var env = new GameEnvironmentProvider().MyEnvironment;

            foreach (var plugin in env.LoadOrder.ListedOrder)
            {
                if (plugin.Mod != null && plugin.Mod.Races != null)
                {
                    foreach (var race in plugin.Mod.Races)
                    {
                        if (race.EditorID.ToLower() == EDID.ToLower())
                        {
                            return race.FormKey;
                        }
                    }
                }
            }

            return new FormKey();
        }

        public static HashSet<NPCAttribute> StringArraysToAttributes(List<string[]> arrList)
        {
            HashSet<NPCAttribute> h = new HashSet<NPCAttribute>();

            foreach (string[] arr in arrList)
            {
                NPCAttribute a = new NPCAttribute();
                a.Path = arr[0];
                a.Value = arr[1];
                h.Add(a);
            }

            return h;
        }

        public static NPCWeightRange StringArrayToWeightRange(string[] arr)
        {
            var weightRange = new NPCWeightRange();
            int tmpLower = 0;
            int tmpUpper = 100;
            int.TryParse(arr[0], out tmpLower); // (default zEBD value of null gets parsed as 0).

            if (arr[1] == null) // (default zEBD value of null gets parsed as 0, which is incorrect for .Upper).
            {
                tmpUpper = 100;
            }
            else
            {
                int.TryParse(arr[1], out tmpUpper);
            }
            weightRange.Lower = tmpLower;
            weightRange.Upper = tmpUpper;

            return weightRange;
        }

        public static BodyGenConfig.MorphDescriptor StringToMorphDescriptor(string s)
        {
            BodyGenConfig.MorphDescriptor newDescriptor = new BodyGenConfig.MorphDescriptor();
            try
            {
                string[] split = s.Split(':');
                newDescriptor.Category = split[0].Trim();
                newDescriptor.Value = split[1].Trim();
                newDescriptor.DispString = s;
            }
            catch
            {

            }
            return newDescriptor;
        }
    }
}

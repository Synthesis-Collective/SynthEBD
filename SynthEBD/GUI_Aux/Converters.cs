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
    }
}

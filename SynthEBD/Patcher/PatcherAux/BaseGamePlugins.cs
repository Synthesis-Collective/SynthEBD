using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Lookup table of the vanilla Skyrim master plugin file names (base game plus official DLCs),
    /// used to distinguish base-game records from mod-added content.
    /// </summary>
    public static class BaseGamePlugins
    {
        /// <summary>The file names of the base-game/DLC master plugins (Skyrim.esm, Update.esm, the three DLC ESMs).</summary>
        public static string[] Plugins = new string[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };
    }
}

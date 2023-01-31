using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Aspects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class NameHandler
    {
        public static string GetNPCNameSafely(INpcGetter getter, Logger logger)
        {
            try
            {
                return getter.Name?.String ?? string.Empty;
            }
            catch
            {
                logger.LogError("Error getting name of NPC: " + EditorIDHandler.GetEditorIDSafely(getter) + ". There may be an issue with its translation strings. Patching will continue.");
                return string.Empty;
            }
        }
    }
}

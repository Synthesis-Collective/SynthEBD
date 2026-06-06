using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Aspects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>Helpers for safely reading NPC identity fields that can throw on malformed data.</summary>
    public class NameHandler
    {
        /// <summary>
        /// Returns an NPC's display name, tolerating records whose localized name strings are
        /// missing or corrupt.
        /// </summary>
        /// <param name="getter">The NPC record to read.</param>
        /// <param name="logger">Logger used to report a non-fatal error if name resolution throws.</param>
        /// <returns>The NPC's name, or <see cref="string.Empty"/> when absent or unreadable.</returns>
        /// <remarks>
        /// Reading <c>Name.String</c> can throw when an NPC's STRINGS/translation data is broken; any
        /// exception is swallowed, logged (identifying the NPC by EditorID/FormKey), and treated as an
        /// empty name so patching can continue.
        /// </remarks>
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

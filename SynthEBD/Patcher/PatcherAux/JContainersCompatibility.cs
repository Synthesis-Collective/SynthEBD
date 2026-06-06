using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Extension helpers for encoding values so they are safe to use as JContainers JSON map keys.
    /// </summary>
    public static class JContainersCompatibility
    {
        /// <summary>
        /// Converts a <see cref="FormKey"/> to a string key safe for JContainers, which treats '.' as a
        /// path separator. Replaces the '.' between the ID and plugin name with '*'.
        /// </summary>
        /// <param name="formKey">The form key to encode.</param>
        /// <returns>The form key's string representation with '.' replaced by '*'.</returns>
        public static string ToJContainersCompatiblityKey(this FormKey formKey)
        {
            return formKey.ToString().Replace('.', '*');
        }
    }
}

using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public static class JContainersCompatibility
    {
        public static string ToJContainersCompatiblityKey(this FormKey formKey)
        {
            return formKey.ToString().Replace('.', '*');
        }
    }
}

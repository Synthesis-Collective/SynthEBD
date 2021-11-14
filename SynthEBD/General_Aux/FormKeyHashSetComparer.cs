using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class FormKeyHashSetComparer
    {
        public static bool Equals(HashSet<FormKey> a, HashSet<FormKey> b)
        {
            bool matched;
            if (a.Count != b.Count) { return false; }
            foreach (var keyA in a)
            {
                matched = false;
                foreach (var keyB in b)
                {
                    if (keyA.Equals(keyB))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) { return false; }
            }
            return true;
        }
    }
}

using Mutagen.Bethesda.Plugins;

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

        public static bool Contains (HashSet<FormKey> collection, FormKey toMatch)
        {
            foreach (var formkey in collection)
            {
                if (Equals(formkey, toMatch))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

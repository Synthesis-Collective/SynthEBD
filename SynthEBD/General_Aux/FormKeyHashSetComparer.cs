using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

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

    public static int ComparableSetHashCode(IEnumerable<FormKey> e)
    {
        bool first = true;
        int hashCode = 0;
        foreach (var item in e.OrderBy(x => x.ToString()).ToArray())
        {
            if (first)
            {
                first = false;
                hashCode = item.GetHashCode();
            }
            else
            {
                hashCode ^= item.GetHashCode();
            }
        }
        return hashCode;
    }
}

class ModKeyHashSetComparer
{
    public static bool Equals(HashSet<ModKey> a, HashSet<ModKey> b)
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

    public static bool Contains(HashSet<ModKey> collection, ModKey toMatch)
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

    public static int ComparableSetHashCode(IEnumerable<ModKey> e)
    {
        bool first = true;
        int hashCode = 0;
        foreach (var item in e.OrderBy(x => x.ToString()).ToArray())
        {
            if (first)
            {
                first = false;
                hashCode = item.GetHashCode();
            }
            else
            {
                hashCode ^= item.GetHashCode();
            }
        }
        return hashCode;
    }
}
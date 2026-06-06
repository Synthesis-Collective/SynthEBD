using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Set-comparison and order-independent hashing helpers for <see cref="HashSet{FormKey}"/>.
/// </summary>
class FormKeyHashSetComparer
{
    /// <summary>Determines whether two FormKey sets contain exactly the same keys.</summary>
    /// <param name="a">First set.</param>
    /// <param name="b">Second set.</param>
    /// <returns><c>true</c> if both sets have equal counts and every key in <paramref name="a"/> appears in <paramref name="b"/>.</returns>
    /// <remarks>Uses an O(n²) nested loop; <see cref="HashSet{T}.SetEquals"/> is an equivalent O(n) replacement.</remarks>
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

    /// <summary>Determines whether <paramref name="collection"/> contains <paramref name="toMatch"/>.</summary>
    /// <param name="collection">Set to search.</param>
    /// <param name="toMatch">Key to look for.</param>
    /// <returns><c>true</c> if the key is present.</returns>
    /// <remarks>
    /// The inner <c>Equals(formkey, toMatch)</c> resolves to <see cref="object.Equals(object, object)"/>
    /// (a value comparison of two FormKeys), <em>not</em> this class's set-equality overload, so the
    /// method is equivalent to <see cref="HashSet{T}.Contains"/>.
    /// </remarks>
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

    /// <summary>Computes an order-independent hash for a collection of FormKeys.</summary>
    /// <param name="e">Keys to hash.</param>
    /// <returns>A hash identical for any two collections holding the same set of keys.</returns>
    /// <remarks>
    /// XOR is already commutative, so the <c>OrderBy</c> and first-element special case do not affect the
    /// result. XOR-folding cancels duplicate pairs to zero — harmless for a duplicate-free set.
    /// </remarks>
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

/// <summary>
/// Set-comparison and order-independent hashing helpers for <see cref="HashSet{ModKey}"/>.
/// A near-verbatim duplicate of <see cref="FormKeyHashSetComparer"/> specialized for ModKey.
/// </summary>
class ModKeyHashSetComparer
{
    /// <summary>Determines whether two ModKey sets contain exactly the same keys.</summary>
    /// <param name="a">First set.</param>
    /// <param name="b">Second set.</param>
    /// <returns><c>true</c> if both sets have equal counts and every key in <paramref name="a"/> appears in <paramref name="b"/>.</returns>
    /// <remarks>Uses an O(n²) nested loop; <see cref="HashSet{T}.SetEquals"/> is an equivalent O(n) replacement.</remarks>
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

    /// <summary>Determines whether <paramref name="collection"/> contains <paramref name="toMatch"/>.</summary>
    /// <param name="collection">Set to search.</param>
    /// <param name="toMatch">Key to look for.</param>
    /// <returns><c>true</c> if the key is present.</returns>
    /// <remarks>
    /// As with <see cref="FormKeyHashSetComparer.Contains"/>, the inner <c>Equals</c> resolves to
    /// <see cref="object.Equals(object, object)"/>, making this equivalent to <see cref="HashSet{T}.Contains"/>.
    /// </remarks>
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

    /// <summary>Computes an order-independent hash for a collection of ModKeys.</summary>
    /// <param name="e">Keys to hash.</param>
    /// <returns>A hash identical for any two collections holding the same set of keys.</returns>
    /// <remarks>
    /// XOR is already commutative, so the <c>OrderBy</c> and first-element special case do not affect the
    /// result. XOR-folding cancels duplicate pairs to zero — harmless for a duplicate-free set.
    /// </remarks>
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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    //https://stackoverflow.com/questions/3973137/order-a-observablecollectiont-without-creating-a-new-one
    /// <summary>Extension methods that sort an existing <see cref="ObservableCollection{T}"/> in place
    /// (without replacing the instance) so bindings to the collection are preserved.</summary>
    public static class ObservableCollection
    {
        /// <summary>Reorders <paramref name="source"/> in place by <paramref name="keySelector"/> (ascending,
        /// or descending when <paramref name="reverse"/> is <c>true</c>) by clearing and re-adding items.</summary>
        public static void Sort<TSource, TKey>(this ObservableCollection<TSource> source, Func<TSource, TKey> keySelector, bool reverse)
        {
            List<TSource> sortedList = source.OrderBy(keySelector).ToList();
            if (reverse)
            {
                sortedList.Reverse();
            }
            source.Clear();
            foreach (var sortedItem in sortedList)
            {
                source.Add(sortedItem);
            }
        }

        /// <summary>Returns whether <paramref name="source"/> is already in the order that
        /// <see cref="Sort"/> would produce for the same key selector and direction.</summary>
        public static bool IsSorted<TSource, TKey>(this ObservableCollection<TSource> source, Func<TSource, TKey> keySelector, bool reverse)
        {
            List<TSource> sortedList = source.OrderBy(keySelector).ToList();
            if (reverse)
            {
                sortedList.Reverse();
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!source[i].Equals(sortedList[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

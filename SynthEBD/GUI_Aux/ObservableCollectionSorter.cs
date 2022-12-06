using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    //https://stackoverflow.com/questions/3973137/order-a-observablecollectiont-without-creating-a-new-one
    public static class ObservableCollection
    {
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>Splits a dictionary into a list of smaller dictionaries, each holding at most a fixed number of entries.</summary>
    /// <typeparam name="T">Key type (must be non-nullable).</typeparam>
    /// <typeparam name="U">Value type.</typeparam>
    public class DictionarySplitter<T,U> where T : notnull
    {
        /// <summary>
        /// Partitions <paramref name="input"/> into contiguous chunks of up to
        /// <paramref name="maxKeyCount"/> entries each.
        /// </summary>
        /// <param name="input">Dictionary to split; its enumeration order determines chunk boundaries.</param>
        /// <param name="maxKeyCount">Maximum number of entries per output dictionary.</param>
        /// <returns>
        /// A list of dictionaries that together contain every entry of <paramref name="input"/>; the final
        /// chunk may be smaller than <paramref name="maxKeyCount"/>. Empty input yields an empty list.
        /// </returns>
        /// <remarks>Used to batch large lookups (e.g. SkyPatcher/Papyrus output) under a per-file size cap.</remarks>
        public static List<Dictionary<T, U>> SplitDictionary(Dictionary<T, U> input, int maxKeyCount)
        {
            return input
                .Chunk(maxKeyCount)
                .Select(chunk => chunk.ToDictionary(entry => entry.Key, entry => entry.Value))
                .ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class DictionarySplitter<T,U> where T : notnull
    {
        public static List<Dictionary<T, U>> SplitDictionary(Dictionary<T, U> input, int maxKeyCount) 
        {
            List<Dictionary<T, U>> output = new();

            int keyCountSegmented = 0;
            int keyCountTotal = 0;
            var currentDict = new Dictionary<T, U>();
            foreach (var entry in input)
            {
                keyCountSegmented++;
                keyCountTotal++;

                currentDict.Add(entry.Key, entry.Value);

                if (keyCountSegmented == maxKeyCount || keyCountTotal == input.Count)
                {
                    output.Add(currentDict);
                    currentDict = new();
                    keyCountSegmented = 0;
                }
            }

            return output;
        }
    }
}

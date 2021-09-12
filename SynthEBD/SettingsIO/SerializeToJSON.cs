using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SerializeToJSON<T>
    {
        public static void SaveJSONFile(T input, string relativeLoc)
        {
            string saveLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), relativeLoc);

            string json = JsonConvert.SerializeObject(input, Formatting.Indented);

            File.WriteAllText(saveLoc, json);
        }
    }
}

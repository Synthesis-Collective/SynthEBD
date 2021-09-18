using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Json;
using Newtonsoft.Json;

namespace SynthEBD
{
    class DeserializeFromJSON<T>
    {
        public static T loadJSONFile(string loadLoc)
        {
            var jsonSettings = new JsonSerializerSettings();
            jsonSettings.AddMutagenConverters();
            jsonSettings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            jsonSettings.Formatting = Formatting.Indented;

            string text = File.ReadAllText(loadLoc);
            return JsonConvert.DeserializeObject<T>(text, jsonSettings);
        }
    }
}

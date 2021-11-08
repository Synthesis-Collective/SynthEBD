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
            jsonSettings.TypeNameHandling = TypeNameHandling.Auto; // required for interfaces | Replace with this in future for security reasons: https://blog.codeinside.eu/2015/03/30/json-dotnet-deserialize-to-abstract-class-or-interface/

            string text = File.ReadAllText(loadLoc);
            return JsonConvert.DeserializeObject<T>(text, jsonSettings);
        }
    }
}

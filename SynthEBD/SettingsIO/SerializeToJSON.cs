using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Json;


namespace SynthEBD
{
    class SerializeToJSON<T>
    {
        public static void SaveJSONFile(T input, string saveLoc)
        {
            var jsonSettings = new JsonSerializerSettings();
            jsonSettings.AddMutagenConverters();
            jsonSettings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            jsonSettings.Formatting = Formatting.Indented;
            jsonSettings.TypeNameHandling = TypeNameHandling.Auto; // required for interfaces | Replace with this in future for security reasons: https://blog.codeinside.eu/2015/03/30/json-dotnet-deserialize-to-abstract-class-or-interface/

            string jsonString = JsonConvert.SerializeObject(input, Formatting.Indented, jsonSettings);
            File.WriteAllText(saveLoc, jsonString);
        }
    }
}

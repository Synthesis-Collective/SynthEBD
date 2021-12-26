using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Mutagen.Bethesda.Json;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SynthEBD
{
    public class JSONhandler<T>
    {
        public static JsonSerializerSettings GetSynthEBDJSONSettings()
        {
            var jsonSettings = new JsonSerializerSettings();
            jsonSettings.AddMutagenConverters();
            jsonSettings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            jsonSettings.Formatting = Formatting.Indented;
            jsonSettings.Converters.Add(new AttributeConverter()); // https://blog.codeinside.eu/2015/03/30/json-dotnet-deserialize-to-abstract-class-or-interface/
            jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()); // https://stackoverflow.com/questions/2441290/javascriptserializer-json-serialization-of-enum-as-string

            return jsonSettings;
        }

        public static T Deserialize(string jsonInputStr)
        {
            return JsonConvert.DeserializeObject<T>(jsonInputStr, GetSynthEBDJSONSettings());
        }

        public static T loadJSONFile(string loadLoc)
        {
            return Deserialize(File.ReadAllText(loadLoc));
        }

        public static string Serialize(T input)
        {
            return JsonConvert.SerializeObject(input, Formatting.Indented, GetSynthEBDJSONSettings());
        }

        public static void SaveJSONFile(T input, string saveLoc)
        {
            File.WriteAllText(saveLoc, Serialize(input));
        }


        private class AttributeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return (objectType == typeof(ITypedNPCAttribute));
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JObject jo = JObject.Load(reader);

                switch (jo["Type"].Value<string>())
                {
                    case "Class": return jo.ToObject<NPCAttributeClass>(serializer);
                    case "Custom": return jo.ToObject<NPCAttributeCustom>(serializer);
                    case "FaceTexture": return jo.ToObject<NPCAttributeFaceTexture>(serializer);
                    case "Faction": return jo.ToObject<NPCAttributeFactions>(serializer);
                    case "Group": return jo.ToObject<NPCAttributeGroup>(serializer);
                    case "NPC": return jo.ToObject<NPCAttributeNPC>(serializer);
                    case "Race": return jo.ToObject<NPCAttributeRace>(serializer);
                    case "VoiceType": return jo.ToObject<NPCAttributeVoiceType>(serializer);
                    default: return null;
                }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }
    }

}

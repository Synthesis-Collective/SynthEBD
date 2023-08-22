using Newtonsoft.Json;
using Mutagen.Bethesda.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class JSONhandler<T>
{
    public static JsonSerializerSettings GetSynthEBDJSONSettings()
    {
        var jsonSettings = new JsonSerializerSettings();
        jsonSettings.AddMutagenConverters();
        jsonSettings.ObjectCreationHandling = ObjectCreationHandling.Replace;
        jsonSettings.Formatting = Formatting.Indented;
        jsonSettings.Converters.Add(new AttributeConverter()); // https://blog.codeinside.eu/2015/03/30/json-dotnet-deserialize-to-abstract-class-or-interface/
        jsonSettings.Converters.Add(new AggressionBackwardCompatibility()); // Thanks ChatGPT!
        jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()); // https://stackoverflow.com/questions/2441290/javascriptserializer-json-serialization-of-enum-as-string

        return jsonSettings;
    }

    public static T Deserialize(string jsonInputStr, out bool success, out string exception)
    {
        try
        {
            success = true;
            exception = "";
            return JsonConvert.DeserializeObject<T>(SettingsUpgrader.UpgradeDeprecatedSettings(jsonInputStr), GetSynthEBDJSONSettings());
        }
        catch (Exception ex)
        {
            success = false;
            exception = ExceptionLogger.GetExceptionStack(ex);
            return default(T);
        }
    }

    public static T LoadJSONFile(string loadLoc, out bool success, out string exception)
    {
        return Deserialize(File.ReadAllText(loadLoc), out success, out exception);
    }

    public static string Serialize(T input, out bool success, out string exception)
    {
        try
        {
            success = true;
            exception = "";
            return JsonConvert.SerializeObject(input, Formatting.Indented, GetSynthEBDJSONSettings());
        }
        catch (Exception ex)
        {
            exception = ex.Message;
            success = false;
            return "";
        }
    }

    public static void SaveJSONFile(T input, string saveLoc, out bool success, out string exception)
    {
        try
        {
            PatcherIO.CreateDirectoryIfNeeded(saveLoc, PatcherIO.PathType.File);
            File.WriteAllText(saveLoc, Serialize(input, out success, out exception));
        }
        catch(Exception ex)
        {
            exception = ex.Message;
            success = false;
        }
    }

    public static T CloneViaJSON(T input)
    {
        return Deserialize(Serialize(input, out _, out _), out _, out _);
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
                case "Keyword": return jo.ToObject<NPCAttributeKeyword>(serializer);
                case "Misc": return jo.ToObject<NPCAttributeMisc>(serializer);
                case "Mod": return jo.ToObject<NPCAttributeMod>(serializer);
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

    public class AggressionBackwardCompatibility : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Aggression);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                string value = reader.Value.ToString();

                // Handle the old "Unagressive" spelling
                if (value.Equals("Unagressive", StringComparison.OrdinalIgnoreCase))
                {
                    return Aggression.Unaggressive;
                }

                // Handle other enum values
                return Enum.Parse(typeof(Aggression), value, ignoreCase: true);
            }

            throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Aggression aggression)
            {
                // Write the enum value as a string
                writer.WriteValue(aggression.ToString());
            }
            else
            {
                throw new JsonSerializationException("Unexpected object type");
            }
        }
    }
}
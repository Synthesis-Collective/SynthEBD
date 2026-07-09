using Newtonsoft.Json;
using Mutagen.Bethesda.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Generic Newtonsoft.Json load/save wrapper for SynthEBD settings models of type <typeparamref name="T"/>.
/// Centralizes the shared serializer configuration (Mutagen converters, indented formatting, string enums,
/// and SynthEBD's custom converters) and provides file-level load/save plus a JSON-based deep clone. All
/// methods are static; the type parameter selects the target model. Deserialization first runs the raw text
/// through <see cref="SettingsUpgrader.UpgradeDeprecatedSettings"/> to migrate legacy keys.
/// </summary>
/// <typeparam name="T">The settings model type to serialize/deserialize.</typeparam>
public class JSONhandler<T>
{
    /// <summary>
    /// Builds the shared <see cref="JsonSerializerSettings"/> used for all SynthEBD serialization:
    /// Mutagen converters, <see cref="ObjectCreationHandling.Replace"/>, indented formatting, the
    /// abstract-attribute converter, the legacy <see cref="Aggression"/> spelling shim, and string-enum output.
    /// </summary>
    /// <returns>A fresh configured <see cref="JsonSerializerSettings"/> instance.</returns>
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

    /// <summary>
    /// Deserializes <paramref name="jsonInputStr"/> into <typeparamref name="T"/>, first applying
    /// deprecated-settings upgrades. Never throws; failures are reported via the out parameters.
    /// </summary>
    /// <param name="jsonInputStr">Raw JSON text.</param>
    /// <param name="success">Set true on success, false on parse failure.</param>
    /// <param name="exception">Empty on success; the captured exception stack on failure.</param>
    /// <returns>The deserialized object, or <c>default(T)</c> on failure.</returns>
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

    /// <summary>
    /// Reads the file at <paramref name="loadLoc"/> and deserializes it into <typeparamref name="T"/>.
    /// Reads from disk. Reports a missing file, an empty/whitespace file, or a parse error via the out
    /// parameters rather than throwing.
    /// </summary>
    /// <param name="loadLoc">Absolute path to the JSON file.</param>
    /// <param name="success">Set true on success, false if the file is missing, empty, or unparseable.</param>
    /// <param name="exception">Empty on success; an explanatory message on failure.</param>
    /// <returns>The deserialized object, or <c>default(T)</c> on failure.</returns>
    public static T LoadJSONFile(string loadLoc, out bool success, out string exception)
    {
        if (!File.Exists(loadLoc))
        {
            success = false;
            exception = "File " + loadLoc + " does not exist.";
            return default(T);
        }

        string contents = String.Empty;

        try
        {
            contents = File.ReadAllText(loadLoc);
        }
        catch (Exception ex)
        {
            success = false;
            exception = ExceptionLogger.GetExceptionStack(ex);
            return default(T);
        }

        if (contents == null || contents.IsNullOrWhitespace())
        {
            success = false;
            exception = "File " + loadLoc + " is empty.";
            return default(T);
        }

        return Deserialize(contents, out success, out exception);
    }

    /// <summary>
    /// Serializes <paramref name="input"/> to indented JSON using the shared settings. Never throws;
    /// failures are reported via the out parameters.
    /// </summary>
    /// <param name="input">The object to serialize.</param>
    /// <param name="success">Set true on success, false on serialization failure.</param>
    /// <param name="exception">Empty on success; the exception message on failure.</param>
    /// <returns>The JSON string, or an empty string on failure.</returns>
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

    /// <summary>
    /// Serializes <paramref name="input"/> and writes it to <paramref name="saveLoc"/>, creating the
    /// containing directory if needed. Writes to disk. Never throws; failures are reported via the out
    /// parameters.
    /// </summary>
    /// <param name="input">The object to save.</param>
    /// <param name="saveLoc">Absolute path of the file to write.</param>
    /// <param name="success">Set true on success, false on serialization or write failure.</param>
    /// <param name="exception">Empty on success; an explanatory message on failure.</param>
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

    /// <summary>
    /// Produces a deep copy of <paramref name="input"/> by serializing it to JSON and deserializing back.
    /// Convenience helper; swallows success/error flags.
    /// </summary>
    /// <param name="input">The object to clone.</param>
    /// <returns>A deep clone, or <c>default(T)</c> if the round-trip fails.</returns>
    public static T CloneViaJSON(T input)
    {
        return Deserialize(Serialize(input, out _, out _), out _, out _);
    }

    /// <summary>
    /// Read-only Newtonsoft converter that materializes the correct concrete <see cref="ITypedNPCAttribute"/>
    /// subclass based on the JSON "Type" discriminator. Writing is not supported.
    /// </summary>
    private class AttributeConverter : JsonConverter
    {
        /// <summary>Returns true only for <see cref="ITypedNPCAttribute"/>.</summary>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(ITypedNPCAttribute));
        }

        /// <summary>
        /// Reads the JSON object, switches on its "Type" field, and deserializes into the matching
        /// concrete NPC attribute type.
        /// </summary>
        /// <returns>The concrete attribute instance, or null if the type is unrecognized.</returns>
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
                case "SubExpression": return jo.ToObject<NPCAttributeSubExpression>(serializer);
                case "VoiceType": return jo.ToObject<NPCAttributeVoiceType>(serializer);
                default: return null;
            }
        }

        /// <summary>Always false: this converter only reads.</summary>
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <summary>Not supported; throws <see cref="NotImplementedException"/>.</summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Newtonsoft converter for the <see cref="Aggression"/> enum that tolerates the legacy misspelling
    /// "Unagressive" when reading older settings and writes the value back as its string name.
    /// </summary>
    public class AggressionBackwardCompatibility : JsonConverter
    {
        /// <summary>Returns true only for <see cref="Aggression"/>.</summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Aggression);
        }

        /// <summary>
        /// Parses a string token into <see cref="Aggression"/>, mapping the old "Unagressive" spelling to
        /// <see cref="Aggression.Unaggressive"/>.
        /// </summary>
        /// <returns>The parsed <see cref="Aggression"/> value.</returns>
        /// <exception cref="JsonSerializationException">Thrown for non-string tokens.</exception>
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

        /// <summary>Writes an <see cref="Aggression"/> value as its string name.</summary>
        /// <exception cref="JsonSerializationException">Thrown if <paramref name="value"/> is not an <see cref="Aggression"/>.</exception>
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
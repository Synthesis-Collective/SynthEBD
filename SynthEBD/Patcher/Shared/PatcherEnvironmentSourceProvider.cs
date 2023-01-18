using DynamicData;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class PatcherEnvironmentSourceProvider
    {
        public Lazy<StandaloneEnvironmentSource> EnvironmentSource { get; }
        public static StringBuilder SettingsLog { get; } = new();
        public string ErrorString;
        public string SourcePath { get; set; } // where to read source from, and save it to

        public PatcherEnvironmentSourceProvider(string sourcePath)
        {
            SourcePath = sourcePath;
            EnvironmentSource = new Lazy<StandaloneEnvironmentSource>(() =>
            {
                if (File.Exists(sourcePath))
                {
                    SettingsLog.AppendLine("Found environment source path at " + sourcePath);

                    var source = JSONhandler<StandaloneEnvironmentSource>.LoadJSONFile(sourcePath, out bool loadSuccess,
                        out string exceptionStr);
                    if (loadSuccess)
                    {
                        SettingsLog.AppendLine("Source Settings: ");
                        SettingsLog.AppendLine("Skyrim Version: " + source.SkyrimVersion);
                        SettingsLog.AppendLine("Game Environment Directory: " + source.GameEnvironmentDirectory);
                        return source;
                    }
                    else
                    {
                        SettingsLog.AppendLine("Could not load Settings Source. Error: " + exceptionStr);
                        ErrorString = "Could not load Settings Source. Error: " + exceptionStr;
                        return new();
                    }
                }
                else
                {
                    SettingsLog.AppendLine("Did not find settings source path at " + sourcePath);
                    SettingsLog.AppendLine("Using default environment and patcher settings locations.");
                    return new();
                }
            });
        }
    }
}

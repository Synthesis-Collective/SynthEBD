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
    /// <summary>
    /// Lazily loads the standalone game-environment source (Skyrim version and game directory) from a JSON
    /// file at a given path, falling back to a default <see cref="StandaloneEnvironmentSource"/> when the
    /// file is missing or fails to load. Load progress and errors are appended to <see cref="SettingsLog"/>.
    /// </summary>
    public class PatcherEnvironmentSourceProvider
    {
        /// <summary>Lazily resolved environment source; evaluated on first access from the configured path.</summary>
        public Lazy<StandaloneEnvironmentSource> EnvironmentSource { get; }
        /// <summary>Shared log accumulating environment-source load diagnostics.</summary>
        public static StringBuilder SettingsLog { get; } = new();
        /// <summary>Set to a human-readable error message if the source file existed but failed to load.</summary>
        public string ErrorString;
        public string SourcePath { get; set; } // where to read source from, and save it to

        /// <summary>
        /// Captures <paramref name="sourcePath"/> and sets up the lazy loader for the environment source.
        /// </summary>
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

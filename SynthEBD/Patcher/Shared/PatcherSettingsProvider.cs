using System.IO;

namespace SynthEBD;

public class PatcherSettingsProvider
{
    public Lazy<LoadSource> SourceSettings { get; }
    public static string SettingsLog { get; set; } = string.Empty;

    public PatcherSettingsProvider()
    {
        SourceSettings = new Lazy<LoadSource>(() =>
        {
            if (File.Exists(Paths.SettingsSourcePath))
            {
                SettingsLog += "Found settings source path at " + Paths.SettingsSourcePath + Environment.NewLine;

                var source = JSONhandler<LoadSource>.LoadJSONFile(Paths.SettingsSourcePath, out bool loadSuccess,
                    out string exceptionStr);
                if (loadSuccess)
                {
                    SettingsLog += "Source Settings: " + Environment.NewLine +
                    "Skyrim Version: " + source.SkyrimVersion + Environment.NewLine +
                    "Game Environment Directory: " + source.GameEnvironmentDirectory + Environment.NewLine +
                    "Load Settings from Portable Folder: " + source.LoadFromDataDir + Environment.NewLine +
                    "Portable Folder Location: " + source.PortableSettingsFolder;
                    source.Initialized = true;
                    return source;
                }
                else
                {
                    SettingsLog += "Could not load Settings Source. Error: " + exceptionStr;
                    Logger.LogError("Could not load Settings Source. Error: " + exceptionStr);
                    return new();
                }
            }
            else
            {
                SettingsLog += "Did not find settings source path at " + Paths.SettingsSourcePath + Environment.NewLine + "Using default environment and patcher settings locations.";
                return new();
            }  
        });
    }

    public void SetNewDataDir(string newDir)
    {
        SourceSettings.Value.PortableSettingsFolder = newDir;
        if (string.IsNullOrWhiteSpace(newDir))
        {
            SourceSettings.Value.LoadFromDataDir = false;
        }
        else
        {
            SourceSettings.Value.LoadFromDataDir = true;
        }
    }
}
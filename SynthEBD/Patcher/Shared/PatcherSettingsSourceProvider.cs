using System.IO;
using System.Text;

namespace SynthEBD;

public class PatcherSettingsSourceProvider
{
    public Lazy<LoadSource> SourceSettings { get; }
    public static StringBuilder SettingsLog { get; } = new();
    public string ErrorString;

    public PatcherSettingsSourceProvider()
    {
        SourceSettings = new Lazy<LoadSource>(() =>
        {
            if (File.Exists(SynthEBDPaths.SettingsSourcePath))
            {
                SettingsLog.AppendLine("Found settings source path at " + SynthEBDPaths.SettingsSourcePath);

                var source = JSONhandler<LoadSource>.LoadJSONFile(SynthEBDPaths.SettingsSourcePath, out bool loadSuccess,
                    out string exceptionStr);
                if (loadSuccess)
                {
                    SettingsLog.AppendLine("Source Settings: ");
                    SettingsLog.AppendLine("Skyrim Version: " + source.SkyrimVersion);
                    SettingsLog.AppendLine("Game Environment Directory: " + source.GameEnvironmentDirectory);
                    SettingsLog.AppendLine("Load Settings from Portable Folder: " + source.LoadFromDataDir);
                    SettingsLog.AppendLine("Portable Folder Location: " + source.PortableSettingsFolder);
                    source.Initialized = true;
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
                SettingsLog.AppendLine("Did not find settings source path at " + SynthEBDPaths.SettingsSourcePath);
                SettingsLog.AppendLine("Using default environment and patcher settings locations.");
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
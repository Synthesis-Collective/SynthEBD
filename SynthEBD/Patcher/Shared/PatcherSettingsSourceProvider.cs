using System.IO;
using System.Text;

namespace SynthEBD;

public class PatcherSettingsSourceProvider
{
    public Lazy<PatcherSettingsSource> SettingsSource { get; }
    public static StringBuilder SettingsLog { get; } = new();
    public string ErrorString;
    public string SourcePath { get; set; }

    public PatcherSettingsSourceProvider(string sourcePath)
    {
        SourcePath = sourcePath;
        SettingsSource = new Lazy<PatcherSettingsSource>(() =>
        {
            if (File.Exists(sourcePath))
            {
                SettingsLog.AppendLine("Found settings source path at " + sourcePath);

                var source = JSONhandler<PatcherSettingsSource>.LoadJSONFile(sourcePath, out bool loadSuccess,
                    out string exceptionStr);
                if (loadSuccess)
                {
                    SettingsLog.AppendLine("Source Settings: ");;
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
                SettingsLog.AppendLine("Did not find settings source path at " + sourcePath);
                SettingsLog.AppendLine("Using default environment and patcher settings locations.");
                return new();
            }  
        });
    }

    public void SetNewDataDir(string newDir)
    {
        SettingsSource.Value.PortableSettingsFolder = newDir;
        if (string.IsNullOrWhiteSpace(newDir))
        {
            SettingsSource.Value.LoadFromDataDir = false;
        }
        else
        {
            SettingsSource.Value.LoadFromDataDir = true;
        }
    }
}
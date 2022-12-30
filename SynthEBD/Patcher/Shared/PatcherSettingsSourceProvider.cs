using System.IO;
using System.Text;

namespace SynthEBD;

public class PatcherSettingsSourceProvider
{
    public Lazy<PatcherSettingsSource> SettingsSource { get; }
    public static StringBuilder SettingsLog { get; } = new();
    public string ErrorString;
    public string SourcePath { get; set; } // where to read source from, and save it to
    public string SettingsRootPath { get; set; } // Where the SynthEBDPaths class should look for its settings files

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
    public void SaveSettingsSource(VM_Settings_General generalSettings, out bool saveSuccess, out string exceptionStr)
    {
        PatcherSettingsSource source = new PatcherSettingsSource()
        {
            LoadFromDataDir = generalSettings.bLoadSettingsFromDataFolder,
            PortableSettingsFolder = generalSettings.PortableSettingsFolder,
            Initialized = true,
        };
        JSONhandler<PatcherSettingsSource>.SaveJSONFile(source, SourcePath, out saveSuccess, out exceptionStr);
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
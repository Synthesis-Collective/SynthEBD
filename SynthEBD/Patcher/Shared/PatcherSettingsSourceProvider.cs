using Noggog;
using System.IO;
using System.Text;

namespace SynthEBD;

public class PatcherSettingsSourceProvider
{
    public PatcherSettingsSource SettingsSource { get; } = new();
    public static StringBuilder SettingsLog { get; } = new();
    public string ErrorString;
    public string SettingsSourcePath { get; set; } // where to read source from, and save it to
    public string SettingsRootPath { get; set; } // Where the SynthEBDPaths class should look for its settings files

    public PatcherSettingsSourceProvider(string sourcePath)
    {
        SettingsSourcePath = sourcePath;

        if (File.Exists(sourcePath))
        {
            SettingsLog.AppendLine("Found settings source path at " + sourcePath);

            var source = JSONhandler<PatcherSettingsSource>.LoadJSONFile(sourcePath, out bool loadSuccess,
                out string exceptionStr);
            if (loadSuccess)
            {
                SettingsLog.AppendLine("Source Settings: "); ;
                SettingsLog.AppendLine("Load Settings from Portable Folder: " + source.UsePortableSettings);
                SettingsLog.AppendLine("Portable Folder Location: " + source.PortableSettingsFolder);
                SettingsSource = source;
            }
            else
            {
                SettingsLog.AppendLine("Could not load Settings Source. Error: " + exceptionStr);
                ErrorString = "Could not load Settings Source. Error: " + exceptionStr;
            }

            if (SettingsSource.UsePortableSettings && !SettingsSource.PortableSettingsFolder.IsNullOrWhitespace() && Directory.Exists(SettingsSource.PortableSettingsFolder))
            {
                SettingsRootPath = source.PortableSettingsFolder;
            }
            else
            {
                SettingsRootPath = Path.GetDirectoryName(sourcePath);
            }
        }
        else
        {
            SettingsLog.AppendLine("Did not find settings source path at " + sourcePath);
            SettingsLog.AppendLine("Using default environment and patcher settings locations.");
            SettingsRootPath = Path.GetDirectoryName(sourcePath);
        }
        SettingsSource.Initialized = true;
    }
    public void SaveSettingsSource(VM_Settings_General generalSettings, out bool saveSuccess, out string exceptionStr)
    {
        PatcherSettingsSource source = new PatcherSettingsSource()
        {
            UsePortableSettings = generalSettings.bLoadSettingsFromDataFolder,
            PortableSettingsFolder = generalSettings.PortableSettingsFolder,
            Initialized = true,
        };
        JSONhandler<PatcherSettingsSource>.SaveJSONFile(source, SettingsSourcePath, out saveSuccess, out exceptionStr);
    }

    public void SetPortableSettingsDir(string newDir)
    {
        SettingsSource.PortableSettingsFolder = newDir;
    }
}
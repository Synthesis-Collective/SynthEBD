using Mutagen.Bethesda.Starfield;
using Noggog;
using System.IO;
using System.Text;

namespace SynthEBD;

public class PatcherSettingsSourceProvider : VM
{
    //public PatcherSettingsSource SettingsSource { get; } = new();
    public static StringBuilder SettingsLog { get; } = new();
    public string ErrorString;
    public string SettingsSourcePath { get; set; } // where to read source from, and save it to
    public string DefaultSettingsRootPath { get; set; } // Either the application directory or the Synthesis extra settings folder, depending on how UI is launched.

    // Loaded from DTO
    public bool Initialized { get; set; } = false;
    public bool UsePortableSettings { get; set; } = false;
    public string PortableSettingsFolder { get; set; } = "";

    public PatcherSettingsSourceProvider(string sourcePath)
    {
        SettingsSourcePath = sourcePath;
        DefaultSettingsRootPath = Path.GetDirectoryName(sourcePath);

        if (File.Exists(sourcePath))
        {
            SettingsLog.AppendLine("Found settings source path at " + sourcePath);

            var source = JSONhandler<PatcherSettingsSource>.LoadJSONFile(sourcePath, out bool loadSuccess,
                out string exceptionStr);
            if (loadSuccess)
            {
                SettingsLog.AppendLine("Source Settings: "); ;
                SettingsLog.AppendLine("Load Settings from Portable Folder: " + source.UsePortableSettings);
                SettingsLog.AppendLine("Portable Settings Folder Location: " + source.PortableSettingsFolder);
                Initialized = source.Initialized;
                UsePortableSettings = source.UsePortableSettings;
                if (PortableSettingsFolderValid(source.PortableSettingsFolder))
                {
                    PortableSettingsFolder = source.PortableSettingsFolder;
                    SettingsLog.AppendLine("Portable Settings Folder is valid");
                }
            }
            else
            {
                SettingsLog.AppendLine("Could not load Settings Source. Error: " + exceptionStr);
                ErrorString = "Could not load Settings Source. Error: " + exceptionStr;
            }
        }
        else
        {
            SettingsLog.AppendLine("Did not find settings source path at " + sourcePath);
            SettingsLog.AppendLine("Using default environment and patcher settings locations.");
        }
        Initialized = true;
    }

    public string GetCurrentSettingsRootPath()
    {
        if (UsePortableSettings && PortableSettingsFolderValid(PortableSettingsFolder))
        {
            return PortableSettingsFolder;
        }
        else
        {
            return DefaultSettingsRootPath;
        }
    }

    public bool PortableSettingsFolderValid(string folderDir)
    {
        return folderDir != null && !folderDir.IsNullOrWhitespace() && Directory.Exists(folderDir);
    }

    public void SaveSettingsSource(out bool saveSuccess, out string exceptionStr)
    {
        PatcherSettingsSource source = new PatcherSettingsSource()
        {
            UsePortableSettings = UsePortableSettings,
            PortableSettingsFolder = PortableSettingsFolder,
            Initialized = true,
        };
        JSONhandler<PatcherSettingsSource>.SaveJSONFile(source, SettingsSourcePath, out saveSuccess, out exceptionStr);
    }
}
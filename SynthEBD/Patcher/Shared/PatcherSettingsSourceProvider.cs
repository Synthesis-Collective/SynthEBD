using Mutagen.Bethesda.Starfield;
using Noggog;
using System.IO;
using System.Text;

namespace SynthEBD;

/// <summary>
/// Loads and persists the settings-source descriptor that determines where SynthEBD reads/writes its
/// settings: either a default root (app directory or Synthesis extra-settings folder) or a user-chosen
/// portable folder. Reads the JSON source on construction and exposes the resolved active root.
/// </summary>
public class PatcherSettingsSourceProvider : VM
{
    //public PatcherSettingsSource SettingsSource { get; } = new();
    /// <summary>Shared log accumulating settings-source load diagnostics.</summary>
    public static StringBuilder SettingsLog { get; } = new();
    /// <summary>Set to a human-readable error message if the source file existed but failed to load.</summary>
    public string ErrorString;
    public string SettingsSourcePath { get; set; } // where to read source from, and save it to
    public string DefaultSettingsRootPath { get; set; } // Either the application directory or the Synthesis extra settings folder, depending on how UI is launched.

    // Loaded from DTO
    public bool Initialized { get; set; } = false;
    public bool UsePortableSettings { get; set; } = false;
    public string PortableSettingsFolder { get; set; } = "";

    /// <summary>
    /// Reads the settings source from <paramref name="sourcePath"/> (if present), populating the portable-folder
    /// configuration and validating it; logs progress to <see cref="SettingsLog"/>. Marks the provider initialized.
    /// </summary>
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
                SettingsLog.AppendLine("Source Settings: ");
                SettingsLog.AppendLine("Load Settings from Portable Folder: " + source.UsePortableSettings);
                SettingsLog.AppendLine("Portable Settings Folder Location: " + source.PortableSettingsFolder);
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

    /// <summary>
    /// Returns the active settings root: the portable folder when portable settings are enabled and valid,
    /// otherwise the default root path.
    /// </summary>
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

    /// <summary>Returns true if <paramref name="folderDir"/> is non-empty and an existing directory.</summary>
    public bool PortableSettingsFolderValid(string folderDir)
    {
        return folderDir != null && !folderDir.IsNullOrWhitespace() && Directory.Exists(folderDir);
    }

    /// <summary>
    /// Serializes the current portable-settings configuration to the settings-source JSON file, reporting
    /// success and any exception text via the out parameters.
    /// </summary>
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
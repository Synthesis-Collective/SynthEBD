using System.IO;

namespace SynthEBD;

public class SettingsIO_General
{
    public static void LoadGeneralSettings(out bool loadSuccess)
    {
        if (File.Exists(PatcherSettings.Paths.GeneralSettingsPath))
        {
            PatcherSettings.General = JSONhandler<Settings_General>.LoadJSONFile(PatcherSettings.Paths.GeneralSettingsPath, out loadSuccess, out string exceptionStr);
            if(loadSuccess && string.IsNullOrWhiteSpace(PatcherSettings.General.OutputDataFolder))
            {
                PatcherSettings.General.OutputDataFolder = PatcherEnvironmentProvider.Instance.Environment.DataFolderPath;
            }
            else if (!loadSuccess)
            {
                Logger.LogError("Could not parse General Settings. Error: " + exceptionStr);
            }
        }
        else
        {
            PatcherSettings.General = new Settings_General();
            PatcherSettings.General.OutputDataFolder = PatcherEnvironmentProvider.Instance.Environment.DataFolderPath;
            loadSuccess = true;
        }
    }
}
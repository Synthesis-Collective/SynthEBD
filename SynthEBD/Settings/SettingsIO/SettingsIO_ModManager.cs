using System.IO;

namespace SynthEBD;

public class SettingsIO_ModManager
{
    public static Settings_ModManager LoadModManagerSettings(out bool loadSuccess)
    {
        Settings_ModManager modManagerSettings = new Settings_ModManager();

        loadSuccess = true;

        if (File.Exists(PatcherSettings.Paths.ModManagerSettingsPath))
        {
            modManagerSettings = JSONhandler<Settings_ModManager>.LoadJSONFile(PatcherSettings.Paths.ModManagerSettingsPath, out loadSuccess, out string exceptionStr);
            if (loadSuccess && string.IsNullOrWhiteSpace(modManagerSettings.CurrentInstallationFolder))
            {
                modManagerSettings.CurrentInstallationFolder = _patcherEnvironmentProvider.Environment.DataFolderPath;
            }
            else if (!loadSuccess)
            {
                Logger.LogError("Could not load Mod Manager Integration Settings. Error: " + exceptionStr);
            }
        }

        return modManagerSettings;
    }
}
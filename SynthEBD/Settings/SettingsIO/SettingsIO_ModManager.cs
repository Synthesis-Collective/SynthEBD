using System.IO;

namespace SynthEBD;

public class SettingsIO_ModManager
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_ModManager(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    public Settings_ModManager LoadModManagerSettings(out bool loadSuccess)
    {
        Settings_ModManager modManagerSettings = new Settings_ModManager();

        loadSuccess = true;

        if (File.Exists(_paths.ModManagerSettingsPath))
        {
            modManagerSettings = JSONhandler<Settings_ModManager>.LoadJSONFile(_paths.ModManagerSettingsPath, out loadSuccess, out string exceptionStr);
            if (loadSuccess && string.IsNullOrWhiteSpace(modManagerSettings.CurrentInstallationFolder))
            {
                modManagerSettings.CurrentInstallationFolder = PatcherEnvironmentProvider.Instance.Environment.DataFolderPath;
            }
            else if (!loadSuccess)
            {
                _logger.LogError("Could not load Mod Manager Integration Settings. Error: " + exceptionStr);
            }
        }

        return modManagerSettings;
    }
}
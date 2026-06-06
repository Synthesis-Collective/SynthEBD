using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads the mod-manager integration settings (<see cref="Settings_ModManager"/>) from JSON, initializing a
/// default object (auto-detecting the current installation folder) when no file is present or deserialization
/// produced an uninitialized object.
/// </summary>
public class SettingsIO_ModManager
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the environment provider, logger, and path resolver.</summary>
    public SettingsIO_ModManager(IEnvironmentStateProvider environmentProvider, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
    }

    /// <summary>
    /// Loads the mod-manager settings from disk, or returns an initialized default if the file is absent.
    /// Reads from disk. If a loaded object has a blank installation folder, re-runs initialization to
    /// auto-detect environment defaults.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or defaulted), false on parse error.</param>
    /// <returns>The loaded or default <see cref="Settings_ModManager"/>.</returns>
    public Settings_ModManager LoadModManagerSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Mod Manager Settings from disk");
        Settings_ModManager modManagerSettings = new Settings_ModManager();
        modManagerSettings.Initialize(_environmentProvider);

        loadSuccess = true;

        if (File.Exists(_paths.ModManagerSettingsPath))
        {
            modManagerSettings = JSONhandler<Settings_ModManager>.LoadJSONFile(_paths.ModManagerSettingsPath, out loadSuccess, out string exceptionStr);
            if (loadSuccess && string.IsNullOrWhiteSpace(modManagerSettings.CurrentInstallationFolder))
            {
                modManagerSettings.Initialize(_environmentProvider); // trigger again; failed deserialization will yield a new settings object
            }
            else if (!loadSuccess)
            {
                _logger.LogError("Could not load Mod Manager Integration Settings. Error: " + exceptionStr);
            }
        }

        _logger.LogStartupEventEnd("Loading Mod Manager Settings from disk");
        return modManagerSettings;
    }
}
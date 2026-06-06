using Noggog;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves the application-wide <see cref="Settings_General"/> model to/from JSON. Owns the
/// general-settings half of the VM ⇄ model persistence flow: reading at startup into
/// <see cref="PatcherState.GeneralSettings"/> (with output-folder and race-grouping fixups) and dumping
/// the general settings view model back to disk on save.
/// </summary>
public class SettingsIO_General
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the environment provider, runtime <see cref="PatcherState"/>, logger, and path resolver.</summary>
    public SettingsIO_General(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Loads general settings from disk into <see cref="PatcherState.GeneralSettings"/>, or substitutes a
    /// fresh default object if the file is missing or unparseable. Side effects: reads the settings file;
    /// on parse failure best-effort writes a timestamped error dump to a sibling Logs folder; resolves the
    /// effective output data folder onto <see cref="SynthEBDPaths.OutputDataFolder"/> (falling back to the
    /// game data folder if the configured one is blank or missing); and dedupes race groupings.
    /// </summary>
    /// <param name="loadSuccess">Set true if settings loaded cleanly (or defaulted), false on parse error.</param>
    public void LoadGeneralSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading general settings from disk");
        if (File.Exists(_paths.GeneralSettingsPath))
        {
            _patcherState.GeneralSettings = JSONhandler<Settings_General>.LoadJSONFile(_paths.GeneralSettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not parse General Settings. Error: " + exceptionStr);
                try
                {
                    var errDir = Path.Combine(Path.GetDirectoryName(_paths.GeneralSettingsPath) ?? "", "..", "Logs");
                    Directory.CreateDirectory(errDir);
                    File.WriteAllText(Path.Combine(errDir, "GeneralSettings_ParseError.txt"),
                        DateTime.Now.ToString("u") + Environment.NewLine + exceptionStr);
                }
                catch { /* best-effort */ }
            }
        }
        else
        {
            _patcherState.GeneralSettings = new Settings_General();
            loadSuccess = true;
        }
        if (_patcherState.GeneralSettings == null)
        {
            _patcherState.GeneralSettings = new Settings_General();
        }
        _logger.LogStartupEventEnd("Loading general settings from disk");

        if (!loadSuccess ||
            _patcherState.GeneralSettings.OutputDataFolder.IsNullOrWhitespace() ||
            !Directory.Exists(_patcherState.GeneralSettings.OutputDataFolder))
        {
            _paths.OutputDataFolder = _environmentProvider.DataFolderPath;
        }
        else
        {
            _paths.OutputDataFolder = _patcherState.GeneralSettings.OutputDataFolder;
        }

        if (_patcherState.GeneralSettings.RaceGroupings == null)
        {
            _patcherState.GeneralSettings.RaceGroupings = new();
        }
        _patcherState.GeneralSettings.RaceGroupings = MiscValidation.CheckRaceGroupingDuplicates(_patcherState.GeneralSettings.RaceGroupings, "General Settings").ToList();
    }

    /// <summary>
    /// Dumps the general-settings view model back into <see cref="PatcherState.GeneralSettings"/> and writes
    /// it to disk. No-op if no general settings model exists. Side effect: writes the settings file.
    /// </summary>
    /// <param name="generalSettingsVM">The view model whose state is persisted.</param>
    public void DumpVMandSave(VM_Settings_General generalSettingsVM)
    {
        if (_patcherState.GeneralSettings == null)
        {
            return;
        }
        _patcherState.GeneralSettings = generalSettingsVM.DumpViewModelToModel();
        JSONhandler<Settings_General>.SaveJSONFile(_patcherState.GeneralSettings, _paths.GeneralSettingsPath, out bool saveSuccess, out string exceptionStr);
        if (!saveSuccess) { _logger.LogMessage("Error saving General Settings: " + exceptionStr); }
    }
}
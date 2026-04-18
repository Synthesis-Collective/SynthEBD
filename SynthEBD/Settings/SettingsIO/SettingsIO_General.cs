using Noggog;
using System.IO;

namespace SynthEBD;

public class SettingsIO_General
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_General(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
    }
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
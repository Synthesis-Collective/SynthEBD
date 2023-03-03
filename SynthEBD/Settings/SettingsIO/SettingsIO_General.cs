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
            if(loadSuccess && string.IsNullOrWhiteSpace(_paths.OutputDataFolder))
            {
                _paths.OutputDataFolder = _environmentProvider.DataFolderPath;
            }
            else if (!loadSuccess)
            {
                _logger.LogError("Could not parse General Settings. Error: " + exceptionStr);
            }
        }
        else
        {
            _patcherState.GeneralSettings = new Settings_General();
            loadSuccess = true;
        }
        _logger.LogStartupEventEnd("Loading general settings from disk");
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
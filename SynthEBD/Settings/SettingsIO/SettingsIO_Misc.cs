using System.IO;

namespace SynthEBD;

public class SettingsIO_Misc
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_Misc(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    public static void SaveSettingsSource(VM_Settings_General generalSettings, out bool saveSuccess, out string exceptionStr)
    {
        LoadSource source = new LoadSource()
        {
            GameEnvironmentDirectory = PatcherEnvironmentProvider.Instance.GameDataFolder, 
            LoadFromDataDir = generalSettings.bLoadSettingsFromDataFolder,
            PortableSettingsFolder = generalSettings.PortableSettingsFolder, 
            SkyrimVersion = PatcherEnvironmentProvider.Instance.SkyrimVersion,
            Initialized = true,
        };
        JSONhandler<LoadSource>.SaveJSONFile(source, SynthEBDPaths.SettingsSourcePath, out saveSuccess, out exceptionStr);
    }

    public Dictionary<string, NPCAssignment> LoadConsistency(out bool loadSuccess)
    {
        var loaded = new Dictionary<string, NPCAssignment>();

        loadSuccess = true;

        if (File.Exists(_paths.ConsistencyPath))
        {
            loaded = JSONhandler<Dictionary<string, NPCAssignment>>.LoadJSONFile(_paths.ConsistencyPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Consistency File. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.ConsistencyPath)))
        {
            loaded = JSONhandler<Dictionary<string, NPCAssignment>>.LoadJSONFile(_paths.GetFallBackPath(_paths.ConsistencyPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Consistency File. Error: " + exceptionStr);
            }
        }
        // note: No need to alert user if consistency can't be loaded - it won't be available on first run

        if (loaded == null) { loaded = new(); } // this can happen when JSON parsing fails - loaded still gets assigned the failed null value
        return loaded;
    }
    public void SaveConsistency(Dictionary<string, NPCAssignment> consistency, out bool saveSuccess)
    {
        JSONhandler<Dictionary<string, NPCAssignment>>.SaveJSONFile(consistency, _paths.ConsistencyPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            _logger.LogError("Could not save Consistency File. Error: " + exceptionStr);
            _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Consistency File to " + _paths.ConsistencyPath, ErrorType.Error, 5);
        }
    }
}
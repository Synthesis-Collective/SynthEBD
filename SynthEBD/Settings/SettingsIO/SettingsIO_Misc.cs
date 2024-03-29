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

    public Dictionary<string, NPCAssignment> LoadConsistency(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Consistency from disk");
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
        _logger.LogStartupEventEnd("Loading Consistency from disk");
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

    public UpdateLog LoadUpdateLog(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Update Log from disk");
        var loaded = new UpdateLog();

        loadSuccess = true;

        if (File.Exists(_paths.UpdateLogPath))
        {
            loaded = JSONhandler<UpdateLog>.LoadJSONFile(_paths.UpdateLogPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Update Log. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.ConsistencyPath)))
        {
            loaded = JSONhandler<UpdateLog>.LoadJSONFile(_paths.GetFallBackPath(_paths.UpdateLogPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Update Log. Error: " + exceptionStr);
            }
        }
        // note: No need to alert user if update log can't be loaded - it won't be available if loading from a version earlier that 1.0.1.2

        if (loaded == null) { loaded = new(); } // this can happen when JSON parsing fails - loaded still gets assigned the failed null value
        _logger.LogStartupEventEnd("Loading Update Log from disk");
        return loaded;
    }
    public void SaveUpdateLog(UpdateLog updateLog, out bool saveSuccess)
    {
        JSONhandler<UpdateLog>.SaveJSONFile(updateLog, _paths.UpdateLogPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            _logger.LogError("Could not save Update Log. Error: " + exceptionStr);
            _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Update Log to " + _paths.ConsistencyPath, ErrorType.Error, 5);
        }
    }
}
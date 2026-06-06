using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves miscellaneous persisted state: the consistency map (NPC ID → its previously assigned
/// <see cref="NPCAssignment"/>, used to keep appearances stable across runs) and the <see cref="UpdateLog"/>.
/// Handles primary/fallback path resolution; absence is treated as normal (no user alert).
/// </summary>
public class SettingsIO_Misc
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the logger and path resolver.</summary>
    public SettingsIO_Misc(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    /// <summary>
    /// Loads the consistency map from the primary path, then the fallback path. Reads from disk. Returns an
    /// empty map if no file exists or if parsing failed (which would otherwise leave a null result).
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or empty), false on parse error.</param>
    /// <returns>The consistency map.</returns>
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
    /// <summary>
    /// Writes the consistency map to disk. On failure logs and raises a timed status-update error.
    /// </summary>
    /// <param name="consistency">The consistency map to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
    public void SaveConsistency(Dictionary<string, NPCAssignment> consistency, out bool saveSuccess)
    {
        JSONhandler<Dictionary<string, NPCAssignment>>.SaveJSONFile(consistency, _paths.ConsistencyPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            _logger.LogError("Could not save Consistency File. Error: " + exceptionStr);
            _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Consistency File to " + _paths.ConsistencyPath, ErrorType.Error, 5);
        }
    }

    /// <summary>
    /// Loads the update log from the primary path, then the fallback path. Reads from disk. Returns an empty
    /// <see cref="UpdateLog"/> if no file exists (e.g. when upgrading from a version earlier than 1.0.1.2) or
    /// if parsing failed.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or empty), false on parse error.</param>
    /// <returns>The loaded or default <see cref="UpdateLog"/>.</returns>
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
    /// <summary>
    /// Writes the update log to disk. On failure logs and raises a timed status-update error.
    /// </summary>
    /// <param name="updateLog">The update log to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
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
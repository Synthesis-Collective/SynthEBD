using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves the user's explicit per-NPC overrides (a set of <see cref="NPCAssignment"/>) to/from JSON.
/// Handles primary/fallback path resolution; absence is treated as normal (no user alert) since the file does
/// not exist until assignments are made in the UI.
/// </summary>
public class SettingsIO_SpecificNPCAssignments
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the logger and path resolver.</summary>
    public SettingsIO_SpecificNPCAssignments(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Loads the specific NPC assignments from the primary path, then the fallback path. Reads from disk.
    /// Returns an empty set if no file exists.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or empty), false on parse error.</param>
    /// <returns>The loaded NPC assignments.</returns>
    public HashSet<NPCAssignment> LoadAssignments(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Specific NPC Assignments from disk");
        HashSet<NPCAssignment> specificNPCAssignments = new HashSet<NPCAssignment>();

        loadSuccess = true;

        if (File.Exists(_paths.SpecificNPCAssignmentsPath))
        {
            specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(_paths.SpecificNPCAssignmentsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.SpecificNPCAssignmentsPath)))
        {
            specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(_paths.GetFallBackPath(_paths.SpecificNPCAssignmentsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
            }
        }
        // note: No need to alert user if Specific NPC Assignments can't be loaded - it won't be available until assignments are made in UI
        _logger.LogStartupEventEnd("Loading Specific NPC Assignments from disk");
        return specificNPCAssignments;
    }

    /// <summary>
    /// Writes the specific NPC assignments to disk. On failure logs and raises a timed status-update error.
    /// </summary>
    /// <param name="assignments">The assignments to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
    public void SaveAssignments(HashSet<NPCAssignment> assignments, out bool saveSuccess)
    {
        JSONhandler<HashSet<NPCAssignment>>.SaveJSONFile(assignments, _paths.SpecificNPCAssignmentsPath, out saveSuccess, out string exceptionStr);
        if (!saveSuccess)
        {
            _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Specific NPC Assignments to " + _paths.SpecificNPCAssignmentsPath, ErrorType.Error, 5);
            _logger.LogMessage("Could not save Specific NPC Assignments. Error: " + exceptionStr);
        }
    }
}
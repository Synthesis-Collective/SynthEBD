using System.IO;

namespace SynthEBD;

public class SettingsIO_SpecificNPCAssignments
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_SpecificNPCAssignments(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
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
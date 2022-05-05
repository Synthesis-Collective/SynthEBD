using System.IO;

namespace SynthEBD
{
    class SettingsIO_SpecificNPCAssignments
    {
        public static HashSet<NPCAssignment> LoadAssignments(out bool loadSuccess)
        {
            HashSet<NPCAssignment> specificNPCAssignments = new HashSet<NPCAssignment>();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.SpecificNPCAssignmentsPath))
            {
                specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.SpecificNPCAssignmentsPath, out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath)))
            {
                specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath), out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
                }
            }
            // note: No need to alert user if Specific NPC Assignments can't be loaded - it won't be available until assignments are made in UI

            return specificNPCAssignments;
        }

        public static void SaveAssignments(HashSet<NPCAssignment> assignments, out bool saveSuccess)
        {
            JSONhandler<HashSet<NPCAssignment>>.SaveJSONFile(assignments, PatcherSettings.Paths.SpecificNPCAssignmentsPath, out saveSuccess, out string exceptionStr);
            if (!saveSuccess)
            {
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Specific NPC Assignments to " + PatcherSettings.Paths.SpecificNPCAssignmentsPath, ErrorType.Error, 5);
                Logger.LogMessage("Could not save Specific NPC Assignments. Error: " + exceptionStr);
            }
        }
    }
}

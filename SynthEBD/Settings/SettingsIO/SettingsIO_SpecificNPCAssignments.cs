using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SettingsIO_SpecificNPCAssignments
    {
        public static HashSet<NPCAssignment> LoadAssignments()
        {
            HashSet<NPCAssignment> specificNPCAssignments = new HashSet<NPCAssignment>();

            if (File.Exists(PatcherSettings.Paths.SpecificNPCAssignmentsPath))
            {
                specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.SpecificNPCAssignmentsPath, out bool success, out string exceptionStr);
                if (!success)
                {
                    Logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath)))
            {
                specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath), out bool success, out string exceptionStr);
                if (!success)
                {
                    Logger.LogError("Could not parse Specific NPC Assignments. Error: " + exceptionStr);
                }
            }

            return specificNPCAssignments;
        }

        public static void SaveAssignments(HashSet<NPCAssignment> assignments)
        {
            JSONhandler<HashSet<NPCAssignment>>.SaveJSONFile(assignments, PatcherSettings.Paths.SpecificNPCAssignmentsPath, out bool success, out string exceptionStr);
            if (!success)
            {
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Block List to " + PatcherSettings.Paths.BlockListPath, ErrorType.Error, 5);
                Logger.LogMessage("Could not save Block List. Error: " + exceptionStr);
            }
        }
    }
}

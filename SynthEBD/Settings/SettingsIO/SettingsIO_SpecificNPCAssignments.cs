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
                try
                {
                    specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.loadJSONFile(PatcherSettings.Paths.SpecificNPCAssignmentsPath);
                }
                catch
                {
                    // Warn User
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath)))
            {
                try
                {
                    specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.loadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.SpecificNPCAssignmentsPath));
                }
                catch
                {
                    // Warn User
                }
            }

            return specificNPCAssignments;
        }
    }
}

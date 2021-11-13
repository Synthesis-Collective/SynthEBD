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
        public static HashSet<NPCAssignment> LoadAssignments(Paths paths)
        {
            HashSet<NPCAssignment> specificNPCAssignments = new HashSet<NPCAssignment>();

            if (File.Exists(paths.SpecificNPCAssignmentsPath))
            {
                try
                {
                    specificNPCAssignments = JSONhandler<HashSet<NPCAssignment>>.loadJSONFile(paths.SpecificNPCAssignmentsPath);
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

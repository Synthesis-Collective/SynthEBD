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
        public static HashSet<SpecificNPCAssignment> LoadAssignments(Paths paths)
        {
            HashSet<SpecificNPCAssignment> specificNPCAssignments = new HashSet<SpecificNPCAssignment>();

            if (File.Exists(paths.SpecificNPCAssignmentsPath))
            {
                try
                {
                    specificNPCAssignments = JSONhandler<HashSet<SpecificNPCAssignment>>.loadJSONFile(paths.SpecificNPCAssignmentsPath);
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

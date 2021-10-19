using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsIO_Misc
    {
        public static HashSet<string> LoadNPCNameExclusions(Paths paths)
        {
            HashSet<string> exclusions = new HashSet<string>();

            if (File.Exists(paths.LinkedNPCNameExclusionsPath))
            {
                exclusions = DeserializeFromJSON<HashSet<string>>.loadJSONFile(paths.LinkedNPCNameExclusionsPath);
            }
            else if (File.Exists(paths.FallBackLinkedNPCNameExclusionsPath))
            {
                // Warn User
                exclusions = DeserializeFromJSON<HashSet<string>>.loadJSONFile(paths.FallBackLinkedNPCNameExclusionsPath);
            }
            else
            {
                // Warn User
            }

            return exclusions;
        }

        public static HashSet<LinkedNPCGroup> LoadLinkedNPCGroups(Paths paths)
        {
            HashSet<LinkedNPCGroup> linkedNPCGroups = new HashSet<LinkedNPCGroup>();

            if (File.Exists(paths.LinkedNPCsPath))
            {
                linkedNPCGroups = DeserializeFromJSON<HashSet<LinkedNPCGroup>>.loadJSONFile(paths.LinkedNPCsPath);
            }
            else if (File.Exists(paths.FallBackLinkedNPCsPath))
            {
                // Warn User
                linkedNPCGroups = DeserializeFromJSON<HashSet<LinkedNPCGroup>>.loadJSONFile(paths.FallBackLinkedNPCsPath);
            }
            else
            {
                // Warn User
            }

            return linkedNPCGroups;
        }
    }
}

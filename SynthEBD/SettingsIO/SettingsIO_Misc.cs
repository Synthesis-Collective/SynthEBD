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
                exclusions = JSONhandler<HashSet<string>>.loadJSONFile(paths.LinkedNPCNameExclusionsPath);
            }
            else if (File.Exists(paths.FallBackLinkedNPCNameExclusionsPath))
            {
                // Warn User
                exclusions = JSONhandler<HashSet<string>>.loadJSONFile(paths.FallBackLinkedNPCNameExclusionsPath);
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
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(paths.LinkedNPCsPath);
            }
            else if (File.Exists(paths.FallBackLinkedNPCsPath))
            {
                // Warn User
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(paths.FallBackLinkedNPCsPath);
            }
            else
            {
                // Warn User
            }

            return linkedNPCGroups;
        }

        public static HashSet<TrimPath> LoadTrimPaths(Paths paths)
        {
            HashSet<TrimPath> trimPaths = new HashSet<TrimPath>();

            if (File.Exists(paths.TrimPathsPath))
            {
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(paths.TrimPathsPath);
            }
            else if (File.Exists(paths.FallBackTrimPathsPath))
            {
                // Warn User
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(paths.FallBackTrimPathsPath);
            }
            else
            {
                // Warn User
            }

            return trimPaths;
        }
    }
}

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
        public static HashSet<string> LoadNPCNameExclusions()
        {
            HashSet<string> exclusions = new HashSet<string>();

            if (File.Exists(PatcherSettings.Paths.LinkedNPCNameExclusionsPath))
            {
                exclusions = JSONhandler<HashSet<string>>.loadJSONFile(PatcherSettings.Paths.LinkedNPCNameExclusionsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.FallBackLinkedNPCNameExclusionsPath))
            {
                // Warn User
                exclusions = JSONhandler<HashSet<string>>.loadJSONFile(PatcherSettings.Paths.FallBackLinkedNPCNameExclusionsPath);
            }
            else
            {
                // Warn User
            }

            return exclusions;
        }

        public static HashSet<LinkedNPCGroup> LoadLinkedNPCGroups()
        {
            HashSet<LinkedNPCGroup> linkedNPCGroups = new HashSet<LinkedNPCGroup>();

            if (File.Exists(PatcherSettings.Paths.LinkedNPCsPath))
            {
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(PatcherSettings.Paths.LinkedNPCsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.FallBackLinkedNPCsPath))
            {
                // Warn User
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(PatcherSettings.Paths.FallBackLinkedNPCsPath);
            }
            else
            {
                // Warn User
            }

            return linkedNPCGroups;
        }

        public static HashSet<TrimPath> LoadTrimPaths()
        {
            HashSet<TrimPath> trimPaths = new HashSet<TrimPath>();

            if (File.Exists(PatcherSettings.Paths.TrimPathsPath))
            {
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(PatcherSettings.Paths.TrimPathsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.FallBackTrimPathsPath))
            {
                // Warn User
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(PatcherSettings.Paths.FallBackTrimPathsPath);
            }
            else
            {
                // Warn User
            }

            return trimPaths;
        }
    }
}

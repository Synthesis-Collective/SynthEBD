using Mutagen.Bethesda.Plugins;
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
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCNameExclusionsPath)))
            {
                // Warn User
                exclusions = JSONhandler<HashSet<string>>.loadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCNameExclusionsPath));
            }
            else
            {
                // Warn User
            }

            return exclusions;
        }

        public static void SaveNPCNameExclusions(HashSet<string> exclusions)
        {
            try
            {
                JSONhandler<HashSet<string>>.SaveJSONFile(exclusions, PatcherSettings.Paths.LinkedNPCNameExclusionsPath);
            }
            catch
            {
                Logger.CallTimedNotifyStatusUpdateAsync("Could not save linked NPC name exclusions to " + PatcherSettings.Paths.LinkedNPCNameExclusionsPath, ErrorType.Error, 5);
            }
        }

        public static HashSet<LinkedNPCGroup> LoadLinkedNPCGroups()
        {
            HashSet<LinkedNPCGroup> linkedNPCGroups = new HashSet<LinkedNPCGroup>();

            if (File.Exists(PatcherSettings.Paths.LinkedNPCsPath))
            {
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(PatcherSettings.Paths.LinkedNPCsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCsPath)))
            {
                // Warn User
                linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.loadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCsPath));
            }
            else
            {
                // Warn User
            }

            return linkedNPCGroups;
        }

        public static void SaveLinkedNPCGroups(HashSet<LinkedNPCGroup> linkedGroups)
        {
            try
            {
                JSONhandler<HashSet<LinkedNPCGroup>>.SaveJSONFile(linkedGroups, PatcherSettings.Paths.LinkedNPCsPath);
            }
            catch
            {
                Logger.CallTimedNotifyStatusUpdateAsync("Could not save linked NPC groups to " + PatcherSettings.Paths.LinkedNPCsPath, ErrorType.Error, 5);
            }
        }

        public static HashSet<TrimPath> LoadTrimPaths()
        {
            HashSet<TrimPath> trimPaths = new HashSet<TrimPath>();

            if (File.Exists(PatcherSettings.Paths.TrimPathsPath))
            {
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(PatcherSettings.Paths.TrimPathsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TrimPathsPath)))
            {
                // Warn User
                trimPaths = JSONhandler<HashSet<TrimPath>>.loadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TrimPathsPath));
            }
            else
            {
                // Warn User
            }

            return trimPaths;
        }

        public static void SaveTrimPaths(HashSet<TrimPath> trimPaths)
        {
            try
            {
                JSONhandler<HashSet<TrimPath>>.SaveJSONFile(trimPaths, PatcherSettings.Paths.TrimPathsPath);
            }
            catch
            {
                Logger.CallTimedNotifyStatusUpdateAsync("Could not save TrimPaths to " + PatcherSettings.Paths.TrimPathsPath, ErrorType.Error, 5);
            }
        }

        public static Dictionary<string, NPCAssignment> LoadConsistency()
        {
            var loaded = new Dictionary<string, NPCAssignment>();
            if (File.Exists(PatcherSettings.Paths.ConsistencyPath))
            {
                loaded = JSONhandler<Dictionary<string, NPCAssignment>>.loadJSONFile(PatcherSettings.Paths.ConsistencyPath);
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.ConsistencyPath)))
            {
                loaded = JSONhandler<Dictionary<string, NPCAssignment>>.loadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.ConsistencyPath));
            }
            // note: No need to alert user if consistency can't be loaded - it won't be available on first run
            return loaded;
        }
        public static void SaveConsistency(Dictionary<string, NPCAssignment> consistency)
        {
            try
            {
                JSONhandler<Dictionary<string, NPCAssignment>>.SaveJSONFile(consistency, PatcherSettings.Paths.ConsistencyPath);
            }
            catch
            {
                Logger.CallTimedNotifyStatusUpdateAsync("Could not save Consistency to " + PatcherSettings.Paths.ConsistencyPath, ErrorType.Error, 5);
            }
        }
    }
}

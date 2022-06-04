using System.IO;

namespace SynthEBD;

public class SettingsIO_Misc
{
    public static void SaveSettingsSource(VM_Settings_General generalSettings, out bool saveSuccess, out string exceptionStr)
    {
        LoadSource source = new LoadSource()
        {
            GameEnvironmentDirectory = PatcherEnvironmentProvider.Instance.GameDataFolder, 
            LoadFromDataDir = generalSettings.bLoadSettingsFromDataFolder,
            PortableSettingsFolder = generalSettings.PortableSettingsFolder, 
            SkyrimVersion = PatcherEnvironmentProvider.Instance.SkyrimVersion
        };
        JSONhandler<LoadSource>.SaveJSONFile(source, Paths.SettingsSourcePath, out saveSuccess, out exceptionStr);
    }

    public static HashSet<string> LoadNPCNameExclusions(out bool loadSuccess)
    {
        HashSet<string> exclusions = new HashSet<string>();

        loadSuccess = true;

        if (File.Exists(PatcherSettings.Paths.LinkedNPCNameExclusionsPath))
        {
            exclusions = JSONhandler<HashSet<string>>.LoadJSONFile(PatcherSettings.Paths.LinkedNPCNameExclusionsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Unique NPC Name Exclusion List. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCNameExclusionsPath)))
        {
            exclusions = JSONhandler<HashSet<string>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCNameExclusionsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Unique NPC Name Exclusion List. Error: " + exceptionStr);
            }
        }
        else
        {
            Logger.LogError("Could not find Unique NPC Name Exclusion List at " + PatcherSettings.Paths.LinkedNPCNameExclusionsPath);
        }

        return exclusions;
    }

    public static void SaveNPCNameExclusions(HashSet<string> exclusions, out bool saveSuccess)
    {
        JSONhandler<HashSet<string>>.SaveJSONFile(exclusions, PatcherSettings.Paths.LinkedNPCNameExclusionsPath, out saveSuccess, out string exceptionStr);
        if (!saveSuccess)
        {
            Logger.LogError("Could not save Unique NPC Name Exclusion List. Error: " + exceptionStr);
            Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Unique NPC Name Exclusion List to " + PatcherSettings.Paths.LinkedNPCNameExclusionsPath, ErrorType.Error, 5);
        }
    }

    public static HashSet<LinkedNPCGroup> LoadLinkedNPCGroups(out bool loadSuccess)
    {
        HashSet<LinkedNPCGroup> linkedNPCGroups = new HashSet<LinkedNPCGroup>();

        loadSuccess = true;

        if (File.Exists(PatcherSettings.Paths.LinkedNPCsPath))
        {
            linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.LoadJSONFile(PatcherSettings.Paths.LinkedNPCsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Linked NPC Groups. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCsPath)))
        {
            linkedNPCGroups = JSONhandler<HashSet<LinkedNPCGroup>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.LinkedNPCsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Linked NPC Groups. Error: " + exceptionStr);
            }
        }
        else
        {
            Logger.LogError("Could not find Linked NPC Groups at " + PatcherSettings.Paths.LinkedNPCsPath);
        }

        return linkedNPCGroups;
    }

    public static void SaveLinkedNPCGroups(HashSet<LinkedNPCGroup> linkedGroups, out bool saveSuccess)
    {
        JSONhandler<HashSet<LinkedNPCGroup>>.SaveJSONFile(linkedGroups, PatcherSettings.Paths.LinkedNPCsPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            Logger.LogError("Could not save Linked NPC Groups. Error: " + exceptionStr);
            Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Linked NPC Groups to " + PatcherSettings.Paths.LinkedNPCsPath, ErrorType.Error, 5);
        }
    }

    public static HashSet<TrimPath> LoadTrimPaths(out bool loadSuccess)
    {
        HashSet<TrimPath> trimPaths = new HashSet<TrimPath>();

        loadSuccess = true;

        if (File.Exists(PatcherSettings.Paths.TrimPathsPath))
        {
            trimPaths = JSONhandler<HashSet<TrimPath>>.LoadJSONFile(PatcherSettings.Paths.TrimPathsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Asset Path Trimming List. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TrimPathsPath)))
        {
            trimPaths = JSONhandler<HashSet<TrimPath>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TrimPathsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Asset Path Trimming List. Error: " + exceptionStr);
            }
        }
        else
        {
            Logger.LogError("Could not find Asset Path Trimming List at " + PatcherSettings.Paths.TrimPathsPath);
        }

        return trimPaths;
    }

    public static void SaveTrimPaths(HashSet<TrimPath> trimPaths, out bool saveSuccess)
    {
        JSONhandler<HashSet<TrimPath>>.SaveJSONFile(trimPaths, PatcherSettings.Paths.TrimPathsPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            Logger.LogError("Could not save Asset Path Trimming List. Error: " + exceptionStr);
            Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Asset Path Trimming List to " + PatcherSettings.Paths.TrimPathsPath, ErrorType.Error, 5);
        }
    }

    public static Dictionary<string, NPCAssignment> LoadConsistency(out bool loadSuccess)
    {
        var loaded = new Dictionary<string, NPCAssignment>();

        loadSuccess = true;

        if (File.Exists(PatcherSettings.Paths.ConsistencyPath))
        {
            loaded = JSONhandler<Dictionary<string, NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.ConsistencyPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Consistency File. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.ConsistencyPath)))
        {
            loaded = JSONhandler<Dictionary<string, NPCAssignment>>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.ConsistencyPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                Logger.LogError("Could not load Consistency File. Error: " + exceptionStr);
            }
        }
        // note: No need to alert user if consistency can't be loaded - it won't be available on first run
        return loaded;
    }
    public static void SaveConsistency(Dictionary<string, NPCAssignment> consistency, out bool saveSuccess)
    {
        JSONhandler<Dictionary<string, NPCAssignment>>.SaveJSONFile(consistency, PatcherSettings.Paths.ConsistencyPath, out saveSuccess, out string exceptionStr);

        if (!saveSuccess)
        {
            Logger.LogError("Could not save Consistency File. Error: " + exceptionStr);
            Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Consistency File to " + PatcherSettings.Paths.ConsistencyPath, ErrorType.Error, 5);
        }
    }
}
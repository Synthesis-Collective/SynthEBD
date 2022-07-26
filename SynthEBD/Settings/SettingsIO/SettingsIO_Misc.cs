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
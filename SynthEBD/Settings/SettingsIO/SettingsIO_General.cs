using System.IO;

namespace SynthEBD;

public class SettingsIO_General
{
    public static void LoadGeneralSettings(out bool loadSuccess)
    {
        if (File.Exists(PatcherSettings.Paths.GeneralSettingsPath))
        {
            PatcherSettings.General = JSONhandler<Settings_General>.LoadJSONFile(PatcherSettings.Paths.GeneralSettingsPath, out loadSuccess, out string exceptionStr);
            if(loadSuccess && string.IsNullOrWhiteSpace(PatcherSettings.Paths.OutputDataFolder))
            {
                PatcherSettings.Paths.OutputDataFolder = PatcherEnvironmentProvider.Instance.Environment.DataFolderPath;
            }
            else if (!loadSuccess)
            {
                Logger.LogError("Could not parse General Settings. Error: " + exceptionStr);
            }
        }
        else
        {
            PatcherSettings.General = new Settings_General();
            loadSuccess = true;
        }
    }

    public static void DumpVMandSave(VM_Settings_General generalSettingsVM)
    {
        VM_Settings_General.DumpViewModelToModel(generalSettingsVM, PatcherSettings.General);
        JSONhandler<Settings_General>.SaveJSONFile(PatcherSettings.General, PatcherSettings.Paths.GeneralSettingsPath, out bool saveSuccess, out string exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving General Settings: " + exceptionStr); }
    }
}
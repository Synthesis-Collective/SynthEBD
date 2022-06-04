using System.IO;
using System.Reflection;

namespace SynthEBD;

public class Paths
{
    private readonly VM_Settings_General _generalSettings;
    private static readonly string SynthEBDexeDirPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
    public static readonly string SettingsSourcePath = Path.Combine(SynthEBDexeDirPath, "Settings", "SettingsSource.json");

    private const string settingsDirRelPath = "Settings";
    private const string assetsDirRelPath = "Asset Packs";
    private const string heightsDirRelPath = "Height Configurations";
    private const string bodyGenDirRelPath = "BodyGen Configurations";
    private const string NPCConfigDirRelPath = "NPC Configuration";
    private const string recordTemplatesDirRelPath = "Record Templates";

    private static readonly string settingsDirPath = Path.Combine(SynthEBDexeDirPath, settingsDirRelPath);

    public Paths(VM_Settings_General generalSettings)
    {
        _generalSettings = generalSettings;
        // create relevant paths if necessary - only in the "home" directory. To avoid inadvertent clutter in the data folder, user must create these directories manually in their data folder

        string settingsDirPath = Path.Combine(SynthEBDexeDirPath, settingsDirRelPath);
        string assetsDirPath = Path.Combine(SynthEBDexeDirPath, assetsDirRelPath);
        string heightsDirPath = Path.Combine(SynthEBDexeDirPath, heightsDirRelPath);
        string bodyGenDirPath = Path.Combine(SynthEBDexeDirPath, bodyGenDirRelPath);
        string NPCConfigDirPath = Path.Combine(SynthEBDexeDirPath, NPCConfigDirRelPath);
        string recordTemplatesDirPath = Path.Combine(SynthEBDexeDirPath, recordTemplatesDirRelPath);

        if (Directory.Exists(settingsDirPath) == false)
        {
            Directory.CreateDirectory(settingsDirPath);
        }
        if (Directory.Exists(assetsDirPath) == false)
        {
            Directory.CreateDirectory(assetsDirPath);
        }
        if (Directory.Exists(heightsDirPath) == false)
        {
            Directory.CreateDirectory(heightsDirPath);
        }
        if (Directory.Exists(bodyGenDirPath) == false)
        {
            Directory.CreateDirectory(bodyGenDirPath);
        }
        if (Directory.Exists(NPCConfigDirPath) == false)
        {
            Directory.CreateDirectory(NPCConfigDirPath);
        }
        if (Directory.Exists(recordTemplatesDirPath) == false)
        {
            Directory.CreateDirectory(recordTemplatesDirPath);
        }

        UpdatePaths();
    }

    private string RelativePath { get; set; } 
    public string LogFolderPath { get; set; } = Path.Combine(SynthEBDexeDirPath, "Logs");
    public string ResourcesFolderPath { get; set; } = Path.Combine(SynthEBDexeDirPath, "Resources");
    public string GeneralSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "GeneralSettings.json");
    public string TexMeshSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "TexMeshSettings.json");
    public string AssetPackDirPath => Path.Combine(RelativePath, assetsDirRelPath);
    public string HeightSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "HeightSettings.json");
    public string HeightConfigDirPath => Path.Combine(RelativePath, heightsDirRelPath);
    public string BodyGenSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "BodyGenSettings.json");
    public string BodyGenConfigDirPath => Path.Combine(RelativePath, bodyGenDirRelPath);
    public string OBodySettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "OBodySettings.json");
    public string MaleTemplateGroupsPath => Path.Combine(RelativePath, settingsDirPath, "SliderGroupGenders", "Male.json");
    public string FemaleTemplateGroupsPath => Path.Combine(RelativePath, settingsDirPath, "SliderGroupGenders", "Female.json");
    public string ConsistencyPath => Path.Combine(RelativePath, NPCConfigDirRelPath, "Consistency.json");
    public string SpecificNPCAssignmentsPath => Path.Combine(RelativePath, NPCConfigDirRelPath, "Specific NPC Assignments.json");
    public string BlockListPath => Path.Combine(RelativePath, NPCConfigDirRelPath, "BlockList.json");
    public string LinkedNPCNameExclusionsPath => Path.Combine(RelativePath, settingsDirRelPath, "LinkedNPCNameExclusions.json");
    public string LinkedNPCsPath => Path.Combine(RelativePath, settingsDirRelPath, "LinkedNPCs.json");
    public string TrimPathsPath => Path.Combine(RelativePath, settingsDirRelPath, "TrimPathsByExtension.json");
    public string RecordReplacerSpecifiersPath => Path.Combine(RelativePath, settingsDirRelPath, "RecordReplacerSpecifiers.json");
    public string RecordTemplatesDirPath => Path.Combine(RelativePath, recordTemplatesDirRelPath);
    public string ModManagerSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "ModManagerSettings.json");

    public string GetFallBackPath(string path)
    {
        var suffix = path.Remove(0, RelativePath.Length).Trim(Path.PathSeparator);
        return Path.Join(SynthEBDexeDirPath, suffix);
    }

    public void UpdatePaths()
    {
        RefreshRelativePath();
    }
    
    private void RefreshRelativePath()
    {
        if (_generalSettings.bLoadSettingsFromDataFolder)
        {
            if (!string.IsNullOrWhiteSpace(PatcherSettings.PortableSettingsFolder) && Directory.Exists(PatcherSettings.PortableSettingsFolder))
            {
                RelativePath = PatcherSettings.PortableSettingsFolder;
            }
            else
            {
                RelativePath = Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, "SynthEBD");
            }
        }
        else
        {
            RelativePath = SynthEBDexeDirPath;
        }
    }
}
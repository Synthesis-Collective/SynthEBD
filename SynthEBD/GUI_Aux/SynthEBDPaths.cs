using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class SynthEBDPaths
{
    private static string RootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
    public static string SettingsSourcePath => Path.Combine(RootPath, "Settings", "SettingsSource.json");

    private const string settingsDirRelPath = "Settings";
    private const string assetsDirRelPath = "Asset Packs";
    private const string heightsDirRelPath = "Height Configurations";
    private const string bodyGenDirRelPath = "BodyGen Configurations";
    private const string NPCConfigDirRelPath = "NPC Configuration";
    private const string recordTemplatesDirRelPath = "Record Templates";

    private static readonly string settingsDirPath = Path.Combine(RootPath, settingsDirRelPath);

    public static void SetRootPath(string rootPath)
    {
        RootPath = rootPath;
    }

    public SynthEBDPaths(
        PatcherEnvironmentProvider environmentProvider,
        PatcherSettingsSourceProvider settingsSourceProvider,
        VM_Settings_General generalSettings)
    {
        // create relevant paths if necessary - only in the "home" directory. To avoid inadvertent clutter in the data folder, user must create these directories manually in their data folder

        string settingsDirPath = Path.Combine(RootPath, settingsDirRelPath);
        string assetsDirPath = Path.Combine(RootPath, assetsDirRelPath);
        string heightsDirPath = Path.Combine(RootPath, heightsDirRelPath);
        string bodyGenDirPath = Path.Combine(RootPath, bodyGenDirRelPath);
        string NPCConfigDirPath = Path.Combine(RootPath, NPCConfigDirRelPath);
        string recordTemplatesDirPath = Path.Combine(RootPath, recordTemplatesDirRelPath);

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

        if (settingsSourceProvider.SourceSettings.Value.Initialized && settingsSourceProvider.SourceSettings.Value.LoadFromDataDir)
        {
            RelativePath = settingsSourceProvider.SourceSettings.Value.PortableSettingsFolder;
        }

        Observable.CombineLatest(
                generalSettings.WhenAnyValue(x => x.bLoadSettingsFromDataFolder),
                generalSettings.WhenAnyValue(x => x.PortableSettingsFolder),
                environmentProvider.WhenAnyValue(x => x.Environment.DataFolderPath),
                (load, settingsFolder, dataPath) =>
                {
                    if (load)
                    {
                        if (!string.IsNullOrWhiteSpace(settingsFolder)
                            && Directory.Exists(settingsFolder))
                        {
                            return settingsFolder;
                        }
                        else
                        {
                            return Path.Combine(dataPath, "SynthEBD");
                        }
                    }
                    else
                    {
                        return RootPath;
                    }
                })
            .Subscribe(x => RelativePath = x);

        generalSettings.WhenAnyValue(x => x.OutputDataFolder).Subscribe(x =>
            {
                if (generalSettings.OutputDataFolder.IsNullOrWhitespace())
                {
                    OutputDataFolder = environmentProvider.Environment.DataFolderPath;
                }
                else
                {
                    OutputDataFolder = generalSettings.OutputDataFolder;
                }
            });
    }

    private string RelativePath { get; set; } 
    public string LogFolderPath { get; set; } = Path.Combine(RootPath, "Logs");
    public string ResourcesFolderPath { get; set; } = Path.Combine(RootPath, "Resources");
    public string GeneralSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "GeneralSettings.json");
    public string TexMeshSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "TexMeshSettings.json");
    public string AssetPackDirPath => Path.Combine(RelativePath, assetsDirRelPath);
    public string HeightSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "HeightSettings.json");
    public string HeightConfigDirPath => Path.Combine(RelativePath, heightsDirRelPath);
    public string BodyGenSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "BodyGenSettings.json");
    public string BodyGenConfigDirPath => Path.Combine(RelativePath, bodyGenDirRelPath);
    public string OBodySettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "OBodySettings.json");
    public string HeadPartsSettingsPath => Path.Combine(RelativePath, settingsDirRelPath, "HeadPartSettings.json");
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
    public string OutputDataFolder { get; set; }

    public string GetFallBackPath(string path)
    {
        var suffix = path.Remove(0, RelativePath.Length).Trim(Path.PathSeparator);
        return Path.Join(RootPath, suffix);
    }
}
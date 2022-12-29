using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class SynthEBDPaths : VM
{
    //private static string RootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
    //public static string SettingsSourcePath => Path.Combine(RootPath, "Settings", "SettingsSource.json");

    private const string settingsDirRelPath = "Settings";
    private const string assetsDirRelPath = "Asset Packs";
    private const string heightsDirRelPath = "Height Configurations";
    private const string bodyGenDirRelPath = "BodyGen Configurations";
    private const string NPCConfigDirRelPath = "NPC Configuration";
    private const string recordTemplatesDirRelPath = "Record Templates";

    private static readonly string settingsDirPath;

    //private readonly IStateProvider _stateProvider;

    //public static void SetRootPath(string rootPath)
    //{
    //   RootPath = rootPath;
    //}

    public SynthEBDPaths(
        IStateProvider stateProvider,
        PatcherSettingsSourceProvider settingsSourceProvider)
    {
        //_stateProvider = stateProvider;
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

        if (settingsSourceProvider.SettingsSource.Value.Initialized && settingsSourceProvider.SettingsSource.Value.LoadFromDataDir)
        {
            RootPath = settingsSourceProvider.SettingsSource.Value.PortableSettingsFolder;
        }

        /*
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
        */
    }

    public string RootPath { get; set; }
    public string LogFolderPath => Path.Combine(RootPath, "Logs");
    public string ResourcesFolderPath => Path.Combine(RootPath, "Resources");
    public string GeneralSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "GeneralSettings.json");
    public string TexMeshSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "TexMeshSettings.json");
    public string AssetPackDirPath => Path.Combine(RootPath, assetsDirRelPath);
    public string HeightSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "HeightSettings.json");
    public string HeightConfigDirPath => Path.Combine(RootPath, heightsDirRelPath);
    public string BodyGenSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "BodyGenSettings.json");
    public string BodyGenConfigDirPath => Path.Combine(RootPath, bodyGenDirRelPath);
    public string OBodySettingsPath => Path.Combine(RootPath, settingsDirRelPath, "OBodySettings.json");
    public string HeadPartsSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "HeadPartSettings.json");
    public string MaleTemplateGroupsPath => Path.Combine(RootPath, settingsDirRelPath, "SliderGroupGenders", "Male.json");
    public string FemaleTemplateGroupsPath => Path.Combine(RootPath, settingsDirRelPath, "SliderGroupGenders", "Female.json");
    public string ConsistencyPath => Path.Combine(RootPath, NPCConfigDirRelPath, "Consistency.json");
    public string SpecificNPCAssignmentsPath => Path.Combine(RootPath, NPCConfigDirRelPath, "Specific NPC Assignments.json");
    public string BlockListPath => Path.Combine(RootPath, NPCConfigDirRelPath, "BlockList.json");
    public string LinkedNPCNameExclusionsPath => Path.Combine(RootPath, settingsDirRelPath, "LinkedNPCNameExclusions.json");
    public string LinkedNPCsPath => Path.Combine(RootPath, settingsDirRelPath, "LinkedNPCs.json");
    public string TrimPathsPath => Path.Combine(RootPath, settingsDirRelPath, "TrimPathsByExtension.json");
    public string RecordReplacerSpecifiersPath => Path.Combine(RootPath, settingsDirRelPath, "RecordReplacerSpecifiers.json");
    public string RecordTemplatesDirPath => Path.Combine(RootPath, recordTemplatesDirRelPath);
    public string ModManagerSettingsPath => Path.Combine(RootPath, settingsDirRelPath, "ModManagerSettings.json");
    public string OutputDataFolder { get; set; }

    public string GetFallBackPath(string path)
    {
        var suffix = path.Remove(0, RootPath.Length).Trim(Path.PathSeparator);
        return Path.Join(RootPath, suffix);
    }
}
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class SynthEBDPaths : VM
{
    private static string _rootPath = "";

    public static readonly string StandaloneSourceDirName = "Settings";
    public static readonly string SettingsSourceFileName = "SettingsSource.json";
    public static readonly string EnvironmentSourceDirName = "EnvironmentSource.json";

    private const string settingsDirRelPath = "Settings";
    private const string assetsDirRelPath = "Asset Packs";
    private const string heightsDirRelPath = "Height Configurations";
    private const string bodyGenDirRelPath = "BodyGen Configurations";
    private const string NPCConfigDirRelPath = "NPC Configuration";
    private const string recordTemplatesDirRelPath = "Record Templates";

    private static readonly string settingsDirPath;

    private readonly PatcherSettingsSourceProvider _settingsSourceProvider;
    private readonly IEnvironmentStateProvider _stateProvider;

    //public static void SetRootPath(string rootPath)
    //{
    //   RootPath = rootPath;
    //}

    public SynthEBDPaths(
        PatcherSettingsSourceProvider settingsSourceProvider,
        IEnvironmentStateProvider stateProvider)
    {
        _settingsSourceProvider = settingsSourceProvider;
        _stateProvider = stateProvider;

        GetRootPath();

        Observable.CombineLatest(
                _settingsSourceProvider.WhenAnyValue(x => x.UsePortableSettings),
                _settingsSourceProvider.WhenAnyValue(x => x.PortableSettingsFolder),
                (_, _) => { return 0; })
            .Skip(1) // don't re-evaluate during initialization
            .Subscribe(_ => {
                GetRootPath();
            });

        // create relevant paths if necessary - only in the "home" directory. To avoid inadvertent clutter in the data folder, user must create these directories manually in their data folder

        string settingsDirPath = Path.Combine(_rootPath, settingsDirRelPath);
        string assetsDirPath = Path.Combine(_rootPath, assetsDirRelPath);
        string heightsDirPath = Path.Combine(_rootPath, heightsDirRelPath);
        string bodyGenDirPath = Path.Combine(_rootPath, bodyGenDirRelPath);
        string NPCConfigDirPath = Path.Combine(_rootPath, NPCConfigDirRelPath);
        string recordTemplatesDirPath = Path.Combine(_rootPath, recordTemplatesDirRelPath);

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

    public string LogFolderPath => Path.Combine(_rootPath, "Logs");
    public string GeneralSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "GeneralSettings.json");
    public string TexMeshSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "TexMeshSettings.json");
    public string AssetPackDirPath => Path.Combine(_rootPath, assetsDirRelPath);
    public string HeightSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "HeightSettings.json");
    public string HeightConfigDirPath => Path.Combine(_rootPath, heightsDirRelPath);
    public string BodyGenSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "BodyGenSettings.json");
    public string BodyGenConfigDirPath => Path.Combine(_rootPath, bodyGenDirRelPath);
    public string OBodySettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "OBodySettings.json");
    public string HeadPartsSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "HeadPartSettings.json");
    public string MaleTemplateGroupsPath => Path.Combine(_rootPath, settingsDirRelPath, "SliderGroupGenders", "Male.json");
    public string FemaleTemplateGroupsPath => Path.Combine(_rootPath, settingsDirRelPath, "SliderGroupGenders", "Female.json");
    public string ConsistencyPath => Path.Combine(_rootPath, NPCConfigDirRelPath, "Consistency.json");
    public string SpecificNPCAssignmentsPath => Path.Combine(_rootPath, NPCConfigDirRelPath, "Specific NPC Assignments.json");
    public string BlockListPath => Path.Combine(_rootPath, NPCConfigDirRelPath, "BlockList.json");
    public string LinkedNPCNameExclusionsPath => Path.Combine(_rootPath, settingsDirRelPath, "LinkedNPCNameExclusions.json");
    public string LinkedNPCsPath => Path.Combine(_rootPath, settingsDirRelPath, "LinkedNPCs.json");
    public string TrimPathsPath => Path.Combine(_rootPath, settingsDirRelPath, "TrimPathsByExtension.json");
    public string RecordReplacerSpecifiersPath => Path.Combine(_rootPath, settingsDirRelPath, "RecordReplacerSpecifiers.json");
    public string RecordTemplatesDirPath => Path.Combine(_rootPath, recordTemplatesDirRelPath);
    public string ModManagerSettingsPath => Path.Combine(_rootPath, settingsDirRelPath, "ModManagerSettings.json");
    public string OutputDataFolder { get; set; }
    public string SettingsSourcePath => Path.Combine(_rootPath, StandaloneSourceDirName, SettingsSourceFileName);
    public string EnvironmentSourcePath => Path.Combine(_rootPath, StandaloneSourceDirName, EnvironmentSourceDirName);

    public string GetFallBackPath(string path)
    {
        var suffix = path.Remove(0, _rootPath.Length).Trim(Path.PathSeparator);
        return Path.Join(_settingsSourceProvider.DefaultSettingsRootPath, suffix);
    }

    public void GetRootPath()
    {
        if (_settingsSourceProvider.Initialized && _settingsSourceProvider.UsePortableSettings)
        {
            if (!_settingsSourceProvider.PortableSettingsFolder.IsNullOrWhitespace())
            {
                _rootPath = _settingsSourceProvider.SettingsRootPath;
            }
            else
            {
                _rootPath = Path.Combine(_stateProvider.DataFolderPath, "SynthEBD");
            }
        }
        else
        {
            _rootPath = _settingsSourceProvider.DefaultSettingsRootPath; // is already synced to SourcePath
        }
    }
}
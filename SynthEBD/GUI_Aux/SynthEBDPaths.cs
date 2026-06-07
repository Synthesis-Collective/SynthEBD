using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>Central resolver for every settings/output directory and file path the app uses. All paths
/// hang off a single <c>_rootPath</c> that follows the active settings source: the default settings root,
/// or a user-chosen folder when portable mode is enabled. The constructor reacts to portable-mode changes
/// and ensures the core "home" directories exist; data-folder equivalents are left for the user to create.</summary>
public class SynthEBDPaths : VM
{
    /// <summary>The resolved root all other paths are combined against; recomputed by <see cref="GetRootPath"/>.</summary>
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

    private readonly PatcherSettingsSourceProvider _settingsSourceProvider;
    private readonly IEnvironmentStateProvider _environmentProvider;
    /// <summary>Resolves the root path, subscribes to portable-mode/folder changes to re-resolve it, and
    /// creates the standard "home" directories (Settings, Asset Packs, Height/BodyGen Configurations, NPC
    /// Configuration, Record Templates) if missing.</summary>
    public SynthEBDPaths(
        PatcherSettingsSourceProvider settingsSourceProvider,
        IEnvironmentStateProvider environmentProvider)
    {
        _settingsSourceProvider = settingsSourceProvider;
        _environmentProvider = environmentProvider;

        GetRootPath();

        Observable.CombineLatest(
                _settingsSourceProvider.WhenAnyValue(x => x.UsePortableSettings),
                _settingsSourceProvider.WhenAnyValue(x => x.PortableSettingsFolder),
                (_, _) => { return 0; })
            .Skip(1) // don't re-evaluate during initialization
            .Subscribe(_ => {
                GetRootPath();
            })
            .DisposeWith(this);

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

    /// <summary>Directory holding per-profile measurement caches (one
    /// <c>&lt;ProfileId&gt;.measurement_cache.json</c> per <see cref="BodyTypeProfile"/>).
    /// Co-located under the standard Settings folder so they move with the user's settings
    /// folder when portable mode is toggled. Created lazily by the cache store on first save.</summary>
    public string MeasurementCacheDirPath => Path.Combine(_rootPath, settingsDirRelPath, "MeasurementCache");
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
    public string UpdateLogPath => Path.Combine(_rootPath, settingsDirRelPath, "UpdateLog.json");
    /// <summary>Destination folder for generated output (NIFs, scripts, etc.). Unlike the other paths
    /// this is assigned at runtime rather than derived from <c>_rootPath</c>.</summary>
    public string OutputDataFolder { get; set; }
    /// <summary>Path of the JSON that records which settings source (default vs portable) is active.</summary>
    public string SettingsSourcePath => Path.Combine(_rootPath, StandaloneSourceDirName, SettingsSourceFileName);
    /// <summary>Path of the JSON that records the chosen game environment source.</summary>
    public string EnvironmentSourcePath => Path.Combine(_rootPath, StandaloneSourceDirName, EnvironmentSourceDirName);

    /// <summary>Re-bases a path under <c>_rootPath</c> onto the default settings root, used to look up a
    /// shipped/default copy of a file when the active root lacks it.</summary>
    public string GetFallBackPath(string path)
    {
        var suffix = path.Remove(0, _rootPath.Length).Trim(Path.PathSeparator);
        return Path.Join(_settingsSourceProvider.DefaultSettingsRootPath, suffix);
    }

    /// <summary>Recomputes <see cref="_rootPath"/>: the portable folder (or, if blank, a <c>SynthEBD</c>
    /// subfolder of the data folder) when portable settings are initialized and enabled; otherwise the
    /// default settings root.</summary>
    public void GetRootPath()
    {
        if (_settingsSourceProvider.Initialized && _settingsSourceProvider.UsePortableSettings)
        {
            if (!_settingsSourceProvider.PortableSettingsFolder.IsNullOrWhitespace())
            {
                _rootPath = _settingsSourceProvider.PortableSettingsFolder;
            }
            else
            {
                _rootPath = Path.Combine(_environmentProvider.DataFolderPath, "SynthEBD");
            }
        }
        else
        {
            _rootPath = _settingsSourceProvider.DefaultSettingsRootPath; // is already synced to SourcePath
        }
    }
}
using System.IO;

namespace SynthEBD;

public class SettingsIO_OBody
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly BodySlideSettingMigrator _bodySlideSettingMigrator;
    private readonly SliderCatalogLoader _sliderCatalogLoader;
    private readonly BodySlideGroupClassifier _bodySlideGroupClassifier;
    public SettingsIO_OBody(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, BodySlideSettingMigrator bodySlideSettingMigrator, SliderCatalogLoader sliderCatalogLoader, BodySlideGroupClassifier bodySlideGroupClassifier)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _bodySlideSettingMigrator = bodySlideSettingMigrator;
        _sliderCatalogLoader = sliderCatalogLoader;
        _bodySlideGroupClassifier = bodySlideGroupClassifier;
    }

    /// <summary>
    /// Stage 4: load slider catalogs (override XMLs + shipped JSON fallbacks) and seed the
    /// <see cref="BodySlideGroupClassifier"/>. Safe to call repeatedly; replaces any previously loaded catalog.
    /// Must run before <see cref="Settings_OBody.ImportBodySlides"/>.
    /// </summary>
    public BodySlideGroupClassifier LoadSliderCatalogs(Settings_OBody settings)
    {
        var catalog = _sliderCatalogLoader.LoadCatalogs(settings);
        _bodySlideGroupClassifier.SetCatalog(catalog);
        if (catalog.BodyTypes.Count > 0)
        {
            _logger.LogMessage($"Loaded {catalog.BodyTypes.Count} BodySlide slider catalog(s) for classification.");
        }
        return _bodySlideGroupClassifier;
    }
    public Settings_OBody LoadOBodySettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading OBody settings from disk");
        Settings_OBody oBodySettings = new();

        loadSuccess = true;

        if (File.Exists(_paths.OBodySettingsPath))
        {
            oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(_paths.OBodySettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load O/AutoBody Settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.OBodySettingsPath)))
        {
            oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(_paths.GetFallBackPath(_paths.OBodySettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load O/AutoBody Settings. Error: " + exceptionStr);
            }
        }

        if (oBodySettings == null)
        {
            oBodySettings = new();
        }

        // Drain any legacy flat-descriptor data into the per-weight model. Safe no-op once SchemaVersion >= 1.
        _bodySlideSettingMigrator.MigrateIfNeeded(oBodySettings);

        foreach (var attributeGroup in _patcherState.GeneralSettings.AttributeGroups) // add any available attribute groups from the general patcher settings
        {
            if (!oBodySettings.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
            {
                oBodySettings.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
            }
        }
        _logger.LogStartupEventEnd("Loading OBody settings from disk");
        return oBodySettings;
    }

    public Dictionary<string, HashSet<string>> LoadDefaultBodySlideAnnotation()
    {
        Dictionary<string, HashSet<string>> output = new Dictionary<string, HashSet<string>>();

        string annotationDir = Path.Combine(_environmentProvider.InternalDataPath, "Default BodySlide Annotations");

        if (Directory.Exists(annotationDir))
        {
            var filePaths = Directory.GetFiles(annotationDir, "*.csv").OrderBy(f => f); // sort alphabetical https://stackoverflow.com/questions/6294275/sorting-the-result-of-directory-getfiles-in-c-sharp

            foreach (string path in filePaths)
            {
                string[] lines = System.IO.File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string[] columns = line.Split(',');
                        
                    if (columns.Length > 1)
                    {
                        string configName = columns[0].Trim();
                        HashSet<string> descriptors = new HashSet<string>();

                        for (int i = 1; i < columns.Length; i++ )
                        {
                            string descriptor = columns[i].Trim();
                            if (descriptor.Any())
                            {
                                descriptors.Add(columns[i].Trim());
                            }
                        }

                        if (output.ContainsKey(configName))
                        {
                            output[configName] = descriptors;
                        }
                        else
                        {
                            output.Add(configName, descriptors);
                        }
                    }
                }
            }
        }

        return output;
    }
}
using System.Collections.Generic;
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
    private readonly BodyTypeFingerprintScanner _bodyTypeFingerprintScanner;
    private readonly BodyTypeSliderExtractor _bodyTypeSliderExtractor;
    private readonly BsdFileParser _bsdFileParser;
    public SettingsIO_OBody(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, BodySlideSettingMigrator bodySlideSettingMigrator, SliderCatalogLoader sliderCatalogLoader, BodySlideGroupClassifier bodySlideGroupClassifier, BodyTypeFingerprintScanner bodyTypeFingerprintScanner, BodyTypeSliderExtractor bodyTypeSliderExtractor, BsdFileParser bsdFileParser)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _bodySlideSettingMigrator = bodySlideSettingMigrator;
        _sliderCatalogLoader = sliderCatalogLoader;
        _bodySlideGroupClassifier = bodySlideGroupClassifier;
        _bodyTypeFingerprintScanner = bodyTypeFingerprintScanner;
        _bodyTypeSliderExtractor = bodyTypeSliderExtractor;
        _bsdFileParser = bsdFileParser;
    }

    /// <summary>
    /// Seeds shipped Body-Type Registry defaults (if not already present), runs the fingerprint
    /// scanner to mark installed entries, derives each installed entry's slider catalog from its
    /// OSD/BSD files, and computes superset relationships. Also keeps the legacy slider-catalog
    /// path alive for the current classifier; the classifier rewrite (Stage 5) will switch to
    /// the registry. Safe to call repeatedly; must run before <see cref="Settings_OBody.ImportBodySlides"/>.
    /// </summary>
    public BodySlideGroupClassifier LoadSliderCatalogs(Settings_OBody settings)
    {
        if (settings == null) return _bodySlideGroupClassifier;

        MergeShippedRegistryDefaults(settings);

        var dataFolder = _environmentProvider.DataFolderPath.ToString() ?? string.Empty;
        _bodyTypeFingerprintScanner.ScanInstalled(dataFolder, settings.BodyTypeRegistry);

        var shapeDataRoot = string.IsNullOrEmpty(dataFolder)
            ? string.Empty
            : Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");
        var fallbackCatalogDir = Path.Combine(_environmentProvider.InternalDataPath, "SliderCatalogs");
        _bodyTypeSliderExtractor.PopulateResolvedSliders(shapeDataRoot, settings.BodyTypeRegistry, _bsdFileParser, fallbackCatalogDir);

        LogRegistrySummary(settings.BodyTypeRegistry);

        _bodySlideGroupClassifier.SetRegistry(settings.BodyTypeRegistry);
        return _bodySlideGroupClassifier;
    }

    /// <summary>
    /// Loads shipped registry entries from InternalData/SliderCatalogs/BodyTypeRegistry.json and
    /// reconciles them with <paramref name="settings"/>:
    ///   * Entry name not present       → added.
    ///   * Existing entry IsUserDefined → left untouched (user edits always win).
    ///   * Existing entry not user-defined → refreshed in place from the shipped copy, so
    ///     hardened fingerprints reach existing users on next load.
    /// </summary>
    private void MergeShippedRegistryDefaults(Settings_OBody settings)
    {
        settings.BodyTypeRegistry ??= new List<BodyTypeRegistryEntry>();

        string registryPath = Path.Combine(_environmentProvider.InternalDataPath, "SliderCatalogs", "BodyTypeRegistry.json");
        if (!File.Exists(registryPath))
        {
            _logger.LogMessage("BodyTypeRegistry: shipped defaults not found at " + registryPath);
            return;
        }

        var shipped = JSONhandler<List<BodyTypeRegistryEntry>>.LoadJSONFile(registryPath, out bool ok, out string err);
        if (!ok || shipped == null)
        {
            _logger.LogMessage("BodyTypeRegistry: failed to load shipped defaults: " + err);
            return;
        }

        var indexByName = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < settings.BodyTypeRegistry.Count; i++)
        {
            var entry = settings.BodyTypeRegistry[i];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Name) && !indexByName.ContainsKey(entry.Name))
            {
                indexByName[entry.Name] = i;
            }
        }

        int added = 0;
        int refreshed = 0;
        foreach (var def in shipped)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Name)) continue;
            def.IsUserDefined = false;

            if (!indexByName.TryGetValue(def.Name, out int existingIdx))
            {
                settings.BodyTypeRegistry.Add(def);
                indexByName[def.Name] = settings.BodyTypeRegistry.Count - 1;
                added++;
                continue;
            }

            // Skip if the user has taken ownership of this row.
            if (settings.BodyTypeRegistry[existingIdx]?.IsUserDefined == true) continue;

            settings.BodyTypeRegistry[existingIdx] = def;
            refreshed++;
        }

        if (added > 0 || refreshed > 0)
        {
            _logger.LogMessage($"BodyTypeRegistry: merged {added} new + refreshed {refreshed} shipped entr{(added + refreshed == 1 ? "y" : "ies")}.");
        }
    }

    private void LogRegistrySummary(List<BodyTypeRegistryEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;
        foreach (var e in entries)
        {
            if (e == null) continue;
            string installed = e.IsInstalled
                ? "installed"
                : (e.ResolvedSliders?.Count > 0 ? "not-installed (fallback)" : "not-installed");
            string superset = string.IsNullOrEmpty(e.SupersetOfBodyType) ? "" : $" ⊃ {e.SupersetOfBodyType}";
            _logger.LogMessage($"BodyTypeRegistry: {e.Name} [{e.Gender}] -- {installed}, {e.ResolvedSliders?.Count ?? 0} slider(s){superset}");
        }
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
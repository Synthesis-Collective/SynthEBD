using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace SynthEBD;

/// <summary>
/// Loads slider catalogs (the per-body-type list of valid slider names) used by
/// <see cref="BodySlideGroupClassifier"/>. Resolution order, for each body type referenced by either
/// the user override map or the shipped fallback directory:
///
///   1. <c>Settings_OBody.SliderCatalogOverridePaths[bodyType]</c> -- if set and the file exists.
///      Treated as a SliderCategories.xml file from a real BodySlide install.
///   2. <c>InternalData/SliderCatalogs/{bodyType}.json</c> -- shipped fallback in the patcher payload.
///
/// Stage 4: callers receive an empty <see cref="SliderCategoryCatalog"/> when nothing is configured;
/// the classifier short-circuits in that case so legacy &lt;Group&gt;-tag classification still runs.
/// </summary>
public class SliderCatalogLoader
{
    private readonly Logger _logger;
    private readonly IEnvironmentStateProvider _environmentProvider;

    public SliderCatalogLoader(Logger logger, IEnvironmentStateProvider environmentProvider)
    {
        _logger = logger;
        _environmentProvider = environmentProvider;
    }

    /// <summary>
    /// Loads catalogs into a fresh <see cref="SliderCategoryCatalog"/> based on the given OBody settings.
    /// Family compatibility from <see cref="Settings_OBody.BodyTypeFamilyCompatibility"/> is folded into
    /// each <see cref="BodyTypeCatalog.Family"/> set so the classifier can collapse aliases.
    /// </summary>
    public SliderCategoryCatalog LoadCatalogs(Settings_OBody settings)
    {
        var catalog = new SliderCategoryCatalog();
        if (settings == null) return catalog;

        // 1) User overrides (XML files from real BodySlide installations)
        if (settings.SliderCatalogOverridePaths != null)
        {
            foreach (var kv in settings.SliderCatalogOverridePaths)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (!File.Exists(kv.Value))
                {
                    _logger.LogMessage($"SliderCatalogLoader: override path for '{kv.Key}' does not exist: {kv.Value}");
                    continue;
                }
                var sliders = ParseSliderCategoriesXml(kv.Value);
                if (sliders.Count == 0)
                {
                    _logger.LogMessage($"SliderCatalogLoader: override file for '{kv.Key}' contained no <Slider> entries: {kv.Value}");
                    continue;
                }
                catalog.BodyTypes[kv.Key] = new BodyTypeCatalog
                {
                    BodyType = kv.Key,
                    Sliders = sliders,
                    // Gender defaults to Female; user can override via shipped fallback or future UI surface.
                    Gender = GuessGenderFromName(kv.Key),
                };
            }
        }

        // 2) Shipped fallback JSON files (one per body type) for any body type not already overridden
        try
        {
            string fallbackDir = Path.Combine(_environmentProvider.InternalDataPath, "SliderCatalogs");
            if (Directory.Exists(fallbackDir))
            {
                foreach (var file in Directory.GetFiles(fallbackDir, "*.json"))
                {
                    var fileModel = JSONhandler<SliderCatalogFile>.LoadJSONFile(file, out bool ok, out string err);
                    if (!ok || fileModel == null)
                    {
                        _logger.LogMessage($"SliderCatalogLoader: failed to load shipped catalog '{file}': {err}");
                        continue;
                    }
                    string bodyType = !string.IsNullOrWhiteSpace(fileModel.BodyType)
                        ? fileModel.BodyType
                        : Path.GetFileNameWithoutExtension(file);

                    if (catalog.BodyTypes.ContainsKey(bodyType)) continue; // override wins

                    var sliders = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    if (fileModel.Sliders != null)
                    {
                        foreach (var s in fileModel.Sliders)
                        {
                            if (!string.IsNullOrWhiteSpace(s)) sliders.Add(s);
                        }
                    }

                    var family = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    if (fileModel.Family != null)
                    {
                        foreach (var f in fileModel.Family)
                        {
                            if (!string.IsNullOrWhiteSpace(f)) family.Add(f);
                        }
                    }

                    catalog.BodyTypes[bodyType] = new BodyTypeCatalog
                    {
                        BodyType = bodyType,
                        Gender = fileModel.Gender,
                        Sliders = sliders,
                        Family = family,
                    };
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError("SliderCatalogLoader: error scanning shipped fallback directory: " + ex.Message);
        }

        // 3) Fold user family compatibility into each catalog entry
        if (settings.BodyTypeFamilyCompatibility != null)
        {
            foreach (var kv in settings.BodyTypeFamilyCompatibility)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                if (!catalog.BodyTypes.TryGetValue(kv.Key, out var entry)) continue;
                foreach (var alias in kv.Value)
                {
                    if (!string.IsNullOrWhiteSpace(alias)) entry.Family.Add(alias);
                }
            }
        }

        return catalog;
    }

    /// <summary>
    /// Parses a SliderCategories.xml file (BodySlide format) into a flat set of slider names.
    /// Mirrors <c>parse_slider_categories</c> in the Python reference -- iterate every &lt;Slider name="..."/&gt;
    /// element regardless of category nesting.
    /// </summary>
    public static HashSet<string> ParseSliderCategoriesXml(string path)
    {
        var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = XDocument.Load(path);
            foreach (var sliderEl in doc.Descendants("Slider"))
            {
                var nameAttr = sliderEl.Attribute("name");
                if (nameAttr != null && !string.IsNullOrWhiteSpace(nameAttr.Value))
                {
                    result.Add(nameAttr.Value);
                }
            }
        }
        catch
        {
            // Caller logs; return whatever we managed to parse (typically empty on a malformed file).
        }
        return result;
    }

    private static Gender GuessGenderFromName(string bodyType)
    {
        // Cheap heuristic: only HIMBO/SAM/SOS are male. Everything else defaults to female.
        // Stage 4 ships catalogs without gender metadata in the override path; the user can fix
        // miscategorization by editing the shipped JSON or future UI.
        if (string.IsNullOrEmpty(bodyType)) return Gender.Female;
        var s = bodyType.ToUpperInvariant();
        if (s.Contains("HIMBO") || s.Contains("SAM") || s.Contains("SOS")) return Gender.Male;
        return Gender.Female;
    }
}

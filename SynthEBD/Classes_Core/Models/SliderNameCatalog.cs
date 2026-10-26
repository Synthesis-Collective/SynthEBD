using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace SynthEBD;

/// <summary>
/// The BodySlide slider names known per body type, from two sources: the Body Type Registry's
/// resolved catalog (<see cref="BodyTypeRegistryEntry.ResolvedSliders"/>: the installed body's
/// ShapeData OSD/BSD scan, or the shipped fallback) and the slider entries found in the loaded
/// preset XMLs. Preset XMLs routinely embed outfit zap/squeeze sliders, so slider pickers offer the
/// catalog by default and the preset-only names on request (<see cref="BuildVisibleSliderNames"/>).
/// Rebuilt once the presets are loaded; also writes the Logs\SliderNameProvenance.txt diagnostic.
/// </summary>
public class SliderNameCatalog
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly Dictionary<string, HashSet<string>> _catalogByBodyType = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _presetContributorsByBodyType = new(StringComparer.OrdinalIgnoreCase);

    public SliderNameCatalog(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    /// <summary>Body types with at least one loaded preset, in first-seen order.</summary>
    public List<string> BodyTypesWithPresets { get; } = new();

    /// <summary>The registry catalog for <paramref name="bodyType"/> (empty when unknown).</summary>
    public IReadOnlyCollection<string> CatalogSliders(string bodyType)
        => _catalogByBodyType.TryGetValue(bodyType ?? "", out var names) ? names : Array.Empty<string>();

    /// <summary>Slider names found in <paramref name="bodyType"/>'s loaded preset XMLs.</summary>
    public IReadOnlyCollection<string> PresetSliders(string bodyType)
        => _presetContributorsByBodyType.TryGetValue(bodyType ?? "", out var names) ? names.Keys : Array.Empty<string>();

    /// <summary>Rebuilds from the loaded presets and the registry, and writes the provenance dump.</summary>
    public void Rebuild(IEnumerable<BodySlideSetting> presets, IEnumerable<BodyTypeRegistryEntry>? registry)
    {
        _catalogByBodyType.Clear();
        _presetContributorsByBodyType.Clear();
        BodyTypesWithPresets.Clear();

        foreach (var preset in presets ?? Enumerable.Empty<BodySlideSetting>())
        {
            var bodyType = preset?.SliderGroup;
            if (string.IsNullOrEmpty(bodyType)) continue; // can happen for manually renamed bodyslides
            if (!_presetContributorsByBodyType.TryGetValue(bodyType, out var contributorsBySlider))
            {
                contributorsBySlider = new(StringComparer.OrdinalIgnoreCase);
                _presetContributorsByBodyType[bodyType] = contributorsBySlider;
                BodyTypesWithPresets.Add(bodyType);
            }
            foreach (var slider in preset!.SliderValues.Keys)
            {
                if (!contributorsBySlider.TryGetValue(slider, out var contributors))
                {
                    contributors = new();
                    contributorsBySlider[slider] = contributors;
                }
                contributors.Add(preset.Label);
            }
        }

        var ordered = new List<string>();
        foreach (var entry in registry ?? Enumerable.Empty<BodyTypeRegistryEntry>())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || _catalogByBodyType.ContainsKey(entry.Name)) continue;
            _catalogByBodyType[entry.Name] = new HashSet<string>(entry.ResolvedSliders ?? new(), StringComparer.OrdinalIgnoreCase);
            ordered.Add(entry.Name);
        }
        foreach (var bodyType in BodyTypesWithPresets)
        {
            if (!ordered.Contains(bodyType, StringComparer.OrdinalIgnoreCase)) ordered.Add(bodyType);
        }
        WriteProvenance(ordered);
    }

    /// <summary>
    /// Computes the slider names a picker should offer: the catalog, plus every preset-contributed
    /// name when <paramref name="showAll"/> is true, plus all rule-referenced names unconditionally
    /// (so saved rules keep displaying their slider even when it isn't otherwise visible).
    /// Deduplicated case-insensitively and sorted.
    /// </summary>
    public static List<string> BuildVisibleSliderNames(IReadOnlyCollection<string> catalogSliders, IReadOnlyCollection<string> presetContributedSliders, IEnumerable<string> ruleReferencedSliders, bool showAll)
    {
        var names = new HashSet<string>(catalogSliders, StringComparer.OrdinalIgnoreCase);
        if (showAll)
        {
            names.UnionWith(presetContributedSliders);
        }
        foreach (var referenced in ruleReferencedSliders)
        {
            if (!string.IsNullOrWhiteSpace(referenced))
            {
                names.Add(referenced);
            }
        }
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Diff-reconciles <paramref name="target"/> to <paramref name="desiredSorted"/> using only
    /// RemoveAt/Move/Insert -- never Clear. A wholesale Clear would raise a Reset that nulls the
    /// SelectedItem of every bound ComboBox (destroying conditions' SliderName values); since
    /// rule-referenced names are always in the desired list, no selected item is ever removed.
    /// Comparison is case-insensitive.
    /// </summary>
    public static void ReconcileSliderNameCollection(ObservableCollection<string> target, IReadOnlyList<string> desiredSorted)
    {
        var desiredSet = new HashSet<string>(desiredSorted, StringComparer.OrdinalIgnoreCase);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < desiredSorted.Count; i++)
        {
            int existingIndex = -1;
            for (int j = i; j < target.Count; j++)
            {
                if (string.Equals(target[j], desiredSorted[i], StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex == i)
            {
                continue;
            }
            if (existingIndex > i)
            {
                target.Move(existingIndex, i);
            }
            else
            {
                target.Insert(i, desiredSorted[i]);
            }
        }
    }

    /// <summary>
    /// Startup diagnostic: writes Logs\SliderNameProvenance.txt listing, per body type, every slider
    /// name and where it came from -- the registry catalog and/or loaded preset XMLs (with the
    /// contributing preset labels). Preset-only names are the ones the catalog-first pickers hide
    /// behind "Show preset-only sliders", so this file answers "why is/isn't slider X offered".
    /// </summary>
    private void WriteProvenance(List<string> orderedBodyTypes)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Slider-name provenance dump - " + DateTime.Now);
            sb.AppendLine("Sources per body type:");
            sb.AppendLine("  catalog = BodyTypeRegistryEntry.ResolvedSliders (ShapeData OSD/BSD scan, or shipped fallback catalog when the body isn't detected as installed)");
            sb.AppendLine("  presets = slider entries parsed from loaded BodySlide preset XMLs into BodySlideSetting.SliderValues (contributing presets listed, capped at 5)");
            sb.AppendLine("A [PRESET-ONLY] mark means the name is NOT in the registry catalog - Slider condition pickers offer it only when 'Show preset-only sliders' is enabled (or a saved rule references it).");
            sb.AppendLine();

            var summary = new List<string>();
            foreach (var bodyType in orderedBodyTypes)
            {
                var catalog = _catalogByBodyType.TryGetValue(bodyType, out var c) ? c : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var fromPresets = _presetContributorsByBodyType.TryGetValue(bodyType, out var p) ? p : new(StringComparer.OrdinalIgnoreCase);

                int presetOnlyCount = fromPresets.Keys.Count(x => !catalog.Contains(x));
                summary.Add(bodyType + ": " + presetOnlyCount + " preset-only / " + fromPresets.Count + " preset-contributed / " + catalog.Count + " catalog");

                sb.AppendLine("=== " + bodyType + " - catalog: " + catalog.Count + ", preset-contributed: " + fromPresets.Count + ", preset-only: " + presetOnlyCount + " ===");
                foreach (var sliderEntry in fromPresets.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string mark = catalog.Contains(sliderEntry.Key) ? "also-in-catalog" : "PRESET-ONLY";
                    string contributors = string.Join("; ", sliderEntry.Value.Take(5));
                    if (sliderEntry.Value.Count > 5)
                    {
                        contributors += " (+" + (sliderEntry.Value.Count - 5) + " more)";
                    }
                    sb.AppendLine("  [" + mark + "] " + sliderEntry.Key + "  <=  " + contributors);
                }
                if (catalog.Any())
                {
                    sb.AppendLine("  catalog sliders: " + string.Join(", ", catalog.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
                }
                sb.AppendLine();
            }

            string filePath = Path.Combine(_paths.LogFolderPath, "SliderNameProvenance.txt");
            PatcherIO.CreateDirectoryIfNeeded(filePath, PatcherIO.PathType.File);
            File.WriteAllText(filePath, sb.ToString());
            _logger.LogMessage("Slider-name provenance: wrote " + filePath + " | " + string.Join(" | ", summary));
        }
        catch (Exception ex)
        {
            _logger.LogError("Slider-name provenance: failed to write dump: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }
}

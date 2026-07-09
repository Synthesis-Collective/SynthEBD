using DynamicData;
using DynamicData.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;

namespace SynthEBD;

/// <summary>
/// View model for the OBody BodySlide annotation-rules panel. Builds and edits per-body-type,
/// per-descriptor slider-classification rules and applies them (via <see cref="BodySlideAnnotator"/>)
/// to assign body-shape descriptors to the loaded BodySlide presets. Round-trips to
/// <see cref="OBodySettings.BodySlideClassificationRules"/>.
/// </summary>
public class VM_BodySlideAnnotator : VM
{
    private readonly PatcherState _patcherState;
    private readonly VM_BodyShapeDescriptorCreationMenu _oBodyDescriptorMenu;
    private readonly VM_BodySlidesMenu _bodySlideMenu;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;

    // Slider-name provenance: per body type, which source contributed each slider name the
    // annotator UI offers — the registry catalog (ResolvedSliders) vs loaded BodySlide preset
    // XMLs (with the contributing preset labels). Populated by InitializeBodySlideInfo. This
    // split drives the catalog-first slider pickers (preset XMLs routinely embed outfit
    // zap/squeeze sliders, so preset-contributed names are hidden behind a per-body-type
    // toggle), and is dumped to Logs\SliderNameProvenance.txt as a startup diagnostic.
    private readonly Dictionary<string, HashSet<string>> _catalogSlidersByGroup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _presetSlidersByGroup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tracks the displayed rule set's ShowPresetOnlySliders toggle so the preview rail's slider picker follows it; swapped on each body-type selection.</summary>
    private readonly SerialDisposable _displayedRuleSetSliderNamesSub = new();

    /// <summary>Autofac factory delegate for <see cref="VM_BodySlideAnnotator"/>.</summary>
    public delegate VM_BodySlideAnnotator Factory(VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu);
    /// <summary>Wires up the ApplyAnnotations command and the preview rail, routing the selected body type into the rail's preset list.</summary>
    public VM_BodySlideAnnotator(PatcherState patcherState, VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu, BodySlideAnnotator bodySlideAnnotator, Logger logger, IEnvironmentStateProvider environmentProvider, Func<VM_CharacterViewer> characterViewerFactory, PreviewNpcResolver previewNpcResolver, SynthEBDPaths paths, DescriptorDefaultSynchronizer descriptorDefaultSynchronizer)
    {
        _patcherState = patcherState;
        _oBodyDescriptorMenu = oBodyDescriptorMenu;
        _bodySlideMenu = bodySlideMenu;
        _bodySlideAnnotator = bodySlideAnnotator;
        _logger = logger;
        _paths = paths;
        DefaultSynchronizer = descriptorDefaultSynchronizer;
        descriptorDefaultSynchronizer.RegisterAnnotator(this);

        PreviewPanel = new VM_SliderAnnotatorPreviewPanel(logger, patcherState, environmentProvider, characterViewerFactory, previewNpcResolver, bodySlideMenu);
        PreviewPanel.DisposeWith(this);
        _displayedRuleSetSliderNamesSub.DisposeWith(this);

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ApplyAnnotations(null, null));

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DisplayedRuleSet))
            {
                PreviewPanel.SetBodyType(DisplayedRuleSet?.BodyTypeGroup, DisplayedRuleSet?.AvailableSliderNames);

                // Follow the selected body type's ShowPresetOnlySliders toggle so the preview
                // rail's slider picker stays in sync without resetting its preset list/selection.
                var watchedRuleSet = DisplayedRuleSet;
                _displayedRuleSetSliderNamesSub.Disposable = watchedRuleSet?
                    .WhenAnyValue(x => x.ShowPresetOnlySliders)
                    .Skip(1)
                    .Subscribe(_ => PreviewPanel.RefreshSliderNames(watchedRuleSet.AvailableSliderNames));
            }
        };
    }

    public string SelectedSliderGroup { get; set; }

    /// <summary>Keeps this menu's per-category default descriptors in lockstep with the
    /// Label by Measurements profiles of the same body type. The per-category rule-set VMs
    /// push their default edits through it (see
    /// <see cref="VM_DescriptorClassificationRuleSet.OnDefaultDescriptorValueChanged"/>).</summary>
    internal DescriptorDefaultSynchronizer DefaultSynchronizer { get; }

    public ObservableCollection<VM_SliderClassificationRulesByBodyType> AnnotationRules { get; set; } = new();

    public VM_SliderClassificationRulesByBodyType DisplayedRuleSet { get; set; }

    /// <summary>The right-rail preview panel: preset list, slider readout, CharacterViewer, and NPC-at-weight search.</summary>
    public VM_SliderAnnotatorPreviewPanel PreviewPanel { get; }

    public Dictionary<string, ObservableCollection<string>> SliderNamesByGroup { get; set; } = new();
    private List<SliderClassificationRulesByBodyType> _stashedUnloadedBodyTypeRules { get; set; } = new(); // for storing rules for descriptors that a user may have inadvertently removed
    public RelayCommand ApplyAnnotationsCommand { get; }

    /// <summary>
    /// Builds the annotator's body-type list from the canonical Body Type Registry (so every known
    /// body type is listed and editable regardless of which BodySlide presets are installed), then
    /// appends any extra groups found only among the loaded presets (e.g. the "Unknown" catch-all)
    /// so unclassified presets stay annotatable. Each body type's available slider names are the
    /// union of the registry's resolved catalog (ShapeData OSD/BSD + shipped fallback) and the names
    /// found in that body type's loaded presets. Body types with no loaded presets are flagged via
    /// <see cref="VM_SliderClassificationRulesByBodyType.HasLoadedPresets"/> so the UI can mark them.
    /// </summary>
    public void InitializeBodySlideInfo()
    {
        SliderNamesByGroup.Clear();
        AnnotationRules.Clear();
        _catalogSlidersByGroup.Clear();
        _presetSlidersByGroup.Clear();

        // Pass 1: collect the slider names present in the loaded BodySlide preset XMLs, keyed by the
        // body type (SliderGroup) the classifier assigned each preset. A key existing here is what
        // "has loaded presets" means below, and this map still drives the BodySlides menu filter.
        foreach (var templateVM in _bodySlideMenu.BodySlidesMale.And(_bodySlideMenu.BodySlidesFemale))
        {
            var template = templateVM.AssociatedModel;

            var currentSliderGroup = template.SliderGroup;

            if (currentSliderGroup == null) // can happen for manually renamed bodyslides
            {
                continue;
            }

            if (!SliderNamesByGroup.ContainsKey(currentSliderGroup))
            {
                SliderNamesByGroup.Add(currentSliderGroup, new ObservableCollection<string>());
            }

            var currentSliderNameList = SliderNamesByGroup[currentSliderGroup];

            // Slider-name provenance: record which presets carry each slider name (drives the
            // catalog-vs-preset-only picker split and the provenance dump).
            if (!_presetSlidersByGroup.TryGetValue(currentSliderGroup, out var contributorsBySlider))
            {
                contributorsBySlider = new(StringComparer.OrdinalIgnoreCase);
                _presetSlidersByGroup.Add(currentSliderGroup, contributorsBySlider);
            }

            foreach (var slider in template.SliderValues.Keys)
            {
                if (!currentSliderNameList.Contains(slider))
                {
                    currentSliderNameList.Add(slider);
                }

                if (!contributorsBySlider.TryGetValue(slider, out var contributors))
                {
                    contributors = new();
                    contributorsBySlider.Add(slider, contributors);
                }
                contributors.Add(template.Label);
            }
        }

        var loadedGroups = new HashSet<string>(SliderNamesByGroup.Keys, StringComparer.OrdinalIgnoreCase);

        // Pass 2: registry body types first (in registry order), then any loaded-only groups. Each
        // body type's rule-set VM receives its registry catalog and its preset-contributed slider
        // names separately — the VM shows the catalog by default and gates the preset-only names
        // (mostly outfit zap/squeeze sliders embedded in preset XMLs) behind ShowPresetOnlySliders.
        var registry = _patcherState.OBodySettings.BodyTypeRegistry ?? new List<BodyTypeRegistryEntry>();
        var orderedBodyTypes = new List<string>();
        var seenBodyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in registry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || !seenBodyTypes.Add(entry.Name))
            {
                continue;
            }
            orderedBodyTypes.Add(entry.Name);
            var catalogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry.ResolvedSliders != null)
            {
                foreach (var s in entry.ResolvedSliders) catalogNames.Add(s);
            }
            _catalogSlidersByGroup[entry.Name] = catalogNames;
        }

        foreach (var loadedGroup in SliderNamesByGroup.Keys)
        {
            if (seenBodyTypes.Add(loadedGroup))
            {
                orderedBodyTypes.Add(loadedGroup);
            }
        }

        foreach (var bodyType in orderedBodyTypes)
        {
            _catalogSlidersByGroup.TryGetValue(bodyType, out var catalogSliders);
            IReadOnlyCollection<string> presetContributedSliders = _presetSlidersByGroup.TryGetValue(bodyType, out var contributorsBySlider)
                ? contributorsBySlider.Keys.ToList()
                : Array.Empty<string>();
            bool hasLoadedPresets = loadedGroups.Contains(bodyType);
            AnnotationRules.Add(new VM_SliderClassificationRulesByBodyType(_oBodyDescriptorMenu, bodyType, catalogSliders ?? new HashSet<string>(), presetContributedSliders, this, hasLoadedPresets));
        }

        _bodySlideMenu.AvailableSliderGroups.Clear();
        _bodySlideMenu.AvailableSliderGroups.Add(VM_BodySlidesMenu.BodyTypeSelectionAll);
        Noggog.ListExt.AddRange(_bodySlideMenu.AvailableSliderGroups, SliderNamesByGroup.Keys);

        DumpSliderNameProvenance(orderedBodyTypes);
    }

    /// <summary>
    /// Startup diagnostic: writes Logs\SliderNameProvenance.txt listing, per body type, every
    /// slider name known to the annotator and where it came from — the registry catalog
    /// (BodyTypeRegistryEntry.ResolvedSliders) and/or loaded BodySlide preset XMLs (with the
    /// contributing preset labels). Preset-only names are the ones the catalog-first pickers
    /// hide behind ShowPresetOnlySliders, so this file answers "why is/isn't slider X offered".
    /// </summary>
    private void DumpSliderNameProvenance(List<string> orderedBodyTypes)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Slider-name provenance dump - " + DateTime.Now);
            sb.AppendLine("Sources per body type:");
            sb.AppendLine("  catalog = BodyTypeRegistryEntry.ResolvedSliders (ShapeData OSD/BSD scan, or shipped fallback catalog when the body isn't detected as installed)");
            sb.AppendLine("  presets = slider entries parsed from loaded BodySlide preset XMLs into BodySlideSetting.SliderValues (contributing presets listed, capped at 5)");
            sb.AppendLine("A [PRESET-ONLY] mark means the name is NOT in the registry catalog - the annotator's slider pickers offer it only when 'Show preset-only sliders' is enabled (or a saved rule references it).");
            sb.AppendLine();

            var summary = new List<string>();
            foreach (var bodyType in orderedBodyTypes)
            {
                if (!_catalogSlidersByGroup.TryGetValue(bodyType, out var catalog))
                {
                    catalog = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                if (!_presetSlidersByGroup.TryGetValue(bodyType, out var fromPresets))
                {
                    fromPresets = new(StringComparer.OrdinalIgnoreCase);
                }

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

    /// <summary>Loads persisted classification rules into the rule-set VMs by body type; rules for body types not present in the current BodySlide set are stashed so they aren't lost on save.</summary>
    public void CopyInFromModel()
    {
        InitializeBodySlideInfo();
        _stashedUnloadedBodyTypeRules.Clear();

        var bodyTypes = SliderNamesByGroup.Keys.ToHashSet().And(_patcherState.OBodySettings.BodySlideClassificationRules.Keys).Distinct().ToArray();

        foreach (var bodyType in bodyTypes)
        {
            SliderClassificationRulesByBodyType rulesByBodyType;
            if (_patcherState.OBodySettings.BodySlideClassificationRules.ContainsKey(bodyType))
            {
                rulesByBodyType = _patcherState.OBodySettings.BodySlideClassificationRules[bodyType];
            }
            else
            {
                rulesByBodyType= new SliderClassificationRulesByBodyType();
            }

            var correspondingVM = AnnotationRules.FirstOrDefault(x => x.BodyTypeGroup == bodyType);
            if (correspondingVM != null)
            {
                correspondingVM.CopyInFromModel(rulesByBodyType);
            }
            else
            {
                _stashedUnloadedBodyTypeRules.Add(rulesByBodyType);
            }
        }

        // Slider-name provenance: a name in a body type's picker that neither the registry catalog
        // nor any loaded preset contributed can only have been injected by a saved rule's
        // SliderName (rule-referenced names are always kept visible so existing rules never show
        // blank slider dropdowns). Surface these — they usually mean the referenced preset was
        // uninstalled or the rule carries a typo.
        foreach (var ruleSetVM in AnnotationRules)
        {
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_catalogSlidersByGroup.TryGetValue(ruleSetVM.BodyTypeGroup, out var catalogNames))
            {
                knownNames.UnionWith(catalogNames);
            }
            if (_presetSlidersByGroup.TryGetValue(ruleSetVM.BodyTypeGroup, out var fromPresets))
            {
                knownNames.UnionWith(fromPresets.Keys);
            }
            foreach (var name in ruleSetVM.AvailableSliderNames.Where(x => !knownNames.Contains(x)))
            {
                _logger.LogMessage("Slider-name provenance: " + ruleSetVM.BodyTypeGroup + ": slider '" + name + "' is offered only because a saved annotation rule references it.");
            }
        }
    }

    /// <summary>Serializes the per-body-type rule-set VMs (plus any stashed unloaded rules) into the classification-rules dictionary, warning on duplicate body-type keys.</summary>
    public Dictionary<string, SliderClassificationRulesByBodyType> DumpToModel()
    {
        Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules = new();
        foreach (var rule in AnnotationRules)
        {
            // Skip pristine body types that only appear because they're in the registry (no loaded
            // presets and nothing authored), so the settings file isn't padded with empty rule
            // skeletons for every registered body type. Preset-backed and authored rules always save.
            if (!rule.HasLoadedPresets && !rule.HasAuthoredContent())
            {
                continue;
            }

            if (!bodySlideClassificationRules.ContainsKey(rule.BodyTypeGroup))
            {
                bodySlideClassificationRules.Add(rule.BodyTypeGroup, rule.DumpToModel());
            }
            else
            {
                _logger.LogError("Warning: Saving Body Slide Annotation Rules from UI: Body Type " + rule.BodyTypeGroup + " has multiple copies in UI");
            }
        }
        foreach (var stashedRule in _stashedUnloadedBodyTypeRules)
        {
            if (!bodySlideClassificationRules.ContainsKey(stashedRule.BodyTypeGroup))
            {
                bodySlideClassificationRules.Add(stashedRule.BodyTypeGroup, stashedRule);
            }
            else
            {
                _logger.LogError("Warning: Saving Body Slide Annotation Rules from Stashed Rules: Body Type " + stashedRule.BodyTypeGroup + " appears to already exist");
            }
        }
        return bodySlideClassificationRules;
    }

    /// <summary>Runs the rule-based annotator over the loaded BodySlides (optionally filtered to one slider group and/or one descriptor category), refreshes border colors, and posts a status notification.</summary>
    public void ApplyAnnotations(string? specifiedSliderGroup, string? specifiedDescriptorCategory)
    {
        var targetVMs = _bodySlideMenu.BodySlidesMale.And(_bodySlideMenu.BodySlidesFemale).ToList();

        if (specifiedSliderGroup != null)
        {
            targetVMs = targetVMs.Where(x => x.AssociatedModel.SliderGroup == specifiedSliderGroup).ToList();
        }

        _bodySlideAnnotator.AnnotateBodySlides(targetVMs.Select(x => x.AssociatedModel).ToList(), this.DumpToModel(), _oBodyDescriptorMenu.DumpToViewModels().Flatten().Select(x => x.ID).ToHashSet(), true, specifiedDescriptorCategory);

        foreach (var targetVM in targetVMs)
        {
            if (targetVM.AssociatedModel.HasAnyDescriptors())
            {
                targetVM.InitializeBorderColor();
            }
        }

        _logger.CallTimedNotifyStatusUpdateAsync("Auto-Applied Annotations", 3, CommonColors.Yellow);
    }
}

/// <summary>
/// View model of a <see cref="SliderClassificationRulesByBodyType"/>: the full set of descriptor
/// classification rule-sets for one body type (e.g. CBBE, HIMBO).
/// </summary>
[DebuggerDisplay("{SliderGroup}: Rule List for {DescriptorClassifiers.Count} Descriptors")]
public class VM_SliderClassificationRulesByBodyType : VM // contains a list of rules for each descriptor
{
    /// <summary>Creates a per-descriptor rule-set VM for each descriptor shell in the subscribed menu and wires the ApplyAnnotations command scoped to this body type. <paramref name="catalogSliderNames"/> is the registry catalog; <paramref name="presetContributedSliderNames"/> are names found in loaded preset XMLs (shown only via <see cref="ShowPresetOnlySliders"/> when a catalog exists, since preset XMLs routinely embed outfit zap/squeeze sliders). <paramref name="hasLoadedPresets"/> is false for registry body types with no installed BodySlide presets — the rules stay editable (slider names come from the registry catalog) but there are no presets to apply/test against, which the UI surfaces in red.</summary>
    public VM_SliderClassificationRulesByBodyType(VM_BodyShapeDescriptorCreationMenu subscribedMenu, string bodyTypeGroup, IReadOnlyCollection<string> catalogSliderNames, IReadOnlyCollection<string> presetContributedSliderNames, VM_BodySlideAnnotator annotatorVM, bool hasLoadedPresets = true)
    {
        _subscribedDescriptorMenu = subscribedMenu;

        BodyTypeGroup = bodyTypeGroup;
        _catalogSliderNames = new HashSet<string>(catalogSliderNames, StringComparer.OrdinalIgnoreCase);
        _presetContributedSliderNames = new HashSet<string>(presetContributedSliderNames, StringComparer.OrdinalIgnoreCase);
        HasSliderCatalog = _catalogSliderNames.Any();
        PresetOnlySliderCount = _presetContributedSliderNames.Count(x => !_catalogSliderNames.Contains(x));
        HasLoadedPresets = hasLoadedPresets;

        AvailableSliderNames = new ObservableCollection<string>();
        RebuildAvailableSliderNames();

        foreach (var descriptorShell in _subscribedDescriptorMenu.TemplateDescriptors)
        {
            DescriptorClassifiers.Add(new(descriptorShell, AvailableSliderNames, annotatorVM, this));
        }

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => annotatorVM.ApplyAnnotations(BodyTypeGroup, null)
        );

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShowPresetOnlySliders))
            {
                RebuildAvailableSliderNames();
            }
        };
    }
    public string BodyTypeGroup { get; } // E.g. HIMBO, CBBE, etc

    private readonly HashSet<string> _catalogSliderNames;
    private readonly HashSet<string> _presetContributedSliderNames;

    /// <summary>True when the Body Type Registry supplied a slider catalog for this body type. Without one (e.g. the "Unknown" group), the pickers always show the full preset-contributed union and the toggle is hidden.</summary>
    public bool HasSliderCatalog { get; }

    /// <summary>How many preset-contributed names are absent from the catalog — the names <see cref="ShowPresetOnlySliders"/> reveals. Shown on the toggle's label.</summary>
    public int PresetOnlySliderCount { get; }

    /// <summary>
    /// When false (default) and a catalog exists, the slider pickers offer catalog sliders plus any
    /// names referenced by this body type's saved rules; when true, every name found in loaded
    /// preset XMLs is offered too (mostly outfit zap/squeeze sliders embedded in presets).
    /// Session-only. The preview rail's sort-slider picker follows this via VM_BodySlideAnnotator.
    /// </summary>
    public bool ShowPresetOnlySliders { get; set; } = false;

    /// <summary>The live slider-name list every picker binds (rule rows share this exact instance; the preview rail copies it). Rebuilt in place — never cleared wholesale, so open dropdowns keep their selections.</summary>
    public ObservableCollection<string> AvailableSliderNames { get; }

    /// <summary>Recomputes the visible slider names for the current toggle state and reconciles the shared collection to them.</summary>
    private void RebuildAvailableSliderNames()
    {
        bool showAll = ShowPresetOnlySliders || !HasSliderCatalog;
        var desired = BuildVisibleSliderNames(_catalogSliderNames, _presetContributedSliderNames, EnumerateReferencedSliderNames(), showAll);
        ReconcileSliderNameCollection(AvailableSliderNames, desired);
    }

    /// <summary>Every slider name currently referenced by this body type's rules, across all descriptor categories. These stay visible regardless of the toggle so no rule row's picker ever shows a blank selection.</summary>
    private IEnumerable<string> EnumerateReferencedSliderNames()
    {
        return DescriptorClassifiers
            .SelectMany(d => d.RuleList)
            .SelectMany(r => r.RuleListORlogic)
            .SelectMany(g => g.RuleListANDlogic)
            .Select(r => r.SliderName);
    }

    /// <summary>
    /// Computes the slider names a picker should offer: the catalog, plus every preset-contributed
    /// name when <paramref name="showAll"/> is true, plus all rule-referenced names unconditionally
    /// (so saved rules keep displaying their slider even when it isn't otherwise visible).
    /// Deduplicated case-insensitively and sorted. Public static for tests.
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
            if (!referenced.IsNullOrWhitespace())
            {
                names.Add(referenced);
            }
        }
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Diff-reconciles <paramref name="target"/> to <paramref name="desiredSorted"/> using only
    /// RemoveAt/Move/Insert — never Clear. A wholesale Clear would raise a Reset that nulls the
    /// SelectedItem of every bound ComboBox (destroying rule rows' SliderName values); since
    /// rule-referenced names are always in the desired list, no selected item is ever removed.
    /// Comparison is case-insensitive. Public static for tests.
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
    /// False when no loaded BodySlide preset XMLs classified to this body type. The rules remain
    /// editable — available slider names come from the Body Type Registry's resolved catalog — but
    /// there are no presets to apply or test the rules against yet. Drives the red "no presets
    /// loaded" cue in the annotator list and the warning banner in the rule editor.
    /// </summary>
    public bool HasLoadedPresets { get; }
    public ObservableCollection<VM_DescriptorClassificationRuleSet> DescriptorClassifiers { get; set; } = new();
    public VM_DescriptorClassificationRuleSet SelectedDescriptor { get; set; }
    private VM_BodyShapeDescriptorCreationMenu _subscribedDescriptorMenu { get; }
    public RelayCommand ApplyAnnotationsCommand { get; }
    private List<DescriptorClassificationRuleSet> _stashedUnloadedDescriptorRules { get; set; } = new(); // for storing rules for descriptors that a user may have inadvertently removed

    /// <summary>Loads each descriptor rule-set from the model into its matching child VM; rule-sets for descriptors not present in the UI are stashed to avoid loss on save.</summary>
    public void CopyInFromModel(SliderClassificationRulesByBodyType model)
    {
        _stashedUnloadedDescriptorRules.Clear();

        foreach (var perDescriptorRuleSet in model.DescriptorClassifiers)
        {
            var correspondingVM = DescriptorClassifiers.FirstOrDefault(x => x.DescriptorCategory == perDescriptorRuleSet.DescriptorCategory);
            if (correspondingVM != null)
            {
                correspondingVM.CopyInFromModel(perDescriptorRuleSet);
            }
            else
            {
                _stashedUnloadedDescriptorRules.Add(perDescriptorRuleSet);
            }
        }

        // Loaded rules may reference sliders outside the currently visible set (CreateFromModel
        // appends them unsorted); rebuild so they land in sorted position and survive future
        // toggle rebuilds via EnumerateReferencedSliderNames.
        RebuildAvailableSliderNames();
    }

    /// <summary>Serializes the child descriptor rule-set VMs (plus stashed unloaded rules) into a <see cref="SliderClassificationRulesByBodyType"/> model.</summary>
    public SliderClassificationRulesByBodyType DumpToModel()
    {
        SliderClassificationRulesByBodyType model = new();
        model.BodyTypeGroup = BodyTypeGroup;
        model.DescriptorClassifiers = DescriptorClassifiers.Select(x => x.DumpToModel()).ToList();
        model.DescriptorClassifiers.AddRange(_stashedUnloadedDescriptorRules);
        return model;
    }

    /// <summary>
    /// True when the user (or a loaded config) has authored anything for this body type — a default
    /// descriptor value or at least one classification rule — or when stashed unloaded descriptor
    /// rules are being carried. Lets the annotator skip persisting pristine registry-only body types.
    /// </summary>
    public bool HasAuthoredContent()
    {
        if (_stashedUnloadedDescriptorRules.Count > 0)
        {
            return true;
        }

        return DescriptorClassifiers.Any(d =>
            (d.DefaultDescriptorValue != null && !string.IsNullOrEmpty(d.DefaultDescriptorValue.Value))
            || d.RuleList.Any());
    }
}

/// <summary>
/// View model of a <see cref="DescriptorClassificationRuleSet"/>: the classification rules and default
/// value for a single descriptor category (e.g. "Build") within one body type.
/// </summary>
[DebuggerDisplay("{DescriptorCategory}: {RuleList.Count} Rule Groups")]
public class VM_DescriptorClassificationRuleSet : VM // rule set for a given descriptor
{
    /// <summary>Captures the descriptor category/values, refreshes the available default-value list, and wires the Add-rule-group and ApplyAnnotations (scoped to this category) commands.</summary>
    public VM_DescriptorClassificationRuleSet(VM_BodyShapeDescriptorShell subscribedDescriptorShell, ObservableCollection<string> availableSliderNames, VM_BodySlideAnnotator annotatorVM, VM_SliderClassificationRulesByBodyType parentVM)
    {
        _subscribedDescriptorShell = subscribedDescriptorShell;
        _annotatorVM = annotatorVM;
        _parentVM = parentVM;
        SubscribedDescriptors = subscribedDescriptorShell.Descriptors;
        DescriptorCategory = _subscribedDescriptorShell.Category;
        AvailableSliderNames = availableSliderNames;

        RefreshAvailableDefaults();

        AddNewRuleGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newRule = new VM_DescriptorAssignmentRuleSet(subscribedDescriptorShell, AvailableSliderNames, RuleList);
                var newRuleGroup = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, newRule.RuleListORlogic);
                newRuleGroup.RuleListANDlogic.Add(new(AvailableSliderNames, newRuleGroup.RuleListANDlogic));
                newRule.RuleListORlogic.Add(newRuleGroup);
                RuleList.Add(newRule);
            } 
        );

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => annotatorVM.ApplyAnnotations(parentVM.BodyTypeGroup, DescriptorCategory)
        );
    }

    private VM_BodyShapeDescriptorShell _subscribedDescriptorShell { get; }
    private readonly VM_BodySlideAnnotator _annotatorVM;
    private readonly VM_SliderClassificationRulesByBodyType _parentVM;
    public ObservableCollection<VM_BodyShapeDescriptor> SubscribedDescriptors { get; }
    public ObservableCollection<IHasValueString> AvailableDefaultDescriptors { get; set; } = new();
    public ObservableCollection<string> AvailableSliderNames { get; }
    public string DescriptorCategory { get; }
    public IHasValueString DefaultDescriptorValue { get; set; }
    public ObservableCollection<VM_DescriptorAssignmentRuleSet> RuleList { get; set; } = new();
    public RelayCommand ApplyAnnotationsCommand { get; }
    public RelayCommand AddNewRuleGroup { get; }

    /// <summary>Fody hook: fires on every <see cref="DefaultDescriptorValue"/> change — user edits
    /// via the ComboBox, hydration (<see cref="CopyInFromModel"/>), and the synchronizer's own
    /// writes alike — and forwards the new value to <see cref="DescriptorDefaultSynchronizer"/>,
    /// which keeps the Label by Measurements profiles of this body type in lockstep. The
    /// synchronizer drops the echo/hydration calls itself (suspension + re-entrancy guards), so
    /// this hook stays unconditional.</summary>
    private void OnDefaultDescriptorValueChanged()
    {
        _annotatorVM?.DefaultSynchronizer?.PushFromSliderRuleSet(
            _parentVM?.BodyTypeGroup ?? "", DescriptorCategory, DefaultDescriptorValue?.Value ?? "");
    }

    /// <summary>Synchronizer-side setter: selects the entry of <see cref="AvailableDefaultDescriptors"/>
    /// whose Value matches <paramref name="value"/> (the leading empty/dummy entry for null/blank,
    /// clearing the default). Returns false when a non-blank <paramref name="value"/> has no
    /// matching descriptor in this category's catalog — the caller logs; nothing is changed. Setting
    /// an equal value is a silent no-op so sync fan-out can't loop.</summary>
    public bool TrySetDefaultValueByName(string value)
    {
        string current = DefaultDescriptorValue?.Value ?? "";
        string target = value ?? "";
        if (string.Equals(current, target, StringComparison.Ordinal)) return true;

        if (string.IsNullOrEmpty(target))
        {
            DefaultDescriptorValue = AvailableDefaultDescriptors.FirstOrDefault(x => x.Value.IsNullOrEmpty());
            return true;
        }

        var match = AvailableDefaultDescriptors.FirstOrDefault(x => string.Equals(x?.Value, target, StringComparison.Ordinal));
        if (match == null) return false;
        DefaultDescriptorValue = match;
        return true;
    }

    /// <summary>Loads the default descriptor value and rebuilds the rule list from the model.</summary>
    public void CopyInFromModel(DescriptorClassificationRuleSet model)
    {
        DefaultDescriptorValue = _subscribedDescriptorShell.Descriptors.FirstOrDefault(x => x.Value == model.DefaultDescriptorValue);

        RuleList.Clear();
        foreach (var rulesForDescriptor in model.RuleList)
        {
            var rulesForDescriptorVM = new VM_DescriptorAssignmentRuleSet(_subscribedDescriptorShell, AvailableSliderNames, RuleList);
            rulesForDescriptorVM.CopyInFromModel(rulesForDescriptor);
            RuleList.Add(rulesForDescriptorVM);
        }
    }

    /// <summary>Serializes the category, default value, and rule list into a <see cref="DescriptorClassificationRuleSet"/> model.</summary>
    public DescriptorClassificationRuleSet DumpToModel()
    {
        DescriptorClassificationRuleSet model = new();
        model.DescriptorCategory = DescriptorCategory;
        model.DefaultDescriptorValue = DefaultDescriptorValue?.Value ?? String.Empty;
        model.RuleList = RuleList.Select(x => x.DumpToModel()).ToList();
        return model;
    }

    /// <summary>Syncs <see cref="AvailableDefaultDescriptors"/> with the subscribed descriptors, keeping a leading empty/dummy option and dropping entries no longer present.</summary>
    private void RefreshAvailableDefaults()
    {
        if (!AvailableDefaultDescriptors.Any(x => x.Value.IsNullOrEmpty()))
        {
            AvailableDefaultDescriptors.Insert(0, new DummyDescriptor());
        }

        foreach (var descriptor in SubscribedDescriptors)
        {
            if (!AvailableDefaultDescriptors.Contains(descriptor))
            {
                AvailableDefaultDescriptors.Add(descriptor);
            }
        }

        for (int i = 0; i < AvailableDefaultDescriptors.Count; i++)
        {
            var descriptor = AvailableDefaultDescriptors[i];
            if (descriptor is DummyDescriptor)
            {
                continue;
            }

            if (!SubscribedDescriptors.Contains(descriptor))
            {
                AvailableDefaultDescriptors.RemoveAt(i);
                i--;
            }
        }
    }

    /// <summary>Placeholder empty-value descriptor used as the "no default" option in the default-value dropdown.</summary>
    private class DummyDescriptor: IHasValueString
    {
        public string Value { get; set; } = string.Empty;
    }
}

/// <summary>
/// View model of a <see cref="DescriptorAssignmentRuleSet"/>: one descriptor value plus the OR-list of
/// AND-gated slider rule groups that, when matched, assign that value. Self-removes when its OR-list empties.
/// </summary>
public class VM_DescriptorAssignmentRuleSet : VM
{
    /// <summary>Captures the descriptor category/values, wires the Add-rule-set command, and auto-removes this rule-set from its parent when its OR-logic list becomes empty.</summary>
    public VM_DescriptorAssignmentRuleSet(VM_BodyShapeDescriptorShell subscribedDescriptorShell, ObservableCollection<string> availableSliderNames, ObservableCollection<VM_DescriptorAssignmentRuleSet> parentCollection)
    {
        DescriptorCategory = subscribedDescriptorShell.Category;
        SubscribedDescriptorValues = subscribedDescriptorShell.Descriptors;
        AvailableSliderNames = availableSliderNames;

        RuleListORlogic.ToObservableChangeSet().Subscribe(x =>
        {
            if (!RuleListORlogic.Any())
            {
                parentCollection.Remove(this);
            }
        }).DisposeWith(this);

        AddNewRuleSet = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newRuleGroup = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, RuleListORlogic);
                newRuleGroup.RuleListANDlogic.Add(new(AvailableSliderNames, newRuleGroup.RuleListANDlogic));
                RuleListORlogic.Add(newRuleGroup);
            }
        );
    }

    public string DescriptorCategory { get; }
    public ObservableCollection<VM_BodyShapeDescriptor> SubscribedDescriptorValues { get; }
    public ObservableCollection<string> AvailableSliderNames { get; }
    public VM_BodyShapeDescriptor SelectedDescriptorValue { get; set; }
    public ObservableCollection<VM_AndGatedSliderRuleGroup> RuleListORlogic { get; set; } = new();
    public RelayCommand AddNewRuleSet { get; }

    /// <summary>Loads the selected descriptor value and the OR-list of AND-gated rule groups from the model.</summary>
    public void CopyInFromModel(DescriptorAssignmentRuleSet model)
    {
        SelectedDescriptorValue = SubscribedDescriptorValues.FirstOrDefault(x => x.Value == model.SelectedDescriptorValue);
        foreach (var ruleSet in model.RuleListORlogic)
        {
            var ruleSetVM = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, RuleListORlogic);
            ruleSetVM.CopyInFromModel(ruleSet);
            RuleListORlogic.Add(ruleSetVM);
        }
    }

    /// <summary>Serializes the selected descriptor value and OR-list of rule groups into a <see cref="DescriptorAssignmentRuleSet"/> model.</summary>
    public DescriptorAssignmentRuleSet DumpToModel()
    {
        var model = new DescriptorAssignmentRuleSet();
        model.RuleListORlogic = RuleListORlogic.Select(x => x.DumpToModel()).ToList();
        model.SelectedDescriptorValue = SelectedDescriptorValue?.Value ?? String.Empty;
        return model;
    }
}

/// <summary>
/// View model of an <see cref="AndGatedSliderRuleGroup"/>: a set of slider rules that must all match
/// (AND logic) for the group to fire. Self-removes when its rule list empties.
/// </summary>
[DebuggerDisplay("AND-gated Rule List: Count = {RuleListANDlogic.Count}")]
public class VM_AndGatedSliderRuleGroup : VM
{
    /// <summary>Wires the Add-rule command and auto-removes this group from its parent when its AND-logic rule list becomes empty.</summary>
    public VM_AndGatedSliderRuleGroup(ObservableCollection<string> availableSliderNames, ObservableCollection<VM_AndGatedSliderRuleGroup> parentCollection)
    {
        AvailableSliderNames = availableSliderNames;

        RuleListANDlogic.ToObservableChangeSet().Subscribe(x =>
            {
                if (!RuleListANDlogic.Any())
                {
                    parentCollection.Remove(this);
                }
        }).DisposeWith(this);

        AddNewRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RuleListANDlogic.Add(new VM_SliderClassificationRule(AvailableSliderNames, RuleListANDlogic))
        );
    }
    public ObservableCollection<string> AvailableSliderNames { get; }
    public ObservableCollection<VM_SliderClassificationRule> RuleListANDlogic { get; set; } = new();
    public RelayCommand AddNewRule { get; }
    /// <summary>True when this group has no rules, or none of its rules are non-empty.</summary>
    public bool IsEmpty => !RuleListANDlogic.Any() || !RuleListANDlogic.Any(x => !x.IsEmpty);

    /// <summary>Loads the AND-logic slider rules from the model.</summary>
    public void CopyInFromModel(AndGatedSliderRuleGroup model)
    {
        foreach (var andGatedRuleGroup in model.RuleListANDlogic)
        {
            RuleListANDlogic.Add(VM_SliderClassificationRule.CreateFromModel(andGatedRuleGroup, AvailableSliderNames, RuleListANDlogic));
        }
    }

    /// <summary>Serializes the AND-logic slider rules into an <see cref="AndGatedSliderRuleGroup"/> model.</summary>
    public AndGatedSliderRuleGroup DumpToModel()
    {
        var model = new AndGatedSliderRuleGroup();
        model.RuleListANDlogic = RuleListANDlogic.Select(x => x.DumpToModel()).ToList();
        return model;
    }
}


/// <summary>
/// View model of a single <see cref="SliderClassificationRule"/>: one slider name, type (big/small),
/// comparator, and threshold value — the atomic predicate of the annotation rule engine.
/// </summary>
[DebuggerDisplay("{SliderName} ({SliderType}) {Comparator} {Value}")]
public class VM_SliderClassificationRule : VM
{
    /// <summary>Captures the available slider names and wires the Delete and Add-AND-rule commands.</summary>
    public VM_SliderClassificationRule(ObservableCollection<string> sliderNames, ObservableCollection<VM_SliderClassificationRule> parentCollection)
    {
        AvaliableSliderNames = sliderNames;

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentCollection.Remove(this)
        );

        AddANDRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentCollection.Add(new(sliderNames, parentCollection))
        );
    }

    public string SliderName { get; set; }

    public BodySliderType SliderType { get; set; }

    public string Comparator { get; set; } = "=";
    public int Value { get; set; }
    public ObservableCollection<string> AvaliableSliderNames { get; set; }
    /// <summary>True when no slider name has been chosen yet.</summary>
    public bool IsEmpty => SliderName.IsNullOrWhitespace();
    public RelayCommand DeleteMe { get; }
    public RelayCommand AddANDRule { get; }

    /// <summary>Builds a <see cref="VM_SliderClassificationRule"/> from its model, adding the model's slider name to the available list if missing.</summary>
    public static VM_SliderClassificationRule CreateFromModel(SliderClassificationRule model, ObservableCollection<string> sliderNames, ObservableCollection<VM_SliderClassificationRule> parentCollection)
    {
        var sliderClassificationRule = new VM_SliderClassificationRule(sliderNames, parentCollection);
        if (!sliderClassificationRule.AvaliableSliderNames.Contains(model.SliderName))
        {
            sliderClassificationRule.AvaliableSliderNames.Add(model.SliderName);
        }
        sliderClassificationRule.SliderName = model.SliderName;
        sliderClassificationRule.SliderType = model.SliderType;
        sliderClassificationRule.Comparator = model.Comparator;
        sliderClassificationRule.Value = model.Value;
        return sliderClassificationRule;
    }

    /// <summary>Serializes this rule into a <see cref="SliderClassificationRule"/> model.</summary>
    public SliderClassificationRule DumpToModel()
    {
        return new()
        {
            SliderName = SliderName,
            SliderType = SliderType,
            Comparator = Comparator,
            Value = Value
        };
    }

    public ObservableCollection<string> SliderNames { get; set; }

    public List<string> ComparatorOptions { get; } = new()
    {
        "=",
        "<=",
        "<",
        ">=",
        ">"
    };
}

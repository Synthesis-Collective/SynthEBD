using System.Collections.ObjectModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for the Label by Sliders preview rail: a sortable preset list (name + one chosen
/// slider's Low/High/Interpolated values), a per-preset slider-value readout, an embedded
/// CharacterViewer, and an "NPCs at weight" search for picking the previewed NPC. Owned by
/// <see cref="VM_BodySlideAnnotator"/>, which routes the annotator's selected body type into
/// <see cref="SetBodyType"/> so the preset list and slider picker track the rule editor.
/// <para>
/// Preview-NPC policy mirrors <see cref="VM_BodySlideSetting.RefreshPreview"/> /
/// <see cref="VM_BodyTypeProfileEditor"/>: an explicit override wins, otherwise the OBody Misc
/// "Preview NPC by Weight" table supplies the NPC for the current gender — except that this
/// panel's weight is continuous (0-100), so the table is read at the <i>nearest</i> configured
/// weight slot rather than by exact key.
/// </para>
/// </summary>
public class VM_SliderAnnotatorPreviewPanel : VM
{
    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    private readonly VM_BodySlidesMenu _bodySlideMenu;
    private readonly PreviewNpcResolver _previewNpcResolver;

    /// <summary>Guards overlapping <see cref="RefreshPreviewAsync"/> calls (same pattern as
    /// <see cref="VM_BodyTypeProfileEditor"/>): only the newest generation applies its BodySlide.</summary>
    private int _refreshPreviewGeneration;

    /// <summary>Body type (SliderGroup) whose presets are listed. Set via <see cref="SetBodyType"/>; empty = no body type selected in the annotator yet.</summary>
    private string _currentBodyType = "";

    /// <summary>Unfiltered preset rows for the current (body type, gender); <see cref="FilteredPresetRows"/> applies <see cref="PresetFilterText"/>.</summary>
    private readonly List<VM_AnnotatorPresetRow> _presetRows = new();

    /// <summary>Unfiltered readout rows for the selected preset; <see cref="SliderReadoutRows"/> applies <see cref="SliderReadoutFilterText"/>.</summary>
    private readonly List<VM_AnnotatorSliderValueRow> _readoutRows = new();

    /// <summary>True once <see cref="Prime"/> ran (first view load), so re-navigations don't re-prime.</summary>
    private bool _primed;

    public VM_SliderAnnotatorPreviewPanel(
        Logger logger,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        Func<VM_CharacterViewer> characterViewerFactory,
        PreviewNpcResolver previewNpcResolver,
        VM_BodySlidesMenu bodySlideMenu)
    {
        _logger = logger;
        _patcherState = patcherState;
        _bodySlideMenu = bodySlideMenu;
        _previewNpcResolver = previewNpcResolver;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        // Lock the model scale like the Body Type Profile editor does: this rail exists to
        // compare body shapes across presets/weights, so the preview NPC's record Height
        // scaling would only add noise between NPC swaps.
        CharacterViewer.HeightOverride = 1.0f;
        CharacterViewer.DisposeWith(this);

        // Navigating off the OBody menu and back recreates UC_CharacterViewer with a fresh GL
        // context; re-issue the last preview so the user doesn't have to re-click the preset.
        CharacterViewer.GlContextReset += () =>
        {
            if (SelectedPresetRow != null || !PreviewNpcOverride.IsNull) _ = RefreshPreviewAsync();
        };

        environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        FindNpcsCommand = new RelayCommand(
            canExecute: _ => !IsFindingNpcs,
            execute: _ => _ = FindNpcCandidatesAsync());

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(PreviewGender):
                    // Presets and NPC candidates are both gendered; the candidate list is
                    // cleared (not re-scanned) so a stale-gender NPC can't be clicked.
                    RebuildPresetRows();
                    NpcCandidates.Clear();
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(PresetFilterText):
                    RebuildFilteredPresetRows();
                    break;
                case nameof(SelectedSliderName):
                    RefreshPresetRowSliderValues();
                    break;
                case nameof(PreviewWeight):
                    RefreshPresetRowSliderValues();
                    RefreshReadoutInterpolation();
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(SelectedPresetRow):
                    RebuildSliderReadout();
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(SliderReadoutFilterText):
                    RebuildFilteredReadoutRows();
                    break;
                case nameof(PreviewNpcOverride):
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(SelectedNpcCandidate):
                    // Clicking a search result routes through the override picker so the
                    // picker's textbox shows who is being previewed.
                    if (SelectedNpcCandidate != null)
                    {
                        PreviewNpcOverride = SelectedNpcCandidate.NpcFormKey;
                    }
                    break;
            }
        };
    }

    public VM_CharacterViewer CharacterViewer { get; }

    public Gender PreviewGender { get; set; } = Gender.Female;

    /// <summary>Preview weight (0-100, continuous). Drives the viewer morph, both Interpolated
    /// readout columns, and the default-NPC / NPC-search weight.</summary>
    public int PreviewWeight { get; set; } = 50;

    public string PresetFilterText { get; set; } = "";

    /// <summary>Slider whose Low/High/Interpolated values populate the preset list's value columns. Null = columns blank.</summary>
    public string? SelectedSliderName { get; set; }

    /// <summary>Slider names available for <see cref="SelectedSliderName"/> — the annotator body type's registry catalog ∪ loaded-preset sliders.</summary>
    public ObservableCollection<string> AvailableSliderNames { get; } = new();

    public ObservableCollection<VM_AnnotatorPresetRow> FilteredPresetRows { get; } = new();

    public VM_AnnotatorPresetRow? SelectedPresetRow { get; set; }

    public string SliderReadoutFilterText { get; set; } = "";

    public ObservableCollection<VM_AnnotatorSliderValueRow> SliderReadoutRows { get; } = new();

    /// <summary>Optional NPC override. When null, the OBody Misc per-weight preview NPC table supplies the NPC (nearest weight slot).</summary>
    public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;

    /// <summary>Exposed for the NPC picker's scoped-types filter.</summary>
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    /// <summary>Current environment link cache; bound by the NPC picker's LinkCache.</summary>
    public ILinkCache? lk { get; private set; }

    public ObservableCollection<PreviewNpcCandidateInfo> NpcCandidates { get; } = new();

    public PreviewNpcCandidateInfo? SelectedNpcCandidate { get; set; }

    public bool IsFindingNpcs { get; private set; }

    public RelayCommand FindNpcsCommand { get; }

    /// <summary>
    /// First-load hook (called from the view's Loaded handler): rebuilds the preset list and
    /// shows the default preview NPC so the rail isn't blank before any preset is clicked.
    /// </summary>
    public void Prime()
    {
        if (_primed) return;
        _primed = true;
        RebuildPresetRows();
        _ = RefreshPreviewAsync();
    }

    /// <summary>
    /// Routes the annotator's selected body type into the panel: repopulates the slider picker
    /// (preserving the current selection when the new body type also has that slider) and the
    /// preset list. Null/empty <paramref name="bodyTypeGroup"/> empties both.
    /// </summary>
    public void SetBodyType(string? bodyTypeGroup, IEnumerable<string>? availableSliderNames)
    {
        _currentBodyType = bodyTypeGroup ?? "";

        var previousSlider = SelectedSliderName;
        AvailableSliderNames.Clear();
        if (availableSliderNames != null)
        {
            foreach (var slider in availableSliderNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                AvailableSliderNames.Add(slider);
            }
        }
        SelectedSliderName = previousSlider != null && AvailableSliderNames.Contains(previousSlider) ? previousSlider : null;

        RebuildPresetRows();
    }

    /// <summary>Rebuilds the unfiltered preset rows from the gendered BodySlides list, gated to the current body type.</summary>
    private void RebuildPresetRows()
    {
        _presetRows.Clear();
        SelectedPresetRow = null;

        if (!_currentBodyType.IsNullOrWhitespace())
        {
            var source = PreviewGender == Gender.Male ? _bodySlideMenu.BodySlidesMale : _bodySlideMenu.BodySlidesFemale;
            if (source != null)
            {
                foreach (var placeHolder in source.OrderBy(p => p?.Label ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    if (placeHolder?.AssociatedModel == null) continue;
                    if (!string.Equals(placeHolder.AssociatedModel.SliderGroup, _currentBodyType, StringComparison.OrdinalIgnoreCase)) continue;
                    _presetRows.Add(new VM_AnnotatorPresetRow(placeHolder));
                }
            }
        }

        RefreshPresetRowSliderValues();
        RebuildFilteredPresetRows();
    }

    /// <summary>Re-applies the text filter to the unfiltered preset rows.</summary>
    private void RebuildFilteredPresetRows()
    {
        FilteredPresetRows.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        foreach (var row in _presetRows)
        {
            if (filter.Length == 0 || row.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                FilteredPresetRows.Add(row);
            }
        }
    }

    /// <summary>Recomputes every preset row's Low/High/Interpolated columns for the chosen slider at the current weight.</summary>
    private void RefreshPresetRowSliderValues()
    {
        foreach (var row in _presetRows)
        {
            row.RefreshSliderValues(SelectedSliderName, PreviewWeight);
        }
    }

    /// <summary>Rebuilds the full slider readout for the newly selected preset.</summary>
    private void RebuildSliderReadout()
    {
        _readoutRows.Clear();

        var sliderValues = SelectedPresetRow?.PlaceHolder.AssociatedModel?.SliderValues;
        if (sliderValues != null)
        {
            foreach (var kvp in sliderValues.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (kvp.Value == null) continue;
                _readoutRows.Add(new VM_AnnotatorSliderValueRow(kvp.Key, kvp.Value, PreviewWeight));
            }
        }

        RebuildFilteredReadoutRows();
    }

    /// <summary>Re-applies the text filter to the readout rows.</summary>
    private void RebuildFilteredReadoutRows()
    {
        SliderReadoutRows.Clear();
        string filter = SliderReadoutFilterText?.Trim() ?? "";
        foreach (var row in _readoutRows)
        {
            if (filter.Length == 0 || row.SliderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SliderReadoutRows.Add(row);
            }
        }
    }

    /// <summary>Recomputes the readout rows' Interpolated column at the current weight.</summary>
    private void RefreshReadoutInterpolation()
    {
        foreach (var row in _readoutRows)
        {
            row.RefreshInterpolation(PreviewWeight);
        }
    }

    /// <summary>
    /// Scans the load order (background thread) for NPCs of the previewed gender at exactly the
    /// current weight, using the same eligibility rules as the Misc-settings Auto-pick, and fills
    /// the candidate list.
    /// </summary>
    private async Task FindNpcCandidatesAsync()
    {
        if (IsFindingNpcs) return;
        IsFindingNpcs = true;
        var gender = PreviewGender;
        int weight = PreviewWeight;
        try
        {
            var candidates = await Task.Run(() => _previewNpcResolver.FindNpcsAtWeight(gender, weight));
            NpcCandidates.Clear();
            SelectedNpcCandidate = null;
            foreach (var candidate in candidates)
            {
                NpcCandidates.Add(candidate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("SliderAnnotatorPreviewPanel: NPC search failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
        finally
        {
            IsFindingNpcs = false;
        }
    }

    /// <summary>
    /// Loads the preview NPC (override → per-weight table at the nearest slot) and applies the
    /// selected preset's morphs at the current weight. Runs with a generation guard so rapid
    /// input changes can't apply a stale preset over a newer one. Unlike the profile editor,
    /// the NPC loads even with no preset selected, so the rail shows the default NPC on open.
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        int myGen = ++_refreshPreviewGeneration;
        var snapshotPreset = SelectedPresetRow?.PlaceHolder.AssociatedModel;
        int snapshotWeight = PreviewWeight;
        Gender snapshotGender = PreviewGender;
        FormKey snapshotNpcOverride = PreviewNpcOverride;

        try
        {
            if (lk == null) return;

            FormKey npc = snapshotNpcOverride;
            if (npc.IsNull)
            {
                npc = ResolveDefaultPreviewNpc(snapshotGender, snapshotWeight);
            }

            if (npc.IsNull)
            {
                _logger.LogMessage("SliderAnnotatorPreviewPanel: no preview NPC configured near weight " + snapshotWeight + " (" + snapshotGender + ") — set one in OBody Misc Settings or pick an NPC override.");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);

            if (myGen != _refreshPreviewGeneration) return;

            if (snapshotPreset != null)
            {
                CharacterViewer.ApplyBodySlide(snapshotPreset, snapshotWeight);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("SliderAnnotatorPreviewPanel.RefreshPreviewAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>
    /// Resolves the default preview NPC for <paramref name="gender"/> from the OBody Misc
    /// per-weight table at the configured slot nearest <paramref name="weight"/>. The panel's
    /// weight is continuous, so nearest-slot (ties round down, like
    /// <see cref="PerWeightDescriptorLookup"/>) replaces the exact-key lookup the fixed-slot
    /// previews use. Slots whose NPC is unset for the gender are skipped in favor of the next
    /// nearest configured slot.
    /// </summary>
    private FormKey ResolveDefaultPreviewNpc(Gender gender, int weight)
    {
        var table = _patcherState?.OBodySettings?.PreviewNpcs?.WeightPreviewNpcs;
        if (table == null || table.Count == 0) return FormKey.Null;

        foreach (var slotWeight in OrderWeightKeysByProximity(table.Keys, weight))
        {
            var pair = table[slotWeight];
            if (pair == null) continue;
            var npc = gender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
            if (!npc.IsNull) return npc;
        }
        return FormKey.Null;
    }

    /// <summary>Orders weight keys by distance to <paramref name="weight"/>; ties prefer the lower key. Public static for tests.</summary>
    public static IEnumerable<int> OrderWeightKeysByProximity(IEnumerable<int> keys, int weight)
    {
        return keys.OrderBy(k => Math.Abs(k - weight)).ThenBy(k => k);
    }
}

/// <summary>
/// One preset row in the Label by Sliders preview rail's preset list: the placeholder's label
/// plus the currently chosen slider's Low / High / Interpolated values (null when the preset
/// doesn't carry that slider, shown as blank). The value properties are plain bindables so the
/// DataGrid's column sorting works on them directly.
/// </summary>
public class VM_AnnotatorPresetRow : VM
{
    public VM_AnnotatorPresetRow(VM_BodySlidePlaceHolder placeHolder)
    {
        PlaceHolder = placeHolder;
    }

    public VM_BodySlidePlaceHolder PlaceHolder { get; }

    public string Label => PlaceHolder.Label ?? "";

    /// <summary>Chosen slider's authored weight-0 value; null when the preset lacks the slider.</summary>
    public int? Low { get; private set; }

    /// <summary>Chosen slider's authored weight-100 value; null when the preset lacks the slider.</summary>
    public int? High { get; private set; }

    /// <summary>Chosen slider's linear blend at the panel's preview weight; null when the preset lacks the slider.</summary>
    public float? Interpolated { get; private set; }

    /// <summary>Recomputes the three value columns for <paramref name="sliderName"/> at <paramref name="weight"/> (null slider blanks them).</summary>
    public void RefreshSliderValues(string? sliderName, int weight)
    {
        var sliderValues = PlaceHolder.AssociatedModel?.SliderValues;
        if (sliderName != null && sliderValues != null && sliderValues.TryGetValue(sliderName, out var slider) && slider != null)
        {
            Low = slider.Small;
            High = slider.Big;
            Interpolated = BodySlideAnnotator.InterpolateSliderValue(slider, weight);
        }
        else
        {
            Low = null;
            High = null;
            Interpolated = null;
        }
    }
}

/// <summary>
/// One row of the selected preset's slider readout: slider name with its authored Low/High
/// endpoints and the interpolated value at the panel's preview weight.
/// </summary>
public class VM_AnnotatorSliderValueRow : VM
{
    private readonly BodySlideSlider _slider;

    public VM_AnnotatorSliderValueRow(string sliderName, BodySlideSlider slider, int weight)
    {
        SliderName = sliderName;
        _slider = slider;
        Low = slider.Small;
        High = slider.Big;
        Interpolated = BodySlideAnnotator.InterpolateSliderValue(slider, weight);
    }

    public string SliderName { get; }
    public int Low { get; }
    public int High { get; }
    public float Interpolated { get; private set; }

    /// <summary>Recomputes <see cref="Interpolated"/> when the panel's preview weight changes.</summary>
    public void RefreshInterpolation(int weight)
    {
        Interpolated = BodySlideAnnotator.InterpolateSliderValue(_slider, weight);
    }
}

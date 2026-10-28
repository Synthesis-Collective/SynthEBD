using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// The Auto-Annotation editor's Sliders tab: a preset table showing one picked slider's values, the
/// selected preset's full slider readout with a morph heatmap, and live slider editing. Edits go to a
/// working copy that the editor's viewer previews immediately (and that Slider conditions and live
/// measurements read while it is unsaved); Save writes it back to the preset's SliderPresets XML,
/// Save As writes it under a new name into a new or existing XML.
/// <para>Shares the editor's viewer, preset selection, gender and weight, so picking a preset here
/// is the same as picking it in the right rail.</para>
/// </summary>
public class VM_AutoAnnotationSliders : VM
{
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly Logger? _logger;
    private readonly SliderNameCatalog? _catalog;
    private readonly Func<VM_SettingsOBody>? _oBodyVM;
    private readonly List<VM_SliderPresetRow> _presetRows = new();
    private readonly List<VM_EditableSliderRow> _readoutRows = new();
    private bool _syncingSelection;

    public VM_AutoAnnotationSliders(VM_BodyTypeProfileEditor editor, Logger? logger, SliderNameCatalog? catalog, Func<VM_SettingsOBody>? oBodyVM)
    {
        _editor = editor;
        _logger = logger;
        _catalog = catalog;
        _oBodyVM = oBodyVM;

        SaveCommand = new RelayCommand(canExecute: _ => IsDirty && CanSaveInPlace, execute: _ => Save());
        SaveAsCommand = new RelayCommand(canExecute: _ => WorkingCopy != null, execute: _ => SaveAs());
        RevertCommand = new RelayCommand(canExecute: _ => IsDirty, execute: _ => Revert());

        _editor.CharacterViewer.BodyShape().BodySlideApplied += ApplyOrClearHighlight;

        // Live morph: collapse per-keystroke edits into one apply.
        Observable.FromEvent(h => WorkingCopyEdited += h, h => WorkingCopyEdited -= h)
            .Throttle(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
            .Subscribe(_ => _editor.ReapplyPreviewMorph())
            .DisposeWith(this);

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SelectedSliderName):
                    RefreshPresetRowValues();
                    if (SelectedSliderName != null) ActiveHighlightSlider = SelectedSliderName;
                    ApplyOrClearHighlight();
                    break;
                case nameof(SelectedReadoutRow):
                    if (SelectedReadoutRow != null) ActiveHighlightSlider = SelectedReadoutRow.SliderName;
                    ApplyOrClearHighlight();
                    break;
                case nameof(ShowMovedVertices):
                    ApplyOrClearHighlight();
                    break;
                case nameof(PresetFilterText):
                    RebuildFilteredPresetRows();
                    break;
                case nameof(SliderReadoutFilterText):
                case nameof(ShowUnsetSliders):
                    RebuildReadout();
                    break;
                case nameof(ShowPresetOnlySliders):
                    RebuildSliderNames();
                    break;
                case nameof(SelectedPresetRow):
                    if (!_syncingSelection && SelectedPresetRow != null && !ReferenceEquals(_editor.SelectedPreset, SelectedPresetRow.PlaceHolder))
                    {
                        _editor.SelectedPreset = SelectedPresetRow.PlaceHolder;
                    }
                    break;
            }
        };
    }

    /// <summary>Raised on every edit of the working copy (throttled into one viewer refresh).</summary>
    private event Action? WorkingCopyEdited;

    // ---------- Preset table ----------

    public ObservableCollection<VM_SliderPresetRow> FilteredPresetRows { get; } = new();
    public VM_SliderPresetRow? SelectedPresetRow { get; set; }
    public string PresetFilterText { get; set; } = "";

    /// <summary>Slider names offered by the table's slider picker (catalog first, see <see cref="ShowPresetOnlySliders"/>).</summary>
    public ObservableCollection<string> AvailableSliderNames { get; } = new();
    public string? SelectedSliderName { get; set; }

    /// <summary>Also offer slider names found only in preset XMLs (mostly outfit zap/squeeze sliders).</summary>
    public bool ShowPresetOnlySliders { get; set; }

    // ---------- Selected preset ----------

    /// <summary>Editable copy of the selected preset; what the viewer shows while <see cref="IsDirty"/>.</summary>
    public BodySlideSetting? WorkingCopy { get; private set; }

    /// <summary>The preset the working copy was taken from.</summary>
    private BodySlideSetting? _source;

    public bool IsDirty { get; private set; }
    public string SelectedPresetLabel => _source?.Label ?? "(no preset selected)";
    public string SourceFileDisplay => string.IsNullOrEmpty(_source?.SourceXmlPath) ? "(source XML unknown)" : Path.GetFileName(_source!.SourceXmlPath);
    public bool CanSaveInPlace => !string.IsNullOrEmpty(_source?.SourceXmlPath) && File.Exists(_source!.SourceXmlPath);

    public ObservableCollection<VM_EditableSliderRow> SliderReadoutRows { get; } = new();
    public VM_EditableSliderRow? SelectedReadoutRow { get; set; }
    public string SliderReadoutFilterText { get; set; } = "";

    /// <summary>Also list the body type's catalog sliders this preset doesn't set, so they can be added.</summary>
    public bool ShowUnsetSliders { get; set; }

    /// <summary>Name for Save As.</summary>
    public string SaveAsName { get; set; } = "";

    public bool ShowMovedVertices { get; set; }
    public string? ActiveHighlightSlider { get; private set; }

    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand RevertCommand { get; }

    /// <summary>The model the editor should preview for <paramref name="model"/>: the working copy when
    /// it holds unsaved edits of that preset, else the preset itself.</summary>
    public BodySlideSetting? PreviewModelFor(BodySlideSetting? model)
        => model != null && IsDirty && ReferenceEquals(model, _source) && WorkingCopy != null ? WorkingCopy : model;

    /// <summary>Rebuilds the preset table from the editor's filtered preset list (body type + gender).</summary>
    public void RebuildPresetRows()
    {
        _presetRows.Clear();
        foreach (var ph in _editor.FilteredPresets)
        {
            if (ph?.AssociatedModel == null) continue;
            _presetRows.Add(new VM_SliderPresetRow(ph));
        }
        RebuildSliderNames();
        RefreshPresetRowValues();
        RebuildFilteredPresetRows();
        SyncSelectionFromEditor();
    }

    /// <summary>Follows the editor's preset selection (asking first when the outgoing preset has
    /// unsaved slider edits).</summary>
    public void OnEditorPresetChanged()
    {
        var incoming = _editor.SelectedPreset?.AssociatedModel;
        if (ReferenceEquals(incoming, _source)) return;
        if (IsDirty && _source != null)
        {
            bool save = !MessageWindow.SuppressAllDialogs && MessageWindow.DisplayNotificationYesNo("Unsaved Slider Edits",
                "'" + _source.Label + "' has unsaved slider edits. Save them to " + SourceFileDisplay + " before switching presets?\n\nNo discards them.");
            if (save && CanSaveInPlace) Save(reapply: false);
        }
        LoadWorkingCopy(incoming);
        SyncSelectionFromEditor();
    }

    /// <summary>Refreshes the Interpolated columns when the preview weight changes.</summary>
    public void OnPreviewWeightChanged()
    {
        RefreshPresetRowValues();
        foreach (var row in _readoutRows) row.RefreshInterpolation(_editor.PreviewWeight);
    }

    private void SyncSelectionFromEditor()
    {
        _syncingSelection = true;
        try
        {
            SelectedPresetRow = FilteredPresetRows.FirstOrDefault(r => ReferenceEquals(r.PlaceHolder, _editor.SelectedPreset));
        }
        finally { _syncingSelection = false; }
    }

    private void LoadWorkingCopy(BodySlideSetting? source)
    {
        _source = source;
        WorkingCopy = source == null ? null : CloneForEditing(source);
        IsDirty = false;
        SaveAsName = source?.Label ?? "";
        ManuallyRaisePropertyChanged(nameof(SelectedPresetLabel));
        ManuallyRaisePropertyChanged(nameof(SourceFileDisplay));
        ManuallyRaisePropertyChanged(nameof(CanSaveInPlace));
        RebuildReadout();
    }

    /// <summary>Copy of <paramref name="source"/> whose slider values can be edited without touching it.
    /// Label and slider group are kept, so the viewer and the rules treat it as the same preset.</summary>
    public static BodySlideSetting CloneForEditing(BodySlideSetting source)
    {
        var copy = new BodySlideSetting
        {
            Label = source.Label,
            ReferencedBodySlide = source.ReferencedBodySlide,
            SliderGroup = source.SliderGroup,
            SourceXmlPath = source.SourceXmlPath,
            BodyShapeDescriptorsByWeight = source.BodyShapeDescriptorsByWeight,
            ManualCategoriesByWeight = source.ManualCategoriesByWeight,
            SliderValues = new Dictionary<string, BodySlideSlider>(StringComparer.OrdinalIgnoreCase),
        };
        foreach (var (name, slider) in source.SliderValues)
        {
            if (slider == null) continue;
            copy.SliderValues[name] = new BodySlideSlider { SliderName = slider.SliderName ?? name, Small = slider.Small, Big = slider.Big };
        }
        return copy;
    }

    private void RebuildSliderNames()
    {
        var bodyType = _editor.SelectedBodyType ?? "";
        var catalog = _catalog?.CatalogSliders(bodyType) ?? Array.Empty<string>();
        var fromPresets = _catalog?.PresetSliders(bodyType) ?? Array.Empty<string>();
        bool showAll = ShowPresetOnlySliders || catalog.Count == 0;
        var desired = SliderNameCatalog.BuildVisibleSliderNames(catalog, fromPresets,
            SelectedSliderName != null ? new[] { SelectedSliderName } : Array.Empty<string>(), showAll);
        SliderNameCatalog.ReconcileSliderNameCollection(AvailableSliderNames, desired);
    }

    private void RefreshPresetRowValues()
    {
        int weight = _editor.PreviewWeight;
        foreach (var row in _presetRows) row.RefreshSliderValues(SelectedSliderName, weight);
    }

    private void RebuildFilteredPresetRows()
    {
        FilteredPresetRows.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        foreach (var row in _presetRows)
        {
            if (filter.Length > 0 && row.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            FilteredPresetRows.Add(row);
        }
    }

    private void RebuildReadout()
    {
        _readoutRows.Clear();
        SliderReadoutRows.Clear();
        if (WorkingCopy == null) return;

        var names = new HashSet<string>(WorkingCopy.SliderValues.Keys, StringComparer.OrdinalIgnoreCase);
        if (ShowUnsetSliders)
        {
            names.UnionWith(_catalog?.CatalogSliders(WorkingCopy.SliderGroup ?? "") ?? Array.Empty<string>());
        }
        foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            _readoutRows.Add(new VM_EditableSliderRow(name, this, _editor.PreviewWeight));
        }
        string filter = SliderReadoutFilterText?.Trim() ?? "";
        foreach (var row in _readoutRows)
        {
            if (filter.Length == 0 || row.SliderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) SliderReadoutRows.Add(row);
        }
    }

    /// <summary>Current working-copy slider, or null when the preset doesn't set it.</summary>
    internal BodySlideSlider? GetWorkingSlider(string name)
        => WorkingCopy?.SliderValues != null && WorkingCopy.SliderValues.TryGetValue(name, out var s) ? s : null;

    /// <summary>The source preset's value for change marking.</summary>
    internal BodySlideSlider? GetSourceSlider(string name)
        => _source?.SliderValues != null && _source.SliderValues.TryGetValue(name, out var s) ? s : null;

    /// <summary>Called by a readout row when the user edits one endpoint.</summary>
    internal void OnSliderEdited(string name, float? small, float? big)
    {
        if (WorkingCopy == null) return;
        if (!WorkingCopy.SliderValues.TryGetValue(name, out var slider) || slider == null)
        {
            slider = new BodySlideSlider { SliderName = name };
            WorkingCopy.SliderValues[name] = slider;
        }
        if (small.HasValue) slider.Small = small.Value;
        if (big.HasValue) slider.Big = big.Value;
        IsDirty = !SliderValuesEqual(WorkingCopy.SliderValues, _source?.SliderValues);
        WorkingCopyEdited?.Invoke();
    }

    private static bool SliderValuesEqual(IReadOnlyDictionary<string, BodySlideSlider> a, IReadOnlyDictionary<string, BodySlideSlider>? b)
    {
        b ??= new Dictionary<string, BodySlideSlider>();
        foreach (var name in a.Keys.Concat(b.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            a.TryGetValue(name, out var x);
            b.TryGetValue(name, out var y);
            float xs = x?.Small ?? 0f, xb = x?.Big ?? 0f, ys = y?.Small ?? 0f, yb = y?.Big ?? 0f;
            if (xs != ys || xb != yb) return false;
            if ((x == null) != (y == null) && (xs != 0f || xb != 0f)) return false;
        }
        return true;
    }

    private void Revert()
    {
        LoadWorkingCopy(_source);
        _editor.ReapplyPreviewMorph();
    }

    /// <summary>Writes the working copy back into the preset's own SliderPresets XML and adopts it as
    /// the preset's slider values.</summary>
    private void Save(bool reapply = true)
    {
        if (_source == null || WorkingCopy == null || !CanSaveInPlace) return;
        try
        {
            var backup = BodySlidePresetXmlWriter.WritePreset(_source.SourceXmlPath, _source.ReferencedBodySlide, WorkingCopy.SliderValues);
            _source.SliderValues = CloneForEditing(WorkingCopy).SliderValues;
            _logger?.LogMessage($"Sliders: saved '{_source.ReferencedBodySlide}' to {_source.SourceXmlPath}" + (backup.Length > 0 ? $" (previous file backed up to {Path.GetFileName(backup)})." : "."));
            _logger?.CallTimedNotifyStatusUpdateAsync("Saved preset " + _source.Label, 3, CommonColors.Green);
            LoadWorkingCopy(_source);
            if (reapply) _editor.ReapplyPreviewMorph();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Sliders: could not save preset '" + _source.Label + "': " + ExceptionLogger.GetExceptionStack(ex));
            MessageWindow.DisplayNotificationOK("Save Failed", "Could not save the preset: " + ex.Message);
        }
    }

    /// <summary>Writes the working copy as a new preset (<see cref="SaveAsName"/>) into a chosen
    /// SliderPresets XML -- a new file, or an existing one to add the preset to -- and adds it to the
    /// BodySlides list.</summary>
    private void SaveAs()
    {
        if (WorkingCopy == null || _source == null) return;
        string name = SaveAsName?.Trim() ?? "";
        if (name.Length == 0)
        {
            MessageWindow.DisplayNotificationOK("Save As", "Enter a name for the new preset first.");
            return;
        }

        string initialDir = !string.IsNullOrEmpty(_source.SourceXmlPath) ? Path.GetDirectoryName(_source.SourceXmlPath) ?? "" : "";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "BodySlide preset files (*.xml)|*.xml",
            DefaultExt = ".xml",
            Title = "Save preset '" + name + "' to a new or existing SliderPresets file",
            OverwritePrompt = false, // an existing file gets the preset added, not replaced
            InitialDirectory = initialDir,
            FileName = !string.IsNullOrEmpty(_source.SourceXmlPath) ? Path.GetFileName(_source.SourceXmlPath) : name + ".xml",
        };
        if (dialog.ShowDialog() != true) return;
        string path = dialog.FileName;

        bool replacingSelf = string.Equals(name, _source.ReferencedBodySlide, StringComparison.Ordinal)
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_source.SourceXmlPath ?? ""), StringComparison.OrdinalIgnoreCase);
        if (replacingSelf)
        {
            Save();
            return;
        }

        bool nameTaken = _oBodyVM?.Invoke()?.BodySlidesUI?.CurrentlyExistingBodySlides?.Contains(name) == true;
        if (nameTaken && !MessageWindow.DisplayNotificationYesNo("Preset Name In Use",
                "A preset named '" + name + "' already exists. Writing it to " + Path.GetFileName(path) + " replaces its sliders there (if it lives in that file) or adds a second preset with the same name. Continue?"))
        {
            return;
        }

        try
        {
            var (setName, groups) = BodySlidePresetXmlWriter.ReadPresetMetadata(_source.SourceXmlPath, _source.ReferencedBodySlide);
            var backup = BodySlidePresetXmlWriter.WritePreset(path, name, WorkingCopy.SliderValues, setName, groups);

            var newPreset = new BodySlideSetting
            {
                Label = name,
                ReferencedBodySlide = name,
                SliderGroup = _source.SliderGroup,
                SourceXmlPath = path,
                SliderValues = CloneForEditing(WorkingCopy).SliderValues,
            };
            _oBodyVM?.Invoke()?.BodySlidesUI?.AddSavedPreset(newPreset, _editor.PreviewGender);
            _logger?.LogMessage($"Sliders: saved new preset '{name}' to {path}" + (backup.Length > 0 ? $" (previous file backed up to {Path.GetFileName(backup)})." : "."));
            _logger?.CallTimedNotifyStatusUpdateAsync("Saved new preset " + name, 3, CommonColors.Green);

            // The source preset is unchanged on disk; drop the edits from it and move to the new preset.
            LoadWorkingCopy(_source);
            _editor.RebuildAvailablePresets();
            var placeHolder = _editor.FilteredPresets.FirstOrDefault(p => ReferenceEquals(p.AssociatedModel, newPreset));
            if (placeHolder != null) _editor.SelectedPreset = placeHolder;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Sliders: could not save preset '" + name + "' to " + path + ": " + ExceptionLogger.GetExceptionStack(ex));
            MessageWindow.DisplayNotificationOK("Save As Failed", "Could not save the preset: " + ex.Message);
        }
    }

    private void ApplyOrClearHighlight()
    {
        var viewer = _editor.CharacterViewer;
        if (ShowMovedVertices && _source != null && !string.IsNullOrWhiteSpace(ActiveHighlightSlider))
        {
            viewer.BodyShape().HighlightSliderMorph(ActiveHighlightSlider);
        }
        else
        {
            viewer.BodyShape().ClearSliderHighlight();
        }
    }

    public override void Dispose()
    {
        _editor.CharacterViewer.BodyShape().BodySlideApplied -= ApplyOrClearHighlight;
        base.Dispose();
    }
}

/// <summary>One preset of the Sliders tab table: the picked slider's Low/High values and its
/// interpolated value at the preview weight (blank when the preset doesn't set the slider).</summary>
public class VM_SliderPresetRow : VM
{
    public VM_SliderPresetRow(VM_BodySlidePlaceHolder placeHolder)
    {
        PlaceHolder = placeHolder;
    }

    public VM_BodySlidePlaceHolder PlaceHolder { get; }
    public string Label => PlaceHolder.Label ?? "";
    public float? Low { get; private set; }
    public float? High { get; private set; }
    public float? Interpolated { get; private set; }

    public void RefreshSliderValues(string? sliderName, int weight)
    {
        var sliderValues = PlaceHolder.AssociatedModel?.SliderValues;
        if (sliderName != null && sliderValues != null && sliderValues.TryGetValue(sliderName, out var slider) && slider != null)
        {
            Low = slider.Small;
            High = slider.Big;
            Interpolated = MeasurementMath.InterpolateSliderValue(slider, weight);
        }
        else
        {
            Low = null;
            High = null;
            Interpolated = null;
        }
    }
}

/// <summary>One slider of the selected preset in the Sliders tab: editable Low (weight 0) and High
/// (weight 100) values, the interpolated value at the preview weight, and whether it differs from the
/// saved preset. The text proxies keep half-typed numbers ("1.", "-") editable; parseable text is pushed
/// to the working copy on every keystroke.</summary>
public class VM_EditableSliderRow : VM
{
    private readonly VM_AutoAnnotationSliders _owner;
    private bool _loading;
    private int _weight;

    public VM_EditableSliderRow(string sliderName, VM_AutoAnnotationSliders owner, int weight)
    {
        SliderName = sliderName;
        _owner = owner;
        _weight = weight;
        _loading = true;
        var slider = owner.GetWorkingSlider(sliderName);
        IsSet = slider != null;
        LowText = BodySlidePresetXmlWriter.FormatValue(slider?.Small ?? 0f);
        HighText = BodySlidePresetXmlWriter.FormatValue(slider?.Big ?? 0f);
        _loading = false;
        Recompute();
    }

    public string SliderName { get; }

    /// <summary>False for a catalog slider the preset doesn't set (shown via "Show unset sliders").</summary>
    public bool IsSet { get; private set; }

    public string LowText { get; set; }
    public string HighText { get; set; }
    public float Interpolated { get; private set; }

    /// <summary>True when this slider's values differ from the saved preset.</summary>
    public bool IsModified { get; private set; }

    private void OnLowTextChanged() => Push(LowText, isLow: true);
    private void OnHighTextChanged() => Push(HighText, isLow: false);

    private void Push(string text, bool isLow)
    {
        if (_loading) return;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value)) return;
        _owner.OnSliderEdited(SliderName, isLow ? value : null, isLow ? null : value);
        IsSet = true;
        Recompute();
    }

    public void RefreshInterpolation(int weight)
    {
        _weight = weight;
        Recompute();
    }

    private void Recompute()
    {
        var working = _owner.GetWorkingSlider(SliderName);
        Interpolated = working == null ? 0f : MeasurementMath.InterpolateSliderValue(working, _weight);
        var saved = _owner.GetSourceSlider(SliderName);
        IsModified = (working?.Small ?? 0f) != (saved?.Small ?? 0f) || (working?.Big ?? 0f) != (saved?.Big ?? 0f);
    }
}

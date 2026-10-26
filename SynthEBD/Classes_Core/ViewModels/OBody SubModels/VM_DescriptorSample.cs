using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Media.Imaging;
using GongSolutions.Wpf.DragDrop;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for <see cref="Window_DescriptorSample"/>, the Rules tab's Show Sample window: a random
/// sample of the (preset, weight) slices that currently carry one descriptor value, rendered side by
/// side like the annotation Panel and sortable by any measurement.
///
/// <para><b>Why.</b> Show Spread shows a value's extremes and typical members; a random sample shows
/// what the value's population looks like as a whole, which is where a rule that is too loose (or
/// catches an unexpected kind of body) becomes obvious.</para>
///
/// <para><b>Population.</b> By default the same set the Rules tab lists under the value node: slices
/// whose derived descriptors (<see cref="VM_BodyTypeProfile.ScanResults"/>) carry the value, for the
/// body type's gender, hidden-and-disabled presets excluded. Ignore Manual Annotations re-derives
/// membership by rules alone (<see cref="VM_BodyTypeProfile.DeriveRuleOnlyDescriptors"/>), as in Show
/// Spread. Snapshotted at open; reopen after a re-scan or rule edit.</para>
///
/// <para><b>Sample.</b> <see cref="SampleSize"/> slices (default 50, or the whole population when
/// smaller) drawn uniformly without replacement (<see cref="AnnotationPanelLayout.PickRandom"/>).
/// Changing the size, One per preset or the annotations toggle redraws; Reshuffle redraws at the
/// same size. Sorting only reorders the drawn set, it never changes which slices are drawn.</para>
///
/// <para>View-only: unlike the Panel there are no value toggles, so nothing here writes annotations.</para>
/// </summary>
public class VM_DescriptorSample : VM, IDropTarget
{
    /// <summary>Default number of slices drawn.</summary>
    public const int DefaultSampleSize = 50;

    private readonly Logger _logger;
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly VM_BodyTypeProfile _profile;
    private readonly SpreadThumbnailRenderer _renderer;
    private readonly Random _rng = new();
    private readonly List<SampleSlice> _allSlices = new();
    private readonly Dictionary<(string PresetLabel, Gender Gender, int Weight), HashSet<string>> _ruleOnlyValues = new();
    private BodyTypeProfile? _profileModel;
    private ExternalDescriptorSeedContext? _seedContext;
    private List<SampleSlice> _pool = new();
    private List<SampleSlice> _drawn = new();
    private List<Entry> _ordered = new();

    /// <summary>One gender-matching cache slice, copied at open. <see cref="InScanResults"/> is the
    /// Rules-tab membership (rules, with annotations seeding as at scan time).</summary>
    private sealed record SampleSlice(
        string PresetLabel, Gender Gender, int Weight,
        IReadOnlyDictionary<string, float?> Measurements,
        bool InScanResults)
    {
        public (string PresetLabel, Gender Gender, int Weight) Key => (PresetLabel, Gender, Weight);
    }

    /// <summary>One drawn slice in display order with its value on the current metric.</summary>
    private sealed record Entry(SampleSlice Slice, double? Value);

    internal VM_DescriptorSample(
        VM_BodyTypeProfileEditor editor,
        VM_BodyTypeProfile profile,
        string category,
        string value,
        Gender gender,
        SceneInputsSnapshot scene,
        VM_CharacterViewer renderSettingsSource,
        Func<string, BodySlideSetting?> presetLookup,
        Logger logger)
    {
        _editor = editor;
        _profile = profile;
        _logger = logger;
        Category = category;
        Value = value;
        Gender = gender;
        Title = $"Sample: {category} = {value} ({profile.Name}, {gender})";

        // ---- slices: the listed cache for this gender, with Rules-tab membership ----
        foreach (var kv in profile.ListedMeasurementCache())
        {
            if (kv.Key.Gender != gender || kv.Value?.Measurements == null) continue;
            bool inScan = profile.ScanResults.TryGetValue(kv.Key, out var sigs) && sigs != null
                && sigs.Any(d => d != null
                    && string.Equals(d.Category, category, StringComparison.Ordinal)
                    && string.Equals(d.Value, value, StringComparison.Ordinal));
            _allSlices.Add(new SampleSlice(kv.Key.PresetLabel, kv.Key.Gender, kv.Key.Weight,
                new Dictionary<string, float?>(kv.Value.Measurements, StringComparer.Ordinal), inScan));
        }

        // ---- metrics: as in the annotation Panel ----
        var categoryNames = VM_BodyTypeProfileEditor.CollectCategoryMeasurementNames(profile, category, gender).ToList();
        foreach (var name in categoryNames)
        {
            Metrics.Add(new PanelMetricOption($"{name}  ({category} rule)", name));
        }
        foreach (var m in profile.Measurements)
        {
            if (m == null || string.IsNullOrEmpty(m.Name) || categoryNames.Contains(m.Name, StringComparer.Ordinal)) continue;
            Metrics.Add(new PanelMetricOption(m.Name, m.Name));
        }
        Metrics.Insert(0, new PanelMetricOption("Free (drag to reorder)", VM_AnnotationPanel.FreeOrderName));
        SelectedMetric = VM_BodyTypeSpread.FindPrimaryMeasurement(profile, category, gender, categoryNames)
            ?? Metrics.Skip(1).FirstOrDefault()?.Name ?? "";
        _valueMetric = SelectedMetric;

        _renderer = new SpreadThumbnailRenderer(profile, scene, renderSettingsSource, presetLookup, logger,
            "Show Sample", WantedKeys, ApplyImagesAndPump);

        PrevPageCommand = new RelayCommand(canExecute: _ => PageIndex > 0, execute: _ => GoToPage(PageIndex - 1));
        NextPageCommand = new RelayCommand(canExecute: _ => PageIndex < PageCount - 1, execute: _ => GoToPage(PageIndex + 1));
        ReshuffleCommand = new RelayCommand(canExecute: _ => _pool.Count > 0, execute: _ => Redraw());
        CopyOrderCommand = new RelayCommand(canExecute: _ => _ordered.Count > 0, execute: _ => CopyOrder());

        RebuildPool();

        this.WhenAnyValue(x => x.SelectedMetric)
            .Skip(1)
            .Subscribe(_ =>
            {
                if (IsFreeOrder) { UpdateSummary(); PageIndex = 0; BuildPage(); return; }
                _valueMetric = SelectedMetric ?? "";
                Resort();
            })
            .DisposeWith(this);
        this.WhenAnyValue(x => x.SampleSize, x => x.OnePerPreset)
            .Skip(1)
            .Subscribe(_ => Redraw())
            .DisposeWith(this);
        this.WhenAnyValue(x => x.IgnoreManualAnnotations)
            .Skip(1)
            .Subscribe(_ => RebuildPool())
            .DisposeWith(this);
        this.WhenAnyValue(x => x.CellsPerPage)
            .Skip(1)
            .Subscribe(_ =>
            {
                if (IsFreeOrder) return;
                PageIndex = _pageStart / PageSize;
                BuildPage();
            })
            .DisposeWith(this);
        this.WhenAnyValue(x => x.ShowBack, x => x.ShowMeasurements)
            .Skip(1)
            .Subscribe(_ => ApplyImagesAndPump())
            .DisposeWith(this);
        this.WhenAnyValue(x => x.ShowValues)
            .Skip(1)
            .Subscribe(_ => RefreshCaptions())
            .DisposeWith(this);

        _editor.PresetHiddenAndDisabled += OnPresetHiddenAndDisabled;
    }

    public string Title { get; }
    public string Category { get; }
    public string Value { get; }
    public Gender Gender { get; }

    /// <summary>How many slices to draw (default <see cref="DefaultSampleSize"/>). Changing it redraws.</summary>
    public int SampleSize { get; set; } = DefaultSampleSize;

    /// <summary>Draw at most one weight per preset, so the sample covers more distinct presets.</summary>
    public bool OnePerPreset { get; set; }

    /// <summary>Off (default): the Rules tab's membership. On: membership by rules alone, with the
    /// presets' manual / library annotations re-derived out (as Show Spread's toggle).</summary>
    public bool IgnoreManualAnnotations { get; set; }

    /// <summary>Draws a new random sample at the current size.</summary>
    public RelayCommand ReshuffleCommand { get; }

    /// <summary>Measurements the cells can be sorted by: the Category's own first, then the rest.</summary>
    public ObservableCollection<PanelMetricOption> Metrics { get; } = new();

    /// <summary>Name of the measurement sorted by, or <see cref="VM_AnnotationPanel.FreeOrderName"/>.</summary>
    public string SelectedMetric { get; set; }

    /// <summary>True in the Free sort mode: tiles can be dragged and the whole sample shows on one page.</summary>
    public bool IsFreeOrder => SelectedMetric == VM_AnnotationPanel.FreeOrderName;

    /// <summary>The measurement captions, Show values and the overlay use: the selected metric, or in
    /// Free mode the last one selected before it.</summary>
    private string _valueMetric = "";

    public RelayCommand CopyOrderCommand { get; }

    /// <summary>Feedback for Copy Order, empty until used.</summary>
    public string CopyStatus { get; private set; } = "";

    /// <summary>When on, each cell's first image shows the back instead of the front.</summary>
    public bool ShowBack { get; set; }

    /// <summary>When on, each image also draws the metric's measurement lines.</summary>
    public bool ShowMeasurements { get; set; }

    /// <summary>Adds each slice's metric value to its caption (and, when the rules alone would not
    /// give it this value, what they give instead).</summary>
    public bool ShowValues { get; set; }

    public ObservableCollection<VM_DescriptorSampleCell> Cells { get; } = new();

    public int PageIndex { get; private set; }
    public int PageCount { get; private set; } = 1;
    public string PageText { get; private set; } = "";
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    /// <summary>"N of M matching slice(s), drawn at random, sorted by X", for the toolbar.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Render progress readout ("Rendering 12 / 48..."), empty when idle.</summary>
    public string RenderStatus { get; set; } = "";

    /// <summary>Cells per page outside Free mode. Not persisted.</summary>
    public int CellsPerPage { get; set; } = AnnotationPanelLayout.DefaultPageSize;

    public bool CanSetCellsPerPage => !IsFreeOrder;

    /// <summary>Cell zoom, changed with Ctrl + mouse wheel (same steps and range as the Panel).</summary>
    public double CellScale { get; private set; } = 1.0;

    private const double BaseCellSize = 250;
    public double CellWidth => BaseCellSize * CellScale;
    public double CellImageHeight => BaseCellSize * CellScale;

    internal void ZoomCells(int wheelDelta)
    {
        if (wheelDelta == 0) return;
        double factor = Math.Pow(1.1, wheelDelta / 120.0);
        CellScale = Math.Clamp(CellScale * factor, VM_AnnotationPanel.MinCellScale, VM_AnnotationPanel.MaxCellScale);
    }

    private int PageSize => IsFreeOrder ? Math.Max(1, _ordered.Count) : Math.Max(1, CellsPerPage);
    private int _pageStart;

    // ---------- population and draw ----------

    /// <summary>What the rules alone give the slice in this Category, computed on first use.</summary>
    private HashSet<string> RuleOnlyValues((string PresetLabel, Gender Gender, int Weight) key)
    {
        if (_ruleOnlyValues.TryGetValue(key, out var cached)) return cached;
        HashSet<string> values;
        try
        {
            _profileModel ??= _profile.DumpToModel();
            _seedContext ??= _editor.BuildExternalDescriptorSeedContext();
            values = _profile.DeriveRuleOnlyDescriptors(key, _profileModel, _seedContext)
                .Where(p => string.Equals(p.Category, Category, StringComparison.Ordinal) && !string.IsNullOrEmpty(p.Value))
                .Select(p => p.Value)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Show Sample: rule-only descriptors for '" + key.PresetLabel + "' failed: " + ExceptionLogger.GetExceptionStack(ex));
            values = new HashSet<string>(StringComparer.Ordinal);
        }
        _ruleOnlyValues[key] = values;
        return values;
    }

    /// <summary>Re-reads which slices carry the value under the current annotations mode, then redraws.</summary>
    private void RebuildPool()
    {
        _pool = IgnoreManualAnnotations
            ? _allSlices.Where(s => RuleOnlyValues(s.Key).Contains(Value)).ToList()
            : _allSlices.Where(s => s.InScanResults).ToList();
        Redraw();
    }

    /// <summary>Draws a fresh random sample from the pool, then sorts it.</summary>
    private void Redraw()
    {
        var samples = _pool.Select(s => new PanelSample(s.PresetLabel, s.Weight, null)).ToList();
        var picks = AnnotationPanelLayout.PickRandom(samples, Math.Max(1, SampleSize), _rng, OnePerPreset);
        _drawn = picks.Select(i => _pool[i]).ToList();
        Resort();
    }

    private double? ValueOf(SampleSlice slice)
        => !string.IsNullOrEmpty(_valueMetric)
           && slice.Measurements.TryGetValue(_valueMetric, out var v) && v.HasValue
            ? v.Value
            : null;

    /// <summary>Orders the drawn sample by the current metric (draw order when there is none; in Free
    /// mode a redraw starts from the last metric's order) and returns to the first page.</summary>
    private void Resort()
    {
        var samples = _drawn.Select(s => new PanelSample(s.PresetLabel, s.Weight, ValueOf(s))).ToList();
        IReadOnlyList<int> order = !string.IsNullOrEmpty(_valueMetric)
            ? AnnotationPanelLayout.SortByValue(samples)
            : Enumerable.Range(0, _drawn.Count).ToList();
        _ordered = order.Select(i => new Entry(_drawn[i], samples[i].Value)).ToList();
        UpdateSummary();
        PageIndex = 0;
        BuildPage();
    }

    private void UpdateSummary()
    {
        int missing = _ordered.Count(e => !e.Value.HasValue);
        bool hasMetric = !string.IsNullOrEmpty(_valueMetric);
        string set = _pool.Count == 0
            ? $"No slices currently carry {Category} = {Value}" + (IgnoreManualAnnotations ? " by rules alone" : "")
            : $"{_ordered.Count} of {_pool.Count} matching slice(s) drawn at random"
              + (OnePerPreset ? " (one per preset)" : "")
              + (IgnoreManualAnnotations ? ", by rules alone" : "");
        Summary = _pool.Count == 0 ? set
            : IsFreeOrder ? set + ", free order (drag tiles to reorder; double-click to load in the viewer)"
            : set + (hasMetric ? $", sorted by {_valueMetric}" : ", in draw order")
              + (missing > 0 && hasMetric ? $" ({missing} with no value, last)" : "");
    }

    private void CopyOrder()
    {
        string mode = IsFreeOrder ? "free order" : $"sorted by {_valueMetric}";
        string header = $"Sample: {Category} = {Value} ({_profile.Name}, {Gender}), {_ordered.Count} of {_pool.Count} drawn at random, {mode}, {DateTime.Now:yyyy-MM-dd HH:mm}";
        string text = AnnotationPanelLayout.FormatOrder(header, _ordered.Select(e => (
            e.Slice.PresetLabel, e.Slice.Weight, (IReadOnlyList<string>)Array.Empty<string>())));
        try
        {
            System.Windows.Clipboard.SetText(text);
            CopyStatus = $"Copied {_ordered.Count} preset(s).";
        }
        catch (Exception ex)
        {
            CopyStatus = "Copy failed: " + ex.Message;
        }
    }

    // ---------- drag and drop (Free mode) ----------

    public void DragOver(IDropInfo dropInfo)
    {
        if (!IsFreeOrder || dropInfo.Data is not VM_DescriptorSampleCell || !ReferenceEquals(dropInfo.TargetCollection, Cells)) return;
        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        dropInfo.Effects = System.Windows.DragDropEffects.Move;
    }

    /// <summary>Moves the dropped tile to the insertion point. Free mode shows one page, so the cell
    /// index is also the index into the full order.</summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (!IsFreeOrder || dropInfo.Data is not VM_DescriptorSampleCell cell) return;
        int from = Cells.IndexOf(cell);
        if (from < 0) return;
        int to = AnnotationPanelLayout.MoveTarget(from, dropInfo.InsertIndex, Cells.Count);
        if (to == from) return;
        Cells.Move(from, to);
        var entry = _ordered[from];
        _ordered.RemoveAt(from);
        _ordered.Insert(to, entry);
    }

    // ---------- paging and captions ----------

    private void GoToPage(int pageIndex)
    {
        PageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        BuildPage();
    }

    private void BuildPage()
    {
        PageCount = AnnotationPanelLayout.PageCount(_ordered.Count, PageSize);
        var (start, count) = AnnotationPanelLayout.PageRange(_ordered.Count, PageIndex, PageSize);
        PageIndex = _ordered.Count == 0 ? 0 : start / PageSize;
        _pageStart = start;
        PageText = $"Page {PageIndex + 1} / {PageCount}";

        Cells.Clear();
        foreach (var entry in _ordered.Skip(start).Take(count))
        {
            Cells.Add(new VM_DescriptorSampleCell(entry.Slice.PresetLabel, entry.Slice.Gender, entry.Slice.Weight, entry.Value, this));
        }
        RefreshCaptions();
        ApplyImagesAndPump();
    }

    private void RefreshCaptions()
    {
        string format = "F" + SpreadStatistics.DecimalsToDistinguish(Cells.Where(c => c.Value.HasValue).Select(c => c.Value!.Value));
        foreach (var cell in Cells)
        {
            var parts = new List<string> { $"W{cell.Weight}" };
            if (ShowValues)
            {
                parts.Add(cell.Value.HasValue ? cell.Value.Value.ToString(format) : "no value");
                // Only worth printing when it disagrees: by rules alone the slice would not carry the
                // value (an annotation suppressed the default or fed an aggregator rule). In the
                // rules-alone mode every drawn slice carries it by construction.
                if (!IgnoreManualAnnotations)
                {
                    var ruleOnly = RuleOnlyValues((cell.PresetLabel, cell.Gender, cell.Weight));
                    if (!ruleOnly.Contains(Value))
                    {
                        parts.Add("rules alone: " + (ruleOnly.Count == 0 ? "(none)" : string.Join(" + ", ruleOnly.OrderBy(v => v, StringComparer.Ordinal))));
                    }
                }
            }
            else if (!cell.Value.HasValue && !string.IsNullOrEmpty(_valueMetric))
            {
                parts.Add($"no {_valueMetric} value");
            }
            cell.Caption = string.Join(" · ", parts);
        }
    }

    // ---------- HD / viewer ----------

    internal void HideAndDisablePreset(VM_DescriptorSampleCell cell) => _editor.HideAndDisablePreset(cell.PresetLabel, cell.Gender);

    /// <summary>A preset was hidden-and-disabled (here or anywhere in the editor): drop its slices from
    /// the population and the sample without redrawing the rest, and re-page.</summary>
    private void OnPresetHiddenAndDisabled(string presetLabel, Gender gender)
    {
        if (gender != Gender) return;
        bool Match(SampleSlice s) => string.Equals(s.PresetLabel, presetLabel, StringComparison.Ordinal);
        _allSlices.RemoveAll(Match);
        _pool.RemoveAll(Match);
        _drawn.RemoveAll(Match);
        int removed = _ordered.RemoveAll(e => Match(e.Slice));
        if (removed == 0) return;
        UpdateSummary();
        BuildPage();
    }

    internal void LoadInEditorViewer(VM_DescriptorSampleCell cell) => _editor.LoadSliceInViewer(cell.PresetLabel, cell.Gender, cell.Weight);

    internal void OpenInCompare(VM_DescriptorSampleCell cell) => _editor.OpenSliceInCompare(cell.PresetLabel, cell.Gender, cell.Weight);

    // ---------- rendering ----------

    public string PrimaryViewLabel => ShowBack ? "Back" : "Front";

    private string CurrentOverlay()
        => ShowMeasurements && !string.IsNullOrEmpty(_valueMetric) ? SpreadThumbnailRenderer.JoinOverlay(new[] { _valueMetric }) : "";

    /// <summary>Current page's images first (in display order), then the next page's.</summary>
    private IEnumerable<SpreadThumbnailRenderer.RenderKey> WantedKeys()
    {
        float primaryAz = ShowBack ? SpreadThumbnailRenderer.BackAzimuth : SpreadThumbnailRenderer.FrontAzimuth;
        string overlay = CurrentOverlay();
        var (start, count) = AnnotationPanelLayout.PageRange(_ordered.Count, PageIndex, PageSize);
        int end = Math.Min(_ordered.Count, start + count + PageSize);
        for (int i = start; i < end; i++)
        {
            var s = _ordered[i].Slice;
            yield return new SpreadThumbnailRenderer.RenderKey(s.PresetLabel, s.Weight, primaryAz, overlay);
            yield return new SpreadThumbnailRenderer.RenderKey(s.PresetLabel, s.Weight, SpreadThumbnailRenderer.SideAzimuth, overlay);
        }
    }

    private void ApplyImagesAndPump()
    {
        ManuallyRaisePropertyChanged(nameof(PrimaryViewLabel));
        float primaryAz = ShowBack ? SpreadThumbnailRenderer.BackAzimuth : SpreadThumbnailRenderer.FrontAzimuth;
        string overlay = CurrentOverlay();
        foreach (var cell in Cells)
        {
            cell.SetImages(_renderer.Lookup(cell.PresetLabel, cell.Weight, primaryAz, overlay),
                _renderer.Lookup(cell.PresetLabel, cell.Weight, SpreadThumbnailRenderer.SideAzimuth, overlay));
        }
        RenderStatus = _renderer.DescribeProgress(WantedKeys());
        _renderer.Pump();
    }

    public override void Dispose()
    {
        _editor.PresetHiddenAndDisabled -= OnPresetHiddenAndDisabled;
        _renderer.Dispose();
        base.Dispose();
    }
}

/// <summary>One drawn slice in the Show Sample window: images, caption, and the HD / 3D / load actions.</summary>
public class VM_DescriptorSampleCell : VM
{
    internal VM_DescriptorSampleCell(string presetLabel, Gender gender, int weight, double? value, VM_DescriptorSample parent)
    {
        PresetLabel = presetLabel;
        Gender = gender;
        Weight = weight;
        Value = value;
        // A single click would fire at the start of every drag in Free mode, so there it takes a double-click.
        LoadInViewerCommand = new RelayCommand(_ => !parent.IsFreeOrder, _ => parent.LoadInEditorViewer(this));
        LoadInViewerFreeCommand = new RelayCommand(_ => parent.IsFreeOrder, _ => parent.LoadInEditorViewer(this));
        HideAndDisableCommand = new RelayCommand(_ => true, _ => parent.HideAndDisablePreset(this));
        OpenIn3DCommand = new RelayCommand(_ => true, _ => parent.OpenInCompare(this));
    }

    public string PresetLabel { get; }
    public Gender Gender { get; }
    public int Weight { get; }
    internal double? Value { get; }
    public string Caption { get; set; } = "";

    public BitmapSource? PrimaryImage { get; private set; }
    public BitmapSource? SideImage { get; private set; }
    public bool IsPrimaryLoading { get; private set; } = true;
    public bool IsSideLoading { get; private set; } = true;
    public bool IsPrimaryFailed { get; private set; }
    public bool IsSideFailed { get; private set; }

    public RelayCommand LoadInViewerCommand { get; }
    public RelayCommand LoadInViewerFreeCommand { get; }
    public RelayCommand HideAndDisableCommand { get; }
    public RelayCommand OpenIn3DCommand { get; }

    internal void SetImages((bool Ready, BitmapSource? Image) primary, (bool Ready, BitmapSource? Image) side)
    {
        PrimaryImage = primary.Image;
        IsPrimaryLoading = !primary.Ready;
        IsPrimaryFailed = primary.Ready && primary.Image == null;
        SideImage = side.Image;
        IsSideLoading = !side.Ready;
        IsSideFailed = side.Ready && side.Image == null;
    }
}

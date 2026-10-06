using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Windows.Media.Imaging;
using GongSolutions.Wpf.DragDrop;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>Where the annotation Panel's slices come from.</summary>
public enum AnnotationPanelSource
{
    /// <summary>The queue's built slices (whatever policy built it decides the set; the Panel only sorts).</summary>
    QueueSlices,

    /// <summary>N slices at even quantiles of the metric over the queue's whole candidate population.</summary>
    EvenlySpaced,
}

/// <summary>
/// View model for <see cref="Window_AnnotationPanel"/>, the annotation queue's Panel: the queue's
/// slices for one Category side by side, sorted low to high by a metric and paged (100 per page by
/// default, set in the toolbar), each
/// rendered like a Show Spread cell with a row of value toggles underneath that save immediately.
///
/// <para><b>Why.</b> A Category that is a continuum cut into bands (Butt = Flat / Normal / Round /
/// Large) drifts when judged one slice at a time. Side by side and sorted, neighbours are compared
/// directly, and a preset that looks out of order points at what the metric misses.</para>
///
/// <para><b>Blind by default.</b> Captions show only the weight (and the alias count): printing the
/// metric value or today's rule label anchors judgement, and the sort already conveys order. Show
/// values adds both, for review after judging.</para>
///
/// <para><b>Snapshot.</b> The slice list is read on open and whenever Source, N or the metric
/// changes. A queue rebuild, or a change of the queue's target Category, marks the Panel stale: its
/// toggles are disabled and a banner asks for a reopen, so no verdict lands through rows the table
/// has replaced or in a Category the Panel was not opened for.</para>
///
/// <para><b>Writes</b> go through <see cref="VM_AnnotationQueue.WriteVerdict"/> for the cell's whole
/// alias family, without moving the annotation table's selection. Rendering is a
/// <see cref="SpreadThumbnailRenderer"/> fed the current page first, then the next page.</para>
/// </summary>
public class VM_AnnotationPanel : VM, IDropTarget
{
    /// <summary><see cref="SelectedMetric"/> value of the Free sort mode (not a measurement name).</summary>
    internal const string FreeOrderName = "\u0001free";

    private readonly Logger _logger;
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly VM_AnnotationQueue _queue;
    private readonly VM_BodyTypeProfile _profile;
    private readonly int _queueGenerationAtOpen;
    private readonly IReadOnlyList<string> _values;
    private readonly SpreadThumbnailRenderer _renderer;
    private readonly HashSet<VM_AnnotationQueueSlice> _countedLabelled = new();
    private readonly Dictionary<(string PresetLabel, int Weight), string> _ruleLabels = new();
    private BodyTypeProfile? _profileModel;
    private ExternalDescriptorSeedContext? _seedContext;
    private List<Entry> _ordered = new();
    private bool _suppressRebuild;

    /// <summary>One slice in display order with its value on the current metric.</summary>
    private sealed record Entry(VM_AnnotationQueueSlice Slice, double? Value);

    internal VM_AnnotationPanel(
        VM_BodyTypeProfileEditor editor,
        VM_AnnotationQueue queue,
        VM_BodyTypeProfile profile,
        string category,
        Gender gender,
        SceneInputsSnapshot scene,
        VM_CharacterViewer renderSettingsSource,
        Func<string, BodySlideSetting?> presetLookup,
        Logger logger)
    {
        _editor = editor;
        _queue = queue;
        _profile = profile;
        _logger = logger;
        Category = category;
        Gender = gender;
        Title = $"Panel: {category} ({profile.Name}, {gender})";
        _queueGenerationAtOpen = queue.QueueGeneration;
        IsListQueue = queue.IsListPolicy;

        // Values in the order the queue's digit legend shows them (the annotation menu's order).
        _values = editor.AnnotationEditor?.GetCategoryValues(category)?.ToList() ?? new List<string>();

        // Metrics: first the measurements that can decide the Category (marked with it), then every
        // other profile measurement -- including ones no rule uses yet -- so candidate measurements can
        // be compared against the eye. No Score: a per-value margin gives no single ordering across
        // cells. Default: the Category's primary measurement (the one its own rules test most), else
        // the queue's Spread measurement, else the first.
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
        Metrics.Insert(0, new PanelMetricOption("Free (drag to reorder)", FreeOrderName));
        string? primary = VM_BodyTypeSpread.FindPrimaryMeasurement(profile, category, gender, categoryNames);
        SelectedMetric = primary
            ?? (Metrics.Any(o => o.Name == queue.SpreadMeasurement) ? queue.SpreadMeasurement : null)
            ?? Metrics.Skip(1).FirstOrDefault()?.Name ?? "";
        _valueMetric = SelectedMetric;

        _renderer = new SpreadThumbnailRenderer(profile, scene, renderSettingsSource, presetLookup, logger,
            "Annotation Panel", WantedKeys, ApplyImagesAndPump);

        PrevPageCommand = new RelayCommand(canExecute: _ => PageIndex > 0, execute: _ => GoToPage(PageIndex - 1));
        NextPageCommand = new RelayCommand(canExecute: _ => PageIndex < PageCount - 1, execute: _ => GoToPage(PageIndex + 1));
        CopyOrderCommand = new RelayCommand(canExecute: _ => _ordered.Count > 0, execute: _ => CopyOrder());

        Rebuild();

        // Free keeps the current order (and the last real metric for captions and the overlay);
        // any other metric re-sorts. Source / N always re-read the slices.
        this.WhenAnyValue(x => x.SelectedMetric)
            .Skip(1)
            .Subscribe(_ =>
            {
                if (IsFreeOrder) { UpdateSummary(); PageIndex = 0; BuildPage(); return; }
                _valueMetric = SelectedMetric ?? "";
                Rebuild();
            })
            .DisposeWith(this);
        this.WhenAnyValue(x => x.Source, x => x.EvenlySpacedCount)
            .Skip(1)
            .Subscribe(_ => Rebuild())
            .DisposeWith(this);
        // A new page size keeps the first visible cell on screen rather than jumping to page 1.
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

        _queue.PropertyChanged += HandleQueuePropertyChanged;
        _editor.PresetHiddenAndDisabled += OnPresetHiddenAndDisabled;
    }

    public string Title { get; }
    public string Category { get; }
    public Gender Gender { get; }

    /// <summary>Measurements the cells can be sorted by: the Category's own first, then the rest.</summary>
    public ObservableCollection<PanelMetricOption> Metrics { get; } = new();

    /// <summary>Name of the measurement sorted by, or <see cref="FreeOrderName"/> (the picker binds its
    /// SelectedValue to it).</summary>
    public string SelectedMetric { get; set; }

    /// <summary>True in the Free sort mode: the order is the user's, tiles can be dragged, and the
    /// whole list shows on one page so any tile can reach any position.</summary>
    public bool IsFreeOrder => SelectedMetric == FreeOrderName;

    /// <summary>The measurement captions, Show values and the overlay use: the selected metric, or in
    /// Free mode the last one selected before it.</summary>
    private string _valueMetric = "";

    /// <summary>Copies the current order of every slice (all pages) to the clipboard as a worklist.</summary>
    public RelayCommand CopyOrderCommand { get; }

    /// <summary>Feedback for Copy Order ("Copied 22 preset(s)."), empty until used.</summary>
    public string CopyStatus { get; private set; } = "";

    /// <summary>When on, each cell's first image shows the back instead of the front.</summary>
    public bool ShowBack { get; set; }

    /// <summary>When on, each image also draws the metric's measurement lines.</summary>
    public bool ShowMeasurements { get; set; }

    /// <summary>Adds each slice's metric value and current rule label to its caption. Off by default
    /// and not persisted: numbers and labels anchor judgement.</summary>
    public bool ShowValues { get; set; }

    public AnnotationPanelSource Source { get; set; } = AnnotationPanelSource.QueueSlices;
    public IReadOnlyList<AnnotationPanelSource> SourceOptions { get; } = Enum.GetValues<AnnotationPanelSource>();

    /// <summary>True when the queue is a List (worklist) queue, which disables Evenly spaced: a worklist
    /// is already a deliberate sample.</summary>
    public bool IsListQueue { get; }
    public bool CanChooseSource => !IsListQueue;

    /// <summary>N for <see cref="AnnotationPanelSource.EvenlySpaced"/>.</summary>
    public int EvenlySpacedCount { get; set; } = AnnotationPanelLayout.DefaultEvenlySpacedCount;
    public bool IsEvenlySpaced => Source == AnnotationPanelSource.EvenlySpaced;

    public ObservableCollection<VM_AnnotationPanelCell> Cells { get; } = new();

    public int PageIndex { get; private set; }
    public int PageCount { get; private set; } = 1;
    public string PageText { get; private set; } = "";
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    /// <summary>"N slice(s), sorted by X" (plus how many have no value), for the toolbar.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Render progress readout ("Rendering 12 / 48..."), empty when idle.</summary>
    public string RenderStatus { get; set; } = "";

    /// <summary>True once the queue this Panel was opened over has been rebuilt or re-targeted.</summary>
    public bool IsStale { get; private set; }
    public bool CanEdit => !IsStale;
    public string StaleMessage { get; private set; } = "";

    // ---------- slices ----------

    private double? ValueOf(VM_AnnotationQueueSlice slice)
        => !string.IsNullOrEmpty(_valueMetric)
           && slice.Row.MeasurementValues != null
           && slice.Row.MeasurementValues.TryGetValue(_valueMetric, out var v) && v.HasValue
            ? v.Value
            : null;

    /// <summary>Re-reads the slice list from the queue for the current Source / N / metric, re-sorts,
    /// and returns to the first page.</summary>
    private void Rebuild()
    {
        if (_suppressRebuild) return;
        if (IsListQueue && Source != AnnotationPanelSource.QueueSlices)
        {
            _suppressRebuild = true;
            try { Source = AnnotationPanelSource.QueueSlices; }
            finally { _suppressRebuild = false; }
        }

        var slices = Source == AnnotationPanelSource.EvenlySpaced
            ? _queue.BuildCandidatePopulation()
            : _queue.Slices.ToList();
        var samples = slices.Select(s => new PanelSample(s.Row.PresetLabel, s.Row.Weight, ValueOf(s))).ToList();

        IReadOnlyList<int> order;
        int populationSize = slices.Count;
        if (Source == AnnotationPanelSource.EvenlySpaced)
        {
            order = AnnotationPanelLayout.PickEvenlySpaced(samples, Math.Max(1, EvenlySpacedCount));
        }
        else if (!string.IsNullOrEmpty(_valueMetric))
        {
            // In Free mode a re-read (Source / N changed) starts from the last metric's order.
            order = AnnotationPanelLayout.SortByValue(samples);
        }
        else
        {
            // No metric to sort by (an aggregator Category): keep the queue's order.
            order = Enumerable.Range(0, slices.Count).ToList();
        }

        _ordered = order.Select(i => new Entry(slices[i], samples[i].Value)).ToList();
        _populationSize = populationSize;
        UpdateSummary();

        PageIndex = 0;
        BuildPage();
    }

    private int _populationSize;

    private void UpdateSummary()
    {
        int missing = _ordered.Count(e => !e.Value.HasValue);
        bool hasMetric = !string.IsNullOrEmpty(_valueMetric);
        string set = Source == AnnotationPanelSource.EvenlySpaced
            ? $"{_ordered.Count} of {_populationSize} candidate slice(s), evenly spaced on {_valueMetric}"
            : $"{_ordered.Count} queue slice(s)";
        Summary = IsFreeOrder
            ? set + ", free order (drag tiles to reorder; double-click to load in the viewer)"
            : Source == AnnotationPanelSource.EvenlySpaced ? set
            : set + (hasMetric ? $", sorted by {_valueMetric}" : ", in queue order")
              + (missing > 0 && hasMetric ? $" ({missing} with no value, last)" : "");
    }

    /// <summary>Cells per page outside Free mode (the Per page box). Not persisted.</summary>
    public int CellsPerPage { get; set; } = AnnotationPanelLayout.DefaultPageSize;

    /// <summary>Per page has no effect in Free mode, which always shows one page.</summary>
    public bool CanSetCellsPerPage => !IsFreeOrder;

    /// <summary>Cells per page: <see cref="CellsPerPage"/>, or everything in Free mode.</summary>
    private int PageSize => IsFreeOrder ? Math.Max(1, _ordered.Count) : Math.Max(1, CellsPerPage);

    /// <summary>Index into <see cref="_ordered"/> of the current page's first cell.</summary>
    private int _pageStart;

    private void CopyOrder()
    {
        string mode = IsFreeOrder ? "free order"
            : Source == AnnotationPanelSource.EvenlySpaced ? $"evenly spaced on {_valueMetric}"
            : $"sorted by {_valueMetric}";
        string header = $"Panel order: {Category} ({_profile.Name}, {Gender}), {mode}, {DateTime.Now:yyyy-MM-dd HH:mm}";
        string text = AnnotationPanelLayout.FormatOrder(header, _ordered.Select(e => (
            e.Slice.Row.PresetLabel,
            e.Slice.Row.Weight,
            (IReadOnlyList<string>)e.Slice.Members.Where(m => !ReferenceEquals(m, e.Slice.Row)).Select(m => m.PresetLabel).ToList())));
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
        if (!IsFreeOrder || dropInfo.Data is not VM_AnnotationPanelCell || !ReferenceEquals(dropInfo.TargetCollection, Cells)) return;
        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        dropInfo.Effects = System.Windows.DragDropEffects.Move;
    }

    /// <summary>Moves the dropped tile to the insertion point. Free mode shows one page, so the cell
    /// index is also the index into the full order.</summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (!IsFreeOrder || dropInfo.Data is not VM_AnnotationPanelCell cell) return;
        int from = Cells.IndexOf(cell);
        if (from < 0) return;
        int to = AnnotationPanelLayout.MoveTarget(from, dropInfo.InsertIndex, Cells.Count);
        if (to == from) return;
        Cells.Move(from, to);
        var entry = _ordered[from];
        _ordered.RemoveAt(from);
        _ordered.Insert(to, entry);
    }

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
            var cell = new VM_AnnotationPanelCell(entry.Slice, entry.Value, _values, this);
            cell.RefreshSelection(StoredValues(entry.Slice));
            Cells.Add(cell);
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
                parts.Add("rules: " + RuleLabel(cell.Slice));
            }
            else if (!cell.Value.HasValue && !string.IsNullOrEmpty(_valueMetric))
            {
                parts.Add($"no {_valueMetric} value");
            }
            if (cell.Slice.Members.Count > 1) parts.Add($"+{cell.Slice.Members.Count - 1} alias");
            cell.Caption = string.Join(" · ", parts);
        }
    }

    /// <summary>What the rules alone assign the slice in this Category (annotations re-derived out),
    /// computed on first use: Show values is the only reader.</summary>
    private string RuleLabel(VM_AnnotationQueueSlice slice)
    {
        var key = (slice.Row.PresetLabel, slice.Row.Weight);
        if (_ruleLabels.TryGetValue(key, out var cached)) return cached;
        string label;
        try
        {
            _profileModel ??= _profile.DumpToModel();
            _seedContext ??= _editor.BuildExternalDescriptorSeedContext();
            var values = _profile.DeriveRuleOnlyDescriptors((slice.Row.PresetLabel, slice.Row.Gender, slice.Row.Weight), _profileModel, _seedContext)
                .Where(p => string.Equals(p.Category, Category, StringComparison.Ordinal) && !string.IsNullOrEmpty(p.Value))
                .Select(p => p.Value)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            label = values.Count == 0 ? "(none)" : string.Join(" + ", values);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Annotation Panel: rule label for '" + slice.Row.PresetLabel + "' failed: " + ExceptionLogger.GetExceptionStack(ex));
            label = "?";
        }
        _ruleLabels[key] = label;
        return label;
    }

    // ---------- verdicts ----------

    private IReadOnlyList<string> StoredValues(VM_AnnotationQueueSlice slice)
        => AnnotationVerdictWriter.ReadValues(_profile.PresetAnnotations, slice.Row.PresetLabel, slice.Row.Gender, slice.Row.Weight, Category);

    /// <summary>A value toggle on a cell: flips <paramref name="value"/> in the slice's stored verdict
    /// and saves it for the whole alias family at once (a tag set, not a radio group -- D23).</summary>
    internal void ToggleValue(VM_AnnotationPanelCell cell, string value)
    {
        if (IsStale) return;
        var current = StoredValues(cell.Slice).ToHashSet(StringComparer.Ordinal);
        if (!current.Remove(value)) current.Add(value);
        // Menu order, with any value the menu no longer lists kept at the end.
        var next = _values.Where(current.Contains).Concat(current.Where(v => !_values.Contains(v))).ToList();

        _queue.WriteVerdict(cell.Slice, Category, next);
        if (next.Count > 0 && _countedLabelled.Add(cell.Slice)) _queue.CountPanelLabel(cell.Slice);
        cell.RefreshSelection(StoredValues(cell.Slice));
    }

    private void HandleQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsStale) return;
        if (e.PropertyName == nameof(VM_AnnotationQueue.QueueGeneration) && _queue.QueueGeneration != _queueGenerationAtOpen)
        {
            MarkStale("The queue was rebuilt or cleared since this panel opened -- close it and press Open Panel again. Editing is disabled.");
        }
        else if (e.PropertyName == nameof(VM_AnnotationQueue.TargetCategory)
                 && !string.Equals(_queue.TargetCategory, Category, StringComparison.Ordinal))
        {
            MarkStale($"The queue's target Category is no longer {Category} -- close this panel and reopen it. Editing is disabled.");
        }
    }

    private void MarkStale(string message)
    {
        IsStale = true;
        StaleMessage = message;
        foreach (var cell in Cells) cell.CanEdit = false;
    }

    // ---------- HD ----------

    internal void HideAndDisablePreset(VM_AnnotationPanelCell cell) => _editor.HideAndDisablePreset(cell.PresetLabel, cell.Gender);

    /// <summary>A preset was hidden-and-disabled (here or anywhere in the editor): drop its cells and
    /// re-page, keeping the page the user is on where possible.</summary>
    private void OnPresetHiddenAndDisabled(string presetLabel, Gender gender)
    {
        if (gender != Gender) return;
        int removed = _ordered.RemoveAll(e => string.Equals(e.Slice.Row.PresetLabel, presetLabel, StringComparison.Ordinal));
        if (removed == 0) return;
        BuildPage();
    }

    internal void LoadInEditorViewer(VM_AnnotationPanelCell cell) => _editor.LoadSliceInViewer(cell.PresetLabel, cell.Gender, cell.Weight);

    // ---------- rendering ----------

    public string PrimaryViewLabel => ShowBack ? "Back" : "Front";

    private string CurrentOverlay()
        => ShowMeasurements && !string.IsNullOrEmpty(_valueMetric) ? SpreadThumbnailRenderer.JoinOverlay(new[] { _valueMetric }) : "";

    /// <summary>Current page's images first (in display order), then the next page's, so paging
    /// forward usually finds its images ready.</summary>
    private IEnumerable<SpreadThumbnailRenderer.RenderKey> WantedKeys()
    {
        float primaryAz = ShowBack ? SpreadThumbnailRenderer.BackAzimuth : SpreadThumbnailRenderer.FrontAzimuth;
        string overlay = CurrentOverlay();
        var (start, count) = AnnotationPanelLayout.PageRange(_ordered.Count, PageIndex, PageSize);
        int end = Math.Min(_ordered.Count, start + count + PageSize);
        for (int i = start; i < end; i++)
        {
            var row = _ordered[i].Slice.Row;
            yield return new SpreadThumbnailRenderer.RenderKey(row.PresetLabel, row.Weight, primaryAz, overlay);
            yield return new SpreadThumbnailRenderer.RenderKey(row.PresetLabel, row.Weight, SpreadThumbnailRenderer.SideAzimuth, overlay);
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
        _queue.PropertyChanged -= HandleQueuePropertyChanged;
        _editor.PresetHiddenAndDisabled -= OnPresetHiddenAndDisabled;
        _renderer.Dispose();
        base.Dispose();
    }
}

/// <summary>One annotation Panel metric: <see cref="Display"/> marks the Category's own measurements.</summary>
public sealed record PanelMetricOption(string Display, string Name)
{
    public override string ToString() => Display;
}

/// <summary>One slice in the annotation Panel: images, caption, and a toggle per Category value.</summary>
public class VM_AnnotationPanelCell : VM
{
    internal VM_AnnotationPanelCell(VM_AnnotationQueueSlice slice, double? value, IReadOnlyList<string> values, VM_AnnotationPanel parent)
    {
        Slice = slice;
        Value = value;
        PresetLabel = slice.Row.PresetLabel;
        Gender = slice.Row.Gender;
        Weight = slice.Row.Weight;
        CanEdit = !parent.IsStale;
        foreach (var v in values)
        {
            Toggles.Add(new VM_AnnotationPanelToggle(v, this, parent));
        }
        // A single click would fire at the start of every drag in Free mode, so there it takes a double-click.
        LoadInViewerCommand = new RelayCommand(_ => !parent.IsFreeOrder, _ => parent.LoadInEditorViewer(this));
        LoadInViewerFreeCommand = new RelayCommand(_ => parent.IsFreeOrder, _ => parent.LoadInEditorViewer(this));
        HideAndDisableCommand = new RelayCommand(_ => true, _ => parent.HideAndDisablePreset(this));
    }

    internal VM_AnnotationQueueSlice Slice { get; }
    internal double? Value { get; }
    public string PresetLabel { get; }
    public Gender Gender { get; }
    public int Weight { get; }
    public string Caption { get; set; } = "";

    /// <summary>"Also covers: X, Y" for an alias family; the label's tooltip.</summary>
    public string AliasTooltip => Slice.Members.Count > 1
        ? PresetLabel + Environment.NewLine + "Also covers: " + string.Join(", ", Slice.Members.Where(m => !ReferenceEquals(m, Slice.Row)).Select(m => m.PresetLabel))
        : PresetLabel;

    /// <summary>False once the Panel is stale; disables the toggles.</summary>
    public bool CanEdit { get; set; }

    public ObservableCollection<VM_AnnotationPanelToggle> Toggles { get; } = new();

    public BitmapSource? PrimaryImage { get; private set; }
    public BitmapSource? SideImage { get; private set; }
    public bool IsPrimaryLoading { get; private set; } = true;
    public bool IsSideLoading { get; private set; } = true;
    public bool IsPrimaryFailed { get; private set; }
    public bool IsSideFailed { get; private set; }

    public RelayCommand LoadInViewerCommand { get; }
    public RelayCommand LoadInViewerFreeCommand { get; }
    public RelayCommand HideAndDisableCommand { get; }

    internal void RefreshSelection(IReadOnlyList<string> stored)
    {
        var set = stored.ToHashSet(StringComparer.Ordinal);
        foreach (var t in Toggles) t.IsSelected = set.Contains(t.Value);
    }

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

/// <summary>One value toggle under a Panel cell. Reflects what is stored; clicking saves.</summary>
public class VM_AnnotationPanelToggle : VM
{
    internal VM_AnnotationPanelToggle(string value, VM_AnnotationPanelCell cell, VM_AnnotationPanel parent)
    {
        Value = value;
        ToggleCommand = new RelayCommand(_ => cell.CanEdit, _ => parent.ToggleValue(cell, value));
    }

    public string Value { get; }
    public bool IsSelected { get; set; }
    public RelayCommand ToggleCommand { get; }
}

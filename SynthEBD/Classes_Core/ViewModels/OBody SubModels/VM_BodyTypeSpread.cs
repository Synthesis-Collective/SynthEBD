using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Media.Imaging;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for <see cref="Window_BodyTypeSpread"/>: for one descriptor Category of a Body Type
/// Profile, one row per descriptor value, each showing the Min / Mean / Median / Peak / Max slice on
/// the selected metric as front + side (or back + side) offscreen renders. Opened from the Rules and
/// Match Presets tabs so the user can judge whether a Category's thresholds put boundary and
/// representative presets where they belong.
///
/// <para><b>Data.</b> Snapshotted at open time, like <see cref="VM_MeasurementHistogram"/>: slices come
/// from <see cref="VM_BodyTypeProfile.MeasurementCache"/> restricted to the body type's gender, and a
/// slice lands in a value's row when its descriptors (classifier output plus seeds, i.e.
/// <see cref="VM_BodyTypeProfile.GetMatchesWithSeeds"/>) carry that value. Each slice keeps its own
/// weight, so a statistic is drawn at whatever weight its slice was measured at. Re-open the window
/// after a re-scan to see new numbers.</para>
///
/// <para><b>Rendering.</b> Through a <see cref="SpreadThumbnailRenderer"/> (shared offscreen renderer,
/// one request at a time, first still-missing image in display order first, fixed whole-body
/// camera). Images are cached per (preset, weight, view angle) for the window's lifetime, so flipping
/// back to an earlier metric is instant.</para>
/// </summary>
public class VM_BodyTypeSpread : VM
{
    private const float FrontAzimuth = SpreadThumbnailRenderer.FrontAzimuth;
    private const float BackAzimuth = SpreadThumbnailRenderer.BackAzimuth;
    private const float SideAzimuth = SpreadThumbnailRenderer.SideAzimuth;

    /// <summary>Row label for slices that carry no value in the Category (no rule fired and the
    /// Category has no default).</summary>
    public const string UnassignedRowLabel = "(unassigned)";

    private readonly Logger _logger;
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly VM_BodyTypeProfile _profile;
    private readonly List<SliceData> _slices;
    private readonly Dictionary<(string PresetLabel, Gender Gender, int Weight), SliceData> _sliceByKey = new();
    private readonly List<RowData> _rowMembers;
    private readonly string? _primaryMeasurement;
    private readonly SpreadThumbnailRenderer _renderer;

    /// <summary>One descriptor value's row membership, in display order (reordered by the user or
    /// by Sort Rows; RebuildRows follows this list).</summary>
    private sealed record RowData(string Value, bool IsUnassigned, List<SliceData> Members);

    /// <summary>One cache slice with the values every metric needs, copied at open time.
    /// <see cref="AllValues"/> is the slice's Category values as Match Presets shows them (rules plus
    /// the preset's manual / library annotations); <see cref="RuleValues"/> is what the rules alone
    /// assign. <see cref="Contradiction"/> is non-empty when an annotation names a value the rules
    /// don't produce.</summary>
    private sealed record SliceData(
        string PresetLabel, Gender Gender, int Weight,
        IReadOnlyDictionary<string, float?> Measurements,
        HashSet<string> AllValues,
        HashSet<string> RuleValues,
        string Contradiction);

    internal VM_BodyTypeSpread(
        VM_BodyTypeProfileEditor editor,
        VM_BodyTypeProfile profile,
        string category,
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
        Gender = gender;
        Title = $"Spread: {category} ({profile.Name}, {gender})";

        // ---- slices ----
        // Two memberships per slice: by the rules alone (manual / library annotations re-derived
        // out, so they neither add a value nor suppress the Category default nor feed aggregator
        // rules) and as Match Presets shows it (rules + annotations). An annotated value the rules
        // don't produce is a contradiction, flagged in orange when annotations are included.
        HashSet<string> InCategory(IEnumerable<(string Category, string Value)> pairs) => pairs
            .Where(p => string.Equals(p.Category, category, StringComparison.Ordinal) && !string.IsNullOrEmpty(p.Value))
            .Select(p => p.Value)
            .ToHashSet(StringComparer.Ordinal);
        static string JoinValues(IEnumerable<string> values) => string.Join(", ", values.OrderBy(v => v, StringComparer.Ordinal));

        var profileModel = profile.DumpToModel();
        var seedContext = editor.BuildExternalDescriptorSeedContext();
        _slices = new List<SliceData>();
        foreach (var kv in profile.ListedMeasurementCache())
        {
            if (kv.Key.Gender != gender || kv.Value?.Measurements == null) continue;
            profile.ScanResults.TryGetValue(kv.Key, out var classifier);
            var all = InCategory(profile.GetMatchesWithSeeds(kv.Key, classifier ?? new List<BodyShapeDescriptor.LabelSignature>())
                .Select(m => (m.Category, m.Value)));
            var ruleOnly = InCategory(profile.DeriveRuleOnlyDescriptors(kv.Key, profileModel, seedContext));
            var annotatedOnly = profile.SeedDescriptors.TryGetValue(kv.Key, out var seeds)
                ? InCategory(seeds)
                : new HashSet<string>(StringComparer.Ordinal);
            annotatedOnly.ExceptWith(ruleOnly);
            string contradiction = annotatedOnly.Count == 0 ? ""
                : $"Annotated {JoinValues(annotatedOnly)}, but the rules say {(ruleOnly.Count == 0 ? "nothing" : JoinValues(ruleOnly))}.";
            var slice = new SliceData(kv.Key.PresetLabel, kv.Key.Gender, kv.Key.Weight,
                new Dictionary<string, float?>(kv.Value.Measurements, StringComparer.Ordinal), all, ruleOnly, contradiction);
            _slices.Add(slice);
            _sliceByKey[kv.Key] = slice;
        }

        // ---- rows: Rules-tab order first, then any other value a slice carries (in either
        // membership, so the row set is stable across the annotations toggle), then unassigned ----
        var order = new List<string>();
        var treeNode = profile.RuleTreeCategories.FirstOrDefault(c => string.Equals(c.Category, category, StringComparison.Ordinal));
        if (treeNode != null) order.AddRange(treeNode.Values.Select(v => v.Value));
        foreach (var v in _slices.SelectMany(s => s.AllValues.Concat(s.RuleValues)).Distinct().OrderBy(v => v, StringComparer.Ordinal))
        {
            if (!order.Contains(v)) order.Add(v);
        }
        _rowMembers = order.Select(v => new RowData(v, false, new List<SliceData>())).ToList();
        RebuildMembership();

        // ---- metrics: every measurement that can decide the Category, then the aggregate score ----
        foreach (var name in VM_BodyTypeProfileEditor.CollectCategoryMeasurementNames(profile, category, gender))
        {
            Metrics.Add(new SpreadMetricOption(name, name));
        }
        Metrics.Add(new SpreadMetricOption("Score (σ margin)", null));
        // One measurement decides the Category: show it directly. Several (including those reached
        // through descriptor references): no single one tells the whole story, so open on Score,
        // which folds them into the margin the rules actually decide on.
        SelectedMetric = Metrics.Count(m => !m.IsScore) > 1 ? Metrics[^1] : Metrics[0];

        _allRules = profile.Rules.ToList();
        _defaultsByCategory = profile.GetDefaultValuesByCategory();
        _stdDevs = VM_BodyTypeProfileEditor.ComputeStdDevsForAllRules(profile);
        foreach (var rule in _allRules)
        {
            foreach (var group in rule?.Groups ?? Enumerable.Empty<VM_AndGatedMeasurementGroup>())
            {
                foreach (var cond in group?.Conditions ?? Enumerable.Empty<VM_MeasurementCondition>())
                {
                    if (cond?.Kind != MeasurementConditionKind.Measurement || string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    if (!_thresholdsByMeasurement.TryGetValue(cond.MeasurementName, out var set))
                        _thresholdsByMeasurement[cond.MeasurementName] = set = new HashSet<double>();
                    set.Add(cond.Value);
                }
            }
        }
        _renderer = new SpreadThumbnailRenderer(profile, scene, renderSettingsSource, presetLookup, logger,
            "Show Spread", WantedKeys, ApplyImagesAndPump);

        // Natural row order: sort by each value's median on the measurement the Category's own
        // rules test most, so thresholds read low -> high (Skinny, Normal, Thick) instead of
        // alphabetically. Aggregator Categories whose rules only reference other descriptors have
        // no such measurement and keep the Rules-tab order.
        _primaryMeasurement = FindPrimaryMeasurement(profile, category, gender);
        if (_primaryMeasurement != null) SortRowsBy(_primaryMeasurement);

        MoveRowUpCommand = new RelayCommand(
            canExecute: r => r is VM_BodyTypeSpreadRow row && Rows.IndexOf(row) > 0,
            execute: r => MoveRow((VM_BodyTypeSpreadRow)r, -1));
        MoveRowDownCommand = new RelayCommand(
            canExecute: r => r is VM_BodyTypeSpreadRow row && Rows.IndexOf(row) is int i && i >= 0 && i < Rows.Count - 1,
            execute: r => MoveRow((VM_BodyTypeSpreadRow)r, +1));
        SortRowsCommand = new RelayCommand(
            canExecute: _ => SortMeasurement != null,
            execute: _ =>
            {
                SortRowsBy(SortMeasurement!);
                RebuildRows();
            });

        this.WhenAnyValue(x => x.SelectedMetric, x => x.PeakBinCount)
            .Subscribe(_ => RebuildRows())
            .DisposeWith(this);
        this.WhenAnyValue(x => x.ShowBack, x => x.ShowMeasurements)
            .Skip(1)
            .Subscribe(_ => ApplyImagesAndPump())
            .DisposeWith(this);
        this.WhenAnyValue(x => x.IgnoreManualAnnotations)
            .Skip(1)
            .Subscribe(_ =>
            {
                RebuildMembership();
                RebuildRows();
            })
            .DisposeWith(this);

        _editor.PresetHiddenAndDisabled += OnPresetHiddenAndDisabled;
    }

    /// <summary>A preset was hidden-and-disabled (from this window's HD button or any other editor list):
    /// drop its slices, recompute the sigmas the Score metric normalizes by over the smaller population,
    /// and re-pick every row's representatives.</summary>
    private void OnPresetHiddenAndDisabled(string presetLabel, Gender gender)
    {
        if (gender != Gender) return;
        int removed = _slices.RemoveAll(s => string.Equals(s.PresetLabel, presetLabel, StringComparison.Ordinal));
        if (removed == 0) return;
        foreach (var key in _sliceByKey.Keys.Where(k => k.Gender == gender && string.Equals(k.PresetLabel, presetLabel, StringComparison.Ordinal)).ToList())
        {
            _sliceByKey.Remove(key);
        }
        _stdDevs = VM_BodyTypeProfileEditor.ComputeStdDevsForAllRules(_profile);
        RebuildMembership();
        RebuildRows();
    }

    /// <summary>Row-level "HD" button on a cell: hides and disables the cell's preset via the editor.</summary>
    internal void HideAndDisablePreset(VM_BodyTypeSpreadCell cell)
    {
        _editor.HideAndDisablePreset(cell.PresetLabel, cell.Gender);
    }

    /// <summary>When on (the default), rows hold presets purely by rule compliance: manual / library
    /// annotations are ignored. When off, rows follow Match Presets (rules + annotations) and a preset
    /// whose annotation contradicts the rules is labeled in orange.</summary>
    public bool IgnoreManualAnnotations { get; set; } = true;

    private HashSet<string> CurrentValues(SliceData s) => IgnoreManualAnnotations ? s.RuleValues : s.AllValues;

    /// <summary>Refills each row's members for the current annotations mode, keeping the row order
    /// (user moves and sorts survive the toggle). The unassigned row is re-added last when any slice
    /// carries no value in the Category.</summary>
    private void RebuildMembership()
    {
        var rebuilt = _rowMembers
            .Where(r => !r.IsUnassigned)
            .Select(r => new RowData(r.Value, false, _slices.Where(s => CurrentValues(s).Contains(r.Value)).ToList()))
            .ToList();
        var unassigned = _slices.Where(s => CurrentValues(s).Count == 0).ToList();
        if (unassigned.Count > 0) rebuilt.Add(new RowData(UnassignedRowLabel, true, unassigned));
        _rowMembers.Clear();
        _rowMembers.AddRange(rebuilt);
    }

    /// <summary>The measurement named by the most Measurement conditions in the Category's own
    /// enabled, gender-eligible rules (ties go to the earlier metric in the picker), or null when
    /// those rules test no measurement directly.</summary>
    private string? FindPrimaryMeasurement(VM_BodyTypeProfile profile, string category, Gender gender)
        => FindPrimaryMeasurement(profile, category, gender,
            Metrics.Where(m => !m.IsScore).Select(m => m.MeasurementName!).ToList());

    /// <summary>As above, with ties going to the earlier name in <paramref name="metricOrder"/>, and
    /// only names in it eligible. Shared with the annotation Panel's default metric.</summary>
    internal static string? FindPrimaryMeasurement(VM_BodyTypeProfile profile, string category, Gender gender,
        IReadOnlyList<string> metricOrder)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in profile.Rules)
        {
            if (rule == null || !string.Equals(rule.DescriptorCategory, category, StringComparison.Ordinal)) continue;
            if (!BodySlideMeasurementEvaluator.RuleGenderMatches(rule.Gender, gender)) continue;
            foreach (var group in rule.Groups)
            {
                if (group?.Conditions == null || group.IsDisabled) continue;
                foreach (var cond in group.Conditions)
                {
                    if (cond?.Kind != MeasurementConditionKind.Measurement || string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    counts[cond.MeasurementName] = counts.GetValueOrDefault(cond.MeasurementName) + 1;
                }
            }
        }
        if (counts.Count == 0) return null;
        return metricOrder
            .Where(counts.ContainsKey)
            .OrderByDescending(m => counts[m])
            .FirstOrDefault();
    }

    /// <summary>Reorders the rows by each row's median on <paramref name="measurementName"/>.
    /// The unassigned row stays last.</summary>
    private void SortRowsBy(string measurementName)
    {
        var sortable = _rowMembers.Where(r => !r.IsUnassigned).ToList();
        var values = sortable
            .Select(r => r.Members.Select(s =>
                s.Measurements.TryGetValue(measurementName, out var f) && f.HasValue ? (double)f.Value : double.NaN))
            .ToList();
        var order = SpreadStatistics.OrderByMedian(values);
        var reordered = order.Select(i => sortable[i]).Concat(_rowMembers.Where(r => r.IsUnassigned)).ToList();
        _rowMembers.Clear();
        _rowMembers.AddRange(reordered);
    }

    private void MoveRow(VM_BodyTypeSpreadRow row, int delta)
    {
        int from = Rows.IndexOf(row);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count) return;
        Rows.Move(from, to);
        var data = _rowMembers[from];
        _rowMembers.RemoveAt(from);
        _rowMembers.Insert(to, data);
    }

    /// <summary>Measurement Sort Rows orders by: the selected metric, or for Score (whose per-row
    /// values each measure how firmly a row holds its own value, so don't compare across rows) the
    /// Category's primary measurement.</summary>
    private string? SortMeasurement => SelectedMetric is { IsScore: false } m ? m.MeasurementName : _primaryMeasurement;

    public RelayCommand MoveRowUpCommand { get; }
    public RelayCommand MoveRowDownCommand { get; }
    public RelayCommand SortRowsCommand { get; }

    /// <summary>When on, each image also draws the lines of the measurement being spread (every
    /// Category measurement for Score), resolved on that preset's own deformed mesh.</summary>
    public bool ShowMeasurements { get; set; }

    /// <summary>Measurement names whose lines the images draw, joined as a cache-key string ("" = none).</summary>
    private string CurrentOverlay()
    {
        if (!ShowMeasurements || SelectedMetric == null) return "";
        var names = SelectedMetric.IsScore
            ? Metrics.Where(m => !m.IsScore).Select(m => m.MeasurementName!)
            : new[] { SelectedMetric.MeasurementName! };
        return SpreadThumbnailRenderer.JoinOverlay(names);
    }

    private readonly List<VM_MeasurementRule> _allRules;

    /// <summary>Every threshold any rule compares each measurement against, so labels are printed
    /// precisely enough to tell a value from the boundary it sits beside.</summary>
    private readonly Dictionary<string, HashSet<double>> _thresholdsByMeasurement = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> _defaultsByCategory;
    private Dictionary<string, double> _stdDevs;

    public string Title { get; }
    public string Category { get; }
    public Gender Gender { get; }

    /// <summary>Metric picker entries: each measurement that can decide the Category, then the score.</summary>
    public ObservableCollection<SpreadMetricOption> Metrics { get; } = new();
    public SpreadMetricOption SelectedMetric { get; set; }

    /// <summary>Bin count for the Peak statistic. Same equal-width binning as the measurement histogram.</summary>
    public int PeakBinCount { get; set; } = SpreadStatistics.DefaultPeakBinCount;

    /// <summary>When on, each cell's first image shows the back instead of the front.</summary>
    public bool ShowBack { get; set; }

    public string PrimaryViewLabel => ShowBack ? "Back" : "Front";

    public IReadOnlyList<string> ColumnHeaders { get; } = Enum.GetNames<SpreadStatistic>();

    public ObservableCollection<VM_BodyTypeSpreadRow> Rows { get; } = new();

    /// <summary>Render progress readout ("Rendering 12 / 40…"), empty when idle.</summary>
    public string RenderStatus { get; set; } = "";

    private void RebuildRows()
    {
        var metric = SelectedMetric;
        Rows.Clear();
        if (metric == null) return;

        var built = new List<(VM_BodyTypeSpreadRow Row, IReadOnlyList<SpreadPick> Picks)>();
        foreach (var (value, isUnassigned, members) in _rowMembers)
        {
            var samples = new List<SpreadSample>();
            if (!(metric.IsScore && isUnassigned))
            {
                foreach (var s in members)
                {
                    double? v = metric.IsScore
                        ? VM_BodyTypeProfileEditor.ScoreSliceForValue(
                            Category, value, s.Measurements, _stdDevs, _allRules, _defaultsByCategory, s.Gender)
                        : s.Measurements.TryGetValue(metric.MeasurementName!, out var f) && f.HasValue ? f.Value : null;
                    if (v.HasValue) samples.Add(new SpreadSample(s.PresetLabel, s.Gender, s.Weight, v.Value));
                }
            }

            var picks = SpreadStatistics.Pick(samples, Math.Max(1, PeakBinCount));
            string empty = members.Count == 0 ? "No presets carry this value."
                : metric.IsScore && isUnassigned ? "No value to score: these presets carry nothing in this Category."
                : picks.Count == 0 ? $"No preset in this row has a value for {metric.Display}."
                : "";
            built.Add((new VM_BodyTypeSpreadRow(value, members.Count, samples.Count, empty), picks));
        }

        // One precision for the whole grid: the fewest decimals (3+) at which every shown slice value
        // reads differently from every other and from the metric's rule thresholds (0 for Score, the
        // decision boundary). Otherwise a Medium max of 31.499737 and a Wide min of 31.500027 both
        // print as "31.5" and the row split looks arbitrary.
        var boundaries = metric.IsScore
            ? new[] { 0.0 }
            : (IEnumerable<double>)(_thresholdsByMeasurement.TryGetValue(metric.MeasurementName!, out var t) ? t : new HashSet<double>());
        int decimals = SpreadStatistics.DecimalsToDistinguish(
            built.SelectMany(b => b.Picks).Select(p => p.Sample.Value).Concat(boundaries));
        string numberFormat = "F" + decimals;

        foreach (var (row, picks) in built)
        {
            foreach (var pick in picks)
            {
                string contradiction = !IgnoreManualAnnotations
                    && _sliceByKey.TryGetValue((pick.Sample.PresetLabel, pick.Sample.Gender, pick.Sample.Weight), out var slice)
                    ? slice.Contradiction
                    : "";
                row.Cells.Add(new VM_BodyTypeSpreadCell(pick, metric.IsScore, contradiction, numberFormat, this));
            }
            Rows.Add(row);
        }
        ApplyImagesAndPump();
    }

    /// <summary>Pushes every cached image into the cells that show it, then makes sure the render
    /// pump is running for whatever is still missing.</summary>
    private void ApplyImagesAndPump()
    {
        ManuallyRaisePropertyChanged(nameof(PrimaryViewLabel));
        float primaryAz = ShowBack ? BackAzimuth : FrontAzimuth;
        foreach (var cell in Rows.SelectMany(r => r.Cells))
        {
            cell.SetImages(Lookup(cell, primaryAz), Lookup(cell, SideAzimuth));
        }
        UpdateRenderStatus();
        _renderer.Pump();
    }

    private (bool Ready, BitmapSource? Image) Lookup(VM_BodyTypeSpreadCell cell, float azimuth)
        => _renderer.Lookup(cell.PresetLabel, cell.Weight, azimuth, CurrentOverlay());

    private IEnumerable<SpreadThumbnailRenderer.RenderKey> WantedKeys()
    {
        float primaryAz = ShowBack ? BackAzimuth : FrontAzimuth;
        string overlay = CurrentOverlay();
        foreach (var cell in Rows.SelectMany(r => r.Cells))
        {
            yield return new SpreadThumbnailRenderer.RenderKey(cell.PresetLabel, cell.Weight, primaryAz, overlay);
            yield return new SpreadThumbnailRenderer.RenderKey(cell.PresetLabel, cell.Weight, SideAzimuth, overlay);
        }
    }

    private void UpdateRenderStatus() => RenderStatus = _renderer.DescribeProgress(WantedKeys());

    /// <summary>Loads a cell's slice into the Body Type Profile editor's live viewer for close
    /// inspection (rotate, zoom, measurement readouts).</summary>
    internal void LoadInEditorViewer(VM_BodyTypeSpreadCell cell)
    {
        _editor.LoadSliceInViewer(cell.PresetLabel, cell.Gender, cell.Weight);
    }

    public override void Dispose()
    {
        _editor.PresetHiddenAndDisabled -= OnPresetHiddenAndDisabled;
        _renderer.Dispose();
        base.Dispose();
    }
}

/// <summary>One Show Spread metric: a measurement (<see cref="MeasurementName"/> set) or the
/// σ-normalized rule margin score for the row's value (<see cref="MeasurementName"/> null).</summary>
public sealed record SpreadMetricOption(string Display, string? MeasurementName)
{
    public bool IsScore => MeasurementName == null;
    public override string ToString() => Display;
}

/// <summary>One descriptor value's row in the Show Spread window.</summary>
public class VM_BodyTypeSpreadRow : VM
{
    public VM_BodyTypeSpreadRow(string value, int sliceCount, int sampleCount, string emptyMessage)
    {
        Value = value;
        EmptyMessage = emptyMessage;
        Header = sampleCount == sliceCount
            ? $"{value}\n{sliceCount} slice(s)"
            : $"{value}\n{sampleCount} of {sliceCount} slice(s) with a value";
    }

    public string Value { get; }
    public string Header { get; }
    public string EmptyMessage { get; }
    public bool IsEmpty => EmptyMessage.Length > 0;
    public ObservableCollection<VM_BodyTypeSpreadCell> Cells { get; } = new();
}

/// <summary>One statistic's representative slice: two images (front or back, plus side) and a caption.</summary>
public class VM_BodyTypeSpreadCell : VM
{
    /// <param name="numberFormat">Numeric format shared by every cell in the window (e.g. "F5"), chosen
    /// so no two different values print alike.</param>
    public VM_BodyTypeSpreadCell(SpreadPick pick, bool isScore, string contradiction, string numberFormat, VM_BodyTypeSpread parent)
    {
        Contradiction = contradiction ?? "";
        Statistic = pick.Statistic;
        PresetLabel = pick.Sample.PresetLabel;
        Gender = pick.Sample.Gender;
        Weight = pick.Sample.Weight;
        string unit = isScore ? "σ" : "";
        string FormatNumber(double v) => v.ToString(numberFormat);
        string value = FormatNumber(pick.Sample.Value) + unit;
        bool between = Math.Abs(pick.StatisticValue - pick.Sample.Value) > 1e-9;
        Caption = $"W{Weight} · {value}"
            + (between ? $"  ({Statistic} {FormatNumber(pick.StatisticValue)}{unit})" : "");
        LoadInViewerCommand = new RelayCommand(_ => true, _ => parent.LoadInEditorViewer(this));
        HideAndDisableCommand = new RelayCommand(_ => true, _ => parent.HideAndDisablePreset(this));
    }

    public SpreadStatistic Statistic { get; }
    public string PresetLabel { get; }
    public Gender Gender { get; }
    public int Weight { get; }
    public string Caption { get; }

    /// <summary>Why this preset's manual annotation disagrees with the rules ("" = it doesn't, or
    /// annotations are being ignored). Non-empty turns the preset label orange.</summary>
    public string Contradiction { get; }
    public bool IsContradicted => Contradiction.Length > 0;

    public BitmapSource? PrimaryImage { get; private set; }
    public BitmapSource? SideImage { get; private set; }
    public bool IsPrimaryLoading { get; private set; } = true;
    public bool IsSideLoading { get; private set; } = true;
    public bool IsPrimaryFailed { get; private set; }
    public bool IsSideFailed { get; private set; }

    public RelayCommand LoadInViewerCommand { get; }
    public RelayCommand HideAndDisableCommand { get; }

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

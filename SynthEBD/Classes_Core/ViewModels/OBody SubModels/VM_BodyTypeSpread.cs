using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CharacterViewer.Rendering.Offscreen;
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
/// <para><b>Rendering.</b> Uses the app's single shared <see cref="IOffscreenRenderer"/> (a FIFO queue
/// on its own GL thread) rather than live viewers — the Compare window already warns against more
/// concurrent GL contexts. This VM feeds it one request at a time, always choosing the first
/// still-missing image in display order, so switching metric or view re-prioritizes immediately
/// instead of waiting behind a backlog. Images are cached per (preset, weight, view angle) for the
/// window's lifetime, so flipping back to an earlier metric is instant.</para>
///
/// <para><b>Camera.</b> Every image uses one fixed <see cref="CameraFraming.OrbitState"/> framing the
/// whole body. Auto-framing (<see cref="CameraFraming.MeshAware"/>) would fit each body's own bounds,
/// scaling a large preset down and a small one up — hiding exactly the size differences being judged.</para>
/// </summary>
public class VM_BodyTypeSpread : VM
{
    /// <summary>Azimuths in the orbit camera's convention: 180 faces the character's front
    /// (Skyrim characters face -Z), 0 its back, 90 its side.</summary>
    private const float FrontAzimuth = 180f;
    private const float BackAzimuth = 0f;
    private const float SideAzimuth = 90f;

    /// <summary>Whole-body framing at model scale 1: the body spans roughly Y 0..128 (the head sits at
    /// Y 120), so the camera orbits the midpoint and backs off far enough for the orbit camera's 25°
    /// vertical FOV to fit ~140 units of height (70 / tan 12.5° ≈ 316).</summary>
    private const float CameraTargetY = 64f;
    private const float CameraDistance = 320f;

    /// <summary>Render size per image; 5:9 portrait to suit a standing body. The view scales it to the cell.</summary>
    internal const int ImageWidth = 240;
    internal const int ImageHeight = 432;

    /// <summary>Row label for slices that carry no value in the Category (no rule fired and the
    /// Category has no default).</summary>
    public const string UnassignedRowLabel = "(unassigned)";

    private readonly Logger _logger;
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly SceneInputsSnapshot _scene;
    private readonly VM_CharacterViewer _renderSettingsSource;
    private readonly Func<string, BodySlideSetting?> _presetLookup;
    private readonly List<SliceData> _slices;
    private readonly Dictionary<(string PresetLabel, Gender Gender, int Weight), SliceData> _sliceByKey = new();
    private readonly List<RowData> _rowMembers;
    private readonly string? _primaryMeasurement;
    private readonly IReadOnlyDictionary<string, NamedKeyVertex> _keyVertsByName;
    private readonly IReadOnlyDictionary<string, RegionVolumeEvaluator.ResolvedRegion>? _resolvedRegions;
    private readonly IReadOnlyDictionary<string, VM_BodyTypeProfile.MeasurementLineSpec> _lineSpecs;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _regionNamesByMeasurement;
    private readonly Dictionary<RenderKey, BitmapSource?> _images = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;
    private bool _loggedFirstRender;
    private bool _loggedFirstOverlay;

    /// <summary>Overlay is the measurement-line set drawn on the image ("" = none), so toggling
    /// Show Measurements, or switching metric with it on, caches a separate image.</summary>
    private readonly record struct RenderKey(string PresetLabel, int Weight, float Azimuth, string Overlay);

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
        _scene = scene;
        _renderSettingsSource = renderSettingsSource;
        _presetLookup = presetLookup;
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
        foreach (var kv in profile.MeasurementCache)
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
        (_keyVertsByName, _resolvedRegions, _lineSpecs, _regionNamesByMeasurement) = profile.SnapshotMeasurementOverlayInputs(renderSettingsSource);

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
        return Metrics
            .Where(m => !m.IsScore && counts.ContainsKey(m.MeasurementName!))
            .OrderByDescending(m => counts[m.MeasurementName!])
            .Select(m => m.MeasurementName)
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
        return string.Join('\u001f', names);
    }

    private readonly List<VM_MeasurementRule> _allRules;

    /// <summary>Every threshold any rule compares each measurement against, so labels are printed
    /// precisely enough to tell a value from the boundary it sits beside.</summary>
    private readonly Dictionary<string, HashSet<double>> _thresholdsByMeasurement = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> _defaultsByCategory;
    private readonly Dictionary<string, double> _stdDevs;

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
        PumpRenders();
    }

    private (bool Ready, BitmapSource? Image) Lookup(VM_BodyTypeSpreadCell cell, float azimuth)
        => _images.TryGetValue(new RenderKey(cell.PresetLabel, cell.Weight, azimuth, CurrentOverlay()), out var img) ? (true, img) : (false, null);

    private IEnumerable<RenderKey> WantedKeys()
    {
        float primaryAz = ShowBack ? BackAzimuth : FrontAzimuth;
        string overlay = CurrentOverlay();
        foreach (var cell in Rows.SelectMany(r => r.Cells))
        {
            yield return new RenderKey(cell.PresetLabel, cell.Weight, primaryAz, overlay);
            yield return new RenderKey(cell.PresetLabel, cell.Weight, SideAzimuth, overlay);
        }
    }

    private void UpdateRenderStatus()
    {
        var wanted = WantedKeys().Distinct().ToList();
        int done = wanted.Count(k => _images.ContainsKey(k));
        RenderStatus = done < wanted.Count ? $"Rendering {done} / {wanted.Count}..." : "";
    }

    /// <summary>Renders missing images one at a time on the UI thread's async flow (the renderer does
    /// the GL work on its own thread). Re-reads the wanted list after every image, so the current
    /// metric/view always renders first. Re-entrant calls while running are no-ops.</summary>
    private async void PumpRenders()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                RenderKey? next = null;
                foreach (var k in WantedKeys())
                {
                    if (!_images.ContainsKey(k)) { next = k; break; }
                }
                if (next == null) break;

                BitmapSource? image = null;
                try
                {
                    image = await RenderAsync(next.Value);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Show Spread: render of '{next.Value.PresetLabel}' W{next.Value.Weight} failed: "
                        + ExceptionLogger.GetExceptionStack(ex));
                }
                if (_cts.IsCancellationRequested) break;
                _images[next.Value] = image;
                ApplyImagesAndPump();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Show Spread: render queue stopped: " + ExceptionLogger.GetExceptionStack(ex));
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task<BitmapSource?> RenderAsync(RenderKey key)
    {
        var preset = _presetLookup(key.PresetLabel);
        if (preset == null)
        {
            _logger?.LogMessage($"Show Spread: preset '{key.PresetLabel}' is no longer in the BodySlide list; skipping its render.");
            return null;
        }

        var vm = _renderSettingsSource;
        var overlayDiag = new System.Runtime.CompilerServices.StrongBox<string?>();
        var request = new OffscreenRenderRequest
        {
            MeshPaths = _scene.MeshPaths,
            OverrideHeadMeshAbsolutePath = _scene.OverrideHeadMeshAbsolutePath,
            TextureOverrides = _scene.TextureOverrides,
            MeshOverrides = _scene.MeshOverrides,
            Morphs = SynthEbdViewerHostState.ToMorphSet(preset),
            MorphWeight = key.Weight,
            Width = ImageWidth,
            Height = ImageHeight,
            Lighting = _scene.Lighting,
            Colors = _scene.Colors,
            BackgroundRgb = _scene.BackgroundRgb,
            Camera = new CameraFraming.OrbitState(CameraDistance, key.Azimuth, 0f, 0f, CameraTargetY, 0f),
            Cancellation = _cts.Token,
            AdditionalScopes = _scene.AdditionalScopes,
            AdditionalDataFolders = _scene.AdditionalDataFolders,
            VanillaLooseOverridesBsa = _scene.VanillaLooseOverridesBsa,
            VanillaLooseOverridesModLoose = _scene.VanillaLooseOverridesModLoose,
            AllowLoadOrderFallback = _scene.AllowLoadOrderFallback,
            // Same render-quality state as the editor's live viewer, as the software fallback does.
            RenderMissingTextureAsWireframe = vm.RenderMissingTextureAsWireframe,
            EnableToneMapping = vm.EnableToneMapping,
            EnableShadows = vm.EnableShadows,
            EnableAmbientOcclusion = vm.EnableAmbientOcclusion,
            SsaoRadius = vm.SsaoRadius,
            SsaoBias = vm.SsaoBias,
            SsaoIntensity = vm.SsaoIntensity,
            SsaoThickness = vm.SsaoThickness,
            SsaoHairGap = vm.SsaoHairGap,
            EnableEyeCatchlight = vm.EnableEyeCatchlight,
            SubsurfaceStrength = vm.SubsurfaceStrength,
            SkinSaturationBoost = vm.SkinSaturationBoost,
            VignetteRadius = vm.VignetteRadius,
            VignetteIntensity = vm.VignetteIntensity,
            Exposure = vm.Exposure,
            TonemapHairRelief = vm.TonemapHairRelief,
            HairAlbedoCompensate = vm.HairAlbedoCompensate,
            DaylightBoost = vm.DaylightBoost,
            DaylightBoostIntensity = vm.DaylightBoostIntensity,
            EnableBloom = vm.EnableBloom,
            BloomIntensity = vm.BloomIntensity,
            MissingMeshPathsOut = new List<string>(),
            BeforeDraw = key.Overlay.Length == 0 ? null : BuildMeasurementOverlayHook(key.Overlay.Split('\u001f'), overlayDiag),
        };

        var sw = Stopwatch.StartNew();
        byte[] bgra = await FallbackPreviewControllerRegistry.SharedRenderer.RenderToBgra32Async(request);
        sw.Stop();
        if (key.Overlay.Length > 0 && !_loggedFirstOverlay)
        {
            _loggedFirstOverlay = true;
            _logger?.LogMessage($"Show Spread: first measurement overlay ('{key.PresetLabel}' W{key.Weight}): "
                + (overlayDiag.Value ?? "hook never ran"));
        }
        if (!_loggedFirstRender)
        {
            _loggedFirstRender = true;
            _logger?.LogMessage($"Show Spread: first render took {sw.ElapsedMilliseconds} ms "
                + $"({ImageWidth}x{ImageHeight}, {request.MissingMeshPathsOut!.Count} missing mesh(es)).");
        }

        int stride = ImageWidth * 4;
        if (bgra == null || bgra.Length < stride * ImageHeight) return null;
        var bitmap = BitmapSource.Create(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Builds the offscreen <see cref="OffscreenRenderRequest.BeforeDraw"/> hook that draws
    /// <paramref name="measurementNames"/>' lines on the render's own deformed mesh. Key vertices
    /// resolve through <see cref="MeasurementMath.TryResolveKeyVertex"/> -- the same resolution the
    /// scan used for this preset -- rather than the live preview's cached indices, which belong to a
    /// different body. Runs on the render thread, so it only reads the immutable snapshots taken at
    /// open time. RegionVolume measurements have no line geometry; they, and measurements whose key
    /// vertices come from a Region, instead tint that region's surface translucent cyan
    /// (<see cref="VM_BodyTypeProfile.AppendMeasurementRegionTint"/>).
    /// <para><paramref name="diag"/> receives a one-line summary (specs found, each vertex ref's
    /// resolution, segment count, region tints, loaded shapes -- or the exception) for the caller to
    /// log on the UI thread, so an overlay that renders nothing says why.</para></summary>
    private Action<VM_CharacterViewer> BuildMeasurementOverlayHook(IReadOnlyList<string> measurementNames,
        System.Runtime.CompilerServices.StrongBox<string?> diag)
    {
        var keyVerts = _keyVertsByName;
        var regions = _resolvedRegions;
        var specs = measurementNames
            .Where(n => _lineSpecs.ContainsKey(n))
            .Select(n => _lineSpecs[n])
            .ToList();
        var regionNames = measurementNames
            .SelectMany(n => _regionNamesByMeasurement.TryGetValue(n, out var r) ? r : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return vm =>
        {
            var refResults = new List<string>();
            try
            {
                OpenTK.Mathematics.Vector3? Resolve(string refName)
                {
                    OpenTK.Mathematics.Vector3? r = MeasurementMath.TryResolveKeyVertex(refName, keyVerts,
                        (shape, idx) => vm.TryGetCurrentVertex(shape, idx, out var p) ? p : null,
                        shape => vm.GetShapePositions(shape),
                        shape => vm.GetShapeBoneInfo(shape),
                        regions,
                        shape => vm.GetZeroedShapePositions(shape, 0),
                        out var pos)
                        ? pos
                        : null;
                    if (!string.IsNullOrEmpty(refName))
                        refResults.Add(refName + (r.HasValue ? "=ok" : "=UNRESOLVED"));
                    return r;
                }

                var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color, string? Label)>();
                foreach (var spec in specs)
                {
                    VM_BodyTypeProfile.AppendMeasurementLineSegments(
                        spec, "", Resolve, (_, _, _) => "", segments);
                }
                vm.SetMeasurementLines(segments);

                var tint = new List<float>();
                var tintResults = new List<string>();
                foreach (var regionName in regionNames)
                {
                    tintResults.Add(regionName
                        + (VM_BodyTypeProfile.AppendMeasurementRegionTint(vm, regions, regionName, tint) ? "=ok" : "=UNRESOLVED"));
                }
                vm.SetMeasurementRegionTint(tint);

                diag.Value = $"{specs.Count}/{measurementNames.Count} measurement(s) have line specs; "
                    + $"refs [{string.Join(", ", refResults.Distinct())}]; {segments.Count} segment(s); "
                    + $"regions [{string.Join(", ", tintResults)}]; "
                    + $"shapes [{string.Join(", ", vm.GetCurrentShapeVertexCounts().Keys)}]; "
                    + DescribeLineGlState(segments.Count > 0 ? segments[0].A : null);
            }
            catch (Exception ex)
            {
                diag.Value = $"hook threw after refs [{string.Join(", ", refResults)}]: {ex.GetType().Name}: {ex.Message}";
                throw;
            }
        };
    }

    /// <summary>Diagnostic: the offscreen context's line-relevant GL state (profile / flags, the
    /// aliased line-width range, whether the renderer's 4.5 px line width is accepted), plus the
    /// first segment's endpoint so it can be sanity-checked against the mesh. Render thread only.</summary>
    private static string DescribeLineGlState(OpenTK.Mathematics.Vector3? firstPoint)
    {
        while (OpenTK.Graphics.OpenGL4.GL.GetError() != OpenTK.Graphics.OpenGL4.ErrorCode.NoError) { } // drain stale errors
        OpenTK.Graphics.OpenGL4.GL.GetInteger(OpenTK.Graphics.OpenGL4.GetPName.ContextFlags, out int flags);
        OpenTK.Graphics.OpenGL4.GL.GetInteger((OpenTK.Graphics.OpenGL4.GetPName)0x9126 /* GL_CONTEXT_PROFILE_MASK */, out int profile);
        var range = new float[2];
        OpenTK.Graphics.OpenGL4.GL.GetFloat(OpenTK.Graphics.OpenGL4.GetPName.AliasedLineWidthRange, range);
        OpenTK.Graphics.OpenGL4.GL.LineWidth(4.5f);
        var lineWidthError = OpenTK.Graphics.OpenGL4.GL.GetError();
        OpenTK.Graphics.OpenGL4.GL.LineWidth(1f);
        return $"GL flags=0x{flags:X} profileMask=0x{profile:X} aliasedLineWidth=[{range[0]}, {range[1]}] "
            + $"LineWidth(4.5)->{lineWidthError}; firstPoint={firstPoint?.ToString() ?? "none"}";
    }

    /// <summary>Loads a cell's slice into the Body Type Profile editor's live viewer for close
    /// inspection (rotate, zoom, measurement readouts).</summary>
    internal void LoadInEditorViewer(VM_BodyTypeSpreadCell cell)
    {
        _editor.LoadSliceInViewer(cell.PresetLabel, cell.Gender, cell.Weight);
    }

    public override void Dispose()
    {
        _cts.Cancel();
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

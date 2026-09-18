using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// The Label-then-Suggest annotation queue: an ordered view over
/// <see cref="VM_PresetAnnotationTable.Rows"/> that serves one (preset, gender, weight) slice at a
/// time under a sampling policy, so labelling becomes "press a digit, press Enter, next body
/// loads" instead of hunting rows in a DataGrid.
///
/// <para><b>It does not own the rows and never reorders them.</b> The table's collection is also
/// the measurement grid the user reads, so the queue keeps its own ordered list of references into
/// it. Selecting a slice is done by moving <see cref="VM_PresetAnnotationTable.SelectedRow"/>,
/// which drives the viewer and the annotation editor through wiring that already exists -- there is
/// deliberately no second path into the viewer and no second writer to
/// <c>profile.PresetAnnotations</c>.</para>
///
/// <para><b>Why a sampling policy rather than "next unlabelled row".</b> Decision <c>D25</c>: a
/// Belly rule fitted on verdicts gathered by walking the decision boundary scored 66/70 in-sample
/// and <b>8/14</b> on a random draw from the rows it changed. Every judging round before that one
/// had sampled the boundary, which is why in-sample accuracy kept flattering the fit. The queue
/// therefore always mixes a fixed, exactly-counted share of uniform-random draws into whatever
/// policy is running, and reports how many of the session's verdicts came from that share. The
/// ordering itself is pure logic in <see cref="AnnotationQueueOrdering"/>, unit-tested without the
/// UI.</para>
///
/// <para>Queue operations never trigger a scan. Rows come from the profile's shared measurement
/// cache; rescanning 25 500 slices takes minutes.</para>
/// </summary>
public class VM_AnnotationQueue : VM
{
    /// <summary>Longest run of consecutive same-(gender, weight) slices when
    /// <see cref="WeightCoherent"/> is on. Eight is enough to amortize the scene rebuild across a
    /// meaningful run while keeping each run's leading slice in policy order -- see
    /// <see cref="AnnotationQueueOrdering.CoalesceRuns"/> for why the runs are capped at all.</summary>
    private const int WeightRunLength = 8;

    private readonly VM_BodyTypeProfileEditor _editor;
    private VM_BodyTypeProfile _watchedProfile;
    private bool _suppressPersist;
    private bool _suppressSelectionEcho;
    private readonly List<VM_AnnotationQueueSlice> _slices = new();
    private int _cursor = -1;
    private CancellationTokenSource _prefetchCts;
    private VM_BodyShapeDescriptorSelectionMenu _shellMenu;

    public VM_AnnotationQueue(VM_BodyTypeProfileEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        AvailablePolicies = new ObservableCollection<AnnotationQueuePolicy>(
            (AnnotationQueuePolicy[])Enum.GetValues(typeof(AnnotationQueuePolicy)));

        BuildQueueCommand = new RelayCommand(
            canExecute: _ => _editor.SelectedProfile != null
                             && !_editor.AnnotationTable.IsScanning
                             && !string.IsNullOrEmpty(TargetCategory),
            execute: _ => BuildQueue());

        CommitAndNextCommand = new RelayCommand(
            canExecute: _ => CurrentSlice != null,
            execute: _ => CommitAndNext());

        SkipCommand = new RelayCommand(
            canExecute: _ => CurrentSlice != null,
            execute: _ => Skip());

        BackCommand = new RelayCommand(
            canExecute: _ => _cursor > 0,
            execute: _ => Back());

        ExportVerdictsCommand = new RelayCommand(
            canExecute: _ => _editor.SelectedProfile != null && !string.IsNullOrEmpty(TargetCategory),
            execute: _ => ExportVerdicts(toClipboard: false));

        CopyVerdictsCommand = new RelayCommand(
            canExecute: _ => _editor.SelectedProfile != null && !string.IsNullOrEmpty(TargetCategory),
            execute: _ => ExportVerdicts(toClipboard: true));

        ToggleValueCommand = new RelayCommand(
            canExecute: _ => CurrentSlice != null,
            execute: x =>
            {
                // KeyBinding passes its CommandParameter as a string; the digit legend passes the
                // hint's own index. Accept both rather than forcing one shape on the XAML.
                if (x is int i) { ToggleValue(i); return; }
                if (x is string sx && int.TryParse(sx, out int parsed)) ToggleValue(parsed);
            });

        ResetSessionCountersCommand = new RelayCommand(
            canExecute: _ => ServedCount > 0 || LabelledCount > 0 || SkippedCount > 0,
            execute: _ => ResetSessionCounters());

        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.AnnotationTable.PropertyChanged += OnAnnotationTablePropertyChanged;

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(TargetCategory):
                    // _suppressPersist also gates the queue drop: a programmatic re-selection (a
                    // profile load, or the ComboBox blanking itself while its item list is rebuilt)
                    // is not the user changing what they are labelling.
                    if (_suppressPersist) break;
                    RefreshValueHints();
                    RefreshTally();
                    InvalidateQueue("Target category changed -- press Build Queue.");
                    PersistSettings();
                    break;
                case nameof(Policy):
                case nameof(SpreadMeasurement):
                case nameof(BinCount):
                case nameof(Seed):
                case nameof(IncludeAnnotated):
                case nameof(DedupeAliases):
                case nameof(WeightCoherent):
                    InvalidateQueue("Sampling settings changed -- press Build Queue.");
                    PersistSettings();
                    break;
                case nameof(RandomFraction):
                    ShowRandomFractionWarning = RandomFraction <= 0.0;
                    InvalidateQueue("Sampling settings changed -- press Build Queue.");
                    PersistSettings();
                    break;
                case nameof(PrefetchEnabled):
                    PersistSettings();
                    break;
            }
        };

        BindToProfile(_editor.SelectedProfile);
    }

    // ---------- settings (persisted per profile via AnnotatorPreferences) ----------

    /// <summary>Descriptor Category the queue is labelling. Only categories the annotation editor
    /// can actually toggle are offered.</summary>
    public string TargetCategory { get; set; } = "";

    /// <summary>Categories available as a target, sourced from the annotation editor's menu so the
    /// queue can never serve slices for something the user has no way to record a verdict in.</summary>
    public ObservableCollection<string> AvailableCategories { get; } = new();

    public AnnotationQueuePolicy Policy { get; set; } = AnnotationQueuePolicy.Spread;
    public ObservableCollection<AnnotationQueuePolicy> AvailablePolicies { get; }

    /// <summary>Measurement <see cref="AnnotationQueuePolicy.Spread"/> stratifies over. Empty lets
    /// <see cref="ResolveSpreadMeasurement"/> pick.</summary>
    public string SpreadMeasurement { get; set; } = "";

    /// <summary>Measurement names offered for <see cref="SpreadMeasurement"/>, from the active
    /// profile.</summary>
    public ObservableCollection<string> AvailableMeasurements { get; } = new();

    public int BinCount { get; set; } = AnnotationQueueOrdering.DefaultBinCount;

    /// <summary>Share of served slices drawn uniformly at random even under Spread / Uncertainty.
    /// See <see cref="ShowRandomFractionWarning"/> for why zeroing it is called out.</summary>
    public double RandomFraction { get; set; } = AnnotationQueueOrdering.DefaultRandomFraction;

    /// <summary>True when <see cref="RandomFraction"/> is zero. Bound to a visible warning, and
    /// <see cref="BuildQueue"/> additionally asks for confirmation before building such a queue --
    /// a pure boundary sample produces verdicts that cannot estimate an error rate, and that is a
    /// property of the data the user will still have months later.</summary>
    public bool ShowRandomFractionWarning { get; private set; }

    public int Seed { get; set; } = 1;

    /// <summary>Serve slices already annotated in the target Category (re-judging mode).</summary>
    public bool IncludeAnnotated { get; set; }

    /// <summary>Collapse measurement-identical slices to one served representative.</summary>
    public bool DedupeAliases { get; set; } = true;

    /// <summary>Keep short runs of one (gender, weight) together so the viewer's same-NPC
    /// short-circuit absorbs most advances.</summary>
    public bool WeightCoherent { get; set; } = true;

    /// <summary>Prewarm the next slice's preview NPC while the user judges the current one.</summary>
    public bool PrefetchEnabled { get; set; } = true;

    // ---------- live queue state ----------

    /// <summary>The slice being judged, or null when no queue is built / the queue is exhausted.</summary>
    public VM_AnnotationQueueSlice CurrentSlice { get; private set; }

    /// <summary>Human-readable identity of <see cref="CurrentSlice"/> for the header.</summary>
    public string CurrentSliceLabel { get; private set; } = "No queue. Pick a Category and press Build Queue.";

    /// <summary>"3 aliases: X, Y, Z" when the current slice represents an alias family; empty
    /// otherwise. Shown so the user knows one verdict is about to cover several presets.</summary>
    public string CurrentAliasSummary { get; private set; } = "";

    /// <summary>True when the current slice came from the interleaved random stream rather than the
    /// policy's ordering. Surfaced because only these verdicts can estimate an error rate.</summary>
    public bool CurrentIsRandomDraw { get; private set; }

    /// <summary>1-based position in the queue; 0 when nothing is being served.</summary>
    public int Position { get; private set; }

    /// <summary>Number of slices in the built queue.</summary>
    public int QueueLength { get; private set; }

    public bool HasQueue { get; private set; }

    public string Status { get; private set; } = "No queue built yet.";

    /// <summary>Numbered values of the target Category, matching the digit keys 1..9.</summary>
    public ObservableCollection<VM_AnnotationValueHint> ValueHints { get; } = new();

    // ---------- session counters ----------

    public int ServedCount { get; private set; }
    public int LabelledCount { get; private set; }
    public int SkippedCount { get; private set; }

    /// <summary>How many of this session's <i>labelled</i> slices came from the random stream. The
    /// number that makes a quoted error rate honest.</summary>
    public int RandomLabelledCount { get; private set; }

    /// <summary>Per-value counts for the target Category across the whole profile (not just this
    /// session), so the user can see a value going under- or over-represented while labelling.</summary>
    public ObservableCollection<VM_AnnotationTallyRow> Tally { get; } = new();

    public RelayCommand BuildQueueCommand { get; }
    public RelayCommand CommitAndNextCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ExportVerdictsCommand { get; }
    public RelayCommand CopyVerdictsCommand { get; }
    public RelayCommand ResetSessionCountersCommand { get; }

    /// <summary>Toggles the Nth value of the target Category. Bound to the digit KeyBindings and to
    /// the clickable entries of the digit legend.</summary>
    public RelayCommand ToggleValueCommand { get; }

    /// <summary>
    /// Second-phase init, called once the annotation editor's descriptor menu exists (it is built
    /// lazily, after <see cref="VM_SettingsOBody.DescriptorUI"/> is available). Populates the
    /// category list and starts tracking menu edits so the digit legend stays in step with what is
    /// actually checked.
    /// </summary>
    public void InitializeAfterMenu()
    {
        var menu = _editor.AnnotationEditor?.DescriptorMenu;
        if (menu != null)
        {
            // Header is the menu's catch-all "something was toggled" signal -- the same one the
            // annotation editor subscribes to for its write-through.
            menu.WhenAnyValue(x => x.Header).Subscribe(_ => RefreshValueHints()).DisposeWith(this);

            // The category list is NOT final at this point. VM_SettingsOBody builds the descriptor
            // creation menu in its constructor and calls InitializeDescriptorFilter immediately, but
            // TemplateDescriptors is still empty then -- the categories are loaded afterwards, and
            // the selection menu picks them up through a throttled subscription that rebuilds its
            // shells. Reading the list once here left the target-category dropdown permanently
            // empty. Track the shells instead.
            _shellMenu = menu;
            menu.DescriptorShells.CollectionChanged += OnDescriptorShellsChanged;
        }

        RefreshAvailableCategories();
        RefreshValueHints();
        RefreshTally();
    }

    public override void Dispose()
    {
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _editor.AnnotationTable.PropertyChanged -= OnAnnotationTablePropertyChanged;
        if (_shellMenu != null) _shellMenu.DescriptorShells.CollectionChanged -= OnDescriptorShellsChanged;
        CancelPrefetch();
        base.Dispose();
    }

    // ---------- queue construction ----------

    /// <summary>
    /// Rebuilds the queue from the table's current rows under the active settings, then serves the
    /// first slice. Pure re-read of already-cached measurements -- never scans.
    /// </summary>
    public void BuildQueue()
    {
        var profile = _editor.SelectedProfile;
        if (profile == null) { Status = "No profile selected."; return; }
        if (string.IsNullOrEmpty(TargetCategory)) { Status = "Pick a target Category first."; return; }

        var rows = _editor.AnnotationTable.Rows;
        if (rows.Count == 0)
        {
            Status = "No rows. Scan the annotation table (or let it load from cache) first.";
            return;
        }

        if (RandomFraction <= 0.0 && !ConfirmZeroRandomFraction()) return;

        var measurementNames = profile.Measurements
            .Where(m => m != null && !string.IsNullOrEmpty(m.Name))
            .Select(m => m.Name)
            .ToList();

        // 1. Candidate filter: drop slices already judged in this Category unless re-judging.
        var candidates = new List<VM_PresetAnnotationRow>();
        foreach (var row in rows)
        {
            if (row == null) continue;
            if (!IncludeAnnotated && HasVerdictInTargetCategory(profile, row)) continue;
            candidates.Add(row);
        }
        if (candidates.Count == 0)
        {
            ClearQueue();
            Status = IncludeAnnotated
                ? "No rows available."
                : "Every row is already annotated in " + TargetCategory + ". Tick Include annotated to re-judge.";
            return;
        }

        // 2. Alias grouping: one representative per measurement signature.
        var groups = GroupAliases(candidates, measurementNames);

        // 3. Per-candidate policy score.
        var effectivePolicy = Policy;
        var scores = new List<double?>(groups.Count);
        if (Policy == AnnotationQueuePolicy.Uncertainty)
        {
            var categoryRules = profile.Rules
                .Where(r => r != null && string.Equals(r.DescriptorCategory, TargetCategory, StringComparison.Ordinal))
                .Select(r => r.DumpToModel())
                .ToList();

            if (categoryRules.Count == 0)
            {
                // Nothing to be uncertain about yet. The handoff's own fallback: with no boundary,
                // covering the range is the informative thing to do.
                effectivePolicy = AnnotationQueuePolicy.Spread;
                Status = TargetCategory + " has no rules yet -- falling back to Spread.";
            }
            else
            {
                var ranges = AnnotationQueueOrdering.ComputeRanges(
                    groups.Select(g => (IReadOnlyDictionary<string, float?>)g.Representative.MeasurementValues),
                    measurementNames);
                foreach (var g in groups)
                {
                    scores.Add(AnnotationQueueOrdering.MinNormalizedBoundaryDistance(
                        categoryRules, g.Representative.MeasurementValues, ranges));
                }
            }
        }

        if (scores.Count == 0)
        {
            string spreadOn = ResolveSpreadMeasurement(profile, measurementNames);
            foreach (var g in groups)
            {
                scores.Add(g.Representative.MeasurementValues != null
                           && !string.IsNullOrEmpty(spreadOn)
                           && g.Representative.MeasurementValues.TryGetValue(spreadOn, out var v)
                           && v.HasValue
                    ? (double?)v.Value
                    : null);
            }
        }

        // 4. Order, then coalesce into weight runs if asked.
        var ordered = AnnotationQueueOrdering.Order(effectivePolicy, scores, RandomFraction, Seed, BinCount);

        var built = ordered
            .Select(e => new VM_AnnotationQueueSlice(groups[e.Index].Representative, groups[e.Index].Members, e.FromRandomDraw))
            .ToList();

        if (WeightCoherent)
        {
            built = AnnotationQueueOrdering
                .CoalesceRuns(built, s => (s.Row.Gender, s.Row.Weight), WeightRunLength)
                .ToList();
        }

        _slices.Clear();
        _slices.AddRange(built);
        QueueLength = _slices.Count;
        HasQueue = QueueLength > 0;
        _cursor = -1;

        int aliasCollapsed = candidates.Count - groups.Count;
        Status = "Queued " + QueueLength + " slice(s) by " + effectivePolicy
                 + " (seed " + Seed + ", " + ordered.Count(e => e.FromRandomDraw) + " random draw(s))"
                 + (aliasCollapsed > 0 ? ", " + aliasCollapsed + " alias slice(s) collapsed" : "")
                 + ".";

        AdvanceTo(0, countAsServed: true);
    }

    /// <summary>Drops the built queue without touching any annotation. Used whenever a setting that
    /// changes what should be served is edited -- silently serving slices from a stale ordering
    /// would misreport what the sample was.</summary>
    private void InvalidateQueue(string reason)
    {
        if (_slices.Count == 0) return;
        ClearQueue();
        Status = reason;
    }

    private void ClearQueue()
    {
        CancelPrefetch();
        _slices.Clear();
        _cursor = -1;
        QueueLength = 0;
        HasQueue = false;
        CurrentSlice = null;
        Position = 0;
        CurrentIsRandomDraw = false;
        CurrentAliasSummary = "";
        CurrentSliceLabel = "No queue. Pick a Category and press Build Queue.";
    }

    /// <summary>
    /// Groups candidates whose profile measurements are identical to three decimals, so one verdict
    /// covers a family of byte-identical re-uploads.
    /// <para>Slices with no computable measurement at all are deliberately <i>not</i> grouped: they
    /// would all share the "every value missing" signature, and an unmeasurable body is not the
    /// same body as another unmeasurable body.</para>
    /// </summary>
    private List<AliasGroup> GroupAliases(List<VM_PresetAnnotationRow> candidates, List<string> measurementNames)
    {
        var groups = new List<AliasGroup>();
        if (!DedupeAliases || measurementNames.Count == 0)
        {
            foreach (var row in candidates) groups.Add(new AliasGroup(row));
            return groups;
        }

        var bySignature = new Dictionary<string, AliasGroup>(StringComparer.Ordinal);
        foreach (var row in candidates)
        {
            bool anyValue = row.MeasurementValues != null
                            && measurementNames.Any(n => row.MeasurementValues.TryGetValue(n, out var v) && v.HasValue);
            if (!anyValue)
            {
                groups.Add(new AliasGroup(row));
                continue;
            }

            string signature = AnnotationQueueOrdering.MeasurementSignature(row.MeasurementValues, measurementNames);
            if (bySignature.TryGetValue(signature, out var existing))
            {
                existing.Members.Add(row);
            }
            else
            {
                var group = new AliasGroup(row);
                bySignature[signature] = group;
                groups.Add(group);
            }
        }
        return groups;
    }

    /// <summary>Measurement <see cref="AnnotationQueuePolicy.Spread"/> stratifies over: the user's
    /// choice, else the first measurement the target Category's rules reference (the axis that
    /// Category is already decided on), else the profile's first measurement.</summary>
    private string ResolveSpreadMeasurement(VM_BodyTypeProfile profile, List<string> measurementNames)
    {
        if (!string.IsNullOrEmpty(SpreadMeasurement) && measurementNames.Contains(SpreadMeasurement, StringComparer.Ordinal))
        {
            return SpreadMeasurement;
        }

        foreach (var rule in profile.Rules)
        {
            if (rule == null) continue;
            if (!string.Equals(rule.DescriptorCategory, TargetCategory, StringComparison.Ordinal)) continue;
            var model = rule.DumpToModel();
            foreach (var group in model.GroupsORlogic ?? new List<AndGatedMeasurementGroup>())
            {
                if (group?.ConditionsANDlogic == null) continue;
                foreach (var cond in group.ConditionsANDlogic)
                {
                    if (cond == null || cond.Kind != MeasurementConditionKind.Measurement) continue;
                    if (string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    if (measurementNames.Contains(cond.MeasurementName, StringComparer.Ordinal)) return cond.MeasurementName;
                }
            }
        }

        return measurementNames.Count > 0 ? measurementNames[0] : "";
    }

    private bool ConfirmZeroRandomFraction()
    {
        return MessageWindow.DisplayNotificationYesNo(
            "No random sampling",
            "Random fraction is 0, so every slice this queue serves is chosen by the "
            + Policy + " policy."
            + Environment.NewLine + Environment.NewLine
            + "Verdicts gathered that way cannot estimate how often a rule is wrong -- a Belly rule "
            + "fitted on boundary-only verdicts scored 66/70 in-sample and 8/14 on a random draw. "
            + "The usual setting is 0.25."
            + Environment.NewLine + Environment.NewLine
            + "Build the queue anyway?");
    }

    // ---------- navigation ----------

    /// <summary>Persists whatever is checked for the current slice, propagates it to the slice's
    /// aliases, and serves the next one.</summary>
    public void CommitAndNext()
    {
        var slice = CurrentSlice;
        if (slice == null) return;

        var profile = _editor.SelectedProfile;
        var selectedValues = _editor.AnnotationEditor?.GetSelectedValues(TargetCategory) ?? new List<string>();

        if (selectedValues.Count == 0)
        {
            // The editor's write-through has already pruned this slice's annotation. Recording it
            // as labelled would inflate the session's coverage with slices carrying no verdict.
            SkippedCount++;
            Status = "Nothing selected for " + TargetCategory + " -- counted as skipped.";
        }
        else
        {
            if (profile != null) PropagateToAliases(profile, slice, selectedValues);
            LabelledCount++;
            if (slice.FromRandomDraw) RandomLabelledCount++;
            Status = "Committed " + TargetCategory + " = " + string.Join(" + ", selectedValues)
                     + (slice.Members.Count > 1 ? " (+" + (slice.Members.Count - 1) + " alias)" : "")
                     + ".";
            RefreshTally();
        }

        AdvanceTo(_cursor + 1, countAsServed: true);
    }

    /// <summary>Leaves the current slice as-is and serves the next one.</summary>
    public void Skip()
    {
        if (CurrentSlice == null) return;
        SkippedCount++;
        Status = "Skipped.";
        AdvanceTo(_cursor + 1, countAsServed: true);
    }

    /// <summary>Returns to the previous slice so it can be re-judged. Does not undo its annotation
    /// -- the editor simply shows what is stored, and any change writes through as usual.</summary>
    public void Back()
    {
        if (_cursor <= 0) return;
        AdvanceTo(_cursor - 1, countAsServed: false);
        Status = "Back to slice " + Position + " of " + QueueLength + ".";
    }

    /// <summary>Toggles the <paramref name="oneBasedIndex"/>-th value of the target Category on the
    /// current slice -- the digit-key path. A tag set, not a radio group: decision <c>D23</c>
    /// established <c>Belly = Fat + Pregnant</c> as a legitimate dual assignment.</summary>
    public void ToggleValue(int oneBasedIndex)
    {
        if (CurrentSlice == null || string.IsNullOrEmpty(TargetCategory)) return;
        var value = _editor.AnnotationEditor?.ToggleValueByIndex(TargetCategory, oneBasedIndex);
        if (value == null)
        {
            Status = "No value " + oneBasedIndex + " in " + TargetCategory + ".";
            return;
        }
        RefreshValueHints();
        Status = TargetCategory + " = " + FormatCurrentSelection() + "  (Enter to commit)";
    }

    private void AdvanceTo(int index, bool countAsServed)
    {
        CancelPrefetch();

        if (index < 0 || index >= _slices.Count)
        {
            _cursor = _slices.Count;
            CurrentSlice = null;
            Position = 0;
            CurrentIsRandomDraw = false;
            CurrentAliasSummary = "";
            CurrentSliceLabel = "Queue finished -- " + LabelledCount + " labelled, " + SkippedCount + " skipped.";
            Status = "Queue exhausted. Build a new one to continue.";
            return;
        }

        ShowSliceAt(index);
        if (countAsServed) ServedCount++;

        // Moving the table's selection is what loads the slice into the viewer and points the
        // annotation editor at it -- one path, already wired, already generation-guarded. The echo
        // guard tells OnTableSelectionChanged that this move came from here, not from the user
        // clicking a row in the grid.
        _suppressSelectionEcho = true;
        try { _editor.AnnotationTable.SelectedRow = _slices[index].Row; }
        finally { _suppressSelectionEcho = false; }

        RefreshValueHints();
        StartPrefetch(index + 1);
    }

    /// <summary>Points the queue's readouts at <paramref name="index"/> without moving the table
    /// selection. Split out of <see cref="AdvanceTo"/> so a user-driven selection change can
    /// re-sync the cursor without re-selecting the row it is already on.</summary>
    private void ShowSliceAt(int index)
    {
        _cursor = index;
        var slice = _slices[index];
        CurrentSlice = slice;
        Position = index + 1;
        CurrentIsRandomDraw = slice.FromRandomDraw;
        CurrentSliceLabel = slice.Row.PresetLabel + "  (W" + slice.Row.Weight + ", " + slice.Row.Gender + ")"
                            + "   [" + Position + " / " + QueueLength + "]";
        CurrentAliasSummary = slice.Members.Count > 1
            ? slice.Members.Count + " identical presets: " + string.Join(", ", slice.Members.Select(m => m.PresetLabel))
            : "";
    }

    /// <summary>
    /// Keeps the queue honest when the user clicks a row in the annotation table while a queue is
    /// live.
    /// <para>The queue's commit path writes the verdict to <see cref="CurrentSlice"/>'s whole alias
    /// family, but the annotation editor follows the <i>table's</i> selection. If those two drift
    /// apart -- which one grid click is enough to do -- Commit would attach the descriptors the user
    /// just ticked for one body to a different body's aliases. So: a click onto a row that is in the
    /// queue moves the cursor there, and a click onto a row that is not detaches the queue from the
    /// current slice entirely rather than leaving a stale target armed.</para>
    /// </summary>
    private void OnTableSelectionChanged()
    {
        if (_suppressSelectionEcho) return;
        if (_slices.Count == 0) return;

        var selected = _editor.AnnotationTable.SelectedRow;
        if (selected == null) return;
        if (CurrentSlice != null && ReferenceEquals(CurrentSlice.Row, selected)) return;

        for (int i = 0; i < _slices.Count; i++)
        {
            if (!ReferenceEquals(_slices[i].Row, selected)) continue;
            ShowSliceAt(i);
            RefreshValueHints();
            Status = "Jumped to slice " + Position + " of " + QueueLength + " (selected in the table).";
            StartPrefetch(i + 1);
            return;
        }

        CurrentSlice = null;
        Position = 0;
        CurrentIsRandomDraw = false;
        CurrentAliasSummary = "";
        CurrentSliceLabel = "Editing a row outside the queue -- Commit and Next is disabled.";
        Status = "Selected row is not in the queue. Edits still save; press Build Queue to resume.";
    }

    // ---------- alias propagation ----------

    /// <summary>
    /// Writes the representative's verdict onto every other member of its alias family, replacing
    /// only the target Category's descriptors and leaving any other Category on those slices alone.
    /// Each member (representative included) also records its siblings in
    /// <see cref="PresetAnnotation.AliasLabels"/>.
    /// <para>Propagating rather than annotating the representative alone is what makes the family
    /// count as judged: a later queue build with de-duplication off, or a different signature
    /// rounding, would otherwise serve the siblings again as unlabelled.</para>
    /// </summary>
    private void PropagateToAliases(VM_BodyTypeProfile profile, VM_AnnotationQueueSlice slice, IReadOnlyList<string> values)
    {
        var aliasLabels = slice.Members
            .Select(m => m.PresetLabel ?? "")
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var member in slice.Members)
        {
            if (member == null) continue;

            var annotation = profile.FindAnnotation(member.PresetLabel, member.Gender, member.Weight);
            bool isRepresentative = ReferenceEquals(member, slice.Row);

            if (!isRepresentative)
            {
                // Replace only this Category on the sibling; another Category's verdict on that
                // slice is the user's and is none of this commit's business.
                if (annotation == null)
                {
                    annotation = new PresetAnnotation
                    {
                        PresetLabel = member.PresetLabel,
                        PresetGender = member.Gender,
                        Weight = member.Weight,
                    };
                    profile.PresetAnnotations.Add(annotation);
                }
                annotation.Descriptors.RemoveAll(d => d != null
                    && string.Equals(d.Category, TargetCategory, StringComparison.Ordinal));
                foreach (var value in values)
                {
                    annotation.Descriptors.Add(new BodyShapeDescriptor.LabelSignature
                    {
                        Category = TargetCategory,
                        Value = value,
                    });
                }

                // Mirror into the row so the annotation table's summary column agrees with what is
                // stored, without a rebuild.
                SyncRowDescriptors(member, annotation);
            }

            if (annotation != null && aliasLabels.Count > 1)
            {
                annotation.AliasLabels = aliasLabels
                    .Where(l => !string.Equals(l, member.PresetLabel ?? "", StringComparison.Ordinal))
                    .ToList();
            }
        }
    }

    private static void SyncRowDescriptors(VM_PresetAnnotationRow row, PresetAnnotation annotation)
    {
        row.CurrentDescriptors.Clear();
        if (annotation?.Descriptors == null) return;
        foreach (var d in annotation.Descriptors)
        {
            if (d == null) continue;
            row.CurrentDescriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = d.Category, Value = d.Value });
        }
    }

    // ---------- prefetch ----------

    /// <summary>
    /// Warms the preview NPC the next slice will need, on a background thread, while the user is
    /// still judging the current one.
    /// <para>Only a weight (or gender) change actually costs anything: the viewer short-circuits
    /// its load when the NPC identity is unchanged, and the preview NPC is configured per weight
    /// slot. So this is a no-op inside a weight run and does real work exactly at the boundaries --
    /// which is also why <see cref="WeightCoherent"/> exists.</para>
    /// </summary>
    private void StartPrefetch(int nextIndex)
    {
        if (!PrefetchEnabled) return;
        if (nextIndex < 0 || nextIndex >= _slices.Count) return;

        var next = _slices[nextIndex];
        var current = CurrentSlice;
        if (current != null && next.Row.Gender == current.Row.Gender && next.Row.Weight == current.Row.Weight)
        {
            return; // same preview NPC -- nothing to warm.
        }

        var cts = new CancellationTokenSource();
        _prefetchCts = cts;
        // Fire-and-forget by design: the result is a cache side effect, and a failed prewarm only
        // costs latency later. Discarded explicitly because CS4014 is an error in this repo.
        _ = _editor.PrefetchPreviewNpcAsync(next.Row.Gender, next.Row.Weight, cts.Token);
    }

    private void CancelPrefetch()
    {
        var cts = _prefetchCts;
        _prefetchCts = null;
        if (cts == null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    // ---------- export ----------

    /// <summary>
    /// Writes the target Category's verdicts as JSON, either to a file or to the clipboard, in the
    /// shape the offline analysis in <c>obody_arm_tools</c> already consumes.
    /// <para>The sampling settings travel with the verdicts. A verdict set is only interpretable
    /// alongside how it was drawn -- that is the whole lesson of <c>D25</c> -- so policy, seed and
    /// random fraction are part of the payload rather than something to reconstruct from memory
    /// later.</para>
    /// </summary>
    public void ExportVerdicts(bool toClipboard)
    {
        var profile = _editor.SelectedProfile;
        if (profile == null || string.IsNullOrEmpty(TargetCategory)) return;

        var payload = BuildVerdictPayload(profile);
        if (payload.Rows.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Verdicts",
                "No annotations in category \"" + TargetCategory + "\" on profile \"" + profile.Name + "\".");
            return;
        }

        if (toClipboard)
        {
            try
            {
                System.Windows.Clipboard.SetText(Newtonsoft.Json.JsonConvert.SerializeObject(
                    payload, Newtonsoft.Json.Formatting.Indented));
                Status = "Copied " + payload.Rows.Count + " " + TargetCategory + " verdict(s) to the clipboard.";
            }
            catch (Exception ex)
            {
                MessageWindow.DisplayNotificationOK("Copy Failed", ex.Message);
            }
            return;
        }

        string defaultName = TargetCategory.ToLowerInvariant() + "_verdicts.json";
        if (!IO_Aux.SelectFileSave("", "Verdicts JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Export " + TargetCategory + " Verdicts", out string path, defaultName))
        {
            return;
        }

        JSONhandler<AnnotationVerdictPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Export Failed", exception);
            return;
        }
        Status = "Exported " + payload.Rows.Count + " " + TargetCategory + " verdict(s) to " + path + ".";
    }

    private AnnotationVerdictPayload BuildVerdictPayload(VM_BodyTypeProfile profile)
    {
        var payload = new AnnotationVerdictPayload
        {
            Profile = profile.Name ?? "",
            Category = TargetCategory,
            Captured = DateTime.Now.ToString("yyyy-MM-dd"),
            Policy = Policy.ToString(),
            Seed = Seed,
            RandomFraction = RandomFraction,
            Note = "Exported from the SynthEBD Label-then-Suggest annotation queue. 'value' is the "
                 + "first descriptor value for readers expecting one label per row; 'values' is the "
                 + "full tag set, since a descriptor category can carry more than one value. "
                 + "'aliases' lists other presets whose measurements are identical to this one's -- "
                 + "collapse an alias family to a single observation before fitting.",
        };

        // One row per alias family: the persisted annotations carry the verdict on every member, but
        // an exported family must count once or the fit is weighted by how often a shape happened to
        // be re-uploaded.
        var emitted = new HashSet<(string Label, Gender Gender, int Weight)>();
        foreach (var annotation in profile.PresetAnnotations)
        {
            if (annotation?.Descriptors == null) continue;
            var values = annotation.Descriptors
                .Where(d => d != null && string.Equals(d.Category, TargetCategory, StringComparison.Ordinal))
                .Select(d => d.Value ?? "")
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (values.Count == 0) continue;

            var key = (annotation.PresetLabel ?? "", annotation.PresetGender, annotation.Weight);
            if (emitted.Contains(key)) continue;

            // Claim the whole family so its siblings don't emit rows of their own.
            emitted.Add(key);
            if (annotation.AliasLabels != null)
            {
                foreach (var alias in annotation.AliasLabels)
                {
                    emitted.Add((alias ?? "", annotation.PresetGender, annotation.Weight));
                }
            }

            payload.Rows.Add(new AnnotationVerdictRow
            {
                Preset = annotation.PresetLabel ?? "",
                Gender = annotation.PresetGender.ToString(),
                Weight = annotation.Weight,
                Value = values[0],
                Values = values,
                Aliases = annotation.AliasLabels != null ? new List<string>(annotation.AliasLabels) : new List<string>(),
            });
        }

        payload.Count = payload.Rows.Count;
        return payload;
    }

    // ---------- profile / editor binding ----------

    private void OnAnnotationTablePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_PresetAnnotationTable.SelectedRow))
        {
            OnTableSelectionChanged();
        }
    }

    private void OnEditorPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_BodyTypeProfileEditor.SelectedProfile))
        {
            BindToProfile(_editor.SelectedProfile);
        }
    }

    private void BindToProfile(VM_BodyTypeProfile profile)
    {
        _watchedProfile = profile;
        ClearQueue();
        ResetSessionCounters();
        RefreshAvailableMeasurements();
        RefreshAvailableCategories();
        LoadSettingsFromProfile();
        RefreshValueHints();
        RefreshTally();
        Status = profile == null ? "No profile selected." : "Pick a Category and press Build Queue.";
    }

    private void LoadSettingsFromProfile()
    {
        var profile = _watchedProfile;
        if (profile == null) return;
        var prefs = profile.GetOrCreateAnnotatorPrefs();

        _suppressPersist = true;
        try
        {
            Policy = prefs.QueuePolicy;
            SpreadMeasurement = prefs.QueueSpreadMeasurement ?? "";
            BinCount = prefs.QueueBinCount > 0 ? prefs.QueueBinCount : AnnotationQueueOrdering.DefaultBinCount;
            RandomFraction = Math.Clamp(prefs.QueueRandomFraction, 0.0, 1.0);
            Seed = prefs.QueueSeed;
            IncludeAnnotated = prefs.QueueIncludeAnnotated;
            DedupeAliases = prefs.QueueDedupeAliases;
            WeightCoherent = prefs.QueueWeightCoherent;
            PrefetchEnabled = prefs.QueuePrefetch;
            // Only settable once the menu has told us which categories exist; InitializeAfterMenu
            // re-applies it. Applying an unavailable category here would silently blank it.
            TargetCategory = AvailableCategories.Contains(prefs.QueueTargetCategory ?? "")
                ? prefs.QueueTargetCategory
                : "";
            ShowRandomFractionWarning = RandomFraction <= 0.0;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void PersistSettings()
    {
        if (_suppressPersist) return;
        var profile = _watchedProfile;
        if (profile == null) return;
        var prefs = profile.GetOrCreateAnnotatorPrefs();
        prefs.QueueTargetCategory = TargetCategory ?? "";
        prefs.QueuePolicy = Policy;
        prefs.QueueSpreadMeasurement = SpreadMeasurement ?? "";
        prefs.QueueBinCount = BinCount;
        prefs.QueueRandomFraction = RandomFraction;
        prefs.QueueSeed = Seed;
        prefs.QueueIncludeAnnotated = IncludeAnnotated;
        prefs.QueueDedupeAliases = DedupeAliases;
        prefs.QueueWeightCoherent = WeightCoherent;
        prefs.QueuePrefetch = PrefetchEnabled;
    }

    private void OnDescriptorShellsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshAvailableCategories();
    }

    /// <summary>
    /// Re-reads the annotation editor's category list. Called on profile change and whenever the
    /// descriptor menu's shells change, since the descriptor catalog is loaded after this VM is
    /// constructed.
    /// <para>Rebuilding the collection resets the bound ComboBox's selection to null, which would
    /// fire the TargetCategory setter and -- unguarded -- persist an empty category over the user's
    /// saved choice and drop a live queue. So the whole rebuild runs under the same suppression
    /// flag the load path uses, the previous selection is restored afterwards, and an unchanged
    /// list returns without touching the collection at all.</para>
    /// </summary>
    private void RefreshAvailableCategories()
    {
        var categories = _editor.AnnotationEditor?.GetCategories() ?? new List<string>();
        if (AvailableCategories.SequenceEqual(categories, StringComparer.Ordinal)) return;

        string previous = TargetCategory;
        _suppressPersist = true;
        try
        {
            AvailableCategories.Clear();
            foreach (var c in categories) AvailableCategories.Add(c);

            if (!string.IsNullOrEmpty(previous))
            {
                // Restore the user's choice, or blank it when the category no longer exists.
                TargetCategory = AvailableCategories.Contains(previous) ? previous : "";
            }
            else
            {
                // Nothing chosen yet: this is the first moment the persisted choice can be
                // validated against a real category list, so apply it now.
                var saved = _watchedProfile?.GetOrCreateAnnotatorPrefs()?.QueueTargetCategory ?? "";
                if (saved.Length > 0 && AvailableCategories.Contains(saved)) TargetCategory = saved;
            }
        }
        finally
        {
            _suppressPersist = false;
        }

        RefreshValueHints();
        RefreshTally();
    }

    private void RefreshAvailableMeasurements()
    {
        AvailableMeasurements.Clear();
        var profile = _watchedProfile;
        if (profile == null) return;
        foreach (var m in profile.Measurements)
        {
            if (m != null && !string.IsNullOrEmpty(m.Name)) AvailableMeasurements.Add(m.Name);
        }
    }

    private void RefreshValueHints()
    {
        ValueHints.Clear();
        if (string.IsNullOrEmpty(TargetCategory)) return;
        var values = _editor.AnnotationEditor?.GetCategoryValues(TargetCategory) ?? new List<string>();
        var selected = new HashSet<string>(
            _editor.AnnotationEditor?.GetSelectedValues(TargetCategory) ?? new List<string>(),
            StringComparer.Ordinal);

        for (int i = 0; i < values.Count; i++)
        {
            ValueHints.Add(new VM_AnnotationValueHint
            {
                // Only the first nine get a digit; the rest stay clickable in the menu below.
                Digit = i < 9 ? (i + 1).ToString() : "",
                Value = values[i],
                IsSelected = selected.Contains(values[i]),
            });
        }
    }

    private void RefreshTally()
    {
        Tally.Clear();
        var profile = _watchedProfile;
        if (profile == null || string.IsNullOrEmpty(TargetCategory)) return;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var annotation in profile.PresetAnnotations)
        {
            if (annotation?.Descriptors == null) continue;
            foreach (var d in annotation.Descriptors)
            {
                if (d == null) continue;
                if (!string.Equals(d.Category, TargetCategory, StringComparison.Ordinal)) continue;
                string value = d.Value ?? "";
                if (value.Length == 0) continue;
                counts[value] = counts.TryGetValue(value, out int c) ? c + 1 : 1;
            }
        }

        foreach (var kv in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            Tally.Add(new VM_AnnotationTallyRow { Value = kv.Key, Count = kv.Value });
        }
    }

    private void ResetSessionCounters()
    {
        ServedCount = 0;
        LabelledCount = 0;
        SkippedCount = 0;
        RandomLabelledCount = 0;
    }

    private bool HasVerdictInTargetCategory(VM_BodyTypeProfile profile, VM_PresetAnnotationRow row)
    {
        var annotation = profile.FindAnnotation(row.PresetLabel, row.Gender, row.Weight);
        if (annotation?.Descriptors == null) return false;
        foreach (var d in annotation.Descriptors)
        {
            if (d == null) continue;
            if (string.Equals(d.Category, TargetCategory, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(d.Value))
            {
                return true;
            }
        }
        return false;
    }

    private string FormatCurrentSelection()
    {
        var selected = _editor.AnnotationEditor?.GetSelectedValues(TargetCategory) ?? new List<string>();
        return selected.Count == 0 ? "(none)" : string.Join(" + ", selected);
    }

    /// <summary>One alias family during queue construction: the slice that will be served plus
    /// every slice whose measurements are identical to it.</summary>
    private sealed class AliasGroup
    {
        public AliasGroup(VM_PresetAnnotationRow representative)
        {
            Representative = representative;
            Members = new List<VM_PresetAnnotationRow> { representative };
        }

        public VM_PresetAnnotationRow Representative { get; }
        public List<VM_PresetAnnotationRow> Members { get; }
    }
}

/// <summary>One served position in the annotation queue.</summary>
public sealed class VM_AnnotationQueueSlice
{
    public VM_AnnotationQueueSlice(VM_PresetAnnotationRow row, IReadOnlyList<VM_PresetAnnotationRow> members, bool fromRandomDraw)
    {
        Row = row;
        Members = members ?? new List<VM_PresetAnnotationRow> { row };
        FromRandomDraw = fromRandomDraw;
    }

    /// <summary>The slice actually shown in the viewer and edited.</summary>
    public VM_PresetAnnotationRow Row { get; }

    /// <summary>Every slice this verdict covers, <see cref="Row"/> included. More than one entry
    /// means the corpus carries the same body under several preset names.</summary>
    public IReadOnlyList<VM_PresetAnnotationRow> Members { get; }

    /// <summary>True when the sampler took this position from the uniform-random stream rather than
    /// from the policy's ordering.</summary>
    public bool FromRandomDraw { get; }
}

/// <summary>One numbered value in the target Category, for the digit-key legend.</summary>
public class VM_AnnotationValueHint : VM
{
    /// <summary>"1".."9", or empty past the ninth value (no digit key reaches it).</summary>
    public string Digit { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsSelected { get; set; }
}

/// <summary>One row of the per-value tally for the target Category.</summary>
public class VM_AnnotationTallyRow : VM
{
    public string Value { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Serialized shape of a verdict export. Matches what the offline analysis in
/// <c>obody_arm_tools</c> reads, with the sampling settings attached so a verdict set stays
/// interpretable after the session that produced it is over.</summary>
public class AnnotationVerdictPayload
{
    public string Profile { get; set; } = "";
    public string Category { get; set; } = "";
    public string Captured { get; set; } = "";
    public string Policy { get; set; } = "";
    public int Seed { get; set; }
    public double RandomFraction { get; set; }
    public string Note { get; set; } = "";
    public int Count { get; set; }
    public List<AnnotationVerdictRow> Rows { get; set; } = new();
}

/// <summary>One exported verdict: an alias family counted once.</summary>
public class AnnotationVerdictRow
{
    public string Preset { get; set; } = "";
    public string Gender { get; set; } = "";
    public int Weight { get; set; }

    /// <summary>First descriptor value, for readers expecting a single label per row.</summary>
    public string Value { get; set; } = "";

    /// <summary>Full tag set. A descriptor category can legitimately carry more than one value
    /// (decision <c>D23</c>: Belly = Fat + Pregnant).</summary>
    public List<string> Values { get; set; } = new();

    /// <summary>Other presets with identical measurements. Collapse before fitting.</summary>
    public List<string> Aliases { get; set; } = new();
}

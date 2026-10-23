using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using Noggog;

namespace SynthEBD;

/// <summary>Measurements-tab undo / redo (Ctrl+Z / Ctrl+Y), drag-and-drop row reordering for the Measurements
/// and Key Vertices grids, and deferral of the live-value refresh during batch edits.</summary>
public partial class VM_BodyTypeProfile
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Undo / redo. The history holds whole-grid snapshots: every row by reference (so a restore keeps
    //  row identity -- selection, rule references, cache keys -- and only re-adds rows that were
    //  deleted) plus each row's definition fields. Edits are recorded after a short quiet period, so a
    //  burst (typing a name, a Ctrl+L load, a drag) is one step. A restore replays membership/order
    //  first, then names (through unique temporary names when several rows are renamed, so the
    //  rule/cache rename cascades can't merge two names), then every other field.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Most steps kept in the undo history.</summary>
    internal const int MeasurementHistoryLimit = 100;

    /// <summary>Quiet period after the last edit before it is recorded as an undo step.</summary>
    private static readonly TimeSpan MeasurementHistoryDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>The definition fields of <see cref="VM_MeasurementDefinition"/>: the ones a user edits and
    /// <see cref="VM_MeasurementDefinition.DumpToModel"/> persists. Every other property change on a row is
    /// derived (live value, validity flags, cross-row pick lists, Fody's dependent UI flags) and must not
    /// trigger re-evaluation, cache revalidation or an undo step.</summary>
    internal static readonly HashSet<string> MeasurementDefinitionProperties = new(StringComparer.Ordinal)
    {
        nameof(VM_MeasurementDefinition.Name),
        nameof(VM_MeasurementDefinition.Kind),
        nameof(VM_MeasurementDefinition.Axis),
        nameof(VM_MeasurementDefinition.NumeratorAxis),
        nameof(VM_MeasurementDefinition.DenominatorAxis),
        nameof(VM_MeasurementDefinition.AngleViewAxis),
        nameof(VM_MeasurementDefinition.VertexRefA),
        nameof(VM_MeasurementDefinition.VertexRefB),
        nameof(VM_MeasurementDefinition.VertexRefC),
        nameof(VM_MeasurementDefinition.VertexRefD),
        nameof(VM_MeasurementDefinition.RegionRefName),
        nameof(VM_MeasurementDefinition.GirthAggregate),
        nameof(VM_MeasurementDefinition.GirthStep),
        nameof(VM_MeasurementDefinition.TraceMetric),
        nameof(VM_MeasurementDefinition.MeasurementRefA),
        nameof(VM_MeasurementDefinition.MeasurementRefB),
        nameof(VM_MeasurementDefinition.Enabled),
    };

    /// <summary>One row's definition fields, by value.</summary>
    internal sealed record MeasurementRowState(
        string Name, MeasurementKind Kind, MeasurementAxis Axis,
        MeasurementAxis? NumeratorAxis, MeasurementAxis? DenominatorAxis, MeasurementAxis? AngleViewAxis,
        string VertexRefA, string VertexRefB, string VertexRefC, string VertexRefD,
        string RegionRefName, GirthAggregate GirthAggregate, float GirthStep, TorsoTraceMetric TraceMetric,
        string MeasurementRefA, string MeasurementRefB, bool Enabled)
    {
        public static MeasurementRowState Of(VM_MeasurementDefinition m) => new(
            m.Name ?? "", m.Kind, m.Axis, m.NumeratorAxis, m.DenominatorAxis, m.AngleViewAxis,
            m.VertexRefA ?? "", m.VertexRefB ?? "", m.VertexRefC ?? "", m.VertexRefD ?? "",
            m.RegionRefName ?? "", m.GirthAggregate, m.GirthStep, m.TraceMetric,
            m.MeasurementRefA ?? "", m.MeasurementRefB ?? "", m.Enabled);

        /// <summary>Writes every field except Name onto <paramref name="m"/> (Fody skips unchanged values).</summary>
        public void ApplyFieldsTo(VM_MeasurementDefinition m)
        {
            m.Kind = Kind; m.Axis = Axis;
            m.NumeratorAxis = NumeratorAxis; m.DenominatorAxis = DenominatorAxis; m.AngleViewAxis = AngleViewAxis;
            m.VertexRefA = VertexRefA; m.VertexRefB = VertexRefB; m.VertexRefC = VertexRefC; m.VertexRefD = VertexRefD;
            m.RegionRefName = RegionRefName; m.GirthAggregate = GirthAggregate; m.GirthStep = GirthStep;
            m.TraceMetric = TraceMetric;
            m.MeasurementRefA = MeasurementRefA; m.MeasurementRefB = MeasurementRefB;
            m.Enabled = Enabled;
        }
    }

    /// <summary>The whole grid: rows in order, each with its field values.</summary>
    internal sealed class MeasurementSnapshot
    {
        public MeasurementSnapshot(IReadOnlyList<(VM_MeasurementDefinition Row, MeasurementRowState State)> rows) => Rows = rows;
        public IReadOnlyList<(VM_MeasurementDefinition Row, MeasurementRowState State)> Rows { get; }

        public bool SameAs(MeasurementSnapshot? other)
        {
            if (other == null || other.Rows.Count != Rows.Count) return false;
            for (int i = 0; i < Rows.Count; i++)
                if (!ReferenceEquals(Rows[i].Row, other.Rows[i].Row) || Rows[i].State != other.Rows[i].State) return false;
            return true;
        }
    }

    private readonly List<MeasurementSnapshot> _measurementUndo = new();
    private readonly List<MeasurementSnapshot> _measurementRedo = new();
    /// <summary>The state the last recorded step left the grid in (what an undo returns from).</summary>
    private MeasurementSnapshot? _measurementCommitted;
    private bool _measurementHistoryDirty;
    private bool _restoringMeasurementHistory;
    private DispatcherTimer? _measurementHistoryTimer;

    public RelayCommand UndoMeasurementEditCommand { get; private set; } = null!;
    public RelayCommand RedoMeasurementEditCommand { get; private set; } = null!;

    public bool CanUndoMeasurementEdit => _measurementUndo.Count > 0 || (_measurementHistoryDirty && !CurrentMatchesCommitted());
    public bool CanRedoMeasurementEdit => _measurementRedo.Count > 0 && !(_measurementHistoryDirty && !CurrentMatchesCommitted());

    internal MeasurementSnapshot CaptureMeasurementSnapshot()
        => new(Measurements.Where(m => m != null).Select(m => (m, MeasurementRowState.Of(m))).ToList());

    private bool CurrentMatchesCommitted() => CaptureMeasurementSnapshot().SameAs(_measurementCommitted);

    /// <summary>Called from the ctor once the rows are loaded: wires the commands, the change hooks and the
    /// baseline every later step is diffed against.</summary>
    private void InitMeasurementHistory()
    {
        UndoMeasurementEditCommand = new RelayCommand(
            canExecute: _ => CanUndoMeasurementEdit,
            execute: _ => UndoMeasurementEdit());
        RedoMeasurementEditCommand = new RelayCommand(
            canExecute: _ => CanRedoMeasurementEdit,
            execute: _ => RedoMeasurementEdit());

        foreach (var m in Measurements) m.PropertyChanged += OnMeasurementRowChangedForHistory;
        Measurements.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementDefinition m in args.OldItems) m.PropertyChanged -= OnMeasurementRowChangedForHistory;
            if (args.NewItems != null)
                foreach (VM_MeasurementDefinition m in args.NewItems) m.PropertyChanged += OnMeasurementRowChangedForHistory;
            NoteMeasurementEdit();
        };
        _measurementCommitted = CaptureMeasurementSnapshot();
    }

    private void OnMeasurementRowChangedForHistory(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && MeasurementDefinitionProperties.Contains(e.PropertyName)) NoteMeasurementEdit();
    }

    private void NoteMeasurementEdit()
    {
        if (_restoringMeasurementHistory) return;
        _measurementHistoryDirty = true;
        try
        {
            _measurementHistoryTimer ??= new DispatcherTimer(MeasurementHistoryDebounce, DispatcherPriority.Background,
                (_, _) => { _measurementHistoryTimer!.Stop(); CommitMeasurementHistory(); },
                Dispatcher.CurrentDispatcher);
            _measurementHistoryTimer.Stop();
            _measurementHistoryTimer.Start();
        }
        catch (InvalidOperationException)
        {
            // No dispatcher (headless): the pending step is flushed by the next undo / redo instead.
        }
    }

    /// <summary>Records the edits since the last step as one undo step. No-op when nothing changed (e.g. an
    /// edit that was typed and then reverted inside the quiet period).</summary>
    internal void CommitMeasurementHistory()
    {
        _measurementHistoryTimer?.Stop();
        if (!_measurementHistoryDirty) return;
        _measurementHistoryDirty = false;
        var now = CaptureMeasurementSnapshot();
        if (now.SameAs(_measurementCommitted)) return;
        if (_measurementCommitted != null)
        {
            _measurementUndo.Add(_measurementCommitted);
            if (_measurementUndo.Count > MeasurementHistoryLimit) _measurementUndo.RemoveAt(0);
        }
        _measurementRedo.Clear();
        _measurementCommitted = now;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public void UndoMeasurementEdit()
    {
        CommitMeasurementHistory();
        if (_measurementUndo.Count == 0 || _measurementCommitted == null) return;
        var target = _measurementUndo[^1];
        _measurementUndo.RemoveAt(_measurementUndo.Count - 1);
        _measurementRedo.Add(_measurementCommitted);
        RestoreMeasurementSnapshot(target);
    }

    public void RedoMeasurementEdit()
    {
        CommitMeasurementHistory();
        if (_measurementRedo.Count == 0 || _measurementCommitted == null) return;
        var target = _measurementRedo[^1];
        _measurementRedo.RemoveAt(_measurementRedo.Count - 1);
        _measurementUndo.Add(_measurementCommitted);
        RestoreMeasurementSnapshot(target);
    }

    private void RestoreMeasurementSnapshot(MeasurementSnapshot target)
    {
        _restoringMeasurementHistory = true;
        try
        {
            using (DeferMeasurementRefresh())
                ApplyMeasurementSnapshot(Measurements, target);
        }
        finally
        {
            _restoringMeasurementHistory = false;
            _measurementHistoryDirty = false;
            _measurementHistoryTimer?.Stop();
            _measurementCommitted = CaptureMeasurementSnapshot();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Makes <paramref name="rows"/> match <paramref name="target"/>: membership and order by row
    /// identity (re-adding deleted rows), then names -- via unique temporary names when several rows are
    /// renamed, so a rename cascade (rule conditions, ratio operands, cache keys) can never merge two names,
    /// e.g. when undoing a swap -- then every other field.</summary>
    internal static void ApplyMeasurementSnapshot(ObservableCollection<VM_MeasurementDefinition> rows, MeasurementSnapshot target)
    {
        var wanted = new HashSet<VM_MeasurementDefinition>(target.Rows.Select(r => r.Row));
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(rows[i])) rows.RemoveAt(i);
        for (int i = 0; i < target.Rows.Count; i++)
        {
            var row = target.Rows[i].Row;
            int cur = rows.IndexOf(row);
            if (cur < 0) rows.Insert(i, row);
            else if (cur != i) rows.Move(cur, i);
        }

        var renames = target.Rows.Where(r => !string.Equals(r.Row.Name ?? "", r.State.Name, StringComparison.Ordinal)).ToList();
        if (renames.Count > 1)
            foreach (var (row, _) in renames) row.Name = "\u0001undo-" + Guid.NewGuid().ToString("N");
        foreach (var (row, state) in renames) row.Name = state.Name;

        foreach (var (row, state) in target.Rows) state.ApplyFieldsTo(row);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Deferred live refresh. RefreshMeasurementValues re-evaluates every row against the viewer mesh;
    //  inside a batch (undo restore, bulk load) it runs once at the end instead of once per change.
    // ─────────────────────────────────────────────────────────────────────────────

    private int _measurementRefreshDeferDepth;
    private bool _measurementRefreshPending;

    /// <summary>True when the caller should skip its refresh because a batch is open; the batch runs one
    /// refresh when it closes.</summary>
    private bool DeferRefreshIfBatching()
    {
        if (_measurementRefreshDeferDepth == 0) return false;
        _measurementRefreshPending = true;
        return true;
    }

    internal IDisposable DeferMeasurementRefresh()
    {
        _measurementRefreshDeferDepth++;
        return System.Reactive.Disposables.Disposable.Create(() =>
        {
            if (--_measurementRefreshDeferDepth > 0 || !_measurementRefreshPending) return;
            _measurementRefreshPending = false;
            RefreshMeasurementValues();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Drag-and-drop row reordering (gong-wpf-dragdrop). The Measurements and Key Vertices grids name this
    //  VM as their DropHandler; a drop moves the dragged row(s) -- one or a multi-selection -- to the
    //  insertion point within the same grid. Measurement moves are recorded by the undo history like any
    //  other edit.
    // ─────────────────────────────────────────────────────────────────────────────

    public void DragOver(IDropInfo dropInfo)
    {
        // A grid sorted by a column header shows a different order than the collection, so an insertion
        // point read off the screen would land somewhere else: no reordering while a sort is applied.
        if ((DraggedRows(dropInfo, Measurements).Count > 0 && !IsViewSorted(Measurements))
            || (DraggedRows(dropInfo, KeyVertices).Count > 0 && !IsViewSorted(KeyVertices)))
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = System.Windows.DragDropEffects.Move;
        }
    }

    private static bool IsViewSorted(System.Collections.IEnumerable collection)
        => System.Windows.Data.CollectionViewSource.GetDefaultView(collection) is { } view && view.SortDescriptions.Count > 0;

    public void Drop(IDropInfo dropInfo)
    {
        var meas = DraggedRows(dropInfo, Measurements);
        if (meas.Count > 0) { if (!IsViewSorted(Measurements)) MoveRows(Measurements, meas, dropInfo.InsertIndex); return; }
        var kvs = DraggedRows(dropInfo, KeyVertices);
        if (kvs.Count > 0 && !IsViewSorted(KeyVertices)) MoveRows(KeyVertices, kvs, dropInfo.InsertIndex);
    }

    /// <summary>The rows of <paramref name="collection"/> being dragged onto it (empty when the drop targets
    /// another collection or carries other data).</summary>
    private static List<T> DraggedRows<T>(IDropInfo dropInfo, ObservableCollection<T> collection) where T : class
    {
        if (!ReferenceEquals(dropInfo.TargetCollection, collection)) return new();
        IEnumerable<object> items = dropInfo.Data is T single
            ? new object[] { single }
            : dropInfo.Data is System.Collections.IEnumerable many ? many.Cast<object>() : Array.Empty<object>();
        return items.OfType<T>().Where(collection.Contains).Distinct().ToList();
    }

    /// <summary>Moves <paramref name="rows"/> (kept in their current relative order) so they sit together at
    /// <paramref name="insertIndex"/>, an insertion point counted in the collection before the move.</summary>
    internal static void MoveRows<T>(ObservableCollection<T> collection, IReadOnlyCollection<T> rows, int insertIndex)
    {
        var ordered = collection.Where(rows.Contains).ToList();
        if (ordered.Count == 0) return;
        insertIndex = Math.Clamp(insertIndex, 0, collection.Count);
        // Rows above the insertion point shift it up by one each once removed.
        int target = insertIndex - collection.Take(insertIndex).Count(rows.Contains);
        var final = collection.Where(x => !rows.Contains(x)).ToList();
        final.InsertRange(Math.Clamp(target, 0, final.Count), ordered);
        // Place each final position in turn; positions before i are already settled, so every Move only
        // touches the unsettled tail.
        for (int i = 0; i < final.Count; i++)
        {
            int from = collection.IndexOf(final[i]);
            if (from != i) collection.Move(from, i);
        }
    }
}

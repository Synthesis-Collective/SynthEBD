using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Phase 5 of the Label-then-Suggest overhaul.
///
/// Owns the Suggest Measurements panel: an algorithm picker plus a per-Category list of
/// measurements ranked by how well they discriminate between annotated descriptor-value
/// groups. The list is editable -- the user can prune weak suggestions or add measurements
/// the algorithm missed -- and the curated set per Category becomes the input to the Phase 6
/// Suggest Rules pass.
///
/// Inputs come from the bottom-section <see cref="VM_PresetAnnotationTable.Rows"/> (scan-time
/// measurement values) intersected with the active profile's <see cref="VM_BodyTypeProfile.PresetAnnotations"/>
/// (which descriptors apply to which slice). Output is purely ephemeral -- only the user's
/// algorithm choice persists, via <see cref="AnnotatorPreferences.SelectionAlgorithm"/>.
/// </summary>
public class VM_SuggestMeasurementsPanel : VM
{
    private readonly VM_BodyTypeProfileEditor _editor;
    private VM_BodyTypeProfile _watchedProfile;
    private bool _suppressPersist;

    public VM_SuggestMeasurementsPanel(VM_BodyTypeProfileEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        SuggestCommand = new RelayCommand(
            canExecute: _ => _editor.SelectedProfile != null && _editor.AnnotationTable.Rows.Count > 0,
            execute: _ => Recompute());

        ClearCommand = new RelayCommand(
            canExecute: _ => Groups.Count > 0,
            execute: _ => { Groups.Clear(); Status = "Cleared."; });

        AvailableAlgorithms = new ObservableCollection<MeasurementSelectionAlgorithm>(
            (MeasurementSelectionAlgorithm[])Enum.GetValues(typeof(MeasurementSelectionAlgorithm)));

        _editor.PropertyChanged += OnEditorPropertyChanged;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectionAlgorithm) && !_suppressPersist)
            {
                PersistAlgorithmChoice();
            }
        };

        BindToProfile(_editor.SelectedProfile);
    }

    /// <summary>Per-Category suggestion lists. Cleared when the profile changes; populated by
    /// <see cref="Recompute"/>. Bound to an ItemsControl in the UC.</summary>
    public ObservableCollection<VM_CategorySuggestionGroup> Groups { get; } = new();

    /// <summary>Two-way bound to the algorithm picker. Persisted to
    /// <see cref="AnnotatorPreferences.SelectionAlgorithm"/> on every change.</summary>
    public MeasurementSelectionAlgorithm SelectionAlgorithm { get; set; } = MeasurementSelectionAlgorithm.Anova;

    /// <summary>Enum values for the algorithm picker.</summary>
    public ObservableCollection<MeasurementSelectionAlgorithm> AvailableAlgorithms { get; }

    /// <summary>Status line shown next to the Suggest button. Switches between
    /// "press Suggest...", run summaries, and error states.</summary>
    public string Status { get; private set; } = "Annotate some rows, then press Suggest Measurements.";

    public RelayCommand SuggestCommand { get; }
    public RelayCommand ClearCommand { get; }

    /// <summary>Snapshot of the curated suggestion lists, keyed by descriptor Category. Phase 6
    /// reads this to know which measurements to synthesize thresholds against.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotByCategory()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var g in Groups)
        {
            if (g == null || string.IsNullOrEmpty(g.Category)) continue;
            result[g.Category] = g.Measurements
                .Select(m => m.MeasurementName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        return result;
    }

    /// <summary>Recomputes <see cref="Groups"/> from the table's current rows + the active
    /// profile's annotations. Idempotent; user can press Suggest repeatedly to re-run after
    /// editing annotations.</summary>
    public void Recompute()
    {
        Groups.Clear();
        var profile = _editor.SelectedProfile;
        if (profile == null) { Status = "No profile selected."; return; }
        var rows = _editor.AnnotationTable.Rows;
        if (rows.Count == 0) { Status = "Scan the table first so measurement values are available."; return; }

        // Index rows by the (label, gender, weight) key so each annotation can find its scan
        // values. PresetAnnotation can be present without a row (e.g. user hasn't re-scanned),
        // in which case that annotation is silently skipped.
        var rowByKey = new Dictionary<(string, Gender, int), VM_PresetAnnotationRow>();
        foreach (var r in rows)
        {
            if (r == null) continue;
            rowByKey[(r.PresetLabel ?? "", r.Gender, r.Weight)] = r;
        }

        // Group annotations by Category. Within a Category, each (Value -> list of matching
        // row keys) becomes one training group for the discriminator.
        var byCategory = new Dictionary<string, Dictionary<string, List<VM_PresetAnnotationRow>>>(StringComparer.Ordinal);
        int totalAnnotated = 0;
        foreach (var pa in profile.PresetAnnotations)
        {
            if (pa == null || pa.Descriptors == null) continue;
            if (!rowByKey.TryGetValue((pa.PresetLabel ?? "", pa.PresetGender, pa.Weight), out var row)) continue;
            totalAnnotated++;
            foreach (var d in pa.Descriptors)
            {
                if (d == null || string.IsNullOrEmpty(d.Category) || string.IsNullOrEmpty(d.Value)) continue;
                if (!byCategory.TryGetValue(d.Category, out var byValue))
                {
                    byValue = new Dictionary<string, List<VM_PresetAnnotationRow>>(StringComparer.Ordinal);
                    byCategory[d.Category] = byValue;
                }
                if (!byValue.TryGetValue(d.Value, out var bucket))
                {
                    bucket = new List<VM_PresetAnnotationRow>();
                    byValue[d.Value] = bucket;
                }
                bucket.Add(row);
            }
        }

        if (byCategory.Count == 0)
        {
            Status = totalAnnotated == 0
                ? "No annotations on scanned rows yet."
                : "Annotations don't reference any descriptor categories.";
            return;
        }

        var allMeasurements = profile.Measurements
            .Where(m => m != null && !string.IsNullOrEmpty(m.Name))
            .Select(m => m.Name)
            .ToList();

        foreach (var (category, byValue) in byCategory.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // Need >=2 distinct Values to do any discrimination. Categories with one Value
            // (the user only annotated "BodyShape:Athletic" so far, never any non-Athletic)
            // get an explanatory entry instead of a silent skip -- otherwise the user wonders
            // why a Category they obviously care about doesn't appear.
            if (byValue.Count < 2)
            {
                Groups.Add(new VM_CategorySuggestionGroup(this, category, byValue.Count, byValue.Sum(v => v.Value.Count), allMeasurements)
                {
                    EmptyReason = "Need at least two distinct annotated values in this Category to discriminate.",
                });
                continue;
            }

            var valueGroups = byValue.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

            var group = new VM_CategorySuggestionGroup(this, category, byValue.Count, byValue.Sum(v => v.Value.Count), allMeasurements);

            foreach (var measurementName in allMeasurements)
            {
                var groups = new List<List<double>>(valueGroups.Count);
                foreach (var (_, members) in valueGroups)
                {
                    var bucket = new List<double>(members.Count);
                    foreach (var r in members)
                    {
                        if (r.MeasurementValues != null
                            && r.MeasurementValues.TryGetValue(measurementName, out var v)
                            && v.HasValue)
                        {
                            bucket.Add(v.Value);
                        }
                    }
                    groups.Add(bucket);
                }

                double score = MeasurementDiscriminators.Score(SelectionAlgorithm, groups);
                if (!(score > 0.0)) continue;

                group.Measurements.Add(new VM_MeasurementSuggestion(group)
                {
                    MeasurementName = measurementName,
                    Score = score,
                    Algorithm = SelectionAlgorithm,
                    IsAutoSuggested = true,
                });
            }

            // Strongest-signal first.
            var sorted = group.Measurements.OrderByDescending(m => m.Score).ToList();
            group.Measurements.Clear();
            foreach (var m in sorted) group.Measurements.Add(m);
            group.RefreshAvailableToAdd();

            Groups.Add(group);
        }

        Status = $"Ranked across {Groups.Count} categor{(Groups.Count == 1 ? "y" : "ies")} ({totalAnnotated} annotation slice(s)) using {SelectionAlgorithm}.";
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
        Groups.Clear();
        if (profile == null)
        {
            Status = "No profile selected.";
            return;
        }
        // Pull the user's persisted algorithm choice without re-firing the persist hook.
        _suppressPersist = true;
        try
        {
            SelectionAlgorithm = profile.GetOrCreateAnnotatorPrefs().SelectionAlgorithm;
        }
        finally
        {
            _suppressPersist = false;
        }
        Status = "Annotate some rows, then press Suggest Measurements.";
    }

    private void PersistAlgorithmChoice()
    {
        var profile = _watchedProfile;
        if (profile == null) return;
        profile.GetOrCreateAnnotatorPrefs().SelectionAlgorithm = SelectionAlgorithm;
    }
}

/// <summary>One Category's worth of suggested measurements. Members are mutable: the user can
/// remove an auto-suggested entry that they consider noise, or add a measurement the algorithm
/// missed (manual entries are tagged so the UI can render them differently).</summary>
public class VM_CategorySuggestionGroup : VM
{
    private readonly VM_SuggestMeasurementsPanel _panel;
    private readonly IReadOnlyList<string> _allMeasurementNames;

    public VM_CategorySuggestionGroup(
        VM_SuggestMeasurementsPanel panel,
        string category,
        int distinctValues,
        int totalMembers,
        IReadOnlyList<string> allMeasurementNames)
    {
        _panel = panel;
        _allMeasurementNames = allMeasurementNames ?? Array.Empty<string>();
        Category = category ?? "";
        DistinctValues = distinctValues;
        TotalAnnotations = totalMembers;

        AddMeasurementCommand = new RelayCommand(
            canExecute: _ => !string.IsNullOrEmpty(SelectedMeasurementToAdd) && !ContainsMeasurement(SelectedMeasurementToAdd),
            execute: _ => AddSelectedMeasurement());

        Measurements.CollectionChanged += (_, __) => RefreshAvailableToAdd();
    }

    public string Category { get; }
    public int DistinctValues { get; }
    public int TotalAnnotations { get; }
    public string EmptyReason { get; set; }

    public ObservableCollection<VM_MeasurementSuggestion> Measurements { get; } = new();

    /// <summary>Names of profile measurements that have not yet been added to this group's
    /// list. Bound to the "Add measurement" ComboBox.</summary>
    public ObservableCollection<string> AvailableToAdd { get; } = new();

    /// <summary>Currently-selected entry in the AvailableToAdd ComboBox. The Add button writes
    /// it into <see cref="Measurements"/> as a manual suggestion.</summary>
    public string SelectedMeasurementToAdd { get; set; }

    public RelayCommand AddMeasurementCommand { get; }

    public string HeaderDisplay => $"{Category}  ({DistinctValues} value(s), {TotalAnnotations} annotation(s))";

    internal void RemoveSuggestion(VM_MeasurementSuggestion s)
    {
        if (s == null) return;
        Measurements.Remove(s);
    }

    internal void RefreshAvailableToAdd()
    {
        AvailableToAdd.Clear();
        var taken = new HashSet<string>(Measurements.Select(m => m.MeasurementName), StringComparer.Ordinal);
        foreach (var name in _allMeasurementNames)
        {
            if (!taken.Contains(name)) AvailableToAdd.Add(name);
        }
        if (!string.IsNullOrEmpty(SelectedMeasurementToAdd) && taken.Contains(SelectedMeasurementToAdd))
        {
            SelectedMeasurementToAdd = null;
        }
    }

    private bool ContainsMeasurement(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var m in Measurements)
        {
            if (string.Equals(m.MeasurementName, name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private void AddSelectedMeasurement()
    {
        var name = SelectedMeasurementToAdd;
        if (string.IsNullOrEmpty(name) || ContainsMeasurement(name)) return;
        Measurements.Add(new VM_MeasurementSuggestion(this)
        {
            MeasurementName = name,
            Score = 0.0,
            Algorithm = _panel.SelectionAlgorithm,
            IsAutoSuggested = false,
        });
        SelectedMeasurementToAdd = null;
    }
}

/// <summary>One measurement suggestion within a Category group.</summary>
public class VM_MeasurementSuggestion : VM
{
    private readonly VM_CategorySuggestionGroup _group;

    public VM_MeasurementSuggestion(VM_CategorySuggestionGroup group)
    {
        _group = group;
        RemoveCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _group?.RemoveSuggestion(this));
    }

    public string MeasurementName { get; set; } = "";
    public double Score { get; set; }
    public MeasurementSelectionAlgorithm Algorithm { get; set; }
    public bool IsAutoSuggested { get; set; }

    /// <summary>Display string for the score column (e.g. "ANOVA F=12.3", "Cohen's d=1.4").</summary>
    public string ScoreDisplay
    {
        get
        {
            if (!IsAutoSuggested) return "(manual)";
            string label = Algorithm switch
            {
                MeasurementSelectionAlgorithm.Anova => "F",
                MeasurementSelectionAlgorithm.CohenD => "d",
                MeasurementSelectionAlgorithm.InformationGain => "IG",
                _ => "score",
            };
            if (double.IsPositiveInfinity(Score)) return $"{label}=inf";
            return $"{label}={Score:0.000}";
        }
    }

    public RelayCommand RemoveCommand { get; }
}

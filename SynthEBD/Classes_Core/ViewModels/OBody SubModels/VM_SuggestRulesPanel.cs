using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Phase 6 of the Label-then-Suggest overhaul.
///
/// Owns the Suggest Rules panel: an algorithm picker plus a list of (Category, Value) rule
/// suggestions synthesized from the user's annotations and the Phase 5 curated measurement
/// list. Each suggestion previews the conditions that would fire the rule and exposes Accept
/// (and Accept All) actions that materialize a draft <see cref="MeasurementRule"/> on the
/// active profile -- the existing Rules tab takes over from there.
///
/// Only the user's algorithm choice persists, via
/// <see cref="AnnotatorPreferences.SynthesisAlgorithm"/>. Accepted rules persist via the
/// existing profile.Rules round-trip; pending suggestions live only for the current session.
/// </summary>
public class VM_SuggestRulesPanel : VM
{
    private readonly VM_BodyTypeProfileEditor _editor;
    private VM_BodyTypeProfile _watchedProfile;
    private bool _suppressPersist;

    public VM_SuggestRulesPanel(VM_BodyTypeProfileEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        SuggestCommand = new RelayCommand(
            canExecute: _ => CanSuggest(),
            execute: _ => Recompute());

        AcceptAllCommand = new RelayCommand(
            canExecute: _ => RuleSuggestions.Any(r => !r.IsAccepted),
            execute: _ => AcceptAllPending());

        ClearCommand = new RelayCommand(
            canExecute: _ => RuleSuggestions.Count > 0,
            execute: _ => { RuleSuggestions.Clear(); Status = "Cleared."; });

        AvailableAlgorithms = new ObservableCollection<RuleSynthesisAlgorithm>(
            (RuleSynthesisAlgorithm[])Enum.GetValues(typeof(RuleSynthesisAlgorithm)));

        _editor.PropertyChanged += OnEditorPropertyChanged;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SynthesisAlgorithm) && !_suppressPersist)
            {
                PersistAlgorithmChoice();
            }
        };

        BindToProfile(_editor.SelectedProfile);
    }

    /// <summary>One entry per (Category, Value) target the algorithm could synthesize a rule
    /// for. Cleared on profile change; populated by <see cref="Recompute"/>.</summary>
    public ObservableCollection<VM_RuleSuggestion> RuleSuggestions { get; } = new();

    /// <summary>Two-way bound to the algorithm picker. Persisted to
    /// <see cref="AnnotatorPreferences.SynthesisAlgorithm"/>.</summary>
    public RuleSynthesisAlgorithm SynthesisAlgorithm { get; set; } = RuleSynthesisAlgorithm.OptimalThresholdPerValue;

    public ObservableCollection<RuleSynthesisAlgorithm> AvailableAlgorithms { get; }

    public string Status { get; private set; } = "Run Suggest Measurements first, then press Suggest Rules.";

    public RelayCommand SuggestCommand { get; }
    public RelayCommand AcceptAllCommand { get; }
    public RelayCommand ClearCommand { get; }

    /// <summary>Materializes <paramref name="suggestion"/> as a draft
    /// <see cref="MeasurementRule"/> on the active profile (IsDraft = true), then marks the
    /// suggestion accepted so the row dims out. Does nothing when no profile is selected or
    /// the suggestion was already accepted.</summary>
    public void Accept(VM_RuleSuggestion suggestion)
    {
        if (suggestion == null || suggestion.IsAccepted) return;
        var profile = _editor.SelectedProfile;
        if (profile == null) return;

        var rule = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature
            {
                Category = suggestion.Category ?? "",
                Value = suggestion.Value ?? "",
            },
            IsDraft = true,
        };
        foreach (var cond in suggestion.Conditions)
        {
            var group = new AndGatedMeasurementGroup();
            group.ConditionsANDlogic.Add(new MeasurementCondition
            {
                MeasurementName = cond.MeasurementName ?? "",
                Comparator = cond.Comparator,
                Value = cond.Threshold,
            });
            rule.GroupsORlogic.Add(group);
        }

        profile.Rules.Add(new VM_MeasurementRule(rule, profile));
        suggestion.IsAccepted = true;
    }

    private bool CanSuggest()
    {
        var profile = _editor.SelectedProfile;
        if (profile == null) return false;
        var snapshot = _editor.SuggestMeasurements?.SnapshotByCategory();
        return snapshot != null && snapshot.Count > 0;
    }

    public void Recompute()
    {
        RuleSuggestions.Clear();
        var profile = _editor.SelectedProfile;
        if (profile == null) { Status = "No profile selected."; return; }

        var snapshot = _editor.SuggestMeasurements?.SnapshotByCategory();
        if (snapshot == null || snapshot.Count == 0)
        {
            Status = "Run Suggest Measurements first to lock in a measurement set.";
            return;
        }

        var rows = _editor.AnnotationTable.Rows;
        if (rows.Count == 0) { Status = "Scan the table first so measurement values are available."; return; }

        var rowByKey = new Dictionary<(string, Gender, int), VM_PresetAnnotationRow>();
        foreach (var r in rows)
        {
            if (r == null) continue;
            rowByKey[(r.PresetLabel ?? "", r.Gender, r.Weight)] = r;
        }

        // Same per-Category training-group structure as Phase 5: each Value gets a list of
        // matching rows. Phase 6 differs in that it then iterates each Value as the positive
        // target with the other Values' rows as negatives.
        var byCategory = new Dictionary<string, Dictionary<string, List<VM_PresetAnnotationRow>>>(StringComparer.Ordinal);
        foreach (var pa in profile.PresetAnnotations)
        {
            if (pa == null || pa.Descriptors == null) continue;
            if (!rowByKey.TryGetValue((pa.PresetLabel ?? "", pa.PresetGender, pa.Weight), out var row)) continue;
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

        int targetCount = 0;
        foreach (var (category, byValue) in byCategory.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // Need >=2 distinct Values for "is target" vs "is not target" to mean anything.
            if (byValue.Count < 2) continue;
            // Synth uses only the curated measurement set from Phase 5.
            if (!snapshot.TryGetValue(category, out var measurementsForCategory) || measurementsForCategory.Count == 0) continue;

            foreach (var (targetValue, positiveRows) in byValue.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                // Negatives = annotations carrying the same Category but a different Value.
                // Restricting to "annotated as something else within this Category" keeps the
                // training set clean (vs. including every un-annotated row, which conflates
                // "actively wrong" with "unknown").
                var negativeRows = byValue
                    .Where(kv => !string.Equals(kv.Key, targetValue, StringComparison.Ordinal))
                    .SelectMany(kv => kv.Value)
                    .ToList();
                if (positiveRows.Count == 0 || negativeRows.Count == 0) continue;

                var samplesPerMeasurement = new Dictionary<string, RuleSynthesizers.PosNegSamples>(StringComparer.Ordinal);
                foreach (var name in measurementsForCategory)
                {
                    var pos = new List<double>(positiveRows.Count);
                    var neg = new List<double>(negativeRows.Count);
                    foreach (var r in positiveRows)
                    {
                        if (r.MeasurementValues != null && r.MeasurementValues.TryGetValue(name, out var v) && v.HasValue)
                        {
                            pos.Add(v.Value);
                        }
                    }
                    foreach (var r in negativeRows)
                    {
                        if (r.MeasurementValues != null && r.MeasurementValues.TryGetValue(name, out var v) && v.HasValue)
                        {
                            neg.Add(v.Value);
                        }
                    }
                    samplesPerMeasurement[name] = new RuleSynthesizers.PosNegSamples(pos, neg);
                }

                var thresholds = RuleSynthesizers.Synthesize(SynthesisAlgorithm, samplesPerMeasurement);
                if (thresholds.Count == 0) continue;

                var suggestion = new VM_RuleSuggestion(this)
                {
                    Category = category,
                    Value = targetValue,
                    Algorithm = SynthesisAlgorithm,
                    PositiveCount = positiveRows.Count,
                    NegativeCount = negativeRows.Count,
                };
                foreach (var t in thresholds)
                {
                    suggestion.Conditions.Add(new VM_RuleConditionPreview
                    {
                        MeasurementName = t.MeasurementName,
                        Comparator = t.Comparator,
                        Threshold = t.Threshold,
                        Score = t.Score,
                        Algorithm = SynthesisAlgorithm,
                    });
                }
                RuleSuggestions.Add(suggestion);
                targetCount++;
            }
        }

        Status = targetCount == 0
            ? $"No (Category, Value) targets had usable signal under {SynthesisAlgorithm}."
            : $"Synthesized {targetCount} rule suggestion(s) using {SynthesisAlgorithm}.";
    }

    private void AcceptAllPending()
    {
        foreach (var s in RuleSuggestions.Where(r => !r.IsAccepted).ToList())
        {
            Accept(s);
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
        RuleSuggestions.Clear();
        if (profile == null) { Status = "No profile selected."; return; }
        _suppressPersist = true;
        try { SynthesisAlgorithm = profile.GetOrCreateAnnotatorPrefs().SynthesisAlgorithm; }
        finally { _suppressPersist = false; }
        Status = "Run Suggest Measurements first, then press Suggest Rules.";
    }

    private void PersistAlgorithmChoice()
    {
        var profile = _watchedProfile;
        if (profile == null) return;
        profile.GetOrCreateAnnotatorPrefs().SynthesisAlgorithm = SynthesisAlgorithm;
    }
}

/// <summary>One synthesized rule suggestion: the (Category, Value) target plus the OR-combined
/// list of single-measurement conditions that would fire it. Acceptance produces a draft
/// <see cref="MeasurementRule"/> on the active profile.</summary>
public class VM_RuleSuggestion : VM
{
    private readonly VM_SuggestRulesPanel _panel;

    public VM_RuleSuggestion(VM_SuggestRulesPanel panel)
    {
        _panel = panel;
        AcceptCommand = new RelayCommand(
            canExecute: _ => !IsAccepted,
            execute: _ => _panel.Accept(this));
    }

    public string Category { get; set; } = "";
    public string Value { get; set; } = "";
    public RuleSynthesisAlgorithm Algorithm { get; set; }

    /// <summary>Number of annotation slices labeled with this Value (positives in the
    /// synthesizer's classifier sense). Diagnostic only.</summary>
    public int PositiveCount { get; set; }

    /// <summary>Number of annotation slices labeled with a different Value within the same
    /// Category (negatives). Diagnostic only.</summary>
    public int NegativeCount { get; set; }

    public ObservableCollection<VM_RuleConditionPreview> Conditions { get; } = new();

    /// <summary>Set true when the user (or Accept All) materializes this as a draft rule.
    /// The XAML dims accepted rows so the user can keep scanning the list without losing
    /// track of which they've already promoted.</summary>
    public bool IsAccepted { get; set; }

    public string Header => string.IsNullOrEmpty(Category) ? Value : Category + ":" + Value;

    public string Stats => $"{PositiveCount} positive(s) / {NegativeCount} negative(s)  ·  {Algorithm}";

    public RelayCommand AcceptCommand { get; }
}

/// <summary>One condition inside a <see cref="VM_RuleSuggestion"/>. Single threshold check
/// against one measurement; multiple instances inside a suggestion are OR-combined when
/// materialised into a draft <see cref="MeasurementRule"/>.</summary>
public class VM_RuleConditionPreview
{
    public string MeasurementName { get; set; } = "";
    public MeasurementComparator Comparator { get; set; }
    public float Threshold { get; set; }
    public double Score { get; set; }
    public RuleSynthesisAlgorithm Algorithm { get; set; }

    /// <summary>One-line preview ready for the rule-condition list:
    /// <c>"Bust  >=  12.345  (J=0.82)"</c>.</summary>
    public string Display
    {
        get
        {
            string cmp = Comparator switch
            {
                MeasurementComparator.LessThan => "<",
                MeasurementComparator.LessThanOrEqual => "<=",
                MeasurementComparator.GreaterThan => ">",
                MeasurementComparator.GreaterThanOrEqual => ">=",
                MeasurementComparator.EqualTo => "==",
                MeasurementComparator.NotEqualTo => "!=",
                _ => "?",
            };
            string scoreLabel = Algorithm switch
            {
                RuleSynthesisAlgorithm.OptimalThresholdPerValue => "J",
                RuleSynthesisAlgorithm.MedianSplit => "gap",
                RuleSynthesisAlgorithm.DecisionStump => "giniGain",
                _ => "score",
            };
            return $"{MeasurementName} {cmp} {Threshold:0.000}  ({scoreLabel}={Score:0.000})";
        }
    }
}

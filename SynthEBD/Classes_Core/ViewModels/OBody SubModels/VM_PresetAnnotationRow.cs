using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SynthEBD;

/// <summary>
/// One row in the Label-then-Suggest annotation table: a single (preset, gender, weight) slice
/// of the profile's body type, with per-measurement scalar values populated by an evaluator
/// pass. Bound directly into a DataGrid; the measurement cells use indexer binding against
/// <see cref="MeasurementValues"/> so the grid can sort and display columns whose names are
/// only known at runtime.
/// </summary>
public class VM_PresetAnnotationRow : VM
{
    public VM_PresetAnnotationRow(string presetLabel, Gender gender, int weight)
    {
        PresetLabel = presetLabel ?? "";
        Gender = gender;
        Weight = weight;
        MeasurementValues = new Dictionary<string, float?>(System.StringComparer.Ordinal);
        CurrentDescriptors = new ObservableCollection<BodyShapeDescriptor.LabelSignature>();
        // Fody weaves PropertyChanged into setters but not collection mutations, so the
        // derived AnnotationCount / IsAnnotated / AnnotationSummary properties have to be
        // refreshed manually whenever the descriptor list shape changes.
        CurrentDescriptors.CollectionChanged += OnCurrentDescriptorsChanged;
    }

    private void OnCurrentDescriptorsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RecomputeDerived();
    }

    private void RecomputeDerived()
    {
        AnnotationCount = CurrentDescriptors.Count;
        IsAnnotated = AnnotationCount > 0;
        if (AnnotationCount == 0)
        {
            AnnotationSummary = "";
            return;
        }
        var parts = new List<string>(AnnotationCount);
        foreach (var d in CurrentDescriptors)
        {
            if (d == null) continue;
            parts.Add((d.Category ?? "") + ":" + (d.Value ?? ""));
        }
        AnnotationSummary = string.Join(", ", parts);
    }

    /// <summary>BodySlide preset identifier (the stable key used elsewhere in the editor).</summary>
    public string PresetLabel { get; }

    /// <summary>Gender of the preset.</summary>
    public Gender Gender { get; }

    /// <summary>Weight slot at which this row was evaluated.</summary>
    public int Weight { get; }

    /// <summary>Friendly row label combining preset / weight / gender. Used as the row-header
    /// column display.</summary>
    public string Display => $"{PresetLabel}  (W{Weight}, {Gender})";

    /// <summary>Per-measurement scalar values keyed by measurement name. <c>null</c> means the
    /// evaluator could not compute the measurement at this slice (missing key vertex, topology
    /// mismatch, degenerate ratio). Every column the table displays must have an entry here --
    /// missing keys would throw under indexer binding.</summary>
    public Dictionary<string, float?> MeasurementValues { get; }

    /// <summary>Descriptors the user has annotated for this slice (mirrors
    /// <see cref="PresetAnnotation.Descriptors"/>). Updated by the annotation editor in Phase 4
    /// and consumed by the table for the "annotated descriptors" column / tooltip.</summary>
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> CurrentDescriptors { get; }

    /// <summary>Convenience flag for the "Annotated?" column. Maintained by
    /// <see cref="RecomputeDerived"/> so DataGrid bindings refresh through Fody-woven
    /// PropertyChanged on the setter.</summary>
    public bool IsAnnotated { get; private set; }

    /// <summary>Number of distinct descriptor signatures annotated for this slice. Maintained
    /// by <see cref="RecomputeDerived"/>.</summary>
    public int AnnotationCount { get; private set; }

    /// <summary>Comma-joined Category:Value summary of <see cref="CurrentDescriptors"/> for the
    /// "Annotations" column. Maintained by <see cref="RecomputeDerived"/>.</summary>
    public string AnnotationSummary { get; private set; } = "";

    /// <summary>True when the evaluator's topology fingerprint did not match the loaded mesh
    /// for this slice -- diagnostic surfacing so the user can spot a wrong-body-type mismatch
    /// without having to scan the per-measurement nulls.</summary>
    public bool HasTopologyMismatch { get; set; }
}

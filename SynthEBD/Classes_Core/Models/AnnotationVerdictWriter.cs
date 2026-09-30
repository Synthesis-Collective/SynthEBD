using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Writes one Category's verdict for an alias family into a profile's
/// <see cref="PresetAnnotation"/> list. The model half of <see cref="VM_AnnotationQueue.WriteVerdict"/>,
/// split out so it can be tested without an editor.
/// </summary>
public static class AnnotationVerdictWriter
{
    /// <summary>
    /// For every member slice: finds or creates its annotation, replaces only
    /// <paramref name="category"/>'s descriptors with <paramref name="values"/>, and -- when the family
    /// has several presets -- records the siblings in <see cref="PresetAnnotation.AliasLabels"/>.
    /// Another Category's descriptors on those slices are left alone.
    /// <para>An empty <paramref name="values"/> is "no verdict": the Category's descriptors are removed,
    /// and an annotation left with no descriptors at all is dropped from the list, as the annotation
    /// editor's write-through does. No annotation is created just to hold nothing.</para>
    /// </summary>
    /// <returns>Each member's annotation after the write (null when it has none), in member order,
    /// so the caller can mirror them into its rows.</returns>
    public static IReadOnlyList<PresetAnnotation?> Write(
        ICollection<PresetAnnotation> annotations,
        IReadOnlyList<(string PresetLabel, Gender Gender, int Weight)> members,
        string category,
        IReadOnlyList<string> values)
    {
        var cleanValues = values
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var aliasLabels = members
            .Select(m => m.PresetLabel ?? "")
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new List<PresetAnnotation?>(members.Count);
        foreach (var (label, gender, weight) in members)
        {
            var annotation = Find(annotations, label, gender, weight);
            if (annotation == null)
            {
                if (cleanValues.Count == 0) { result.Add(null); continue; }
                annotation = new PresetAnnotation { PresetLabel = label, PresetGender = gender, Weight = weight };
                annotations.Add(annotation);
            }

            annotation.Descriptors.RemoveAll(d => d != null
                && string.Equals(d.Category, category, StringComparison.Ordinal));
            foreach (var value in cleanValues)
            {
                annotation.Descriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = category, Value = value });
            }

            if (annotation.Descriptors.Count == 0)
            {
                annotations.Remove(annotation);
                result.Add(null);
                continue;
            }

            if (aliasLabels.Count > 1)
            {
                annotation.AliasLabels = aliasLabels
                    .Where(l => !string.Equals(l, label ?? "", StringComparison.Ordinal))
                    .ToList();
            }
            result.Add(annotation);
        }
        return result;
    }

    /// <summary>The values <paramref name="category"/> holds in a slice's annotation (empty when none).</summary>
    public static IReadOnlyList<string> ReadValues(
        IEnumerable<PresetAnnotation> annotations, string presetLabel, Gender gender, int weight, string category)
    {
        var annotation = Find(annotations, presetLabel, gender, weight);
        if (annotation?.Descriptors == null) return Array.Empty<string>();
        return annotation.Descriptors
            .Where(d => d != null && string.Equals(d.Category, category, StringComparison.Ordinal) && !string.IsNullOrEmpty(d.Value))
            .Select(d => d.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Same lookup as <see cref="VM_BodyTypeProfile.FindAnnotation"/>.</summary>
    private static PresetAnnotation? Find(IEnumerable<PresetAnnotation> annotations, string presetLabel, Gender gender, int weight)
    {
        if (string.IsNullOrEmpty(presetLabel)) return null;
        foreach (var pa in annotations)
        {
            if (pa == null || pa.Weight != weight || pa.PresetGender != gender) continue;
            if (string.Equals(pa.PresetLabel, presetLabel, StringComparison.Ordinal)) return pa;
        }
        return null;
    }
}

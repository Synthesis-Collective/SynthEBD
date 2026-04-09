using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Tier 1 annotation: applies library-sourced descriptors to a <see cref="BodySlideSetting"/>
/// based on <see cref="AnnotationLibraryLoader"/> data.
///
/// Precedence rules per weight slot and descriptor category:
///   Manual  >  Library  >  RulesBased  >  Classifier
/// A Manual entry for a category in a slot blocks library writes for that category/slot.
/// Existing Library entries are overwritten (re-applying the library is idempotent).
/// </summary>
public class AnnotationLibraryAnnotator
{
    private readonly AnnotationLibraryLoader _loader;

    public AnnotationLibraryAnnotator(AnnotationLibraryLoader loader)
    {
        _loader = loader;
    }

    /// <summary>
    /// Applies all matching library entries to <paramref name="preset"/>.
    /// Returns true if at least one descriptor was written.
    /// </summary>
    public bool Annotate(BodySlideSetting preset)
    {
        if (preset == null) return false;

        var libraries = _loader.LoadLibraries();
        bool anyApplied = false;

        foreach (var lib in libraries.Values)
        {
            if (lib.Entries == null) continue;

            var entry = lib.Entries.FirstOrDefault(e =>
                string.Equals(e.PresetName, preset.ReferencedBodySlide, System.StringComparison.OrdinalIgnoreCase));

            if (entry?.DescriptorsByWeight == null) continue;

            foreach (var (weight, descriptors) in entry.DescriptorsByWeight)
            {
                if (descriptors == null || descriptors.Count == 0) continue;

                if (!preset.BodyShapeDescriptorsByWeight.TryGetValue(weight, out var slot))
                {
                    slot = new HashSet<AnnotatedDescriptorSignature>();
                    preset.BodyShapeDescriptorsByWeight[weight] = slot;
                }

                foreach (var label in descriptors)
                {
                    if (label == null) continue;

                    // Never overwrite a Manual entry for the same category.
                    if (slot.Any(x => x.Category == label.Category && x.Source == BodyShapeAnnotationSource.Manual))
                        continue;

                    // Remove any previous Library/non-Manual entry for this category in this slot.
                    slot.RemoveWhere(x => x.Category == label.Category && x.Source != BodyShapeAnnotationSource.Manual);

                    slot.Add(new AnnotatedDescriptorSignature(label, BodyShapeAnnotationSource.Library));
                    anyApplied = true;
                }
            }
        }

        return anyApplied;
    }

    /// <summary>
    /// Convenience overload: annotates every preset in <paramref name="presets"/>.
    /// Returns the count that had at least one descriptor written.
    /// </summary>
    public int AnnotateAll(IEnumerable<BodySlideSetting> presets)
    {
        if (presets == null) return 0;
        int count = 0;
        foreach (var p in presets)
        {
            if (Annotate(p)) count++;
        }
        return count;
    }
}

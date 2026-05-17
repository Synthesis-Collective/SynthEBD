using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Phase 5 of the BodySlide Classifier pipeline.
///
/// Reads the deformed body mesh from a <see cref="VM_CharacterViewer"/>, evaluates a
/// <see cref="BodyTypeProfile"/>'s measurements and rules against it, and produces a set of
/// <see cref="AnnotatedDescriptorSignature"/>s with <c>Source = Classifier</c>. The result is
/// merged into a <see cref="BodySlideSetting.BodyShapeDescriptorsByWeight"/> slot under the
/// locked merge policy: never overwrite Manual/Library/RulesBased; replace prior Classifier
/// entries so re-runs reflect rule edits.
///
/// Stateless and side-effect-free so call sites can compose it freely (live RefreshPreview
/// pass, manual Classify command, future batch mode).
/// </summary>
public static class BodySlideMeasurementEvaluator
{
    /// <summary>Why a measurement could not be evaluated. Surfaced for diagnostics.</summary>
    public enum MeasurementFailureReason
    {
        MissingKeyVertex = 0,
        VertexOutOfRange = 1,
        MalformedDefinition = 2,
        DegenerateRatio = 3,
    }

    /// <summary>Result of a single evaluation pass.</summary>
    public class EvaluationResult
    {
        /// <summary>Successfully computed measurement values, keyed by definition name.</summary>
        public Dictionary<string, float> Measurements { get; } = new(StringComparer.Ordinal);

        /// <summary>Names of measurements that couldn't be evaluated (with reason for diagnostics).</summary>
        public Dictionary<string, MeasurementFailureReason> FailedMeasurements { get; } = new(StringComparer.Ordinal);

        /// <summary>Rules that matched and produced a descriptor. Always tagged <see cref="BodyShapeAnnotationSource.Classifier"/>.</summary>
        public List<AnnotatedDescriptorSignature> Descriptors { get; } = new();

        /// <summary>True when the profile's topology fingerprint disagreed with the viewer's loaded mesh.</summary>
        public bool TopologyMismatch { get; set; }
    }

    /// <summary>
    /// Evaluates <paramref name="profile"/> against the current deformed mesh state of
    /// <paramref name="viewer"/>. Caller is responsible for ensuring the viewer has finished
    /// loading + deforming before invoking (e.g. <c>await ApplyBodySlide</c>).
    ///
    /// Draft rules (<see cref="MeasurementRule.IsDraft"/>) are skipped by default so unreviewed
    /// suggestions never produce live descriptors in the production preview path. Set
    /// <paramref name="includeDrafts"/> = true for authoring-time previews (the profile editor's
    /// Match Presets scan), where the whole point is to see what draft thresholds would produce.
    /// </summary>
    public static EvaluationResult Evaluate(VM_CharacterViewer viewer, BodyTypeProfile profile, bool includeDrafts = false)
    {
        var result = new EvaluationResult();
        if (viewer == null || profile == null) return result;

        result.TopologyMismatch = !FingerprintMatches(profile, viewer);

        var keyVertsByName = new Dictionary<string, NamedKeyVertex>(StringComparer.Ordinal);
        if (profile.KeyVertices != null)
        {
            foreach (var kv in profile.KeyVertices)
            {
                if (kv == null || string.IsNullOrEmpty(kv.Name)) continue;
                keyVertsByName[kv.Name] = kv;
            }
        }

        MeasurementMath.VertexLookup lookup = (shape, idx) =>
            viewer.TryGetCurrentVertex(shape, idx, out var p) ? (OpenTK.Mathematics.Vector3?)p : null;
        MeasurementMath.ShapePositionsLookup shapeLookup = shape => viewer.GetShapePositions(shape);

        if (profile.Measurements != null)
        {
            foreach (var def in profile.Measurements)
            {
                if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                // First-wins on duplicate measurement names — matches the KeyVertex side's
                // GroupBy.First() at VM_BodyTypeProfileEditor.cs and the equivalent dictionary
                // build above. The editor surfaces duplicates visually, but pre-existing /
                // hand-edited profiles may still contain them; skipping the second occurrence
                // keeps evaluation deterministic and consistent across both grids.
                if (result.Measurements.ContainsKey(def.Name)
                    || result.FailedMeasurements.ContainsKey(def.Name)) continue;
                if (MeasurementMath.TryEvaluate(def, keyVertsByName, lookup, shapeLookup, out float v))
                {
                    result.Measurements[def.Name] = v;
                }
                else
                {
                    result.FailedMeasurements[def.Name] = ClassifyFailure(def, keyVertsByName, lookup);
                }
            }
        }

        if (profile.Rules != null)
        {
            // De-dup descriptors emitted by multiple matching rules so a single (Category, Value)
            // doesn't appear twice in the output.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in profile.Rules)
            {
                if (rule == null) continue;
                if (rule.IsDraft && !includeDrafts) continue;
                if (rule.Descriptor == null
                    || string.IsNullOrEmpty(rule.Descriptor.Category)
                    || string.IsNullOrEmpty(rule.Descriptor.Value)) continue;
                if (!MeasurementMath.RuleMatches(rule, result.Measurements)) continue;

                string key = rule.Descriptor.Category + "::" + rule.Descriptor.Value;
                if (!seen.Add(key)) continue;

                result.Descriptors.Add(new AnnotatedDescriptorSignature(rule.Descriptor, BodyShapeAnnotationSource.Classifier));
            }
        }

        return result;
    }

    /// <summary>
    /// Picks the first profile in <paramref name="profiles"/> that matches the viewer's current
    /// mesh state. Match strategy (highest priority first):
    ///   1. Per-shape vertex counts equal the profile fingerprint exactly.
    ///   2. Total vertex count matches and <paramref name="sliderGroupHint"/> equals BodyTypeName.
    ///   3. <paramref name="sliderGroupHint"/> equals BodyTypeName (name-only fallback).
    /// Returns null when no profile applies.
    /// </summary>
    public static BodyTypeProfile FindMatchingProfile(IEnumerable<BodyTypeProfile> profiles, VM_CharacterViewer viewer, string sliderGroupHint)
    {
        if (profiles == null || viewer == null) return null;

        var counts = viewer.GetCurrentShapeVertexCounts();
        int total = counts.Values.Sum();

        BodyTypeProfile shapeMatch = null;
        BodyTypeProfile totalAndNameMatch = null;
        BodyTypeProfile nameMatch = null;

        foreach (var p in profiles)
        {
            if (p == null) continue;

            bool nameMatches = !string.IsNullOrEmpty(sliderGroupHint)
                && string.Equals(p.BodyTypeName, sliderGroupHint, StringComparison.OrdinalIgnoreCase);

            if (p.Fingerprint != null && p.Fingerprint.ShapeVertexCounts != null && p.Fingerprint.ShapeVertexCounts.Count > 0)
            {
                if (ShapeCountsMatch(p.Fingerprint.ShapeVertexCounts, counts))
                {
                    shapeMatch ??= p;
                }
                else if (p.Fingerprint.VertexCount > 0 && p.Fingerprint.VertexCount == total && nameMatches)
                {
                    totalAndNameMatch ??= p;
                }
            }

            if (nameMatches) nameMatch ??= p;
        }

        return shapeMatch ?? totalAndNameMatch ?? nameMatch;
    }

    /// <summary>
    /// Merges classifier-sourced descriptors into <paramref name="slot"/> under the locked policy:
    ///   - Skip writes for any (Category, Value) that already exists with Source = Manual / Library / RulesBased.
    ///   - Remove existing Source = Classifier entries first so re-runs reflect updated rules.
    ///   - Add the supplied descriptors (already tagged Classifier by <see cref="Evaluate"/>).
    /// Returns the number of descriptors actually added (after policy filtering).
    /// </summary>
    public static int MergeIntoSlot(HashSet<AnnotatedDescriptorSignature> slot, IEnumerable<AnnotatedDescriptorSignature> classifierResults)
    {
        if (slot == null) return 0;

        // Step 1: drop prior classifier entries.
        slot.RemoveWhere(d => d != null && d.Source == BodyShapeAnnotationSource.Classifier);

        if (classifierResults == null) return 0;

        // Step 2: build a protected-set lookup for everything we must NOT overwrite.
        var protectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var existing in slot)
        {
            if (existing == null) continue;
            if (existing.Source == BodyShapeAnnotationSource.Classifier) continue;
            protectedKeys.Add(existing.Category + "::" + existing.Value);
        }

        int added = 0;
        foreach (var d in classifierResults)
        {
            if (d == null || string.IsNullOrEmpty(d.Category) || string.IsNullOrEmpty(d.Value)) continue;
            string key = d.Category + "::" + d.Value;
            if (protectedKeys.Contains(key)) continue;
            // Ensure source is Classifier even if the caller forgot (defense in depth).
            slot.Add(new AnnotatedDescriptorSignature(d.ToLabelSignature(), BodyShapeAnnotationSource.Classifier));
            added++;
        }
        return added;
    }

    /// <summary>
    /// Convenience wrapper: evaluate <paramref name="profile"/> against <paramref name="viewer"/>
    /// and merge the results into the per-weight slot of <paramref name="setting"/>. Returns the
    /// evaluation result for diagnostic display (live readout, classifier counts, mismatch flag).
    /// Does nothing and returns null when there is no slot at <paramref name="weight"/>.
    /// </summary>
    public static EvaluationResult EvaluateAndMerge(VM_CharacterViewer viewer, BodyTypeProfile profile, BodySlideSetting setting, int weight)
    {
        if (viewer == null || profile == null || setting == null) return null;
        if (setting.BodyShapeDescriptorsByWeight == null) return null;
        if (!setting.BodyShapeDescriptorsByWeight.TryGetValue(weight, out var slot) || slot == null) return null;

        var result = Evaluate(viewer, profile);
        MergeIntoSlot(slot, result.Descriptors);
        return result;
    }

    private static bool FingerprintMatches(BodyTypeProfile profile, VM_CharacterViewer viewer)
    {
        if (profile?.Fingerprint == null) return true; // no fingerprint authored == no claim made
        if (profile.Fingerprint.ShapeVertexCounts == null || profile.Fingerprint.ShapeVertexCounts.Count == 0)
            return true;

        var counts = viewer.GetCurrentShapeVertexCounts();
        return ShapeCountsMatch(profile.Fingerprint.ShapeVertexCounts, counts);
    }

    private static bool ShapeCountsMatch(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        if (a == null || b == null) return false;
        // Profile fingerprint may have more shapes than the viewer (head/hands present in CBBE
        // but only torso loaded right now), so match in profile->viewer direction. Each profile
        // shape that matters must exist in the viewer with the same count.
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out int bCount)) return false;
            if (bCount != pair.Value) return false;
        }
        return true;
    }

    private static MeasurementFailureReason ClassifyFailure(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, MeasurementMath.VertexLookup lookup)
    {
        int needed = def.Kind == MeasurementKind.RatioDistance ? 4 : 2;
        if (def.VertexRefNames == null || def.VertexRefNames.Count < needed) return MeasurementFailureReason.MalformedDefinition;

        for (int i = 0; i < needed; i++)
        {
            var refName = def.VertexRefNames[i];
            if (string.IsNullOrEmpty(refName) || !keyVertsByName.TryGetValue(refName, out var kv) || kv == null)
                return MeasurementFailureReason.MissingKeyVertex;
            if (lookup(kv.ShapeName, kv.VertexIndex) == null)
                return MeasurementFailureReason.VertexOutOfRange;
        }

        // Reaching here on RatioDistance means a degenerate denominator.
        return def.Kind == MeasurementKind.RatioDistance
            ? MeasurementFailureReason.DegenerateRatio
            : MeasurementFailureReason.MalformedDefinition;
    }
}

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
        /// <summary>A <see cref="MeasurementKind.RegionVolume"/> measurement's
        /// <see cref="MeasurementDefinition.RegionRefName"/> was empty or had no resolved region
        /// supplied (the caller didn't pass a <c>resolvedRegions</c> entry for it).</summary>
        MissingRegion = 4,
        /// <summary>A <see cref="MeasurementKind.RegionVolume"/> region was supplied but is invalid
        /// (the box failed watertightness/validation at resolve time) or its shape's deformed
        /// positions weren't available.</summary>
        RegionNotResolved = 5,
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

    /// <summary>True when a rule's <see cref="MeasurementRule.Gender"/> filter matches the
    /// caller's evaluation context. <see cref="RuleGender.Either"/> rules always pass. Male /
    /// Female rules require <paramref name="evaluationGender"/> to be the matching Mutagen
    /// <c>Gender</c>; passing null filters them out (the safe default for call sites that
    /// don't know the preset's gender).</summary>
    public static bool RuleGenderMatches(RuleGender ruleGender, Gender? evaluationGender)
    {
        if (ruleGender == RuleGender.Either) return true;
        if (evaluationGender == null) return false;
        return (ruleGender == RuleGender.Male   && evaluationGender == Gender.Male)
            || (ruleGender == RuleGender.Female && evaluationGender == Gender.Female);
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
    ///
    /// <paramref name="evaluationGender"/> filters rules by their
    /// <see cref="MeasurementRule.Gender"/>. Either rules always fire; Male/Female rules require
    /// a matching gender. Null = unknown context (only Either rules fire — safe default for the
    /// live preview path where the placeholder's gender isn't routed through the call).
    ///
    /// <paramref name="measurementNamesAllowlist"/> restricts measurement evaluation to just
    /// those names (others are skipped — they won't appear in
    /// <see cref="EvaluationResult.Measurements"/> or <see cref="EvaluationResult.FailedMeasurements"/>).
    /// Used by the partial-fill scan path that computes just the measurements a cached entry
    /// is missing instead of re-evaluating the whole set. Null = evaluate every defined
    /// measurement (the original behavior; what every other call site wants).
    ///
    /// <paramref name="skipRules"/> short-circuits rule evaluation entirely so
    /// <see cref="EvaluationResult.Descriptors"/> stays empty. The partial-fill path uses this
    /// because cached measurement values are persisted but descriptors are always re-derived
    /// from the full cache by <see cref="VM_BodyTypeProfile.RebuildScanResultsFromCache"/>, so
    /// evaluating rules with a partial measurement set would just throw away the result.
    ///
    /// <paramref name="resolvedRegions"/> supplies the per-body/weight baked
    /// <see cref="RegionVolumeEvaluator.ResolvedRegion"/> for every <see cref="MeasurementKind.RegionVolume"/>
    /// measurement, keyed by region name. These are expensive to resolve (clip + boundary-loop +
    /// validate against the sliders-0 mesh) so the caller bakes them once per body/weight (see
    /// <c>RegionVolumeEvaluator.ResolveRegions</c>) and passes them in; this method just evaluates
    /// them against the current deformed positions. Null (the default for non-scan call sites)
    /// means region-volume measurements fail with <see cref="MeasurementFailureReason.MissingRegion"/>.
    /// </summary>
    public static EvaluationResult Evaluate(
        VM_CharacterViewer viewer,
        BodyTypeProfile profile,
        bool includeDrafts = false,
        Gender? evaluationGender = null,
        IReadOnlySet<string>? measurementNamesAllowlist = null,
        bool skipRules = false,
        IReadOnlyDictionary<string, RegionVolumeEvaluator.ResolvedRegion>? resolvedRegions = null)
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
        // Bone-info lookup is consulted only by the BoneTransition criterion; everything else
        // ignores it. Surface it unconditionally — the per-criterion gate in TryResolve avoids
        // the actual fetch when the row's criterion isn't BoneTransition*.
        MeasurementMath.ShapeBoneInfoLookup boneLookup = shape => viewer.GetShapeBoneInfo(shape);
        // Zeroed (sliders-0, weight-0) positions, consulted only by Coordinate-strategy key vertices:
        // their stored position is matched to the nearest current zeroed vertex (renumber-/variant-stable)
        // before the deformed position is read via `lookup`. Weight 0 is the canonical undeformed reference
        // used at both capture and match time.
        MeasurementMath.ShapePositionsLookup zeroedShapeLookup = shape => viewer.GetZeroedShapePositions(shape, 0);

        if (profile.Measurements != null)
        {
            foreach (var def in profile.Measurements)
            {
                if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                // Allowlist filter (partial-fill scan path): skip measurements not in the
                // caller's requested set. Null allowlist = evaluate everything (the normal
                // path).
                if (measurementNamesAllowlist != null && !measurementNamesAllowlist.Contains(def.Name)) continue;
                // First-wins on duplicate measurement names — matches the KeyVertex side's
                // GroupBy.First() at VM_BodyTypeProfileEditor.cs and the equivalent dictionary
                // build above. The editor surfaces duplicates visually, but pre-existing /
                // hand-edited profiles may still contain them; skipping the second occurrence
                // keeps evaluation deterministic and consistent across both grids.
                if (result.Measurements.ContainsKey(def.Name)
                    || result.FailedMeasurements.ContainsKey(def.Name)) continue;
                // RegionVolume reads a baked region + the shape's deformed positions, not key
                // vertices, so it takes a separate path from MeasurementMath.TryEvaluate.
                if (def.Kind == MeasurementKind.RegionVolume)
                {
                    if (TryEvaluateRegionVolume(def, resolvedRegions, shapeLookup, out float rv, out var rreason))
                        result.Measurements[def.Name] = rv;
                    else
                        result.FailedMeasurements[def.Name] = rreason;
                    continue;
                }
                if (MeasurementMath.TryEvaluate(def, keyVertsByName, lookup, shapeLookup, boneLookup, resolvedRegions, zeroedShapeLookup, out float v))
                {
                    result.Measurements[def.Name] = v;
                }
                else
                {
                    result.FailedMeasurements[def.Name] = ClassifyFailure(def, keyVertsByName, lookup);
                }
            }
        }

        // Rule evaluation is unconditionally skippable for the partial-fill path. Descriptors
        // are always re-derived from the full cached measurement set by
        // VM_BodyTypeProfile.RebuildScanResultsFromCache after the scan completes, so
        // evaluating rules here with a partial measurement set would just throw away the
        // work (and produce wrong descriptors that the post-scan rebuild would overwrite).
        if (skipRules) return result;

        // De-dup descriptors emitted by multiple matching rules (and by the default pass) so a
        // single (Category, Value) doesn't appear twice in the output.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Match set fed to DescriptorRef conditions, and the source of truth for which Categories
        // already produced a descriptor (so the default pass below knows which Categories to skip).
        // Stays empty on the first rule pass for any non-aggregator rule (which is fine — they ignore it).
        var matched = new HashSet<(string Category, string Value)>();

        if (profile.Rules != null)
        {
            // Aggregator rules (any condition with Kind=DescriptorRef) need to fire AFTER the
            // rules they reference, so the matched-descriptor set is populated when their
            // predicate is evaluated. RuleDependencyOrder topo-sorts the eligible rules; rules
            // caught in a cycle are dropped from the sort and logged (the UI prevents cycles
            // at edit time, but hand-edited JSON could still produce one).
            var eligible = new List<MeasurementRule>();
            foreach (var rule in profile.Rules)
            {
                if (rule == null) continue;
                if (rule.IsDraft && !includeDrafts) continue;
                if (rule.Descriptor == null
                    || string.IsNullOrEmpty(rule.Descriptor.Category)
                    || string.IsNullOrEmpty(rule.Descriptor.Value)) continue;
                // Gender filter: rules tagged Male only fire when evaluating a male preset,
                // Female only for female. Either rules always pass. See RuleGenderMatches.
                if (!RuleGenderMatches(rule.Gender, evaluationGender)) continue;
                eligible.Add(rule);
            }

            var ordered = RuleDependencyOrder.SortByDescriptorDependencies(eligible, out var skipped);

            foreach (var rule in ordered)
            {
                if (!MeasurementMath.RuleMatches(rule, result.Measurements, matched)) continue;

                string key = rule.Descriptor.Category + "::" + rule.Descriptor.Value;
                if (!seen.Add(key)) continue;

                matched.Add((rule.Descriptor.Category, rule.Descriptor.Value));
                result.Descriptors.Add(new AnnotatedDescriptorSignature(rule.Descriptor, BodyShapeAnnotationSource.Classifier));
            }
        }

        // Per-Category default fallback: for any Category with a configured default that produced
        // NO rule descriptor on this evaluation, emit the default value (tagged Classifier, exactly
        // like a rule output). Runs even when the profile has no rules — a Category whose rules all
        // failed (or that has none) "falls into" its default. Gender filtering is implicit: only
        // gender-eligible rules populated `matched`, so a male-only rule that didn't fire for a
        // female preset leaves the Category open to its default here.
        foreach (var def in ComputeDefaultDescriptors(profile.DefaultDescriptorValuesByCategory, matched))
        {
            string key = def.Category + "::" + def.Value;
            if (!seen.Add(key)) continue;
            result.Descriptors.Add(new AnnotatedDescriptorSignature(def, BodyShapeAnnotationSource.Classifier));
        }

        return result;
    }

    /// <summary>
    /// Computes the per-Category default descriptors to emit after the rule pass: for each
    /// (Category -> Value) in <paramref name="defaultsByCategory"/> whose Category produced
    /// <b>no</b> descriptor during the rule pass (i.e. no entry in <paramref name="matchedDescriptors"/>
    /// has that Category), yields a (Category, Value) signature. Blank entries and Categories that
    /// already matched are skipped. Pure / allocation-light (no side effects) and shared by
    /// <see cref="Evaluate"/> and the editor's cache-rederive path
    /// (<c>VM_BodyTypeProfile.DeriveDescriptorsFor</c>) so both produce identical defaults.
    /// </summary>
    public static IEnumerable<BodyShapeDescriptor.LabelSignature> ComputeDefaultDescriptors(
        IReadOnlyDictionary<string, string> defaultsByCategory,
        IReadOnlyCollection<(string Category, string Value)> matchedDescriptors)
    {
        if (defaultsByCategory == null || defaultsByCategory.Count == 0) yield break;

        // Categories that already produced a descriptor — those are "covered" and get no default.
        var matchedCategories = new HashSet<string>(StringComparer.Ordinal);
        if (matchedDescriptors != null)
            foreach (var m in matchedDescriptors) matchedCategories.Add(m.Category);

        foreach (var pair in defaultsByCategory)
        {
            string category = pair.Key;
            string value = pair.Value;
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(value)) continue;
            if (matchedCategories.Contains(category)) continue;
            yield return new BodyShapeDescriptor.LabelSignature { Category = category, Value = value };
        }
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
        return FindMatchingProfile(profiles, viewer.GetCurrentShapeVertexCounts(), sliderGroupHint);
    }

    /// <summary>
    /// Counts-based overload of <see cref="FindMatchingProfile(IEnumerable{BodyTypeProfile}, VM_CharacterViewer, string)"/>.
    /// Takes the per-shape vertex counts directly instead of pulling them from a live viewer, so a
    /// headless caller (e.g. the startup body-type detector that surveys the installed default body
    /// NIF) can match without rendering. Same priority order: per-shape fingerprint match, then
    /// total + name match, then name-only. When <paramref name="sliderGroupHint"/> is null only the
    /// per-shape fingerprint branch can fire.
    /// </summary>
    public static BodyTypeProfile FindMatchingProfile(IEnumerable<BodyTypeProfile> profiles, IReadOnlyDictionary<string, int> shapeVertexCounts, string sliderGroupHint)
    {
        if (profiles == null || shapeVertexCounts == null) return null;

        var counts = shapeVertexCounts;
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

        // Vertex-count fingerprint only matters for Explicit-strategy KVs, whose
        // VertexIndex is a literal pointer into the authored mesh — if the live
        // mesh has a different vertex count it's almost certainly a different
        // body variant and the indices land on wrong anatomy. BoundingBox-strategy
        // KVs re-resolve from scratch on every evaluation by scanning the live
        // mesh's vertices inside an AABB, so they survive vertex-count changes
        // unscathed (CBBE 18436 vs 12740 etc.). Profiles that mix strategies must
        // still match on count because the Explicit KVs would silently corrupt.
        // Note: this only suppresses the spurious TopologyMismatch flag — the
        // engine still emits measurements either way, since the per-KV resolver
        // either finds a candidate or fails on its own merits.
        bool hasExplicitKeyVertices = false;
        if (profile.KeyVertices != null)
        {
            foreach (var kv in profile.KeyVertices)
            {
                if (kv != null && kv.Strategy == KeyVertexStrategy.Explicit)
                {
                    hasExplicitKeyVertices = true;
                    break;
                }
            }
        }
        if (!hasExplicitKeyVertices) return true;

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

    /// <summary>
    /// Evaluates a <see cref="MeasurementKind.RegionVolume"/> measurement: looks up the baked
    /// <see cref="RegionVolumeEvaluator.ResolvedRegion"/> for <see cref="MeasurementDefinition.RegionRefName"/>,
    /// fetches the deformed positions for the region's shape, and integrates the volume. Returns
    /// false (with a reason) when the region ref is empty, no resolved region was supplied, the
    /// region is invalid (box failed validation), or the shape's positions aren't available.
    /// Pure aside from the <paramref name="shapeLookup"/> delegate, so it is unit-testable without
    /// a live viewer (see <c>RegionVolumeWiringTests</c>).
    /// </summary>
    public static bool TryEvaluateRegionVolume(
        MeasurementDefinition def,
        IReadOnlyDictionary<string, RegionVolumeEvaluator.ResolvedRegion>? resolvedRegions,
        MeasurementMath.ShapePositionsLookup shapeLookup,
        out float value,
        out MeasurementFailureReason reason)
    {
        value = 0f;
        var rname = def?.RegionRefName?.Trim() ?? "";
        if (rname.Length == 0)
        {
            reason = MeasurementFailureReason.MalformedDefinition;
            return false;
        }
        if (resolvedRegions == null || !resolvedRegions.TryGetValue(rname, out var resolved) || resolved == null)
        {
            reason = MeasurementFailureReason.MissingRegion;
            return false;
        }
        if (!resolved.IsValid)
        {
            reason = MeasurementFailureReason.RegionNotResolved;
            return false;
        }
        var positions = shapeLookup?.Invoke(resolved.ShapeName);
        if (positions == null || positions.Length == 0)
        {
            reason = MeasurementFailureReason.RegionNotResolved;
            return false;
        }
        value = (float)RegionVolumeEvaluator.ComputeVolume(resolved, positions, resolved.CapMode);
        reason = MeasurementFailureReason.MalformedDefinition; // unused on success
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

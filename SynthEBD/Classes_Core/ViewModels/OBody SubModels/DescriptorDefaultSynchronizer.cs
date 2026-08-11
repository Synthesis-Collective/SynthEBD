using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Keeps the per-category "default descriptor value" synchronized between the two labeling systems
/// that both own one: Label by Sliders (<see cref="VM_DescriptorClassificationRuleSet.DefaultDescriptorValue"/>,
/// per (body type, category)) and Label by Measurements (<see cref="VM_BodyTypeProfile"/> defaults,
/// per (profile, category), joined on <c>BodyTypeName</c> ↔ <c>BodyTypeGroup</c>, case-insensitive).
/// Both menus label the same presets, so two different catch-all values for one category would mean
/// a preset's descriptor depended on which menu last touched it — this service enforces the
/// invariant <b>one default per (body type, category), whichever menu edits it</b>.
///
/// <para>The two defaults being identical is precisely why
/// <see cref="BodySlideMeasurementEvaluator.CollectExternalDescriptors"/> keeps the slider-side
/// default OUT of the classifier's external-descriptor seed. Seeds mark a category covered by a
/// senior source and suppress its measurement-side default; seeding a value that duplicates that
/// default would cancel it and leave the category unlabeled. Slider <i>rule matches</i> still seed
/// and still suppress — only the default is exempt. Keep that exemption in place if this
/// synchronizer's policy ever changes.</para>
///
/// <para><b>Live edits</b> propagate unconditionally and immediately in both directions
/// (<see cref="PushFromSliderRuleSet"/> / <see cref="PushFromMeasurementProfile"/>), fanning out to
/// the slider rule set AND every profile sharing the body type — including clears. Fan-out writes
/// are wrapped in <see cref="_isApplying"/> so the receiving side's own change hook doesn't echo.</para>
///
/// <para><b>Reconciliation</b> (<see cref="ReconcileAll"/> after settings hydration;
/// <see cref="ReconcileProfile"/> when a profile is imported/duplicated/retargeted) resolves
/// pre-existing disagreement: an empty side always adopts the non-empty side; a true conflict
/// (both non-empty, different) is decided by the OBody Misc toggle
/// (<see cref="VM_OBodyMiscSettings.PreferSliderDefaultsOnConflict"/>, default = measurement side
/// wins) and logged. Hydration runs under <see cref="BeginHydration"/> suspension so the bulk
/// VM loads don't fire pushes against half-loaded state.</para>
/// </summary>
public class DescriptorDefaultSynchronizer
{
    private readonly Logger _logger;

    private VM_BodySlideAnnotator? _annotator;
    private VM_BodyTypeProfileEditor? _profileEditor;
    private VM_OBodyMiscSettings? _miscSettings;

    /// <summary>True while this service itself is writing values, so the targets' change hooks
    /// (which call back into the Push* methods) no-op instead of echoing.</summary>
    private bool _isApplying;

    /// <summary>&gt; 0 while settings hydration is in flight (see <see cref="BeginHydration"/>).</summary>
    private int _hydrationDepth;

    /// <summary>False until the first post-hydration reconcile has run. Keeps any push fired by
    /// stray VM construction before the first settings load inert — live sync only makes sense
    /// once both menus hold real data.</summary>
    private bool _activated;

    public DescriptorDefaultSynchronizer(Logger logger)
    {
        _logger = logger;
    }

    public void RegisterAnnotator(VM_BodySlideAnnotator annotator) => _annotator = annotator;
    public void RegisterProfileEditor(VM_BodyTypeProfileEditor profileEditor) => _profileEditor = profileEditor;
    public void RegisterMiscSettings(VM_OBodyMiscSettings miscSettings) => _miscSettings = miscSettings;

    /// <summary>Suspends live pushes for the duration of a settings hydration pass (bulk VM loads
    /// set defaults wholesale; propagating them mid-load would smear half-loaded state across
    /// menus). Balanced by <see cref="EndHydrationAndReconcile"/>.</summary>
    public void BeginHydration() => _hydrationDepth++;

    /// <summary>Ends a hydration pass; when the last nested pass ends, runs a full
    /// <see cref="ReconcileAll"/> so both menus agree before the user sees either, then (first
    /// time) activates live pushes.</summary>
    public void EndHydrationAndReconcile()
    {
        _hydrationDepth = Math.Max(0, _hydrationDepth - 1);
        if (_hydrationDepth == 0)
        {
            ReconcileAll();
            _activated = true;
        }
    }

    private bool SyncInactive => !_activated || _hydrationDepth > 0 || _isApplying;

    /// <summary>Conflict policy, read live from the OBody Misc toggle. False (default) =
    /// measurement side wins.</summary>
    private bool PreferSliderSideOnConflict => _miscSettings?.PreferSliderDefaultsOnConflict ?? false;

    /// <summary>Live-edit entry point for the slider side: the user changed (or cleared) the
    /// default of <paramref name="category"/> under body type <paramref name="bodyTypeGroup"/> in
    /// the Label by Sliders menu. Propagates the value to every measurement profile of that body
    /// type. Called by <see cref="VM_DescriptorClassificationRuleSet"/>'s change hook, so it also
    /// fires for this service's own writes — the <see cref="_isApplying"/> guard drops those.</summary>
    public void PushFromSliderRuleSet(string bodyTypeGroup, string category, string value)
    {
        if (SyncInactive) return;
        if (string.IsNullOrWhiteSpace(bodyTypeGroup) || string.IsNullOrWhiteSpace(category)) return;

        _isApplying = true;
        try
        {
            foreach (var profile in ProfilesForBodyType(bodyTypeGroup))
            {
                profile.SetDefaultValueForCategory(category, value ?? "");
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    /// <summary>Live-edit entry point for the measurement side: the user changed (or cleared) the
    /// default of <paramref name="category"/> on <paramref name="sourceProfile"/> via the rule
    /// tree's Make Default checkbox. Propagates to the slider rule set of the profile's body type
    /// and to every sibling profile sharing it.</summary>
    public void PushFromMeasurementProfile(VM_BodyTypeProfile sourceProfile, string category, string value)
    {
        if (SyncInactive) return;
        var bodyType = sourceProfile?.BodyTypeName;
        if (sourceProfile == null || string.IsNullOrWhiteSpace(bodyType) || string.IsNullOrWhiteSpace(category)) return;

        _isApplying = true;
        try
        {
            ApplyToSliderRuleSet(bodyType, category, value ?? "");
            foreach (var profile in ProfilesForBodyType(bodyType))
            {
                if (ReferenceEquals(profile, sourceProfile)) continue;
                profile.SetDefaultValueForCategory(category, value ?? "");
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    /// <summary>Reconciles every body type known to either menu. Runs after settings hydration
    /// (both sides fully loaded) and re-enables live sync afterwards via the hydration counter.</summary>
    public void ReconcileAll()
    {
        var bodyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_annotator != null)
        {
            foreach (var ruleSet in _annotator.AnnotationRules)
            {
                if (!string.IsNullOrWhiteSpace(ruleSet?.BodyTypeGroup)) bodyTypes.Add(ruleSet.BodyTypeGroup);
            }
        }
        if (_profileEditor != null)
        {
            foreach (var profile in _profileEditor.Profiles)
            {
                if (!string.IsNullOrWhiteSpace(profile?.BodyTypeName)) bodyTypes.Add(profile.BodyTypeName);
            }
        }

        foreach (var bodyType in bodyTypes)
        {
            try
            {
                ReconcileBodyType(bodyType);
            }
            catch (Exception ex)
            {
                // Reconciliation runs inside settings hydration — a bad body type must not take
                // down the load (or leave sync suspended); skip it and keep going.
                _logger.LogError("Descriptor default sync: reconciling body type '" + bodyType + "' failed: " + ExceptionLogger.GetExceptionStack(ex));
            }
        }
    }

    /// <summary>Targeted reconcile for one profile's body type — used when a profile is imported,
    /// duplicated, or retargeted to a different BodyTypeName mid-session, so it and the slider menu
    /// agree without waiting for the next settings load. No-op for profiles with no body type and
    /// during hydration (the end-of-hydration <see cref="ReconcileAll"/> covers everything then).</summary>
    public void ReconcileProfile(VM_BodyTypeProfile profile)
    {
        if (_hydrationDepth > 0) return;
        var bodyType = profile?.BodyTypeName;
        if (string.IsNullOrWhiteSpace(bodyType)) return;
        ReconcileBodyType(bodyType!);
    }

    /// <summary>Harmonizes one body type: for each category with a default anywhere (the slider
    /// rule set or any profile of the body type), computes the canonical value via
    /// <see cref="DecideCanonicalDefault"/> and applies it to every participant. Empty sides adopt;
    /// true conflicts follow the Misc toggle and are logged. Reconciliation never clears — only
    /// live edits propagate emptiness.</summary>
    private void ReconcileBodyType(string bodyType)
    {
        var sliderRuleSet = _annotator?.AnnotationRules?
            .FirstOrDefault(x => x != null && string.Equals(x.BodyTypeGroup, bodyType, StringComparison.OrdinalIgnoreCase));
        var profiles = ProfilesForBodyType(bodyType).ToList();
        if (sliderRuleSet == null && profiles.Count <= 1) return; // nothing to harmonize against

        // Union of categories that carry a default on either side.
        var categories = new HashSet<string>(StringComparer.Ordinal);
        if (sliderRuleSet != null)
        {
            foreach (var classifier in sliderRuleSet.DescriptorClassifiers)
            {
                if (classifier == null || string.IsNullOrWhiteSpace(classifier.DescriptorCategory)) continue;
                if (!string.IsNullOrEmpty(classifier.DefaultDescriptorValue?.Value)) categories.Add(classifier.DescriptorCategory);
            }
        }
        foreach (var profile in profiles)
        {
            foreach (var kvp in profile.GetDefaultValuesByCategory())
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrEmpty(kvp.Value)) categories.Add(kvp.Key);
            }
        }

        _isApplying = true;
        try
        {
            foreach (var category in categories)
            {
                var sliderClassifier = sliderRuleSet?.DescriptorClassifiers?
                    .FirstOrDefault(x => x != null && string.Equals(x.DescriptorCategory, category, StringComparison.Ordinal));
                string sliderValue = sliderClassifier?.DefaultDescriptorValue?.Value ?? "";
                var measurementValues = profiles.Select(p => p.GetDefaultValueForCategory(category)).ToList();

                string canonical = DecideCanonicalDefault(sliderValue, measurementValues, PreferSliderSideOnConflict, out bool hadConflict);
                if (string.IsNullOrEmpty(canonical)) continue;

                bool anyChange = false;
                if (sliderClassifier != null && !string.Equals(sliderValue, canonical, StringComparison.Ordinal))
                {
                    if (sliderClassifier.TrySetDefaultValueByName(canonical))
                    {
                        anyChange = true;
                    }
                    else
                    {
                        _logger.LogMessage($"Descriptor default sync: could not set {bodyType} / {category} default to '{canonical}' in Label by Sliders — that value does not exist in the descriptor catalog for this category.");
                    }
                }
                foreach (var profile in profiles)
                {
                    if (!string.Equals(profile.GetDefaultValueForCategory(category), canonical, StringComparison.Ordinal))
                    {
                        profile.SetDefaultValueForCategory(category, canonical);
                        anyChange = true;
                    }
                }

                if (anyChange)
                {
                    string reason = hadConflict
                        ? (PreferSliderSideOnConflict ? "conflict — slider side wins (per Misc setting)" : "conflict — measurement side wins (per Misc setting)")
                        : "adopted from the side that had a value";
                    _logger.LogMessage($"Descriptor default sync: {bodyType} / {category} default set to '{canonical}' across both labeling menus ({reason}).");
                }
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    /// <summary>
    /// The pure canonical-value decision, split out for testability: given the slider-side default
    /// and every same-body-type profile's default for one category, returns the value both menus
    /// should carry, and whether reaching it required overriding a disagreement.
    /// <list type="bullet">
    /// <item><description>All empty → "" (nothing to reconcile).</description></item>
    /// <item><description>Exactly one side has values → that side's value (adoption; the first
    /// non-empty profile value, in list order, is the measurement side's canonical).</description></item>
    /// <item><description>Both sides non-empty and equal → that value (conflict flagged only if a
    /// sibling profile diverges).</description></item>
    /// <item><description>Both sides non-empty and different → <paramref name="preferSliderSide"/>
    /// picks the winner; <paramref name="hadConflict"/> = true.</description></item>
    /// </list>
    /// </summary>
    public static string DecideCanonicalDefault(string sliderValue, IReadOnlyList<string> measurementValues, bool preferSliderSide, out bool hadConflict)
    {
        sliderValue ??= "";
        string firstMeasurement = measurementValues?.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        // Conflict = the non-empty values are not all identical (slider vs measurement, or
        // measurement profiles among themselves).
        var nonEmpty = new List<string>();
        if (!string.IsNullOrEmpty(sliderValue)) nonEmpty.Add(sliderValue);
        if (measurementValues != null) nonEmpty.AddRange(measurementValues.Where(v => !string.IsNullOrEmpty(v)));
        hadConflict = nonEmpty.Distinct(StringComparer.Ordinal).Count() > 1;

        if (string.IsNullOrEmpty(sliderValue)) return firstMeasurement;
        if (string.IsNullOrEmpty(firstMeasurement)) return sliderValue;
        if (string.Equals(sliderValue, firstMeasurement, StringComparison.Ordinal)) return sliderValue;
        return preferSliderSide ? sliderValue : firstMeasurement;
    }

    private IEnumerable<VM_BodyTypeProfile> ProfilesForBodyType(string bodyType)
    {
        if (_profileEditor == null || string.IsNullOrWhiteSpace(bodyType)) yield break;
        foreach (var profile in _profileEditor.Profiles)
        {
            if (profile == null) continue;
            if (string.Equals(profile.BodyTypeName, bodyType, StringComparison.OrdinalIgnoreCase))
            {
                yield return profile;
            }
        }
    }

    /// <summary>Writes <paramref name="value"/> into the slider-side default for
    /// (<paramref name="bodyType"/>, <paramref name="category"/>) when that rule set / category VM
    /// exists. Silently no-ops when the body type has no slider rule set (profiles of uninstalled
    /// bodies still sync among themselves); logs when the value can't be represented because it
    /// isn't in the category's descriptor catalog.</summary>
    private void ApplyToSliderRuleSet(string bodyType, string category, string value)
    {
        var sliderRuleSet = _annotator?.AnnotationRules?
            .FirstOrDefault(x => x != null && string.Equals(x.BodyTypeGroup, bodyType, StringComparison.OrdinalIgnoreCase));
        var classifier = sliderRuleSet?.DescriptorClassifiers?
            .FirstOrDefault(x => x != null && string.Equals(x.DescriptorCategory, category, StringComparison.Ordinal));
        if (classifier == null) return;

        if (!classifier.TrySetDefaultValueByName(value) && !string.IsNullOrEmpty(value))
        {
            _logger.LogMessage($"Descriptor default sync: could not set {bodyType} / {category} default to '{value}' in Label by Sliders — that value does not exist in the descriptor catalog for this category.");
        }
    }
}

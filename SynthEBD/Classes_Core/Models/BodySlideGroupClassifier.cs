using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Result of <see cref="BodySlideGroupClassifier.Classify"/>: the canonical body type the preset
/// belongs to and which gender list it should be added to. <see cref="BodyType"/> is "Unknown"
/// when no installed registry entry covers at least <see cref="BodySlideGroupClassifier.CoverageThreshold"/>
/// of the preset's sliders.
/// </summary>
public class BodySlideClassification
{
    public string BodyType { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public string Reason { get; set; } = "";
}

/// <summary>
/// Slider-only classifier. Inputs are a preset's slider-name set and the Body-Type Registry;
/// <b>no preset-author metadata</b> (the <c>set</c> attribute, the preset name, the
/// <c>&lt;Group&gt;</c> tags) is consulted, because authors set those sloppily.
///
/// Pipeline:
///   1. <b>Catalog-filter:</b> candidates = registry entries with a non-empty
///      <see cref="BodyTypeRegistryEntry.ResolvedSliders"/>. Catalogs come from the user's
///      local OSD/BSD reference files when the body is installed, otherwise from the shipped
///      fallback <c>InternalData/SliderCatalogs/{SafeName}.json</c> -- so a preset for a body
///      the user hasn't actually installed still classifies (e.g. BHUNP presets without BHUNP).
///   2. <b>Coverage match:</b> for each candidate, count how many preset sliders are in the
///      candidate's <c>ResolvedSliders</c>. Keep candidates whose coverage is ≥ 75 %.
///      Strict subset is not required because reference OSDs occasionally drop legacy sliders
///      their derived presets still use (e.g. CBBE 3BA's reference OSD drops CBBE's AreolaSize,
///      yet Alera-style 3BA presets still set it).
///   3. <b>Closest-match pick:</b> installed entries beat uninstalled on tie; then fewer missing
///      wins; then smaller native catalog wins. Installed-wins preserves the "user actually has
///      this body" signal -- when two catalogs cover a preset equally, the installed one routes
///      to a body the user can actually render.
///
/// All-miss returns <c>BodyType="Unknown"</c> with a best-effort gender (Male if any preset
/// slider is known to a male-gender entry with a catalog; Female otherwise).
/// </summary>
public class BodySlideGroupClassifier
{
    /// <summary>
    /// Minimum fraction of the preset's sliders that a registry entry must carry to be
    /// considered a candidate. 0.75 is permissive enough to absorb the 2-4 sliders of
    /// legacy/derived-OSD drift seen across CBBE / CBBE 3BA / BHUNP / UBE / HIMBO reference
    /// catalogs, while still rejecting unrelated bodies (whose overlap with any given preset
    /// is typically far below 50 %).
    /// </summary>
    public const double CoverageThreshold = 0.75;

    private List<BodyTypeRegistryEntry> _registry = new();

    public BodySlideGroupClassifier()
    {
    }

    /// <summary>True when at least one registry entry has a non-empty slider catalog
    /// (whether from a local install or the shipped fallback).</summary>
    public bool HasCatalogs
    {
        get
        {
            if (_registry == null) return false;
            foreach (var e in _registry)
            {
                if (e == null) continue;
                if (e.ResolvedSliders != null && e.ResolvedSliders.Count > 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Replace the in-memory registry. Called from <see cref="SettingsIO_OBody.LoadSliderCatalogs"/>
    /// after fingerprint scanning and slider extraction populate <see cref="BodyTypeRegistryEntry.IsInstalled"/>
    /// and <see cref="BodyTypeRegistryEntry.ResolvedSliders"/>.
    /// </summary>
    public void SetRegistry(List<BodyTypeRegistryEntry> registry)
    {
        _registry = registry ?? new List<BodyTypeRegistryEntry>();
    }

    /// <summary>
    /// Classify a preset into one of the installed body types by coverage ratio. Returns
    /// <c>BodyType="Unknown"</c> when no installed entry covers at least
    /// <see cref="CoverageThreshold"/> of the preset's sliders.
    /// </summary>
    /// <param name="trace">
    /// Optional sink for per-step diagnostic lines. When supplied, the classifier emits one line
    /// per registry entry describing its coverage and why it was kept or dropped, plus a final
    /// line for the pick or the miss reason. Leave null for the common (silent) hot-path calls
    /// at preset-load time; pass a Logger-bound sink from the UI when the user wants to see why
    /// a specific preset failed to classify.
    /// </param>
    public BodySlideClassification Classify(string presetName, ICollection<string> presetSliderNames, Action<string> trace = null)
    {
        void Trace(string line) { trace?.Invoke(line); }

        if (!HasCatalogs)
        {
            // Name preserved for stability: emitted as "no-installed-bodies" even though
            // catalogs now include fallback-seeded uninstalled bodies -- this branch only fires
            // when the registry has zero usable catalogs at all.
            Trace($"Classifier['{presetName}']: no usable catalogs in registry (every entry has empty ResolvedSliders). Shipped fallback may be missing or failed to load.");
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "no-installed-bodies" };
        }
        if (presetSliderNames == null || presetSliderNames.Count == 0)
        {
            // A preset that moves no sliders has no fingerprint -- can't be classified by sliders.
            Trace($"Classifier['{presetName}']: preset has 0 sliders -- cannot classify.");
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "preset-has-no-sliders" };
        }

        // Normalize the preset slider list once: strip empty strings so coverage math isn't
        // skewed by malformed <SetSlider name=""/> entries.
        var normalizedPresetSliders = new List<string>(presetSliderNames.Count);
        foreach (var s in presetSliderNames)
        {
            if (!string.IsNullOrEmpty(s)) normalizedPresetSliders.Add(s);
        }
        if (normalizedPresetSliders.Count == 0)
        {
            Trace($"Classifier['{presetName}']: preset has 0 non-empty sliders -- cannot classify.");
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "preset-has-no-sliders" };
        }

        // BodySlide presets commonly target multiple SliderSets via the <Preset groups="..."/>
        // attribute -- a single <Preset> can include SetSliders for the body *and* outfits
        // (cape, cloak, fur skirt, etc.). Those outfit sliders never appear in any body
        // catalog, so they'd inflate every candidate's `missing` count and push coverage
        // below threshold for no good reason. Pre-filter the preset to sliders present in
        // at least one registry catalog (the "known body sliders" union). Non-body sliders
        // are dropped; coverage math then runs on body-only input.
        var knownBodySliders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _registry)
        {
            if (e?.ResolvedSliders == null) continue;
            foreach (var s in e.ResolvedSliders) knownBodySliders.Add(s);
        }
        var bodyOnlyPresetSliders = new List<string>(normalizedPresetSliders.Count);
        List<string> droppedNonBody = trace != null ? new List<string>() : null;
        foreach (var s in normalizedPresetSliders)
        {
            if (knownBodySliders.Contains(s)) bodyOnlyPresetSliders.Add(s);
            else if (droppedNonBody != null && droppedNonBody.Count < 8) droppedNonBody.Add(s);
        }
        int droppedCount = normalizedPresetSliders.Count - bodyOnlyPresetSliders.Count;

        if (trace != null)
        {
            int totalCatalogs = 0;
            int installedCatalogs = 0;
            foreach (var e in _registry)
            {
                if (e == null || e.ResolvedSliders == null || e.ResolvedSliders.Count == 0) continue;
                totalCatalogs++;
                if (e.IsInstalled) installedCatalogs++;
            }
            Trace($"Classifier['{presetName}']: {normalizedPresetSliders.Count} preset slider(s); "
                + $"{totalCatalogs} catalog(s) loaded ({installedCatalogs} installed, {totalCatalogs - installedCatalogs} fallback); "
                + $"threshold = {CoverageThreshold:P0}.");
            if (droppedCount > 0)
            {
                string sample = droppedNonBody.Count > 0
                    ? $" e.g. [{string.Join(", ", droppedNonBody)}{(droppedCount > droppedNonBody.Count ? ", ..." : "")}]"
                    : "";
                Trace($"  [filter] dropped {droppedCount} non-body slider(s){sample}; "
                    + $"classifying on {bodyOnlyPresetSliders.Count} body slider(s).");
            }
        }

        if (bodyOnlyPresetSliders.Count == 0)
        {
            Trace($"Classifier['{presetName}']: no preset sliders match any registry catalog -- cannot classify.");
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "no-body-sliders-in-preset" };
        }

        // From here down, coverage math uses body-only sliders as the denominator.
        normalizedPresetSliders = bodyOnlyPresetSliders;

        BodyTypeRegistryEntry best = null;
        int bestMissing = int.MaxValue;
        int bestNative = int.MaxValue;
        bool bestInstalled = false;
        int candidateCount = 0;

        foreach (var entry in _registry)
        {
            if (entry == null) continue;
            if (entry.ResolvedSliders == null || entry.ResolvedSliders.Count == 0)
            {
                if (entry != null) Trace($"  [skip] {entry.Name} [{entry.Gender}]: no catalog loaded.");
                continue;
            }

            int missing = 0;
            List<string> missingSample = trace != null ? new List<string>() : null;
            foreach (var s in normalizedPresetSliders)
            {
                if (!entry.ResolvedSliders.Contains(s))
                {
                    missing++;
                    if (missingSample != null && missingSample.Count < 8) missingSample.Add(s);
                }
            }

            double coverage = (double)(normalizedPresetSliders.Count - missing) / normalizedPresetSliders.Count;
            string tag = entry.IsInstalled ? "installed" : "fallback";
            if (coverage < CoverageThreshold)
            {
                if (trace != null)
                {
                    string sample = missingSample != null && missingSample.Count > 0
                        ? $" missing e.g. [{string.Join(", ", missingSample)}{(missing > missingSample.Count ? ", ..." : "")}]"
                        : "";
                    Trace($"  [drop] {entry.Name} [{entry.Gender}] ({tag}, native={entry.ResolvedSliders.Count}): "
                        + $"coverage {coverage:P1} < {CoverageThreshold:P0}, missing {missing}/{normalizedPresetSliders.Count}.{sample}");
                }
                continue;
            }

            candidateCount++;
            int nativeCount = entry.ResolvedSliders.Count;
            bool installed = entry.IsInstalled;

            Trace($"  [cand] {entry.Name} [{entry.Gender}] ({tag}, native={nativeCount}): "
                + $"coverage {coverage:P1}, missing {missing}/{normalizedPresetSliders.Count}.");

            // Ranking: fewest missing wins; on tie, installed beats uninstalled; on tie, smaller
            // native catalog wins. Installed-wins preserves "user actually has this body" --
            // routing to a renderable body when coverage is equal. Smaller-native keeps presets
            // that only move CBBE-common sliders resolved to CBBE rather than CBBE 3BA.
            bool better = missing < bestMissing
                || (missing == bestMissing && installed && !bestInstalled)
                || (missing == bestMissing && installed == bestInstalled && nativeCount < bestNative);
            if (best == null || better)
            {
                best = entry;
                bestMissing = missing;
                bestNative = nativeCount;
                bestInstalled = installed;
            }
        }

        if (best == null)
        {
            var gender = InferGenderFromSliders(normalizedPresetSliders);
            Trace($"Classifier['{presetName}']: no candidate passed the {CoverageThreshold:P0} coverage threshold. "
                + $"Inferred gender={gender} (Male if any preset slider is in a male catalog, else Female).");
            return new BodySlideClassification
            {
                BodyType = "Unknown",
                Gender = gender,
                Reason = "no-coverage-match",
            };
        }

        string reason;
        if (bestMissing == 0)
        {
            reason = candidateCount > 1
                ? $"exact-match-most-general (of {candidateCount})"
                : "exact-match";
        }
        else
        {
            reason = candidateCount > 1
                ? $"closest-match-{bestMissing}-drift (of {candidateCount})"
                : $"closest-match-{bestMissing}-drift";
        }

        Trace($"Classifier['{presetName}']: picked {best.Name} [{best.Gender}] "
            + $"({(bestInstalled ? "installed" : "fallback")}, native={bestNative}, missing={bestMissing}) -- {reason}.");

        return new BodySlideClassification
        {
            BodyType = best.Name,
            Gender = best.Gender,
            Reason = reason,
        };
    }

    /// <summary>
    /// Best-effort gender for an unclassifiable preset: Male if any of the preset's sliders is in
    /// the slider catalog of a male body type (installed or fallback-seeded), Female otherwise.
    /// Falls back to Female when the registry has no male catalogs loaded.
    /// </summary>
    private Gender InferGenderFromSliders(ICollection<string> presetSliderNames)
    {
        foreach (var entry in _registry)
        {
            if (entry == null) continue;
            if (entry.Gender != Gender.Male) continue;
            if (entry.ResolvedSliders == null || entry.ResolvedSliders.Count == 0) continue;
            foreach (var s in presetSliderNames)
            {
                if (!string.IsNullOrEmpty(s) && entry.ResolvedSliders.Contains(s)) return Gender.Male;
            }
        }
        return Gender.Female;
    }
}

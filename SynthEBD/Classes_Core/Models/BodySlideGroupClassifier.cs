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
/// Slider-only classifier. Inputs are a preset's slider-name set and the installed Body-Type
/// Registry; <b>no preset-author metadata</b> (the <c>set</c> attribute, the preset name, the
/// <c>&lt;Group&gt;</c> tags) is consulted, because authors set those sloppily.
///
/// Pipeline:
///   1. <b>Installed-filter:</b> candidates = registry entries with <see cref="BodyTypeRegistryEntry.IsInstalled"/>
///      true and a non-empty <see cref="BodyTypeRegistryEntry.ResolvedSliders"/>.
///   2. <b>Coverage match:</b> for each candidate, count how many preset sliders are in the
///      candidate's <c>ResolvedSliders</c>. Keep candidates whose coverage is ≥ 75 %.
///      Strict subset is no longer required because reference OSDs occasionally drop legacy
///      sliders their derived presets still use (e.g. CBBE 3BA's reference OSD drops CBBE's
///      AreolaSize, yet Alera-style 3BA presets still set it).
///   3. <b>Closest-match pick:</b> prefer the candidate with the fewest missing preset sliders;
///      on tie, prefer the smaller native catalog. This keeps a preset that only moves
///      CBBE-common sliders resolved to CBBE rather than CBBE 3BA.
///
/// All-miss returns <c>BodyType="Unknown"</c> with a best-effort gender (Male if any preset
/// slider is known to a male-gender installed entry; Female otherwise).
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

    /// <summary>True when at least one installed registry entry has a non-empty slider catalog.</summary>
    public bool HasCatalogs
    {
        get
        {
            if (_registry == null) return false;
            foreach (var e in _registry)
            {
                if (e == null) continue;
                if (e.IsInstalled && e.ResolvedSliders != null && e.ResolvedSliders.Count > 0) return true;
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
    public BodySlideClassification Classify(string presetName, ICollection<string> presetSliderNames)
    {
        if (!HasCatalogs)
        {
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "no-installed-bodies" };
        }
        if (presetSliderNames == null || presetSliderNames.Count == 0)
        {
            // A preset that moves no sliders has no fingerprint -- can't be classified by sliders.
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
            return new BodySlideClassification { BodyType = "Unknown", Gender = Gender.Female, Reason = "preset-has-no-sliders" };
        }

        BodyTypeRegistryEntry best = null;
        int bestMissing = int.MaxValue;
        int bestNative = int.MaxValue;
        int candidateCount = 0;

        foreach (var entry in _registry)
        {
            if (entry == null) continue;
            if (!entry.IsInstalled) continue;
            if (entry.ResolvedSliders == null || entry.ResolvedSliders.Count == 0) continue;

            int missing = 0;
            foreach (var s in normalizedPresetSliders)
            {
                if (!entry.ResolvedSliders.Contains(s)) missing++;
            }

            double coverage = (double)(normalizedPresetSliders.Count - missing) / normalizedPresetSliders.Count;
            if (coverage < CoverageThreshold) continue;

            candidateCount++;
            int nativeCount = entry.ResolvedSliders.Count;

            // Ranking: fewest missing wins; on tie, smaller native catalog wins.
            // Smaller-native tiebreak keeps a preset that uses only CBBE-common sliders resolved
            // to CBBE rather than CBBE 3BA when both achieve 100 % coverage.
            bool better = missing < bestMissing
                || (missing == bestMissing && nativeCount < bestNative);
            if (best == null || better)
            {
                best = entry;
                bestMissing = missing;
                bestNative = nativeCount;
            }
        }

        if (best == null)
        {
            return new BodySlideClassification
            {
                BodyType = "Unknown",
                Gender = InferGenderFromSliders(normalizedPresetSliders),
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

        return new BodySlideClassification
        {
            BodyType = best.Name,
            Gender = best.Gender,
            Reason = reason,
        };
    }

    /// <summary>
    /// Best-effort gender for an unclassifiable preset: Male if any of the preset's sliders is in
    /// the slider catalog of an installed male body type, Female otherwise. Falls back to Female
    /// when the registry has no installed male body types loaded.
    /// </summary>
    private Gender InferGenderFromSliders(ICollection<string> presetSliderNames)
    {
        foreach (var entry in _registry)
        {
            if (entry == null || !entry.IsInstalled) continue;
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

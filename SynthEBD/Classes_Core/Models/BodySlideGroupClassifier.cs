using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Result of <see cref="BodySlideGroupClassifier.Classify"/>: the canonical body type the preset
/// belongs to and which gender list it should be added to. <see cref="BodyType"/> is "Unknown"
/// when no installed registry entry's slider catalog can host the preset's slider names.
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
///   2. <b>Strict subset match:</b> keep candidates whose <c>ResolvedSliders</c> contains every
///      slider name in the preset.
///   3. <b>Superset resolution:</b> if more than one candidate matches (only possible when one's
///      slider set is a subset of another's), the preset uses no slider exclusive to the larger
///      catalog -- so we pick the most general (smallest <c>ResolvedSliders</c> count). This makes
///      a preset that only moves CBBE-common sliders resolve to CBBE rather than CBBE 3BA.
///
/// All-miss returns <c>BodyType="Unknown"</c> with a best-effort gender (Male if any preset
/// slider is known to a male-gender installed entry; Female otherwise).
/// </summary>
public class BodySlideGroupClassifier
{
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
    /// Classify a preset into one of the installed body types. Returns a result with
    /// <c>BodyType="Unknown"</c> when the preset's slider set is not a subset of any installed
    /// entry's <see cref="BodyTypeRegistryEntry.ResolvedSliders"/>.
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

        var matches = new List<BodyTypeRegistryEntry>();
        foreach (var entry in _registry)
        {
            if (entry == null) continue;
            if (!entry.IsInstalled) continue;
            if (entry.ResolvedSliders == null || entry.ResolvedSliders.Count == 0) continue;

            bool allIn = true;
            foreach (var s in presetSliderNames)
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (!entry.ResolvedSliders.Contains(s)) { allIn = false; break; }
            }
            if (allIn) matches.Add(entry);
        }

        if (matches.Count == 0)
        {
            return new BodySlideClassification
            {
                BodyType = "Unknown",
                Gender = InferGenderFromSliders(presetSliderNames),
                Reason = "no-subset-match",
            };
        }

        if (matches.Count == 1)
        {
            return new BodySlideClassification
            {
                BodyType = matches[0].Name,
                Gender = matches[0].Gender,
                Reason = "subset-match",
            };
        }

        // Multiple installed catalogs cover the preset. Pick the most general -- the entry with the
        // smallest ResolvedSliders count. Because matching uses strict subset, the preset cannot use
        // any slider exclusive to the larger catalog (otherwise the smaller catalog would not match),
        // so the most general candidate is the safest choice.
        BodyTypeRegistryEntry pick = matches[0];
        foreach (var m in matches)
        {
            if (m.ResolvedSliders.Count < pick.ResolvedSliders.Count) pick = m;
        }
        return new BodySlideClassification
        {
            BodyType = pick.Name,
            Gender = pick.Gender,
            Reason = $"subset-match-most-general (of {matches.Count})",
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

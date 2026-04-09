using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Result of <see cref="BodySlideGroupClassifier.Classify"/>: the canonical body type the preset
/// belongs to and which gender list it should be added to.
/// </summary>
public class BodySlideClassification
{
    public string BodyType { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public string Reason { get; set; } = "";
}

/// <summary>
/// Three-stage classifier mirroring <c>classify_by_sliders</c> in the Python reference
/// (<c>bodyslide_checker.py</c>):
///
///   1. <b>Strict containment:</b> if a preset's slider names are a subset of exactly one catalog,
///      pick that catalog. With multiple matches, fall through.
///   2. <b>Unambiguous name match:</b> if the preset name contains exactly one body-type token,
///      pick that body type.
///   3. <b>Score fallback:</b> compute |preset â© catalog| / |preset| for each catalog and pick the
///      highest scorer if its margin over the runner-up exceeds <see cref="ScoreMarginThreshold"/>.
///
/// Family compatibility (<see cref="BodyTypeCatalog.Family"/>) collapses aliases onto their
/// canonical body type so e.g. a 3BA preset can resolve as CBBE when the user has aliased them.
/// Returns <c>null</c> when no catalog is loaded or no body type can be determined.
/// </summary>
public class BodySlideGroupClassifier
{
    private readonly Logger _logger;
    private SliderCategoryCatalog _catalog = new();
    private Dictionary<string, string> _aliasToCanonical = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Score margin (best - second_best) below which the score-based fallback declines to classify.
    /// 0.15 mirrors the default in the Python reference; tunable later if needed.
    /// </summary>
    public double ScoreMarginThreshold { get; set; } = 0.15;

    public BodySlideGroupClassifier(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>True when at least one body type catalog has been loaded.</summary>
    public bool HasCatalogs => _catalog != null && _catalog.BodyTypes.Count > 0;

    /// <summary>Replace the in-memory catalog. Called once at settings load by <see cref="SettingsIO_OBody"/>.</summary>
    public void SetCatalog(SliderCategoryCatalog catalog)
    {
        _catalog = catalog ?? new SliderCategoryCatalog();
        _aliasToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _catalog.BodyTypes)
        {
            // Body type maps to itself
            _aliasToCanonical[kv.Key] = kv.Key;
            if (kv.Value?.Family == null) continue;
            foreach (var alias in kv.Value.Family)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                // First mapping wins; subsequent collisions are ignored.
                if (!_aliasToCanonical.ContainsKey(alias))
                {
                    _aliasToCanonical[alias] = kv.Key;
                }
            }
        }
    }

    /// <summary>
    /// Classify a preset into one of the loaded body types. Returns <c>null</c> when the catalog is
    /// empty or the preset is too ambiguous (no clear winner across all three stages).
    /// </summary>
    public BodySlideClassification Classify(string presetName, ICollection<string> presetSliderNames)
    {
        if (!HasCatalogs) return null;
        if (presetSliderNames == null || presetSliderNames.Count == 0)
        {
            // Stage 2 (name match) can still resolve a preset with no sliders.
            return ClassifyByName(presetName);
        }

        // Stage 1: strict containment
        var subsetMatches = new List<BodyTypeCatalog>();
        foreach (var entry in _catalog.BodyTypes.Values)
        {
            if (entry?.Sliders == null || entry.Sliders.Count == 0) continue;
            bool allIn = true;
            foreach (var s in presetSliderNames)
            {
                if (!entry.Sliders.Contains(s)) { allIn = false; break; }
            }
            if (allIn) subsetMatches.Add(entry);
        }

        if (subsetMatches.Count == 1)
        {
            return Result(subsetMatches[0], "strict-containment");
        }
        if (subsetMatches.Count > 1)
        {
            // Multiple catalogs fully cover the preset -- try to collapse them via family compatibility.
            var canonical = CollapseFamily(subsetMatches);
            if (canonical != null) return Result(canonical, "strict-containment-family-collapse");
            // Otherwise fall through to stage 2/3
        }

        // Stage 2: unambiguous preset name match
        var byName = ClassifyByName(presetName);
        if (byName != null) return byName;

        // Stage 3: score-based fallback
        return ClassifyByScore(presetSliderNames);
    }

    private BodySlideClassification ClassifyByName(string presetName)
    {
        if (string.IsNullOrEmpty(presetName) || _catalog.BodyTypes.Count == 0) return null;
        var hits = new List<BodyTypeCatalog>();
        foreach (var entry in _catalog.BodyTypes.Values)
        {
            if (entry == null || string.IsNullOrEmpty(entry.BodyType)) continue;
            if (presetName.IndexOf(entry.BodyType, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hits.Add(entry);
                continue;
            }
            // Also check aliases (family members) so e.g. a name containing "3BA" matches CBBE when aliased.
            if (entry.Family != null)
            {
                foreach (var alias in entry.Family)
                {
                    if (string.IsNullOrEmpty(alias)) continue;
                    if (presetName.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hits.Add(entry);
                        break;
                    }
                }
            }
        }

        if (hits.Count == 1) return Result(hits[0], "preset-name-match");
        if (hits.Count > 1)
        {
            var canonical = CollapseFamily(hits);
            if (canonical != null) return Result(canonical, "preset-name-match-family-collapse");
        }
        return null;
    }

    private BodySlideClassification ClassifyByScore(ICollection<string> presetSliderNames)
    {
        var scores = new List<(BodyTypeCatalog Entry, double Score)>();
        double presetCount = presetSliderNames.Count;
        foreach (var entry in _catalog.BodyTypes.Values)
        {
            if (entry?.Sliders == null || entry.Sliders.Count == 0) continue;
            int overlap = 0;
            foreach (var s in presetSliderNames)
            {
                if (entry.Sliders.Contains(s)) overlap++;
            }
            if (overlap == 0) continue;
            scores.Add((entry, overlap / presetCount));
        }
        if (scores.Count == 0) return null;
        scores.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (scores.Count == 1) return Result(scores[0].Entry, $"score:{scores[0].Score:F2}");

        double margin = scores[0].Score - scores[1].Score;
        if (margin >= ScoreMarginThreshold)
        {
            return Result(scores[0].Entry, $"score:{scores[0].Score:F2} (margin {margin:F2})");
        }

        // Try collapsing the top contenders by family
        var topGroup = new List<BodyTypeCatalog>();
        double topScore = scores[0].Score;
        foreach (var s in scores)
        {
            if (Math.Abs(s.Score - topScore) < 1e-9) topGroup.Add(s.Entry);
            else break;
        }
        var canonical = CollapseFamily(topGroup);
        if (canonical != null) return Result(canonical, $"score-family-collapse:{topScore:F2}");
        return null;
    }

    /// <summary>
    /// If every entry in <paramref name="entries"/> resolves (via the alias map) to the same
    /// canonical body type, returns that canonical entry. Otherwise returns <c>null</c>.
    /// </summary>
    private BodyTypeCatalog CollapseFamily(IList<BodyTypeCatalog> entries)
    {
        if (entries == null || entries.Count == 0) return null;
        string canonicalName = null;
        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.BodyType)) return null;
            if (!_aliasToCanonical.TryGetValue(e.BodyType, out var c)) c = e.BodyType;
            if (canonicalName == null) canonicalName = c;
            else if (!string.Equals(canonicalName, c, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return canonicalName != null && _catalog.BodyTypes.TryGetValue(canonicalName, out var entry)
            ? entry
            : null;
    }

    private static BodySlideClassification Result(BodyTypeCatalog entry, string reason)
    {
        return new BodySlideClassification
        {
            BodyType = entry.BodyType,
            Gender = entry.Gender,
            Reason = reason,
        };
    }
}

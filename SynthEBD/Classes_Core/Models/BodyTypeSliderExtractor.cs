// Define CATALOG_VERBOSE_LOGGING to re-enable per-OSD LCP + raw-name samples and
// per-entry normalized slider samples. Default off -- these are diagnostic aids for
// slider-name-normalization issues, noisy in the common case.
//#define CATALOG_VERBOSE_LOGGING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Populates <see cref="BodyTypeRegistryEntry.ResolvedSliders"/> for every installed registry
/// entry by reading each entry's <see cref="BodyTypeRegistryEntry.ShapeDataFolders"/> under
/// the BodySlide ShapeData root and unioning the slider names from the referenced files.
/// Each entry is either a single .osd/.bsd file (preferred -- the body's reference mesh, which
/// defines the canonical slider catalog) or a folder scanned recursively (used by UUNP-style
/// bodies that ship per-slider .bsd files in a flat layout).
/// Scanning the reference file(s) only -- not every outfit OSD -- keeps the catalog to the body's
/// true slider set so subset checks between bodies (e.g. CBBE ⊂ CBBE 3BA) work as intended.
///
/// After sliders are resolved, <see cref="BodyTypeRegistryEntry.SupersetOfBodyType"/> is
/// auto-filled by finding the largest proper subset (same gender) among the other installed
/// entries. This resolves CBBE ⊂ CBBE 3BA, Unified UNP ⊂ BHUNP, etc., from slider evidence alone.
/// </summary>
public class BodyTypeSliderExtractor
{
    private readonly Logger _logger;

    public BodyTypeSliderExtractor(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Walks ShapeData for each installed entry, builds the union slider set, and computes
    /// superset links. Uninstalled entries have their <see cref="BodyTypeRegistryEntry.ResolvedSliders"/>
    /// cleared so stale data from a previous scan doesn't leak through.
    /// </summary>
    /// <param name="shapeDataRoot">Absolute path to CalienteTools/BodySlide/ShapeData under the game's Data folder.</param>
    /// <param name="entries">All registry entries (installed and not).</param>
    /// <param name="parser">Parser used to read OSD/BSD files.</param>
    public void PopulateResolvedSliders(string shapeDataRoot, IEnumerable<BodyTypeRegistryEntry> entries, BsdFileParser parser)
    {
        if (entries == null) return;
        if (parser == null) throw new ArgumentNullException(nameof(parser));

        var materialized = new List<BodyTypeRegistryEntry>();
        foreach (var e in entries)
        {
            if (e == null) continue;
            materialized.Add(e);
            e.ResolvedSliders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        bool shapeDataExists = !string.IsNullOrWhiteSpace(shapeDataRoot) && Directory.Exists(shapeDataRoot);
        if (!shapeDataExists)
        {
            _logger.LogMessage("BodyTypeSliderExtractor: ShapeData root missing -- no slider catalogs can be derived.");
            ComputeSupersets(materialized);
            return;
        }

        foreach (var entry in materialized)
        {
            if (!entry.IsInstalled) continue;
            if (entry.ShapeDataFolders == null || entry.ShapeDataFolders.Count == 0) continue;

            foreach (var rawPath in entry.ShapeDataFolders)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var sub = rawPath.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar)
                                 .TrimStart(Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(shapeDataRoot, sub);

                if (File.Exists(fullPath))
                {
                    var osd = string.Equals(Path.GetExtension(fullPath), ".bsd", StringComparison.OrdinalIgnoreCase)
                        ? parser.ParseBsdFile(fullPath)
                        : parser.ParseOsdFile(fullPath);
                    if (osd != null) AddNormalizedSliders(entry, osd);
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var osd in parser.ParseAllOsdInDirectory(fullPath, recursive: false))
                    {
                        AddNormalizedSliders(entry, osd);
                    }
                }
                else
                {
                    _logger.LogMessage($"BodyTypeSliderExtractor: '{entry.Name}' ShapeData target missing on disk: '{fullPath}'");
                }
            }

#if CATALOG_VERBOSE_LOGGING
            string sample = entry.ResolvedSliders.Count == 0
                ? ""
                : " Sample: " + string.Join(", ", entry.ResolvedSliders.Take(5));
            _logger.LogMessage($"BodyTypeSliderExtractor: '{entry.Name}' resolved {entry.ResolvedSliders.Count} slider(s) from {entry.ShapeDataFolders.Count} ShapeData folder(s).{sample}");
#endif
        }

        ComputeSupersets(materialized);
    }

    /// <summary>
    /// For each installed entry A, picks the installed same-gender entry B with the largest
    /// slider set that is a <i>near</i>-subset of A. Strict subset fails in practice because
    /// newer bodies drop or rename a handful of legacy sliders from their ancestors (e.g.
    /// CBBE 3BA's reference OSD omits CBBE's AreolaSize/BreastFlattness). We therefore allow
    /// up to <c>max(2, |B|/50)</c> of B's sliders to be absent from A (~2% drift tolerance
    /// with a 2-slider floor for small catalogs). O(N²) with N ≈ 10 -- trivial.
    /// </summary>
    private static void ComputeSupersets(List<BodyTypeRegistryEntry> entries)
    {
        foreach (var a in entries)
        {
            a.SupersetOfBodyType = "";
            if (!a.IsInstalled) continue;
            if (a.ResolvedSliders == null || a.ResolvedSliders.Count == 0) continue;

            BodyTypeRegistryEntry best = null;
            foreach (var b in entries)
            {
                if (ReferenceEquals(a, b)) continue;
                if (!b.IsInstalled) continue;
                if (b.Gender != a.Gender) continue;
                if (b.ResolvedSliders == null || b.ResolvedSliders.Count == 0) continue;
                if (b.ResolvedSliders.Count >= a.ResolvedSliders.Count) continue;

                int tolerance = Math.Max(2, b.ResolvedSliders.Count / 50);
                int missing = 0;
                foreach (var s in b.ResolvedSliders)
                {
                    if (!a.ResolvedSliders.Contains(s)) missing++;
                    if (missing > tolerance) break;
                }
                if (missing > tolerance) continue;

                if (best == null || b.ResolvedSliders.Count > best.ResolvedSliders.Count)
                {
                    best = b;
                }
            }

            if (best != null)
            {
                a.SupersetOfBodyType = best.Name;
            }
        }
    }

    /// <summary>
    /// BodySlide's OSD format stores slider entries as concatenated `&lt;shape&gt;&lt;slider&gt;`
    /// strings (e.g. `3BA RefAreolaSize`), while preset XMLs reference the canonical slider
    /// name only (e.g. `AreolaSize`). To make subset matching work, we compute the longest
    /// common prefix across all slider names in a single OSD file -- that prefix is the shape
    /// tag -- and strip it before adding to the catalog.
    ///
    /// Single-shape OSDs collapse to `LCP = &lt;shape&gt;`, so one strip yields the canonical
    /// name. Merged-shape OSDs that concatenate two or more shape tags in the same file
    /// (e.g. HIMBO's reference OSDs contain both `HIMBO - Body&lt;slider&gt;` and
    /// `HIMBO - Boxers&lt;slider&gt;`) defeat that: the global LCP only covers the shared head
    /// (`HIMBO - Bo`), leaving shape-specific lowercase tails (`dy`, `xers`) glued to the
    /// canonical names. BodySlide canonical slider names are PascalCase, so we recognize the
    /// merged-shape case by a lowercase-leading post-LCP name and recover the canonical
    /// suffix by dropping the leading lowercase run.
    /// </summary>
    private void AddNormalizedSliders(BodyTypeRegistryEntry entry, OsdFile osd)
    {
        if (osd?.Sliders == null || osd.Sliders.Count == 0) return;

        var rawNames = new List<string>(osd.Sliders.Count);
        foreach (var s in osd.Sliders)
        {
            if (!string.IsNullOrWhiteSpace(s?.Name)) rawNames.Add(s.Name);
        }
        if (rawNames.Count == 0) return;

        string lcp = ComputeLongestCommonPrefix(rawNames);

        // Guard the pathological case where every slider has the identical name -- LCP would
        // equal the full name and stripping it would leave empty strings. Treat as "no prefix."
        int minLen = int.MaxValue;
        foreach (var n in rawNames) if (n.Length < minLen) minLen = n.Length;
        if (lcp.Length >= minLen) lcp = "";

        // Stage 1: strip the OSD-wide LCP.
        var afterLcp = new List<string>(rawNames.Count);
        foreach (var name in rawNames)
        {
            var s = lcp.Length > 0 && name.StartsWith(lcp, StringComparison.Ordinal)
                ? name.Substring(lcp.Length)
                : name;
            afterLcp.Add(s);
        }

        // Stage 2: detect merged-shape OSD. Canonical slider names are PascalCase, so a
        // leftover lowercase head on any stripped name means the LCP truncated partway
        // through a shape tag because a second shape forced early divergence.
        bool multiShape = false;
        foreach (var s in afterLcp)
        {
            if (s.Length > 0 && char.IsLower(s[0])) { multiShape = true; break; }
        }

#if CATALOG_VERBOSE_LOGGING
        var rawSample = string.Join(", ", rawNames.Take(3));
        string mode = multiShape ? "multi-shape" : "single-shape";
        _logger.LogMessage($"BodyTypeSliderExtractor: '{entry.Name}' OSD '{osd.ShapeName}' ({mode}) LCP='{lcp}' raw sample: {rawSample}");
#endif

        foreach (var s in afterLcp)
        {
            var canonical = s;
            if (multiShape)
            {
                int i = 0;
                while (i < canonical.Length && char.IsLower(canonical[i])) i++;
                if (i > 0) canonical = canonical.Substring(i);
            }
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                entry.ResolvedSliders.Add(canonical);
            }
        }
    }

    private static string ComputeLongestCommonPrefix(IList<string> names)
    {
        if (names == null || names.Count == 0) return "";
        if (names.Count == 1) return "";
        string prefix = names[0];
        for (int i = 1; i < names.Count; i++)
        {
            var name = names[i];
            int j = 0;
            int max = Math.Min(prefix.Length, name.Length);
            while (j < max && prefix[j] == name[j]) j++;
            prefix = prefix.Substring(0, j);
            if (prefix.Length == 0) break;
        }
        return prefix;
    }
}

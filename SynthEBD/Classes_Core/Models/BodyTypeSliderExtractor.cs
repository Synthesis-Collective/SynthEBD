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
                    var osd = parser.ParseOsdFile(fullPath);
                    if (osd != null) AddNormalizedSliders(entry, osd);
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var osd in parser.ParseAllOsdInDirectory(fullPath))
                    {
                        AddNormalizedSliders(entry, osd);
                    }
                }
            }

            _logger.LogMessage($"BodyTypeSliderExtractor: '{entry.Name}' resolved {entry.ResolvedSliders.Count} slider(s) from {entry.ShapeDataFolders.Count} ShapeData folder(s).");
        }

        ComputeSupersets(materialized);
    }

    /// <summary>
    /// For each installed entry A, picks the installed same-gender entry B with the largest
    /// slider set that is a proper subset of A. A's <see cref="BodyTypeRegistryEntry.SupersetOfBodyType"/>
    /// becomes B.Name (empty if no such B exists). O(N²) with N ≈ 10 -- trivial.
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

                if (!b.ResolvedSliders.IsSubsetOf(a.ResolvedSliders)) continue;

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

        foreach (var name in rawNames)
        {
            var unprefixed = lcp.Length > 0 && name.StartsWith(lcp, StringComparison.Ordinal)
                ? name.Substring(lcp.Length)
                : name;
            if (!string.IsNullOrWhiteSpace(unprefixed))
            {
                entry.ResolvedSliders.Add(unprefixed);
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

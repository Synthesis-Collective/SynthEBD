using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// One-off utility that regenerates the shipped slider catalogs under
/// <c>InternalData/SliderCatalogs/{BodyType}.json</c> from a local BodySlide install. Presets
/// for body types that the end user hasn't installed still need a slider catalog to be
/// classifiable; this test produces that catalog by running the same OSD-normalization pipeline
/// the runtime extractor uses, against a known-good developer install of each body.
///
/// Intentionally skipped so it doesn't run in CI. Un-skip, update <see cref="ModRoot"/> if
/// needed, run manually, commit the regenerated JSONs.
/// </summary>
public class SliderCatalogRegenerator
{
    private const string ModRoot = @"S:\Temp\BS Trainer\mods";
    private const string RepoCatalogs = @"S:\Dev\SynthEBD\SynthEBD\InternalData\SliderCatalogs";

    // Each job: (registry entry name, list of mod folders to search, list of ShapeData-relative
    // paths from BodyTypeRegistry.json). Mod folders are searched in order; the first mod that
    // contains a given ShapePath wins. Multiple mod folders cover bodies whose reference OSDs
    // are split across sibling mods (e.g. COCO Body A and B).
    private static readonly (string Name, string[] ModFolders, string[] ShapePaths)[] Jobs = new[]
    {
        ("CBBE",
            new[] { @"Caliente's Beautiful Bodies Enhancer -CBBE-" },
            new[] { "CBBE/CBBE Body.osd" }),
        ("CBBE 3BA",
            new[] { @"CBBE 3BA" },
            new[] { "CBBE 3BA Reference/CBBE 3BA Ref.osd" }),
        ("Unified UNP",
            new[] { @"Legacy UUNP (for SE)" },
            new[] { "Unified UNP" }),
        ("BHUNP",
            new[] { @"Baka Haeun UNP" },
            new[] { "BHUNP 3BBB Advanced/BHUNP 3BBB Advanced Ver 4.osd", "BHUNP 3BBB Advanced/BHUNP 3BBB Advanced Ver 3.osd" }),
        ("Touched by Dibella",
            new[] { @"Touched by Dibella" },
            new[] { "TBD/Body/TouchedByDibellaRef.osd" }),
        ("COCO Body",
            new[] { @"COCO BodyV6.8 3BBB SSE Ultimate Body A", @"COCO BodyV6.8 3BBB SSE Ultimate Body B" },
            new[] { "[COCO]SMP_BODY_V6/[COCO 3BBB V6]Body_A.osd", "[COCO]SMP_BODY_V6/[COCO 3BBB V6]Body_B.osd" }),
        ("SOMBody",
            new[] { @"Shape of Maiden 3BBB (SOMBody)" },
            new[] { "[SOMResources] SOMBody Slider/[SOMResources] SOMBody Slider (Base).osd" }),
        ("HIMBO",
            new[] { @"Highly Improved Male Body Overhaul" },
            new[] { "HIMBO Ref/HIMBO Body - Vanilla.osd" }),
        ("UBE",
            new[] { @"UBE 2.0" },
            new[] { "UBE SE 2.0 Release Body/UBE SE 2.0 Release Body.osd" }),
    };

    [Fact(Skip = "Regenerates shipped slider catalogs from the developer's local BodySlide install. Un-skip and run manually when reference OSDs change.")]
    public void RegenerateShippedSliderCatalogs()
    {
        Directory.CreateDirectory(RepoCatalogs);
        var log = new StringBuilder();

        foreach (var (name, modFolders, shapePaths) in Jobs)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in shapePaths)
            {
                string? resolved = null;
                foreach (var mod in modFolders)
                {
                    var candidate = Path.Combine(ModRoot, mod, "CalienteTools", "BodySlide", "ShapeData", rel);
                    if (File.Exists(candidate) || Directory.Exists(candidate)) { resolved = candidate; break; }
                }
                Assert.True(resolved != null, $"{name}: could not locate ShapeData entry '{rel}' in any of: {string.Join(", ", modFolders)}");

                if (File.Exists(resolved))
                {
                    ExtractOsdSliders(resolved!, union);
                }
                else
                {
                    foreach (var f in Directory.EnumerateFiles(resolved!, "*.osd", SearchOption.TopDirectoryOnly))
                    {
                        ExtractOsdSliders(f, union);
                    }
                    foreach (var f in Directory.EnumerateFiles(resolved!, "*.bsd", SearchOption.TopDirectoryOnly))
                    {
                        // UUNP-style: one BSD file per slider, name is the file stem.
                        var stem = Path.GetFileNameWithoutExtension(f);
                        if (!string.IsNullOrWhiteSpace(stem)) union.Add(stem);
                    }
                }
            }

            var sorted = union.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var outPath = Path.Combine(RepoCatalogs, SanitizeFileName(name) + ".json");
            File.WriteAllText(outPath, JsonConvert.SerializeObject(sorted, Formatting.Indented));
            log.AppendLine($"{name}: {sorted.Count} sliders -> {Path.GetFileName(outPath)}");
        }

        Console.WriteLine(log.ToString());
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(c == ' ' ? '_' : c);
        return sb.ToString();
    }

    /// <summary>
    /// Reads slider names from an OSD file and unions them into <paramref name="union"/>
    /// after applying the same LCP / multi-shape normalization the runtime extractor uses
    /// (see BodyTypeSliderExtractor.AddNormalizedSliders). Vertex delta payloads are
    /// skipped -- only names matter for classification.
    /// </summary>
    private static void ExtractOsdSliders(string osdPath, HashSet<string> union)
    {
        var rawNames = new List<string>();
        using (var fs = File.OpenRead(osdPath))
        using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false))
        {
            uint magic = br.ReadUInt32();
            Assert.True(magic == 0x4F534400u, $"Bad OSD magic 0x{magic:X8} in {osdPath}");
            br.ReadUInt32(); // version
            uint count = br.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                byte nameLen = br.ReadByte();
                var nameBytes = br.ReadBytes(nameLen);
                rawNames.Add(Encoding.ASCII.GetString(nameBytes));
                ushort diffCount = br.ReadUInt16();
                br.BaseStream.Seek(diffCount * 14, SeekOrigin.Current);
            }
        }

        NormalizeAndUnion(rawNames, union);
    }

    private static void NormalizeAndUnion(List<string> rawNames, HashSet<string> union)
    {
        if (rawNames.Count == 0) return;
        string lcp = rawNames.Count == 1 ? "" : LongestCommonPrefix(rawNames);
        int minLen = rawNames.Min(n => n.Length);
        if (lcp.Length >= minLen) lcp = "";

        var afterLcp = rawNames
            .Select(n => lcp.Length > 0 && n.StartsWith(lcp, StringComparison.Ordinal)
                ? n.Substring(lcp.Length)
                : n)
            .ToList();

        bool multiShape = afterLcp.Any(s => s.Length > 0 && char.IsLower(s[0]));

        foreach (var s in afterLcp)
        {
            var canonical = s;
            if (multiShape)
            {
                int i = 0;
                while (i < canonical.Length && char.IsLower(canonical[i])) i++;
                if (i > 0) canonical = canonical.Substring(i);
            }
            if (!string.IsNullOrWhiteSpace(canonical)) union.Add(canonical);
        }
    }

    private static string LongestCommonPrefix(IList<string> names)
    {
        string prefix = names[0];
        for (int i = 1; i < names.Count; i++)
        {
            int j = 0;
            int max = Math.Min(prefix.Length, names[i].Length);
            while (j < max && prefix[j] == names[i][j]) j++;
            prefix = prefix.Substring(0, j);
            if (prefix.Length == 0) break;
        }
        return prefix;
    }
}

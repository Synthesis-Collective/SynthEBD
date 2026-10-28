using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// SynthEBD-side OSD catalog loader. Walks <see cref="PatcherState.OBodySettings"/>'s
/// BodyTypeRegistry to map a BodySlide preset's SliderGroup → list of OSD/BSD
/// files; falls back to a directory scan under
/// <c>{Data}/CalienteTools/BodySlide/ShapeData</c> when the registry has no
/// matching entry.
///
/// Extracted from <c>VM_CharacterViewer.LoadOsdFilesForGroup</c> (Phase B2c.2)
/// so the viewer no longer needs PatcherState or IEnvironmentStateProvider in
/// its constructor. The SynthEBD viewer-host extension methods consume this
/// before calling <see cref="VM_CharacterViewer.SetMorphContext"/>.
/// </summary>
public sealed class SynthEbdOsdLoader
{
    private readonly IEnvironmentStateProvider _env;
    private readonly PatcherState _patcherState;
    private readonly BsdFileParser _bsdFileParser;

    public SynthEbdOsdLoader(IEnvironmentStateProvider env, PatcherState patcherState, BsdFileParser bsdFileParser)
    {
        _env = env;
        _patcherState = patcherState;
        _bsdFileParser = bsdFileParser;
    }

    /// <summary>
    /// Returns the parsed OSD files associated with <paramref name="sliderGroup"/>,
    /// or an empty list if the ShapeData root is missing. Returns null only on
    /// invalid input. Behaviour matches the pre-extraction
    /// <c>LoadOsdFilesForGroup</c> path exactly so smoke-test deltas are zero.
    /// </summary>
    public List<OsdFile> LoadForSliderGroup(string sliderGroup)
    {
        string dataFolder = _env.DataFolderPath;
        string shapeDataRoot = Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");
        if (!Directory.Exists(shapeDataRoot))
        {
            return new List<OsdFile>();
        }

        // Primary path: look up the body type in the registry and parse the OSD/BSD files in
        // the entry's declared ShapeDataFolders. If the entry is a superset of another body
        // (e.g. CBBE 3BA ⊃ CBBE), include the parent's folders too -- a 3BA preset may move
        // CBBE-shared sliders whose deltas live only in the CBBE shape data.
        var registry = _patcherState?.OBodySettings?.BodyTypeRegistry;
        var entry = FindRegistryEntry(registry, sliderGroup);
        if (entry != null)
        {
            var folders = new List<string>();
            CollectShapeDataFolders(entry, registry, folders, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registryOsd = new List<OsdFile>();
            foreach (var rawPath in folders)
            {
                var sub = rawPath.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar)
                                 .TrimStart(Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(shapeDataRoot, sub);
                if (File.Exists(fullPath))
                {
                    var osd = string.Equals(Path.GetExtension(fullPath), ".bsd", StringComparison.OrdinalIgnoreCase)
                        ? _bsdFileParser.ParseBsdFile(fullPath)
                        : _bsdFileParser.ParseOsdFile(fullPath);
                    if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var osd in _bsdFileParser.ParseAllOsdInDirectory(fullPath, recursive: false))
                    {
                        if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                    }
                }
            }
            return registryOsd;
        }

        // Fallback (registry miss / "Unknown" preset): legacy substring scan over every direct
        // child of ShapeData, then full-tree scan if no name contained the group string.
        var matchingDirs = Directory.GetDirectories(shapeDataRoot)
            .Where(d => Path.GetFileName(d).Contains(sliderGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingDirs.Length == 0)
            matchingDirs = Directory.GetDirectories(shapeDataRoot);

        var allOsd = new List<OsdFile>();
        foreach (var dir in matchingDirs)
            allOsd.AddRange(_bsdFileParser.ParseAllOsdInDirectory(dir));

        return allOsd;
    }

    /// <summary>
    /// Parses a BodySlide source mesh's slider data. <see cref="BodyMorphFiles.Data"/> (from the slider
    /// set) is regrouped per target shape: each item picks one entry out of its .osd (by entry name) or
    /// takes its whole .bsd (one slider per file), and is filed under its target mesh shape with its
    /// preset slider name, flagged <see cref="OsdFile.SliderNamesAreCanonical"/>. That is what lets the
    /// deformer match the data to the mesh: an .osd's own shape name is its file stem (e.g. "SE 3BBB
    /// Body Amazing v2", which matches no shape "3BA"), and a .bsd's is its slider name.
    /// <see cref="BodyMorphFiles.RawPaths"/> (registry fallback, no slider set) load as-is, first per
    /// shape name winning. Missing or unparseable files are skipped. Cached per
    /// <paramref name="files"/> instance (the source-mesh cache hands out the same one each time): the
    /// result is read-only to the deformer, and a per-slider-file body (UUNP: 126 .bsd files) would
    /// otherwise re-parse on every preset switch.
    /// </summary>
    public List<OsdFile> LoadFiles(BodyMorphFiles files)
    {
        if (files == null) return new List<OsdFile>();
        return _loadedMorphFiles.GetValue(files, LoadFilesUncached);
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<BodyMorphFiles, List<OsdFile>> _loadedMorphFiles = new();

    private List<OsdFile> LoadFilesUncached(BodyMorphFiles files)
    {
        var result = new List<OsdFile>();

        var parsed = new Dictionary<string, OsdFile?>(StringComparer.OrdinalIgnoreCase);
        OsdFile? Parse(string path)
        {
            if (!parsed.TryGetValue(path, out var file))
            {
                file = !File.Exists(path) ? null
                    : string.Equals(Path.GetExtension(path), ".bsd", StringComparison.OrdinalIgnoreCase)
                        ? _bsdFileParser.ParseBsdFile(path)
                        : _bsdFileParser.ParseOsdFile(path);
                parsed[path] = file;
            }
            return file;
        }

        var byTarget = new Dictionary<string, Dictionary<string, OsdSliderData>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in files.Data ?? Array.Empty<BodyMorphData>())
        {
            var file = Parse(item.FilePath);
            if (file == null || file.Sliders.Count == 0) continue;
            bool isBsd = string.Equals(Path.GetExtension(item.FilePath), ".bsd", StringComparison.OrdinalIgnoreCase);
            var entry = isBsd ? file.Sliders[0]
                : file.Sliders.FirstOrDefault(s => string.Equals(s.Name, item.EntryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;

            if (!byTarget.TryGetValue(item.TargetShape, out var sliders))
                byTarget[item.TargetShape] = sliders = new Dictionary<string, OsdSliderData>(StringComparer.OrdinalIgnoreCase);
            sliders[item.SliderName] = new OsdSliderData { Name = item.SliderName, VertexDeltas = entry.VertexDeltas };
        }
        foreach (var (target, sliders) in byTarget)
        {
            result.Add(new OsdFile { ShapeName = target, Sliders = sliders.Values.ToList(), SliderNamesAreCanonical = true });
        }

        var seenShapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files.RawPaths ?? Array.Empty<string>())
        {
            var file = string.IsNullOrWhiteSpace(path) ? null : Parse(path);
            if (file != null && seenShapes.Add(file.ShapeName)) result.Add(file);
        }
        return result;
    }

    private static BodyTypeRegistryEntry? FindRegistryEntry(List<BodyTypeRegistryEntry>? registry, string name)
    {
        if (registry == null || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var e in registry)
        {
            if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    private static void CollectShapeDataFolders(BodyTypeRegistryEntry entry, List<BodyTypeRegistryEntry>? registry,
        List<string> folders, HashSet<string> visited)
    {
        if (entry == null || !visited.Add(entry.Name)) return;
        if (entry.ShapeDataFolders != null)
        {
            foreach (var f in entry.ShapeDataFolders)
            {
                if (!string.IsNullOrWhiteSpace(f)) folders.Add(f);
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.SupersetOfBodyType))
        {
            var parent = FindRegistryEntry(registry, entry.SupersetOfBodyType);
            if (parent != null)
                CollectShapeDataFolders(parent, registry, folders, visited);
        }
    }
}

/// <summary>The slider data for one BodySlide source mesh. <see cref="Data"/> comes from its slider set
/// (each slider's data entries); <see cref="RawPaths"/> are whole .osd/.bsd files used as-is, for a
/// mesh found without a slider set. <see cref="Sliders"/> is the set's slider metadata, which
/// <see cref="BodySlideBuildRules"/> needs to turn a preset into the values BodySlide would build with.
/// See <see cref="SynthEbdOsdLoader.LoadFiles"/>.</summary>
public sealed record BodyMorphFiles(IReadOnlyList<BodyMorphData> Data, IReadOnlyList<string> RawPaths,
    IReadOnlyList<BodySlideSliderInfo>? Sliders = null)
{
    /// <summary>Distinct files behind this data, for logging and tie-breaks.</summary>
    public IReadOnlyList<string> Paths => Data.Select(d => d.FilePath).Concat(RawPaths)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>One slider data entry of a BodySlide slider set: the entry <see cref="EntryName"/> inside
/// the .osd at <see cref="FilePath"/> (a .bsd is the entry itself), deforming mesh shape
/// <see cref="TargetShape"/> for preset slider <see cref="SliderName"/>. From the set's
/// <c>&lt;Data name=".." target=".."&gt;file.osd\entry&lt;/Data&gt;</c> (or older
/// <c>&lt;datafile&gt;file.bsd&lt;/datafile&gt;</c>) under <c>&lt;Slider name=".."&gt;</c>.</summary>
public sealed record BodyMorphData(string FilePath, string EntryName, string TargetShape, string SliderName);

/// <summary>One slider of a BodySlide slider set, as its build uses it: the default value at weight 100
/// and 0 (the <c>big</c> / <c>small</c> attributes, or <c>default</c> for both when the set doesn't
/// generate weights), and the flags that change how the value applies. <see cref="IsShapeDelta"/> is false
/// for zap (vertex-deleting), UV and clamp sliders, which are not position deltas.</summary>
public sealed record BodySlideSliderInfo(string Name, float DefaultBig, float DefaultSmall,
    bool Invert, bool Hidden, bool IsShapeDelta = true);

/// <summary>
/// The value rules a BodySlide build applies to each slider (BodySlideApp's build loop): the preset's
/// value when the preset lists the slider, otherwise the set's default -- which is how hidden sliders
/// such as 3BA's NeckSeam or BHUNP's Neck (small 100, big 0: they narrow the neck at weight 0 to meet the
/// vanilla head's weight-0 neck) always take effect, since presets never list them -- and then
/// <c>1 - value</c> for an inverted slider. A body BodySlide built has all of this baked in; a source
/// mesh deformed directly from its slider data needs it applied, or it comes out wrong (a neck gap below
/// weight 100, inverted sliders backwards).
/// </summary>
public static class BodySlideBuildRules
{
    /// <summary><paramref name="preset"/>'s morph values turned into the values BodySlide would build
    /// with, for a mesh whose slider set is described by <paramref name="sliders"/>. Sliders the set
    /// doesn't describe pass through unchanged; zap / UV / clamp sliders are dropped. Values are on the
    /// 0-100 scale <see cref="MorphSlider"/> uses.</summary>
    public static MorphSet Apply(MorphSet preset, IReadOnlyList<BodySlideSliderInfo>? sliders)
    {
        if (sliders == null || sliders.Count == 0) return preset;

        var result = new Dictionary<string, MorphSlider>(
            preset.Sliders.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var info in sliders)
        {
            if (!info.IsShapeDelta)
            {
                result.Remove(info.Name);
                continue;
            }
            var value = preset.Sliders.TryGetValue(info.Name, out var set) ? set
                : new MorphSlider(info.DefaultBig, info.DefaultSmall);
            if (info.Invert) value = new MorphSlider(100f - value.Big, 100f - value.Small);
            result[info.Name] = value;
        }
        return new MorphSet { Label = preset.Label, Sliders = result };
    }
}

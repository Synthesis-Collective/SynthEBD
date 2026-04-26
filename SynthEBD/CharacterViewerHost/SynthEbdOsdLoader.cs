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

using System.IO;
using System.Xml.Linq;

namespace SynthEBD;

/// <summary>
/// Finds, for each Body Type Registry entry, the BodySlide <b>source mesh</b> its worn body is built
/// from -- and the OSD slider data authored against that mesh -- and works out which entry the
/// installed worn body is.
///
/// <para>Used by the BodySlide Compare window to preview body types other than the installed one: the
/// viewer only ever loads one worn body per gender (<c>femalebody_1.nif</c> / <c>malebody_1.nif</c>), so
/// a pane showing another body type renders that type's source mesh instead
/// (<see cref="VM_CharacterViewer.BodyReplacement"/>), deformed by the source mesh's own OSDs. The
/// installed type keeps the real worn body, with its sibling .tri and the user's own build.</para>
///
/// <para><b>Which mesh.</b> The body a BodySlide build writes is defined by a slider set (<c>.osp</c>):
/// <c>DataFolder</c> + <c>SourceFile</c> name the mesh under <c>CalienteTools\BodySlide\ShapeData</c>, its
/// sliders' <c>Data</c> entries name the OSDs, and <c>OutputFile</c> is the body it produces. Candidates
/// are the sets in the entry's <c>.osp</c> identity fingerprints whose output is the gender's main body
/// (<c>femalebody</c> / <c>malebody</c>, or a suffixed form such as UBE's <c>femalebody_tangent</c>), sets
/// writing to the vanilla <c>character assets</c> folder first. The registry's own
/// <see cref="BodyTypeRegistryEntry.ShapeDataFolders"/> files are not used for the mesh: they are the
/// slider-catalog references, which can be a different topology altogether (CBBE 3BA's
/// <c>CBBE 3BA Ref.nif</c> is one 33945-vertex merged shape; the body it builds from is
/// <c>SE 3BBB Body Amazing v2.nif</c>). They are only the fallback when no slider set qualifies.</para>
///
/// <para><b>Where it looks.</b> The merged Data folder first (what the game sees), then -- per body type
/// not found there -- each inactive mod of the configured mod manager as a Data root of its own
/// (<see cref="ISourceResolver.GetInactiveModFolders"/>, highest priority first): alternative bodies are
/// normally installed but disabled, since they all write the same worn-body files. A body found in an
/// inactive mod takes its textures from that mod (<see cref="Entry.ModFolder"/>).</para>
///
/// <para><b>Installed match.</b> The worn body is type T when its largest shape (the torso) also exists
/// in one of T's candidate source meshes with the same vertex count. Everything is cached for the app's
/// lifetime; installing a different body needs a restart anyway for the registry scan to notice.</para>
///
/// <para><b>Textures.</b> A source mesh's NIF names its body's default textures, but in a merged data
/// folder those paths resolve to whichever body is installed -- UV-mapped for a different mesh. Under a
/// configured mod manager, <see cref="FindTextureFolder"/> returns the mod folder that ships the source
/// mesh (MO2: the highest-priority enabled mod; Vortex: the staging folder its deployment manifest names),
/// where that body's own textures (if it ships any) can be found; with no mod manager it returns null and
/// the viewer falls back to neutral beige.</para>
/// </summary>
public sealed class BodyTypeSourceMeshes
{
    /// <summary>A body mesh BodySlide builds from: absolute NIF path, its slider data, plus the NIF's
    /// data-relative path (for locating the mod that ships it).</summary>
    public sealed record SourceMesh(string SliderSetName, string NifPath, string RelativeNifPath, BodyMorphFiles Morphs,
        string? OutputKey = null);

    /// <summary>A registry entry with at least one source mesh on disk. <see cref="Mesh"/> is the one
    /// rendered; <see cref="Candidates"/> are all that qualify (checked against the worn body).
    /// <see cref="ModFolder"/> is the inactive mod it was found in; null = the merged Data folder.
    /// <see cref="PartMeshes"/> maps every game mesh path the body type's slider sets build (keyed by
    /// <see cref="BodyReplacement.PartKey"/>: hands, feet, beast hands, tails...) to its source NIF, so the
    /// viewer can swap the NPC's other body parts to match (<see cref="BodyReplacement.PartMeshes"/>).</summary>
    public sealed record Entry(string Name, Gender Gender, SourceMesh Mesh, IReadOnlyList<SourceMesh> Candidates,
        string? ModFolder = null, IReadOnlyDictionary<string, string>? PartMeshes = null);

    private const string ShapeDataRelative = @"CalienteTools\BodySlide\ShapeData";
    private const string VanillaOutputPath = @"meshes\actors\character\character assets";

    private readonly IEnvironmentStateProvider _env;
    private readonly PatcherState _patcherState;
    private readonly InstalledBodyTypeDetector _detector;
    private readonly SourceResolverProvider _sourceResolvers;
    private readonly Logger _logger;
    private readonly object _lock = new();
    private IReadOnlyList<Entry>? _available;
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>?> _surveyByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Gender, string?> _installedByGender = new();
    private readonly Dictionary<string, string?> _textureFolderByEntry = new(StringComparer.OrdinalIgnoreCase);

    public BodyTypeSourceMeshes(IEnvironmentStateProvider env, PatcherState patcherState,
        InstalledBodyTypeDetector detector, SourceResolverProvider sourceResolvers, Logger logger)
    {
        _env = env;
        _patcherState = patcherState;
        _detector = detector;
        _sourceResolvers = sourceResolvers;
        _logger = logger;
    }

    /// <summary>Registry entries (in registry order) with a source mesh on disk. Parses slider sets on
    /// first call, then cached.</summary>
    public IReadOnlyList<Entry> GetAvailable()
    {
        lock (_lock)
        {
            return _available ??= BuildAvailable();
        }
    }

    /// <summary>The available entry the installed worn body for <paramref name="gender"/> is, or null
    /// when none matches (or the worn body can't be read). Parses NIFs on first use, so call it off the
    /// UI thread.</summary>
    public string? FindInstalledBodyType(Gender gender)
    {
        var available = GetAvailable();
        lock (_lock)
        {
            if (_installedByGender.TryGetValue(gender, out var cached)) return cached;

            string? result = null;
            var worn = _detector.SurveyDefaultBodyShapeCounts(gender);
            if (worn != null && worn.Count > 0)
            {
                var torso = worn.OrderByDescending(kv => kv.Value).First();
                foreach (var entry in available.Where(e => e.Gender == gender))
                {
                    if (entry.Candidates.Any(c => Survey(c.NifPath, entry.Name) is { } counts
                            && counts.TryGetValue(torso.Key, out int n) && n == torso.Value))
                    {
                        result = entry.Name;
                        break;
                    }
                }
                _logger?.LogMessage(result != null
                    ? $"BodyTypeSourceMeshes: the installed {gender} body is {result}."
                    : $"BodyTypeSourceMeshes: the installed {gender} body (largest shape '{torso.Key}', {torso.Value} vertices) "
                      + "matches no available body type's BodySlide source mesh; every body type will render its source mesh.");
            }
            _installedByGender[gender] = result;
            return result;
        }
    }

    /// <summary>The mod folder that ships <paramref name="entry"/>'s source mesh -- where that body's own
    /// textures live, if it ships any: the inactive mod it was found in, else the deployed mod per the
    /// configured mod manager (<see cref="ISourceResolver.FindProvidingModFolder"/>). Null with no mod
    /// manager configured or when no mod supplies the file.</summary>
    public string? FindTextureFolder(Entry entry)
    {
        lock (_lock)
        {
            if (_textureFolderByEntry.TryGetValue(entry.Name, out var cached)) return cached;

            string? folder = entry.ModFolder;
            if (folder == null)
            {
                try
                {
                    folder = _sourceResolvers.GetResolver()?.FindProvidingModFolder(entry.Mesh.RelativeNifPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogMessage($"BodyTypeSourceMeshes: providing-mod lookup for {entry.Name} failed: {ex.Message}");
                }
            }
            _logger?.LogMessage($"BodyTypeSourceMeshes: {entry.Name} textures from "
                + (folder ?? "(no mod folder - neutral skin)"));
            _textureFolderByEntry[entry.Name] = folder;
            return folder;
        }
    }

    /// <summary>Whether <paramref name="entry"/>'s body ships its own skin: the diffuse its source mesh's
    /// main skin shape names exists in <see cref="FindTextureFolder"/>, by the viewer's own lookup rule
    /// (<see cref="VM_CharacterViewer.ResolveTextureUnderFolder"/>) -- so the answer predicts whether the
    /// render shows that skin or neutral beige. Many body mods ship none (they leave skins to texture
    /// mods). Parses the NIF on first use, so call it off the UI thread; cached.</summary>
    public bool HasOwnSkin(Entry entry)
    {
        lock (_lock)
        {
            if (_hasOwnSkinByEntry.TryGetValue(entry.Name, out var cached)) return cached;
        }
        var folder = FindTextureFolder(entry);
        bool has = folder != null
            && VM_CharacterViewer.ResolveTextureUnderFolder(folder, MainSkinDiffuse(entry)) != null;
        lock (_lock)
        {
            _hasOwnSkinByEntry[entry.Name] = has;
        }
        return has;
    }

    private readonly Dictionary<string, bool> _hasOwnSkinByEntry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _mainSkinDiffuseByNif = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The NIF-embedded diffuse of <paramref name="entry"/>'s source mesh's main skin shape
    /// (cached; parses the NIF on first use).</summary>
    public string? MainSkinDiffuse(Entry entry)
    {
        lock (_lock)
        {
            if (_mainSkinDiffuseByNif.TryGetValue(entry.Mesh.NifPath, out var cached)) return cached;
        }
        var diffuse = _detector.SurveyMainSkinDiffuse(entry.Mesh.NifPath);
        lock (_lock)
        {
            _mainSkinDiffuseByNif[entry.Mesh.NifPath] = diffuse;
        }
        return diffuse;
    }

    /// <summary>True when a mod manager is configured, so installed mods can be listed.</summary>
    public bool CanListMods => _sourceResolvers.GetResolver() != null;

    /// <summary>Installed mods (deployed or not, in <see cref="ISourceResolver.GetAllModFolders"/> order)
    /// that contain the skin texture <paramref name="entry"/>'s source mesh asks for, by the viewer's own
    /// lookup rule -- i.e. every mod whose textures the body would actually pick up. Whether a mod's skin
    /// fits the body's UV layout is the user's call; the file name can't tell. Reads the disk on every
    /// call (the mod list can change while the app runs) apart from the cached NIF survey; call it off
    /// the UI thread.</summary>
    public IReadOnlyList<string> FindModsWithSkin(Entry entry)
    {
        var diffuse = MainSkinDiffuse(entry);
        if (string.IsNullOrWhiteSpace(diffuse)) return Array.Empty<string>();
        try
        {
            return (_sourceResolvers.GetResolver()?.GetAllModFolders() ?? Array.Empty<string>())
                .Where(folder => VM_CharacterViewer.ResolveTextureUnderFolder(folder, diffuse) != null)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogMessage($"BodyTypeSourceMeshes: could not search mods for {entry.Name}'s skin: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>The installed mod folder named <paramref name="modName"/> (case-insensitive), or null.</summary>
    public string? FindModFolderByName(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName)) return null;
        try
        {
            return _sourceResolvers.GetResolver()?.GetAllModFolders()
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), modName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger?.LogMessage($"BodyTypeSourceMeshes: could not look up mod '{modName}': {ex.Message}");
            return null;
        }
    }

    private IReadOnlyDictionary<string, int>? Survey(string nifPath, string entryName)
    {
        if (!_surveyByPath.TryGetValue(nifPath, out var counts))
            _surveyByPath[nifPath] = counts = _detector.SurveyShapeCounts(nifPath, $"'{entryName}' source mesh");
        return counts;
    }

    private List<Entry> BuildAvailable()
    {
        var result = new List<Entry>();
        var registry = _patcherState?.OBodySettings?.BodyTypeRegistry;
        if (registry == null) return result;

        // Data roots in search order: the merged Data folder (ModFolder null), then inactive mods.
        var roots = new List<(string Root, string? ModFolder)>();
        string? dataFolder = _env?.DataFolderPath;
        if (!string.IsNullOrWhiteSpace(dataFolder)) roots.Add((dataFolder, null));
        IReadOnlyList<string> inactive = Array.Empty<string>();
        try
        {
            inactive = _sourceResolvers.GetResolver()?.GetInactiveModFolders() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger?.LogMessage("BodyTypeSourceMeshes: could not list inactive mods: " + ex.Message);
        }
        // Only mods that ship BodySlide data can hold a body; skips the (often hundreds of) others cheaply.
        roots.AddRange(inactive.Where(m => Directory.Exists(Path.Combine(m, @"CalienteTools\BodySlide"))).Select(m => (m, (string?)m)));

        foreach (var entry in registry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
            foreach (var (root, modFolder) in roots)
            {
                string shapeDataRoot = Path.Combine(root, ShapeDataRelative);
                if (!Directory.Exists(shapeDataRoot)) continue;

                var candidates = FindSliderSetMeshes(root, shapeDataRoot, entry);
                if (candidates.Count == 0 && FindRegistryShapeDataMesh(shapeDataRoot, entry) is { } fallback)
                    candidates.Add(fallback);
                if (candidates.Count == 0) continue;

                var parts = CollectPartMeshes(root, dataFolder, entry, candidates[0]);
                result.Add(new Entry(entry.Name, entry.Gender, candidates[0], candidates, modFolder, parts));
                _logger?.LogMessage($"BodyTypeSourceMeshes: {entry.Name} -> '{candidates[0].SliderSetName}' ({candidates[0].NifPath}; "
                    + $"{candidates[0].Morphs.Data.Count} slider data entries in {candidates[0].Morphs.Paths.Count} file(s); {candidates.Count} candidate set(s); "
                    + $"{parts.Count} part mesh(es) [{string.Join(", ", parts.Keys.Select(k => Path.GetFileName(k)).Distinct())}]"
                    + (modFolder != null ? $"; from inactive mod '{Path.GetFileName(modFolder)}'" : "") + ").");
                break;
            }
        }
        return result;
    }

    /// <summary>Slider sets in <paramref name="entry"/>'s slider-set fingerprints (<c>.osp</c>, or the older
    /// <c>.xml</c> under SliderSets) that build its gender's main body and whose source NIF exists;
    /// vanilla-output-path sets first, otherwise in file order. Reads both formats: current sets name
    /// their folder <c>DataFolder</c> and each slider's data <c>&lt;Data name target&gt;file.osd\entry</c>;
    /// older ones (UUNP) use <c>SetFolder</c> and <c>&lt;datafile name target&gt;slider.bsd</c>.</summary>
    private List<SourceMesh> FindSliderSetMeshes(string dataFolder, string shapeDataRoot, BodyTypeRegistryEntry entry)
    {
        string bodyName = entry.Gender == Gender.Female ? "femalebody" : "malebody";
        var found = new List<(SourceMesh Mesh, bool Vanilla)>();

        foreach (var fingerprint in entry.IdentityFingerprints ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) continue;
            var relative = NormalizeRelative(fingerprint);
            bool isSliderSet = relative.EndsWith(".osp", StringComparison.OrdinalIgnoreCase)
                || (relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && relative.Contains(@"\SliderSets\", StringComparison.OrdinalIgnoreCase));
            if (!isSliderSet) continue;
            var ospPath = Path.Combine(dataFolder, relative);
            if (!File.Exists(ospPath)) continue;

            XDocument doc;
            try { doc = XDocument.Load(ospPath); }
            catch (Exception ex)
            {
                _logger?.LogMessage($"BodyTypeSourceMeshes: could not parse slider set file '{ospPath}': {ex.Message}");
                continue;
            }

            foreach (var set in doc.Descendants("SliderSet"))
            {
                string outputFile = set.Element("OutputFile")?.Value.Trim() ?? "";
                if (!string.Equals(outputFile, bodyName, StringComparison.OrdinalIgnoreCase)
                    && !outputFile.StartsWith(bodyName + "_", StringComparison.OrdinalIgnoreCase)) continue;

                string setFolder = NormalizeRelative((set.Element("DataFolder") ?? set.Element("SetFolder"))?.Value.Trim() ?? "");
                string sourceFile = set.Element("SourceFile")?.Value.Trim() ?? "";
                if (sourceFile.Length == 0) continue;
                string relativeNif = Path.Combine(ShapeDataRelative, setFolder, sourceFile);
                string nifPath = Path.Combine(dataFolder, relativeNif);
                if (!File.Exists(nifPath)) continue;

                var data = ReadSliderData(set, Path.Combine(shapeDataRoot, setFolder));
                var raw = new List<string>();
                var siblingOsd = Path.ChangeExtension(nifPath, ".osd");
                if (data.Count == 0 && File.Exists(siblingOsd)) raw.Add(siblingOsd);

                string outputPath = NormalizeRelative(set.Element("OutputPath")?.Value.Trim() ?? "");
                bool vanilla = string.Equals(outputPath, VanillaOutputPath, StringComparison.OrdinalIgnoreCase);
                found.Add((new SourceMesh(set.Attribute("name")?.Value ?? sourceFile, nifPath, relativeNif,
                    new BodyMorphFiles(data, raw, ReadSliderInfo(set)), BodyReplacement.PartKey(Path.Combine(outputPath, outputFile))), vanilla));
            }
        }

        // Ties (several vanilla-path sets, e.g. BHUNP Ver 3 and Ver 4) go to the registry's own ShapeData
        // order where a set's OSD is one it lists, so the entry's author picks the default variant.
        var registryOsds = (entry.ShapeDataFolders ?? new List<string>())
            .Select(f => Path.GetFileName(NormalizeRelative(f)))
            .ToList();
        int RegistryRank(SourceMesh m)
        {
            int best = int.MaxValue;
            foreach (var osd in m.Morphs.Paths)
            {
                int i = registryOsds.FindIndex(r => string.Equals(r, Path.GetFileName(osd), StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i < best) best = i;
            }
            return best;
        }

        return found.OrderByDescending(f => f.Vanilla).ThenBy(f => RegistryRank(f.Mesh)).Select(f => f.Mesh).ToList();
    }

    /// <summary>
    /// Every mesh the body type's slider sets build -> its source NIF: each set in the entry's slider-set
    /// files with an output path, output file and source file, keyed by
    /// <see cref="BodyReplacement.PartKey"/> of <c>OutputPath\OutputFile</c> (so it matches the NPC's
    /// resolved mesh paths). The source NIF is looked up under <paramref name="root"/>, then under the
    /// merged Data folder: hands and feet often come from a parent body's ShapeData (CBBE 3BA's hands set
    /// uses the CBBE folder). First set per key wins, except the chosen body mesh, which always owns its key
    /// (BHUNP Ver 3 and Ver 4 both write femalebody). Hands and feet are not morphed -- the viewer only
    /// deforms the body -- so only the mesh is taken.
    /// </summary>
    private Dictionary<string, string> CollectPartMeshes(string root, string? dataFolder, BodyTypeRegistryEntry entry, SourceMesh body)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fingerprint in entry.IdentityFingerprints ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) continue;
            var relative = NormalizeRelative(fingerprint);
            bool isSliderSet = relative.EndsWith(".osp", StringComparison.OrdinalIgnoreCase)
                || (relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && relative.Contains(@"\SliderSets\", StringComparison.OrdinalIgnoreCase));
            if (!isSliderSet) continue;
            var setFile = Path.Combine(root, relative);
            if (!File.Exists(setFile)) continue;

            XDocument doc;
            try { doc = XDocument.Load(setFile); }
            catch { continue; } // already reported by FindSliderSetMeshes

            foreach (var set in doc.Descendants("SliderSet"))
            {
                string outputPath = NormalizeRelative(set.Element("OutputPath")?.Value.Trim() ?? "");
                string outputFile = set.Element("OutputFile")?.Value.Trim() ?? "";
                string sourceFile = set.Element("SourceFile")?.Value.Trim() ?? "";
                if (outputPath.Length == 0 || outputFile.Length == 0 || sourceFile.Length == 0) continue;
                string setFolder = NormalizeRelative((set.Element("DataFolder") ?? set.Element("SetFolder"))?.Value.Trim() ?? "");

                string relativeNif = Path.Combine(ShapeDataRelative, setFolder, sourceFile);
                string? nif = new[] { root, dataFolder }
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => Path.Combine(r!, relativeNif))
                    .FirstOrDefault(File.Exists);
                if (nif != null) parts.TryAdd(BodyReplacement.PartKey(Path.Combine(outputPath, outputFile)), nif);
            }
        }
        if (body.OutputKey != null) parts[body.OutputKey] = body.NifPath;
        return parts;
    }

    /// <summary>Fallback when no slider set qualifies: the .nif beside a registry ShapeData .osd (or a
    /// listed folder's first .nif), deformed by that .osd.</summary>
    private static SourceMesh? FindRegistryShapeDataMesh(string shapeDataRoot, BodyTypeRegistryEntry entry)
    {
        foreach (var rawPath in entry.ShapeDataFolders ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var sub = NormalizeRelative(rawPath);
            var fullPath = Path.Combine(shapeDataRoot, sub);

            string? nif = null;
            if (File.Exists(fullPath))
            {
                var sibling = Path.ChangeExtension(fullPath, ".nif");
                if (File.Exists(sibling)) nif = sibling;
            }
            else if (Directory.Exists(fullPath))
            {
                var nifs = Directory.GetFiles(fullPath, "*.nif").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                nif = nifs.FirstOrDefault(n => File.Exists(Path.ChangeExtension(n, ".osd"))) ?? nifs.FirstOrDefault();
            }
            if (nif == null) continue;

            var osd = Path.ChangeExtension(nif, ".osd");
            var relative = Path.Combine(ShapeDataRelative, Path.GetRelativePath(shapeDataRoot, nif));
            return new SourceMesh("(registry ShapeData)", nif, relative, new BodyMorphFiles(
                Array.Empty<BodyMorphData>(), File.Exists(osd) ? new[] { osd } : Array.Empty<string>()));
        }
        return null;
    }

    /// <summary>Every slider data entry of <paramref name="set"/> whose file exists under
    /// <paramref name="setFolder"/>: one per (slider, target shape). A <c>.bsd</c> value is the file
    /// itself; an <c>.osd</c> value is <c>file.osd\entry</c> (entry defaulting to the data's name).</summary>
    internal static List<BodyMorphData> ReadSliderData(XElement set, string setFolder)
    {
        var result = new List<BodyMorphData>();
        foreach (var slider in set.Elements("Slider"))
        {
            string sliderName = slider.Attribute("name")?.Value ?? "";
            if (sliderName.Length == 0) continue;
            foreach (var data in slider.Elements().Where(e =>
                         string.Equals(e.Name.LocalName, "Data", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.Name.LocalName, "datafile", StringComparison.OrdinalIgnoreCase)))
            {
                string target = data.Attribute("target")?.Value ?? "";
                string value = NormalizeRelative(data.Value);
                if (target.Length == 0 || value.Length == 0) continue;

                string file, entryName;
                if (value.EndsWith(".bsd", StringComparison.OrdinalIgnoreCase))
                {
                    file = value;
                    entryName = data.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(value);
                }
                else
                {
                    int split = value.LastIndexOf('\\');
                    file = split > 0 ? value.Substring(0, split) : value;
                    entryName = split > 0 ? value.Substring(split + 1) : data.Attribute("name")?.Value ?? "";
                    if (!file.EndsWith(".osd", StringComparison.OrdinalIgnoreCase) || entryName.Length == 0) continue;
                }

                var path = Path.Combine(setFolder, file);
                if (File.Exists(path)) result.Add(new BodyMorphData(path, entryName, target, sliderName));
            }
        }
        return result;
    }

    /// <summary>Each slider's build metadata (<see cref="BodySlideSliderInfo"/>), read as BodySlide does
    /// (SliderData::LoadSliderData): <c>big</c> / <c>small</c> defaults when the set's OutputFile has
    /// <c>GenWeights="true"</c>, else <c>default</c> for both; <c>invert</c>, <c>hidden</c>, and
    /// <c>zap</c> / <c>uv</c> / <c>clamp</c> marking sliders that are not position deltas.</summary>
    internal static List<BodySlideSliderInfo> ReadSliderInfo(XElement set)
    {
        static bool Flag(XElement e, string name) => string.Equals(e.Attribute(name)?.Value, "true", StringComparison.OrdinalIgnoreCase);
        static float Num(XElement e, string name) => float.TryParse(e.Attribute(name)?.Value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;

        bool genWeights = string.Equals(set.Element("OutputFile")?.Attribute("GenWeights")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var result = new List<BodySlideSliderInfo>();
        foreach (var slider in set.Elements("Slider"))
        {
            string name = slider.Attribute("name")?.Value ?? "";
            if (name.Length == 0) continue;
            float big = genWeights ? Num(slider, "big") : Num(slider, "default");
            float small = genWeights ? Num(slider, "small") : big;
            result.Add(new BodySlideSliderInfo(name, big, small, Flag(slider, "invert"), Flag(slider, "hidden"),
                IsShapeDelta: !Flag(slider, "zap") && !Flag(slider, "uv") && !Flag(slider, "clamp")));
        }
        return result;
    }

    private static string NormalizeRelative(string path)
        => path.Replace('/', '\\').Trim().TrimStart('\\').TrimEnd('\\');
}

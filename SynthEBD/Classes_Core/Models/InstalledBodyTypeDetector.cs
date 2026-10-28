using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Loads the player's installed default body mesh from disk (no rendering) and reports its
/// per-shape vertex counts, so the startup auto-selector can match it against the captured
/// <see cref="TopologyFingerprint"/> of each <see cref="BodyTypeProfile"/>.
///
/// The vanilla body path (<c>meshes\actors\character\character assets\{female|male}body_1.nif</c>)
/// is resolved loose-first via <see cref="GameAssetResolver"/>, so whatever body mod the user
/// installed (CBBE / CBBE 3BA / BHUNP / HIMBO / …) — which overwrites that path — is what we read.
/// Best-effort: every failure mode (unresolvable path, unparseable NIF) logs and returns null
/// rather than throwing, so it is safe to call from a non-blocking startup task.
/// </summary>
public class InstalledBodyTypeDetector
{
    private readonly GameAssetResolver _assetResolver;
    private readonly CharacterPreviewCache _previewCache;
    private readonly Logger _logger;

    public InstalledBodyTypeDetector(GameAssetResolver assetResolver, CharacterPreviewCache previewCache, Logger logger)
    {
        _assetResolver = assetResolver;
        _previewCache = previewCache;
        _logger = logger;
    }

    /// <summary>
    /// Surveys the installed default body NIF for <paramref name="gender"/> and returns its
    /// per-shape vertex counts (shape name -> vertex count, case-insensitive), or null when the
    /// mesh can't be resolved or parsed. The returned dictionary's keys/comparer line up with
    /// <see cref="TopologyFingerprint.ShapeVertexCounts"/>, so it can be fed straight into
    /// <see cref="BodySlideMeasurementEvaluator.FindMatchingProfile(IEnumerable{BodyTypeProfile}, IReadOnlyDictionary{string, int}, string)"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int>? SurveyDefaultBodyShapeCounts(Gender gender)
    {
        // Game-relative path WITH the meshes\ prefix that GameAssetResolver expects. The _1
        // (weight-100) body shares topology with _0, so either yields identical vertex counts.
        string relativeGamePath = gender == Gender.Female
            ? @"meshes\actors\character\character assets\femalebody_1.nif"
            : @"meshes\actors\character\character assets\malebody_1.nif";

        string? diskPath;
        try
        {
            diskPath = _assetResolver.ResolveAssetPath(relativeGamePath);
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"InstalledBodyTypeDetector: failed to resolve {gender} default body '{relativeGamePath}': {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(diskPath))
        {
            _logger.LogMessage($"InstalledBodyTypeDetector: {gender} default body '{relativeGamePath}' not found in loose files or any BSA.");
            return null;
        }

        return SurveyShapeCounts(diskPath, $"{gender} default body");
    }

    /// <summary>The NIF-embedded diffuse (slot 0) of the main skin shape at <paramref name="diskPath"/> --
    /// the largest <c>ShaderType 5</c> (skin-tint) shape -- or null when there is none or the NIF can't
    /// be read. What the viewer's skin texture for that body hinges on.</summary>
    public string? SurveyMainSkinDiffuse(string diskPath)
    {
        try
        {
            var survey = _previewCache.MeshBuilder.SurveyNif(diskPath);
            if (!survey.LoadOk) return null;
            return survey.Shapes.Where(s => s.ShaderType == 5)
                .OrderByDescending(s => s.VertexCount)
                .FirstOrDefault()?.BakedDiffusePath;
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"InstalledBodyTypeDetector: failed to survey skin textures of '{diskPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Per-shape vertex counts of the NIF at <paramref name="diskPath"/> (shape name -> vertex count,
    /// case-insensitive), or null when it can't be parsed. <paramref name="description"/> names the
    /// mesh in log lines. Used for the installed default body above and, by the BodySlide Compare
    /// window, for body types' BodySlide source meshes.
    /// </summary>
    public IReadOnlyDictionary<string, int>? SurveyShapeCounts(string diskPath, string description)
    {
        NifMeshBuilder.NifSurveyResult survey;
        try
        {
            survey = _previewCache.MeshBuilder.SurveyNif(diskPath);
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"InstalledBodyTypeDetector: failed to survey {description} '{diskPath}': {ex.Message}");
            return null;
        }

        if (!survey.LoadOk)
        {
            _logger.LogMessage($"InstalledBodyTypeDetector: could not parse {description} '{diskPath}': {survey.Error}");
            return null;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var shape in survey.Shapes)
        {
            if (string.IsNullOrEmpty(shape.ShapeName)) continue;
            // Sum rather than overwrite in the unlikely event two shapes share a name.
            counts.TryGetValue(shape.ShapeName, out int existing);
            counts[shape.ShapeName] = existing + shape.VertexCount;
        }

        int total = 0;
        foreach (var v in counts.Values) total += v;
        string breakdown = string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"));
        _logger.LogMessage($"InstalledBodyTypeDetector: surveyed {description} '{diskPath}' — {counts.Count} shape(s) [{breakdown}], {total} vertices total.");
        return counts;
    }
}

using System.Diagnostics;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CharacterViewer.Rendering.Offscreen;

namespace SynthEBD;

/// <summary>
/// Offscreen thumbnail renderer shared by the Body Type Profile editor's image-grid windows (Show
/// Spread and the annotation queue's Panel): renders (preset, weight, view angle, measurement
/// overlay) images of the editor's preview NPC and caches them for the owning window's lifetime.
///
/// <para><b>Rendering.</b> Uses the app's single shared <see cref="IOffscreenRenderer"/> (a FIFO queue
/// on its own GL thread) rather than live viewers — the Compare window already warns against more
/// concurrent GL contexts. The pump feeds it one request at a time, always choosing the first
/// still-missing image in the owner's <c>wantedKeys</c> order, so a change of metric, view or page
/// re-prioritizes immediately instead of waiting behind a backlog. After every image the owner's
/// <c>imageReady</c> callback runs so it can push images into its cells.</para>
///
/// <para><b>Camera.</b> Every image uses one fixed <see cref="CameraFraming.OrbitState"/> framing the
/// whole body. Auto-framing (<see cref="CameraFraming.MeshAware"/>) would fit each body's own bounds,
/// scaling a large preset down and a small one up — hiding exactly the size differences being judged.</para>
/// </summary>
internal sealed class SpreadThumbnailRenderer : IDisposable
{
    /// <summary>Azimuths in the orbit camera's convention: 180 faces the character's front
    /// (Skyrim characters face -Z), 0 its back, 90 its side.</summary>
    internal const float FrontAzimuth = 180f;
    internal const float BackAzimuth = 0f;
    internal const float SideAzimuth = 90f;

    /// <summary>Whole-body framing at model scale 1: the body spans roughly Y 0..128 (the head sits at
    /// Y 120), so the camera orbits the midpoint and backs off far enough for the orbit camera's 25°
    /// vertical FOV to fit ~140 units of height (70 / tan 12.5° ≈ 316).</summary>
    private const float CameraTargetY = 64f;
    private const float CameraDistance = 320f;

    /// <summary>Render size per image; 5:9 portrait to suit a standing body. The view scales it to the cell.</summary>
    internal const int ImageWidth = 240;
    internal const int ImageHeight = 432;

    /// <summary>Overlay is the measurement-line set drawn on the image ("" = none; see
    /// <see cref="JoinOverlay"/>), so toggling Show Measurements, or switching metric with it on,
    /// caches a separate image.</summary>
    internal readonly record struct RenderKey(string PresetLabel, int Weight, float Azimuth, string Overlay);

    private readonly Logger _logger;
    private readonly string _logName;
    private readonly SceneInputsSnapshot _scene;
    private readonly VM_CharacterViewer _renderSettingsSource;
    private readonly Func<string, BodySlideSetting?> _presetLookup;
    private readonly Func<IEnumerable<RenderKey>> _wantedKeys;
    private readonly Action _imageReady;
    private readonly IReadOnlyDictionary<string, NamedKeyVertex> _keyVertsByName;
    private readonly IReadOnlyDictionary<string, RegionVolumeEvaluator.ResolvedRegion>? _resolvedRegions;
    private readonly IReadOnlyDictionary<string, VM_BodyTypeProfile.MeasurementLineSpec> _lineSpecs;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _regionNamesByMeasurement;
    private readonly Dictionary<RenderKey, BitmapSource?> _images = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;
    private bool _loggedFirstRender;
    private bool _loggedFirstOverlay;

    /// <param name="logName">Window name prefixed to log lines ("Show Spread").</param>
    /// <param name="wantedKeys">Every image the owner wants, highest priority first. Re-read after
    /// every render; duplicates are fine.</param>
    /// <param name="imageReady">Runs on the UI thread after each image lands in the cache.</param>
    internal SpreadThumbnailRenderer(
        VM_BodyTypeProfile profile,
        SceneInputsSnapshot scene,
        VM_CharacterViewer renderSettingsSource,
        Func<string, BodySlideSetting?> presetLookup,
        Logger logger,
        string logName,
        Func<IEnumerable<RenderKey>> wantedKeys,
        Action imageReady)
    {
        _scene = scene;
        _renderSettingsSource = renderSettingsSource;
        _presetLookup = presetLookup;
        _logger = logger;
        _logName = logName;
        _wantedKeys = wantedKeys;
        _imageReady = imageReady;
        (_keyVertsByName, _resolvedRegions, _lineSpecs, _regionNamesByMeasurement) = profile.SnapshotMeasurementOverlayInputs(renderSettingsSource);
    }

    /// <summary>Cache key string for an overlay drawing <paramref name="measurementNames"/>' lines.</summary>
    internal static string JoinOverlay(IEnumerable<string> measurementNames) => string.Join('\u001f', measurementNames);

    /// <summary>The cached image for a key: Ready is false while it has not rendered yet; a ready
    /// null image means the render failed.</summary>
    internal (bool Ready, BitmapSource? Image) Lookup(string presetLabel, int weight, float azimuth, string overlay)
        => _images.TryGetValue(new RenderKey(presetLabel, weight, azimuth, overlay), out var img) ? (true, img) : (false, null);

    /// <summary>"Rendering done / wanted..." over <paramref name="keys"/>, or "" when all are cached.</summary>
    internal string DescribeProgress(IEnumerable<RenderKey> keys)
    {
        var wanted = keys.Distinct().ToList();
        int done = wanted.Count(k => _images.ContainsKey(k));
        return done < wanted.Count ? $"Rendering {done} / {wanted.Count}..." : "";
    }

    /// <summary>Renders missing images one at a time on the UI thread's async flow (the renderer does
    /// the GL work on its own thread). Re-reads the wanted list after every image, so the owner's
    /// current view always renders first. Re-entrant calls while running are no-ops.</summary>
    internal async void Pump()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                RenderKey? next = null;
                foreach (var k in _wantedKeys())
                {
                    if (!_images.ContainsKey(k)) { next = k; break; }
                }
                if (next == null) break;

                BitmapSource? image = null;
                try
                {
                    image = await RenderAsync(next.Value);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"{_logName}: render of '{next.Value.PresetLabel}' W{next.Value.Weight} failed: "
                        + ExceptionLogger.GetExceptionStack(ex));
                }
                if (_cts.IsCancellationRequested) break;
                _images[next.Value] = image;
                _imageReady();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"{_logName}: render queue stopped: " + ExceptionLogger.GetExceptionStack(ex));
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task<BitmapSource?> RenderAsync(RenderKey key)
    {
        var preset = _presetLookup(key.PresetLabel);
        if (preset == null)
        {
            _logger?.LogMessage($"{_logName}: preset '{key.PresetLabel}' is no longer in the BodySlide list; skipping its render.");
            return null;
        }

        var vm = _renderSettingsSource;
        var overlayDiag = new System.Runtime.CompilerServices.StrongBox<string?>();
        var request = new OffscreenRenderRequest
        {
            MeshPaths = _scene.MeshPaths,
            OverrideHeadMeshAbsolutePath = _scene.OverrideHeadMeshAbsolutePath,
            TextureOverrides = _scene.TextureOverrides,
            MeshOverrides = _scene.MeshOverrides,
            Morphs = SynthEbdViewerHostState.ToMorphSet(preset),
            MorphWeight = key.Weight,
            Width = ImageWidth,
            Height = ImageHeight,
            Lighting = _scene.Lighting,
            Colors = _scene.Colors,
            BackgroundRgb = _scene.BackgroundRgb,
            Camera = new CameraFraming.OrbitState(CameraDistance, key.Azimuth, 0f, 0f, CameraTargetY, 0f),
            Cancellation = _cts.Token,
            AdditionalScopes = _scene.AdditionalScopes,
            AdditionalDataFolders = _scene.AdditionalDataFolders,
            VanillaLooseOverridesBsa = _scene.VanillaLooseOverridesBsa,
            VanillaLooseOverridesModLoose = _scene.VanillaLooseOverridesModLoose,
            AllowLoadOrderFallback = _scene.AllowLoadOrderFallback,
            // Same render-quality state as the editor's live viewer, as the software fallback does.
            RenderMissingTextureAsWireframe = vm.RenderMissingTextureAsWireframe,
            EnableToneMapping = vm.EnableToneMapping,
            EnableShadows = vm.EnableShadows,
            EnableAmbientOcclusion = vm.EnableAmbientOcclusion,
            SsaoRadius = vm.SsaoRadius,
            SsaoBias = vm.SsaoBias,
            SsaoIntensity = vm.SsaoIntensity,
            SsaoThickness = vm.SsaoThickness,
            SsaoHairGap = vm.SsaoHairGap,
            EnableEyeCatchlight = vm.EnableEyeCatchlight,
            SubsurfaceStrength = vm.SubsurfaceStrength,
            SkinSaturationBoost = vm.SkinSaturationBoost,
            VignetteRadius = vm.VignetteRadius,
            VignetteIntensity = vm.VignetteIntensity,
            Exposure = vm.Exposure,
            TonemapHairRelief = vm.TonemapHairRelief,
            HairAlbedoCompensate = vm.HairAlbedoCompensate,
            DaylightBoost = vm.DaylightBoost,
            DaylightBoostIntensity = vm.DaylightBoostIntensity,
            EnableBloom = vm.EnableBloom,
            BloomIntensity = vm.BloomIntensity,
            MissingMeshPathsOut = new List<string>(),
            BeforeDraw = key.Overlay.Length == 0 ? null : BuildMeasurementOverlayHook(key.Overlay.Split('\u001f'), overlayDiag),
        };

        var sw = Stopwatch.StartNew();
        byte[] bgra = await FallbackPreviewControllerRegistry.SharedRenderer.RenderToBgra32Async(request);
        sw.Stop();
        if (key.Overlay.Length > 0 && !_loggedFirstOverlay)
        {
            _loggedFirstOverlay = true;
            _logger?.LogMessage($"{_logName}: first measurement overlay ('{key.PresetLabel}' W{key.Weight}): "
                + (overlayDiag.Value ?? "hook never ran"));
        }
        if (!_loggedFirstRender)
        {
            _loggedFirstRender = true;
            _logger?.LogMessage($"{_logName}: first render took {sw.ElapsedMilliseconds} ms "
                + $"({ImageWidth}x{ImageHeight}, {request.MissingMeshPathsOut!.Count} missing mesh(es)).");
        }

        int stride = ImageWidth * 4;
        if (bgra == null || bgra.Length < stride * ImageHeight) return null;
        var bitmap = BitmapSource.Create(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Builds the offscreen <see cref="OffscreenRenderRequest.BeforeDraw"/> hook that draws
    /// <paramref name="measurementNames"/>' lines on the render's own deformed mesh. Key vertices
    /// resolve through <see cref="MeasurementMath.TryResolveKeyVertex"/> -- the same resolution the
    /// scan used for this preset -- rather than the live preview's cached indices, which belong to a
    /// different body. Runs on the render thread, so it only reads the immutable snapshots taken at
    /// open time. RegionVolume measurements have no line geometry; they, and measurements whose key
    /// vertices come from a Region, instead tint that region's surface translucent cyan
    /// (<see cref="VM_BodyTypeProfile.AppendMeasurementRegionTint"/>).
    /// <para><paramref name="diag"/> receives a one-line summary (specs found, each vertex ref's
    /// resolution, segment count, region tints, loaded shapes -- or the exception) for the caller to
    /// log on the UI thread, so an overlay that renders nothing says why.</para></summary>
    private Action<VM_CharacterViewer> BuildMeasurementOverlayHook(IReadOnlyList<string> measurementNames,
        System.Runtime.CompilerServices.StrongBox<string?> diag)
    {
        var keyVerts = _keyVertsByName;
        var regions = _resolvedRegions;
        var specs = measurementNames
            .Where(n => _lineSpecs.ContainsKey(n))
            .Select(n => _lineSpecs[n])
            .ToList();
        var regionNames = measurementNames
            .SelectMany(n => _regionNamesByMeasurement.TryGetValue(n, out var r) ? r : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return vm =>
        {
            var refResults = new List<string>();
            try
            {
                OpenTK.Mathematics.Vector3? Resolve(string refName)
                {
                    OpenTK.Mathematics.Vector3? r = MeasurementMath.TryResolveKeyVertex(refName, keyVerts,
                        (shape, idx) => vm.TryGetCurrentVertex(shape, idx, out var p) ? p : null,
                        shape => vm.GetShapePositions(shape),
                        shape => vm.GetShapeBoneInfo(shape),
                        regions,
                        shape => vm.GetZeroedShapePositions(shape, 0),
                        out var pos)
                        ? pos
                        : null;
                    if (!string.IsNullOrEmpty(refName))
                        refResults.Add(refName + (r.HasValue ? "=ok" : "=UNRESOLVED"));
                    return r;
                }

                var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color, string? Label)>();
                foreach (var spec in specs)
                {
                    VM_BodyTypeProfile.AppendMeasurementLineSegments(
                        spec, "", Resolve, (_, _, _) => "", segments);
                }
                vm.SetMeasurementLines(segments);

                var tint = new List<float>();
                var tintResults = new List<string>();
                foreach (var regionName in regionNames)
                {
                    tintResults.Add(regionName
                        + (VM_BodyTypeProfile.AppendMeasurementRegionTint(vm, regions, regionName, tint) ? "=ok" : "=UNRESOLVED"));
                }
                vm.SetMeasurementRegionTint(tint);

                diag.Value = $"{specs.Count}/{measurementNames.Count} measurement(s) have line specs; "
                    + $"refs [{string.Join(", ", refResults.Distinct())}]; {segments.Count} segment(s); "
                    + $"regions [{string.Join(", ", tintResults)}]; "
                    + $"shapes [{string.Join(", ", vm.GetCurrentShapeVertexCounts().Keys)}]; "
                    + DescribeLineGlState(segments.Count > 0 ? segments[0].A : null);
            }
            catch (Exception ex)
            {
                diag.Value = $"hook threw after refs [{string.Join(", ", refResults)}]: {ex.GetType().Name}: {ex.Message}";
                throw;
            }
        };
    }

    /// <summary>Diagnostic: the offscreen context's line-relevant GL state (profile / flags, the
    /// aliased line-width range, whether the renderer's 4.5 px line width is accepted), plus the
    /// first segment's endpoint so it can be sanity-checked against the mesh. Render thread only.</summary>
    private static string DescribeLineGlState(OpenTK.Mathematics.Vector3? firstPoint)
    {
        while (OpenTK.Graphics.OpenGL4.GL.GetError() != OpenTK.Graphics.OpenGL4.ErrorCode.NoError) { } // drain stale errors
        OpenTK.Graphics.OpenGL4.GL.GetInteger(OpenTK.Graphics.OpenGL4.GetPName.ContextFlags, out int flags);
        OpenTK.Graphics.OpenGL4.GL.GetInteger((OpenTK.Graphics.OpenGL4.GetPName)0x9126 /* GL_CONTEXT_PROFILE_MASK */, out int profile);
        var range = new float[2];
        OpenTK.Graphics.OpenGL4.GL.GetFloat(OpenTK.Graphics.OpenGL4.GetPName.AliasedLineWidthRange, range);
        OpenTK.Graphics.OpenGL4.GL.LineWidth(4.5f);
        var lineWidthError = OpenTK.Graphics.OpenGL4.GL.GetError();
        OpenTK.Graphics.OpenGL4.GL.LineWidth(1f);
        return $"GL flags=0x{flags:X} profileMask=0x{profile:X} aliasedLineWidth=[{range[0]}, {range[1]}] "
            + $"LineWidth(4.5)->{lineWidthError}; firstPoint={firstPoint?.ToString() ?? "none"}";
    }

    /// <summary>Cancels the in-flight render and stops the pump. Cached images are dropped with the owner.</summary>
    public void Dispose() => _cts.Cancel();
}

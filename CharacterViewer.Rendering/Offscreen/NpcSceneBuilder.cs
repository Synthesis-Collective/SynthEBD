using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenTK.Mathematics;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// The NPC <see cref="IOffscreenSceneBuilder"/>: builds a <see cref="VM_CharacterViewer"/> per
/// render from the request's mesh paths, applies texture and mesh overrides and the host's
/// <see cref="NpcRenderRequest.BeforeDraw"/> hook (where a host deforms the body shapes), and frames the camera from the request's
/// <see cref="CameraFraming"/>.
/// </summary>
public sealed class NpcSceneBuilder : IOffscreenSceneBuilder
{
    private readonly CharacterPreviewCache _previewCache;
    private readonly GameAssetResolver _assets;
    private readonly ICharacterViewerSettings _settings;
    private readonly CharacterViewerLogGate _logGate;
    private readonly ICharacterViewerLogger _logger;

    public NpcSceneBuilder(
        CharacterPreviewCache previewCache,
        GameAssetResolver assets,
        ICharacterViewerSettings settings,
        CharacterViewerLogGate logGate,
        ICharacterViewerLogger logger)
    {
        _previewCache = previewCache;
        _assets = assets;
        _settings = settings;
        _logGate = logGate;
        _logger = logger;
    }

    /// <summary>Drops the resolved NPC mesh paths; the renderer clears the shared asset cache itself.</summary>
    public void InvalidateCaches() => _previewCache.ClearMeshPaths();

    public void Prewarm(OffscreenRenderRequest baseRequest, CancellationToken cancellation)
    {
        if (baseRequest is not NpcRenderRequest request || request.MeshPaths == null) return;

        var paths = request.MeshPaths;
        if (!string.IsNullOrWhiteSpace(request.OverrideHeadMeshAbsolutePath))
            paths = paths.WithHeadMeshPath(request.OverrideHeadMeshAbsolutePath);

        // Snapshot this worker thread's load/build split across the prewarm
        // parse so it lands in the per-render CSV (the render thread, after a
        // full prewarm, parses nothing — its ParseMs is ~0). ThreadStatic +
        // synchronous PrewarmNpc means these deltas are exactly this NPC's
        // offloaded parse cost. Accumulate (+=) so a render-thread re-parse of
        // an evicted part adds on top rather than clobbering.
        double loadMsStart = _previewCache.MeshBuilder.ThreadLoadMs;
        double buildMsStart = _previewCache.MeshBuilder.ThreadBuildMs;
        double decodeMsStart = _previewCache.ThreadDecodeMs;
        _previewCache.PrewarmNpc(paths, request.MeshOverrides, cancellation);
        if (request.TimingsOut is { } pt)
        {
            pt.LoadMs += _previewCache.MeshBuilder.ThreadLoadMs - loadMsStart;
            pt.BuildShapesMs += _previewCache.MeshBuilder.ThreadBuildMs - buildMsStart;
            pt.PrewarmDecodeMs += _previewCache.ThreadDecodeMs - decodeMsStart;
        }
    }

    public IOffscreenScene Build(OffscreenSceneContext context, OffscreenRenderRequest baseRequest)
    {
        if (baseRequest is not NpcRenderRequest request)
            throw new ArgumentException("NpcSceneBuilder renders NpcRenderRequest requests.", nameof(baseRequest));
        var vm = new VM_CharacterViewer(
            _assets, _settings, _previewCache, _logGate, _logger
            /* renderThread defaults to InlineRenderThreadMarshaller */);
        try
        {
            // This VM owns a real offscreen GL context and must BUILD the scene — opt it out of
            // the test-only ForceRenderingUnavailableForTesting capture-and-skip path in LoadAsync.
            vm.IsOffscreenRenderInstance = true;
            vm.AdditionalScopes = request.AdditionalScopes;
            vm.AdditionalDataFolders = request.AdditionalDataFolders;
            vm.VanillaLooseOverridesBsa = request.VanillaLooseOverridesBsa;
            vm.VanillaLooseOverridesModLoose = request.VanillaLooseOverridesModLoose;
            vm.AllowLoadOrderFallback = request.AllowLoadOrderFallback;
            // Granular cancellation for the install/texture path — lets a host
            // cancel abort partway through a heavy shape's texture loads rather
            // than only at the coarser phase boundaries below.
            vm.RenderCancellation = request.Cancellation;
            // Share uploaded textures across renders (vanilla + mod-shared) so
            // the GL-upload floor — the dominant steady-state cost per the
            // profiler — is paid once instead of per NPC.
            vm.ResidentTextureCache = context.ResidentTextures;
            Load(vm, request);
            ConfigureCamera(vm, request, context);
            return new Scene(vm);
        }
        catch
        {
            vm.Dispose();
            throw;
        }
    }

    private sealed class Scene(VM_CharacterViewer vm) : IOffscreenScene
    {
        public GlRenderer Renderer => vm.Renderer;
        public OrbitCamera Camera => vm.Camera;
        public void Dispose() => vm.Dispose();
    }

    private void Load(VM_CharacterViewer vm, NpcRenderRequest request)
    {
        var timings = request.TimingsOut;
        long tStart = Stopwatch.GetTimestamp();

        // Initialize the VM against the offscreen GL context. Shaders ship
        // beside this assembly via ModuleResourceLocator.
        vm.InitializeGl(ModuleResourceLocator.ShaderDirectory);

        // Push lighting from the request before the scene loads so
        // GlRenderer's lighting state is current by the time we render.
        if (request.Lighting != null) vm.SelectedLightingLayout = request.Lighting;
        if (request.Colors != null) vm.SelectedLightingColorScheme = request.Colors;

        // ApplyMaterial reads RenderMissingTextureAsWireframe per-shape
        // during the load, so push the request's value before LoadAsync.
        vm.RenderMissingTextureAsWireframe = request.RenderMissingTextureAsWireframe;
        // Tone-mapping is read at draw time by GlRenderer, so this push
        // could happen later, but keeping all request->VM forwards in one
        // place reduces the chance of one being missed for a future feature.
        vm.EnableToneMapping = request.EnableToneMapping;
        vm.EnableShadows = request.EnableShadows;
        vm.ExcludeHairShadowCaster = request.ExcludeHairShadowCaster;
        vm.SoftenShadowEdges = request.SoftenShadowEdges;
        vm.ShadowPcfRadius = request.ShadowPcfRadius;
        vm.TightShadowFrustum = request.TightShadowFrustum;
        vm.ShadowFrustumRadius = request.ShadowFrustumRadius;
        vm.EnableAmbientOcclusion = request.EnableAmbientOcclusion;
        vm.SsaoRadius = request.SsaoRadius;
        vm.SsaoBias = request.SsaoBias;
        vm.SsaoIntensity = request.SsaoIntensity;
        vm.SsaoThickness = request.SsaoThickness;
        vm.SsaoHairGap = request.SsaoHairGap;
        vm.EnableEyeCatchlight = request.EnableEyeCatchlight;
        vm.SubsurfaceStrength = request.SubsurfaceStrength;
        vm.SkinSaturationBoost = request.SkinSaturationBoost;
        vm.VignetteRadius = request.VignetteRadius;
        vm.VignetteIntensity = request.VignetteIntensity;
        vm.Exposure = request.Exposure;
        vm.TonemapHairRelief = request.TonemapHairRelief;
        vm.HairAlbedoCompensate = request.HairAlbedoCompensate;
        vm.DaylightBoost = request.DaylightBoost;
        vm.DaylightBoostIntensity = request.DaylightBoostIntensity;
        vm.EnableBloom = request.EnableBloom;
        vm.BloomIntensity = request.BloomIntensity;

        long tSetupDone = Stopwatch.GetTimestamp();

        // Build runs inline on THIS (render) thread for the offscreen path, so the
        // resolver's + mesh builder's per-thread counters attribute the build's own
        // resolve and parse cost here. Snapshot the deltas to split `build` into
        // resolve (uncacheable scope walk) vs parse (cache miss) vs the clone/morph
        // remainder. Captured before LoadAsync; the install-phase resolves/parses
        // after tBuildDone are excluded.
        double resolveMsStart = _assets.ThreadResolveMs;
        double parseMsStart = _previewCache.MeshBuilder.ThreadParseMs;
        double loadMsStart = _previewCache.MeshBuilder.ThreadLoadMs;
        double buildShapesMsStart = _previewCache.MeshBuilder.ThreadBuildMs;

        // Synchronously load + drain. The marshaller is inline so LoadAsync's
        // scene-queue handoff runs on this thread; ProcessPendingSceneToCompletion
        // then flushes the install queue against the bound FBO.
        var identity = new NpcIdentity("offscreen", "offscreen");
        vm.LoadAsync(identity, request.MeshPaths, request.OverrideHeadMeshAbsolutePath,
            request.Cancellation).GetAwaiter().GetResult();
        request.Cancellation.ThrowIfCancellationRequested();
        long tBuildDone = Stopwatch.GetTimestamp();
        // Decode is a subset of install; snapshot this thread's cumulative decode
        // counter across the install span to split decode from GL upload. Uses the
        // per-thread (not process-wide) counter so concurrent prewarm-worker decode
        // doesn't get charged to this render's decodeMs.
        double decodeMsStart = _previewCache.ThreadDecodeMs;
        vm.ProcessPendingSceneToCompletion(ct: request.Cancellation);

        // Surface any unresolved mesh game-paths so the host can flag an
        // incomplete render. Populated during LoadAllMeshParts.
        if (request.MissingMeshPathsOut != null && vm.MissingMeshPaths.Count > 0)
        {
            request.MissingMeshPathsOut.AddRange(vm.MissingMeshPaths);
        }

        // Same for textures the NIFs referenced but the texture manager
        // couldn't decode. The shapes are rendered as wireframe in the
        // missing-texture color (see GlRenderer.MissingTextureWireframeColor)
        // and the host pairs the list with a "missing texture" tile overlay.
        if (request.MissingTexturePathsOut != null && vm.MissingTexturePaths.Count > 0)
        {
            request.MissingTexturePathsOut.AddRange(vm.MissingTexturePaths);
        }

        // Optional post-load adjustments. Texture overrides are
        // queued internally if the scene wasn't ready; after
        // ProcessPendingSceneToCompletion above the scene IS ready, so
        // these apply immediately.
        if (request.TextureOverrides != null)
        {
            vm.ApplyTextureOverrides(request.TextureOverrides);
        }
        // Auxiliary mesh overrides (e.g. a subgroup mesh, outfit or headgear).
        // The scene is ready post-drain, so this loads/skins/textures/hides the
        // extra shapes inline against the bound FBO — they'll be in the next
        // Render() pass. Slot occupancy hides the base body under body armor and
        // hair under headgear, exactly as in the live preview.
        if (request.MeshOverrides != null)
        {
            // Base NPC textures were copied to MissingTexturePathsOut above, before
            // any override ran. Snapshot the missing set now so anything the attire
            // build newly misses can be attributed to the outfit/headgear rather
            // than the base NPC (see MissingOutfitTexturePathsOut).
            var baseMissingTextures = request.MissingOutfitTexturePathsOut != null
                ? new HashSet<string>(vm.MissingTexturePaths, StringComparer.OrdinalIgnoreCase)
                : null;

            vm.ApplyMeshOverrides(request.MeshOverrides, request.Cancellation);
            if (request.MeshOverrideWarningsOut != null && vm.MeshOverrideWarnings.Count > 0)
            {
                request.MeshOverrideWarningsOut.AddRange(vm.MeshOverrideWarnings);
            }
            if (request.MeshOverrideWarningDetailsOut != null && vm.MeshOverrideWarningDetails.Count > 0)
            {
                request.MeshOverrideWarningDetailsOut.AddRange(vm.MeshOverrideWarningDetails);
            }
            if (baseMissingTextures != null)
            {
                foreach (var texPath in vm.MissingTexturePaths)
                {
                    if (!baseMissingTextures.Contains(texPath))
                        request.MissingOutfitTexturePathsOut!.Add(texPath);
                }
            }
        }
        // Host scene hook: runs against the fully built scene, after the overrides, so a
        // deformation it applies (ApplyShapeDeformer is immediate here) and any overlay it
        // computes from vertex positions match this render. A throwing hook costs what it
        // had left to do, not the frame.
        if (request.BeforeDraw != null)
        {
            try
            {
                request.BeforeDraw(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError("OffscreenRenderer: BeforeDraw hook threw", ex);
            }
        }

        long tInstallDone = Stopwatch.GetTimestamp();

        if (timings != null)
        {
            timings.SetupMs = MsBetween(tStart, tSetupDone);
            timings.BuildMs = MsBetween(tSetupDone, tBuildDone);
            timings.ResolveMs = _assets.ThreadResolveMs - resolveMsStart;
            timings.ParseMs = _previewCache.MeshBuilder.ThreadParseMs - parseMsStart;
            // += so a render-thread re-parse adds onto the prewarm worker's split
            // (captured in Prewarm) rather than overwriting it.
            timings.LoadMs += _previewCache.MeshBuilder.ThreadLoadMs - loadMsStart;
            timings.BuildShapesMs += _previewCache.MeshBuilder.ThreadBuildMs - buildShapesMsStart;
            timings.InstallMs = MsBetween(tBuildDone, tInstallDone);
            timings.DecodeMs = _previewCache.ThreadDecodeMs - decodeMsStart;
        }
    }

    private static double MsBetween(long startTimestamp, long endTimestamp)
        => (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Sets the VM camera's orbit parameters from the request's
    /// <see cref="CameraFraming"/>. Portrait mode auto-frames the head using
    /// the loaded scene's NPC base height; Fixed mode applies the explicit
    /// values directly. Both modes leave the renderer in a state where
    /// <c>vm.Renderer.Render(vm.Camera, w, h)</c> produces the framed image.</summary>
    private void ConfigureCamera(VM_CharacterViewer vm, NpcRenderRequest request, OffscreenSceneContext context)
    {
        var camera = vm.Camera;

        switch (request.Camera)
        {
            case CameraFraming.Portrait portrait:
            {
                // Frame the head: target it, pull back enough that the head
                // height (≈22 units in NIF coords for a normal-scale NPC)
                // fills the requested portion of the framebuffer. The
                // OrbitCamera defaults (Az=180 facing the camera, El=15)
                // are reasonable for a portrait; we just dial Distance and
                // Target to match the request.
                float headWorldY = 120f * vm.NpcBaseHeight;   // NIF "NPC Head" Z position
                camera.Target = new Vector3(0f, headWorldY, 0f);
                // Heuristic: distance ~= head height / tan(half_fov) divided by
                // the framing band. Headband = HeadTopOffset - HeadBottomOffset
                // (1.0 = full frame). With OrbitCamera's default ~50° vertical
                // FOV, distance scales inversely with the framing band.
                float band = MathF.Max(0.05f, portrait.HeadTopOffset - portrait.HeadBottomOffset);
                float headHeight = 22f * vm.NpcBaseHeight;
                camera.Distance = MathF.Max(camera.MinDistance, headHeight / band * 1.6f);
                camera.Azimuth = 180f;
                camera.Elevation = 0f;
                break;
            }
            case CameraFraming.Fixed fixedCam:
            {
                // OrbitCamera doesn't directly expose eye position; the closest
                // approximation is to set Target to the head and convert
                // (X, Y, Z) into a delta from the target. Yaw/Pitch derive
                // from that delta. Roll is unsupported by OrbitCamera and is
                // ignored. FOV is camera-internal and currently fixed.
                float headWorldY = 120f * vm.NpcBaseHeight;
                camera.Target = new Vector3(0f, headWorldY, 0f);
                var dx = fixedCam.X;
                var dy = fixedCam.Y - headWorldY;
                var dz = fixedCam.Z;
                camera.Distance = MathF.Max(camera.MinDistance,
                    MathF.Sqrt(dx * dx + dy * dy + dz * dz));
                camera.Azimuth = MathF.Atan2(dx, dz) * (180f / MathF.PI);
                camera.Elevation = MathF.Atan2(dy,
                    MathF.Sqrt(dx * dx + dz * dz)) * (180f / MathF.PI);
                break;
            }
            case CameraFraming.MeshAware meshAware:
            {
                // Prefer the request's DiagnosticLog when set — hosts using
                // AsyncLocal flow-scoped capture MUST snapshot their writer there
                // because this runs on the renderer's dedicated render thread,
                // which does not inherit the host's logical call context. Fall
                // back to _logger gated on _logGate.Verbose for hosts that route
                // framing diagnostics through their shared verbose-log channel.
                Action<string>? log = request.DiagnosticLog
                    ?? ((_logGate != null && _logGate.Verbose && _logger != null)
                        ? (Action<string>)(msg => _logger.LogMessage(msg))
                        : null);
                MeshAwareCameraFitter.ApplyTo(vm, meshAware, context.Width, context.Height, log: log);
                break;
            }
            case CameraFraming.OrbitState orbit:
            {
                // Lossless orbit-state placement — the host has already
                // computed the camera state (typically via interactive
                // UI: drag-to-orbit, scroll-to-zoom, middle-drag to pan)
                // and wants the saved PNG to match the preview exactly.
                camera.Distance = MathF.Max(camera.MinDistance, orbit.Distance);
                camera.Azimuth = orbit.Azimuth;
                camera.Elevation = orbit.Elevation;
                camera.Target = new Vector3(orbit.TargetX, orbit.TargetY, orbit.TargetZ);
                break;
            }
        }
    }
}

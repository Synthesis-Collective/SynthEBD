using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Reference <see cref="IOffscreenRenderer"/> implementation. Owns a hidden
/// <see cref="GameWindow"/> for the GL context, an FBO for offscreen
/// rendering, and an ImageSharp encoder for PNG output. One renderer
/// instance handles many requests serially via a dedicated render thread.
///
/// <para><b>Threading model (1.3.0+):</b> a single long-lived render thread
/// owns the GL context for the renderer's entire lifetime. The constructor
/// creates the hidden <see cref="GameWindow"/> on the calling thread (GLFW
/// installs its event hook there), immediately releases the context via
/// <c>Context.MakeNoneCurrent()</c>, then starts the render thread which
/// calls <c>MakeCurrent</c> exactly once and processes
/// <see cref="RenderToPngAsync"/> / <see cref="RenderToBgra32Async"/>
/// requests from a <see cref="BlockingCollection{T}"/> queue. No other
/// thread ever touches the context — eliminating the WGL "in use" race
/// that the previous per-render <c>MakeCurrent</c> / <c>MakeNoneCurrent</c>
/// approach hit when parallel render requests landed on different
/// thread-pool threads.</para>
///
/// <para><b>Per-render flow:</b> a fresh <see cref="VM_CharacterViewer"/> is
/// constructed for each render with the default
/// <see cref="InlineRenderThreadMarshaller"/> (no WPF dispatcher). The VM is
/// initialized against the offscreen GL context, fed the request's
/// <see cref="ResolvedNpcMeshPaths"/>, drained to completion via
/// <see cref="VM_CharacterViewer.ProcessPendingSceneToCompletion"/>, then
/// rendered into the FBO. The VM is disposed before the next render so
/// GL state can't leak between requests. The <see cref="CharacterPreviewCache"/>
/// (the only shared cross-request state worth keeping) lives at the host
/// level and amortizes NIF parses + DDS decodes across calls.</para>
/// </summary>
public sealed class GameWindowOffscreenRenderer : IOffscreenRenderer
{
    private GameWindow? _gw;

    // Two-FBO MSAA pipeline:
    //   _msaaFbo  (4× multisampled color + depth renderbuffers) is the actual
    //             draw target. Smooths alpha-tested cutout edges via
    //             SAMPLE_ALPHA_TO_COVERAGE in GlRenderer's Pass 1, and
    //             general edge anti-aliasing for the rest of the scene.
    //   _resolveFbo (single-sample Texture2D color + depth renderbuffer)
    //             is the readback target. Each render blits MSAA → resolve
    //             before glReadPixels.
    private int _msaaFbo = -1;
    private int _msaaColorRbo = -1;
    private int _msaaDepthRbo = -1;
    private int _resolveFbo = -1;
    private int _resolveColorTex = -1;
    private int _resolveDepthRbo = -1;
    private (int W, int H) _fboSize = (0, 0);
    private const int MsaaSamples = 4;

    private volatile bool _disposed;

    // VM dependencies — passed to each per-request VM instance.
    private readonly CharacterPreviewCache _previewCache;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdParser;
    private readonly BodyTriFileParser _triParser;
    private readonly GameAssetResolver _assets;
    private readonly ICharacterViewerSettings _settings;
    private readonly CharacterViewerLogGate _logGate;
    private readonly ICharacterViewerLogger _logger;

    // Shared GL texture cache, created on the render thread once the context is
    // current and reused across every render so the same vanilla / mod-shared
    // textures upload to the GPU once instead of per render. Render-thread-only.
    private ResidentTextureCache? _residentTextures;

    // Dedicated render thread + queue. The thread owns the GL context for
    // its lifetime so we never migrate context across threads.
    private readonly Thread _renderThread;
    private readonly BlockingCollection<RenderJob> _renderQueue = new();

    // A job is either a render (Request set) or a maintenance action (Maintenance
    // set) — both run on the render thread with the context current, preserving
    // ordering so e.g. a cache clear can't race in-flight renders.
    private readonly record struct RenderJob(
        OffscreenRenderRequest Request,
        bool EncodeAsPng,
        TaskCompletionSource<byte[]> Tcs,
        Action? Maintenance = null);

    internal GameWindowOffscreenRenderer(
        CharacterPreviewCache previewCache,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdParser,
        BodyTriFileParser triParser,
        GameAssetResolver assets,
        ICharacterViewerSettings settings,
        CharacterViewerLogGate logGate,
        ICharacterViewerLogger logger)
    {
        _previewCache = previewCache;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdParser = bsdParser;
        _triParser = triParser;
        _assets = assets;
        _settings = settings;
        _logGate = logGate;
        _logger = logger;

        // Prime nifly's block-type factory (a Meyers/function-local-static
        // singleton) once on this thread before any render or prewarm worker
        // parses a NIF, so the first concurrent parse can't race the singleton's
        // lazy initialization. The factory map is populated once in the ctor and
        // read-only afterward, so concurrent NifFile.Load on separate instances is
        // safe — which is what PrewarmAsync relies on. Best-effort: a failure here
        // only forfeits that head start, it doesn't break rendering.
        try { using (nifly.NiFactoryRegister.Get()) { } }
        catch (Exception primeEx)
        {
            System.Diagnostics.Debug.WriteLine(
                "[OffscreenRenderer] NiFactoryRegister prime failed (non-fatal): " + primeEx.Message);
        }

        // Construct the hidden GameWindow on the constructing thread — GLFW
        // installs its event hook on the first thread that touches it.
        // The window stays at 8×8 and never becomes visible; the actual
        // render target is the FBO, which can be any size independent
        // of the window.
        var nws = new NativeWindowSettings
        {
            ClientSize = new Vector2i(8, 8),
            StartVisible = false,
            APIVersion = new Version(4, 0),
            Profile = ContextProfile.Core,
            StartFocused = false,
        };
        _gw = new GameWindow(GameWindowSettings.Default, nws);

        // GameWindow's constructor binds the GL context to this thread.
        // Release it so the dedicated render thread's first MakeCurrent
        // succeeds — without this, WGL would refuse with "resource in use".
        try
        {
            _gw.Context.MakeNoneCurrent();
        }
        catch (Exception releaseEx)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[OffscreenRenderer] Constructor MakeNoneCurrent FAILED: {releaseEx.Message}");
            // Not fatal — render thread's MakeCurrent may still succeed if
            // nothing else races for the context. Logged so the host can
            // diagnose if subsequent renders fail.
        }

        _renderThread = new Thread(RenderThreadLoop)
        {
            IsBackground = true,
            Name = "CharacterViewer.OffscreenRender",
        };
        _renderThread.Start();
    }

    public Task<byte[]> RenderToPngAsync(OffscreenRenderRequest request)
        => EnqueueRender(request, encodeAsPng: true);

    public Task<byte[]> RenderToBgra32Async(OffscreenRenderRequest request)
        => EnqueueRender(request, encodeAsPng: false);

    public void InvalidateCaches()
    {
        if (_disposed) return;
        // Enqueue as a maintenance job so the resident-texture clear runs on the
        // render thread (GL context) in order with any in-flight renders. The
        // CPU-side preview cache is cleared on the same hop for atomicity.
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _renderQueue.Add(new RenderJob(null!, false, tcs, Maintenance: () =>
            {
                _residentTextures?.Clear();
                _previewCache.Clear();
                // The env may resolve assets differently now (game path / load order
                // changed), so drop the scope-aware resolve cache too.
                _assets.ClearResolveCache();
            }));
        }
        catch (InvalidOperationException)
        {
            // Race: dispose ran between the check and Add — nothing to invalidate.
        }
    }

    public Task PrewarmAsync(OffscreenRenderRequest request)
    {
        if (_disposed || request?.MeshPaths == null) return Task.CompletedTask;

        var ct = request.Cancellation;
        // Run the GL-free parse + decode on the thread pool so it overlaps the GL
        // render thread (busy with other NPCs). Push the request's resolution
        // scopes on THIS worker flow first — the preview cache's resolves read them
        // off AsyncLocal, and the push covers the synchronous PrewarmNpc below.
        // Deliberately NOT passing ct to Task.Run: prewarm is best-effort and must
        // never fault the host's await, so cancellation is observed via the inner
        // ThrowIfCancellationRequested checks and swallowed here.
        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var scopes = _assets.PushScopes(
                    request.AdditionalScopes,
                    request.AdditionalDataFolders,
                    request.VanillaLooseOverridesBsa,
                    request.VanillaLooseOverridesModLoose);

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
                _previewCache.PrewarmNpc(paths, request.MeshOverrides, ct);
                if (request.TimingsOut is { } pt)
                {
                    pt.LoadMs += _previewCache.MeshBuilder.ThreadLoadMs - loadMsStart;
                    pt.BuildShapesMs += _previewCache.MeshBuilder.ThreadBuildMs - buildMsStart;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled before/while warming — the matching render (if it still
                // runs) re-resolves any misses, so there's nothing to surface.
            }
            catch (Exception ex)
            {
                // Prewarm is a pure optimization; never let it fault the host's
                // await. A miss costs a re-decode on the render thread, no more.
                System.Diagnostics.Debug.WriteLine(
                    "[OffscreenRenderer] Prewarm failed (non-fatal): " + ex.Message);
            }
        });
    }

    private Task<byte[]> EnqueueRender(OffscreenRenderRequest request, bool encodeAsPng)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GameWindowOffscreenRenderer));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Width <= 0 || request.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(request),
                "Width and Height must be positive.");

        // RunContinuationsAsynchronously prevents host await-continuations
        // from hijacking the dedicated render thread when the TCS completes —
        // otherwise the next queued job couldn't start until the host's
        // continuation finishes.
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _renderQueue.Add(new RenderJob(request, encodeAsPng, tcs));
        }
        catch (InvalidOperationException)
        {
            // Race: dispose ran between the _disposed check and Add.
            throw new ObjectDisposedException(nameof(GameWindowOffscreenRenderer));
        }
        return tcs.Task;
    }

    private void RenderThreadLoop()
    {
        // Take ownership of the GL context for this thread's lifetime.
        // After this single MakeCurrent, no other thread ever calls MakeCurrent
        // on _gw, so WGL never sees a context-migration race.
        try
        {
            _gw!.MakeCurrent();
            System.Diagnostics.Debug.WriteLine(
                $"[OffscreenRenderer] Render thread MakeCurrent OK tid={Environment.CurrentManagedThreadId}");
            // Context is current on this thread now — safe to create the shared
            // texture cache (it queries VRAM + will own GL handles on this context).
            _residentTextures = new ResidentTextureCache(_logger, logGate: _logGate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[OffscreenRenderer] Render thread MakeCurrent FAILED: {ex.Message}");
            _logger?.LogError(
                "OffscreenRenderer: dedicated render thread could not bind GL context: " + ex.Message, ex);

            // Mark renderer dead so EnqueueRender starts throwing
            // ObjectDisposedException, then drain anything already queued
            // so callers see the failure rather than hanging.
            _disposed = true;
            _renderQueue.CompleteAdding();
            while (_renderQueue.TryTake(out var job))
            {
                job.Tcs.TrySetException(ex);
            }
            return;
        }

        try
        {
            foreach (var job in _renderQueue.GetConsumingEnumerable())
            {
                try
                {
                    if (_disposed)
                    {
                        job.Tcs.TrySetException(new ObjectDisposedException(nameof(GameWindowOffscreenRenderer)));
                        continue;
                    }
                    if (job.Maintenance != null)
                    {
                        job.Maintenance();
                        job.Tcs.TrySetResult(System.Array.Empty<byte>());
                        continue;
                    }
                    job.Request.Cancellation.ThrowIfCancellationRequested();
                    // Do the GL work on this thread, then hand the read-back pixel
                    // buffer to the thread pool for the CPU-only PNG/BGRA encode so
                    // this thread starts the next queued render immediately instead
                    // of blocking ~50-100 ms per NPC on encode. The job's Tcs is
                    // completed from the encode task. Render-phase failures still
                    // throw here and are caught below; encode-phase failures are
                    // set on the Tcs inside DispatchEncode.
                    RenderedFrame frame = RenderInternalCore(job.Request);
                    DispatchEncode(frame, job);
                }
                catch (OperationCanceledException oce)
                {
                    job.Tcs.TrySetCanceled(oce.CancellationToken);
                }
                catch (Exception ex)
                {
                    job.Tcs.TrySetException(ex);
                }
            }
        }
        finally
        {
            // GL resources must be released on the thread that owns the
            // context. Best-effort: if any step fails the others still try.
            try { _residentTextures?.Dispose(); _residentTextures = null; } catch { /* best-effort */ }
            try { DestroyFboIfPresent(); } catch { /* best-effort */ }
            try { _gw?.Context.MakeNoneCurrent(); } catch { /* best-effort */ }
            try { _gw?.Close(); } catch { /* best-effort */ }
            try { _gw?.Dispose(); } catch { /* best-effort */ }
            _gw = null;
        }
    }

    private RenderedFrame RenderInternalCore(OffscreenRenderRequest request)
    {
        if (_gw == null) throw new InvalidOperationException("GameWindow not initialized.");

        // Route the renderer's verbose diagnostic lines into the host's
        // per-request capture sink (when supplied). The host installs an
        // AsyncLocal flow writer on its own thread before calling
        // RenderToPngAsync, then snapshots a thread-agnostic closure into
        // request.DiagnosticLog. Without this push, _logger.LogMessage calls
        // from this dedicated render thread would see a null AsyncLocal on
        // the host side and the per-mesh / shader / texture dumps would be
        // silently dropped (the bare Thread we're running on doesn't inherit
        // the host's ExecutionContext). Hosts that haven't overridden
        // PushDiagnosticSink get the interface's default no-op and so are
        // unaffected by this push.
        using var diagnosticSinkScope = _logger.PushDiagnosticSink(request.DiagnosticLog);

        EnsureFbo(request.Width, request.Height);

        // Per-render asset-resolution scope. PushScopes binds the four scoping
        // values to this flow's AsyncLocal stack and returns a token that
        // restores the prior values on dispose. The VM's LoadAsync pushes the
        // same values internally — this outer push is defense-in-depth so any
        // resolver call made before LoadAsync (or inside the post-load
        // ApplyTextureOverrides / ApplyMorphSet paths) still sees the right
        // scope chain. AdditionalScopes (1.2.0+) overrides AdditionalDataFolders
        // (1.1.0) when both are provided.
        using var scopes = _assets.PushScopes(
            request.AdditionalScopes,
            request.AdditionalDataFolders,
            request.VanillaLooseOverridesBsa,
            request.VanillaLooseOverridesModLoose);
        var vm = new VM_CharacterViewer(
            _bodySlideDeformer, _bsdParser, _triParser, _assets,
            _settings, _previewCache, _logGate, _logger
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
            // Granular cancellation for the install/texture path — lets a host
            // cancel abort partway through a heavy shape's texture loads rather
            // than only at the coarser phase boundaries in LoadAndRender.
            vm.RenderCancellation = request.Cancellation;
            // Share uploaded textures across renders (vanilla + mod-shared) so
            // the GL-upload floor — the dominant steady-state cost per the
            // profiler — is paid once instead of per NPC.
            vm.ResidentTextureCache = _residentTextures;
            // Protect this render's textures from mid-render eviction.
            _residentTextures?.BeginRenderPass();
            LoadAndRender(vm, request);
            long tDrawDone = Stopwatch.GetTimestamp();

            // Resolve the multisampled draw target into the single-sample
            // resolve FBO so glReadPixels gets a correctly-AA'd image.
            // Filter is Nearest because MSAA resolve handles the averaging
            // (Linear here would double-blur).
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);
            GL.BlitFramebuffer(
                0, 0, request.Width, request.Height,
                0, 0, request.Width, request.Height,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

            byte[] pixels = ReadPixelsRgba(request.Width, request.Height);
            FlipVertical(pixels, request.Width, request.Height);

            // Force every pixel's alpha to 255 (fully opaque) before PNG
            // encode.
            //
            // What we observed:
            //   - basic.frag writes FragColor = vec4(finalColor, baseColor.a)
            //     at the end of the shader. baseColor.a is the result of
            //     texture_alpha × vertex_alpha (after the multiply earlier
            //     in the shader).
            //   - glClear sets the FBO to alpha=1.0 across all pixels, but
            //     shader writes overwrite alpha wherever a fragment is drawn.
            //   - For shapes whose texture alpha and/or vertex alpha falls
            //     below 1 (vanilla Khajiit faces with SLSF1_Vertex_Alpha;
            //     alpha-tested hair card silhouettes; anything cutout-like),
            //     framebuffer pixels end up storing α < 1.
            //   - ReadPixelsRgba pulls all four channels into bytes. The PNG
            //     encoder writes all four channels. PNG viewers — including
            //     the WPF Image control that NPC2's gallery uses to render
            //     mugshot tiles — honor PNG alpha and composite the image
            //     over their panel background. Khajiit face pixels with
            //     α ≈ 0.5 alpha-blend with the gray panel underneath at 50%
            //     mix, producing the visible "head darker than body" /
            //     "skin swallows the illumination" effect that looks like
            //     dimmed shading but is actually alpha bleed-through.
            //   - The live 3D preview pathway empirically displays opaque
            //     for the same scene (no transparency visible in the preview
            //     window). We have NOT verified the exact mechanism that
            //     produces that opaque display: candidates we didn't trace
            //     are GLWpfControl explicitly normalizing alpha during its
            //     present, WPF's D3DImage compositor ignoring source alpha
            //     at composition time, or the display path's underlying
            //     D3D9 surface format not having a usable alpha channel.
            //     What we know is the per-fragment α values from the shader
            //     reach the off-screen pipeline's readback bytes but never
            //     surface in the on-screen pathway.
            //
            // Why this is safe for hair / cutout silhouettes:
            //   The RGB channel is already correctly anti-aliased by the
            //   MSAA resolve. At a hair-card silhouette pixel where some
            //   subsamples passed alpha-test and others discarded, the
            //   resolve averages the passed samples' hair color with the
            //   discarded samples' cleared-background color in the RGB
            //   channel. The resulting RGB is the correct soft blend.
            //   Stripping alpha to 255 doesn't change RGB — edge softness
            //   lives in the RGB blend, not in the PNG alpha channel — so
            //   hair silhouettes stay smooth.
            //
            // Mugshots are tiles rendered against an opaque background, not
            // transparent overlays, so the alpha channel carries no useful
            // information in the output PNG. If transparent-background
            // mugshot exports are ever wanted (portrait stickers for
            // external use), this stamp should become conditional on a
            // request flag rather than unconditional.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            long tReadbackDone = Stopwatch.GetTimestamp();

            if (request.TimingsOut is { } t)
                t.ReadbackMs = MsBetween(tDrawDone, tReadbackDone);

            // The PNG/BGRA encode is pure CPU over this managed pixel buffer with
            // no GL or VM dependency, so the caller (DispatchEncode) offloads it to
            // the thread pool after this method disposes the VM on the render
            // thread. EncodeMs is recorded there. `pixels` is a fresh per-render
            // allocation (ReadPixelsRgba), so the encode task owns it outright —
            // the next render's FBO reuse can't race it.
            return new RenderedFrame(pixels, request.Width, request.Height);
        }
        finally
        {
            vm.Dispose();
            // The using on `scopes` above pops the resolver state. Removed the
            // per-render ClearExtractedFiles call — it raced with concurrent
            // interactive previews (whose loads share the same extraction
            // directory). Hosts that want to flush the cache should call
            // GameAssetResolver.ClearExtractedFiles() at quiescence (e.g. on
            // shutdown) instead.
        }
    }

    /// <summary>A rendered frame's read-back pixels (RGBA8, top-left origin,
    /// alpha stamped to 255) awaiting CPU encode. Decoupled from the render
    /// thread so the encode can run on the thread pool.</summary>
    private readonly record struct RenderedFrame(byte[] Pixels, int Width, int Height);

    /// <summary>Offloads the PNG/BGRA encode of a rendered frame to the thread
    /// pool and completes the job's <see cref="TaskCompletionSource{TResult}"/>
    /// from there, so the dedicated render thread returns to draining the queue
    /// immediately. The encode is pure CPU over a managed buffer (no GL, no VM),
    /// and <see cref="MugshotPngEncoder"/> is immutable/stateless, so concurrent
    /// encodes from back-to-back renders are safe. The host semaphore that bounds
    /// in-flight renders also bounds how many frames can await encode at once, so
    /// pixel buffers can't pile up unbounded.</summary>
    private void DispatchEncode(RenderedFrame frame, in RenderJob job)
    {
        // Capture into locals — a lambda can't close over the `in` parameter, and
        // these are all the encode needs (the render thread keeps no reference to
        // the frame after this returns).
        var tcs = job.Tcs;
        var timings = job.Request.TimingsOut;
        bool encodeAsPng = job.EncodeAsPng;
        Task.Run(() =>
        {
            try
            {
                long tEncodeStart = Stopwatch.GetTimestamp();
                byte[] encoded = encodeAsPng
                    ? EncodePngFromRgba(frame.Pixels, frame.Width, frame.Height)
                    : RgbaToBgra(frame.Pixels);
                if (timings != null)
                    timings.EncodeMs = MsBetween(tEncodeStart, Stopwatch.GetTimestamp());
                tcs.TrySetResult(encoded);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
    }

    private void LoadAndRender(VM_CharacterViewer vm, OffscreenRenderRequest request)
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

        // Optional post-load adjustments. Texture overrides and morphs are
        // queued internally if the scene wasn't ready; after
        // ProcessPendingSceneToCompletion above the scene IS ready, so
        // these apply immediately.
        if (request.TextureOverrides != null)
        {
            vm.ApplyTextureOverrides(request.TextureOverrides);
        }
        // Auxiliary mesh overrides (SynthEBD subgroup mesh, NPC2 outfit/headgear).
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
        if (request.Morphs != null)
        {
            vm.ApplyMorphSet(request.Morphs, request.MorphWeight);
        }

        long tInstallDone = Stopwatch.GetTimestamp();

        // Re-bind the multisampled draw FBO (VM's GL calls may have unbound
        // it) and clear before we render the new scene. MSAA on this FBO is
        // implicit from the multisampled attachments; the explicit Multisample
        // enable is defensive — some drivers leave it disabled by default.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        GL.Enable(EnableCap.Multisample);
        GL.Viewport(0, 0, request.Width, request.Height);
        // Push the request's background into vm.BackgroundColor so the VM's
        // WhenAnyValue subscription forwards it to Renderer.ClearColor. The
        // Render() body resets GL.ClearColor from its own field on every
        // frame, so setting GL.ClearColor here directly would be stomped.
        vm.BackgroundColor = System.Windows.Media.Color.FromRgb(
            request.BackgroundRgb.R, request.BackgroundRgb.G, request.BackgroundRgb.B);
        float r = request.BackgroundRgb.R / 255f;
        float g = request.BackgroundRgb.G / 255f;
        float b = request.BackgroundRgb.B / 255f;
        GL.ClearColor(r, g, b, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        ConfigureCamera(vm, request);

        // Last chance to bail before the draw + readback + encode. Past this
        // point the work is a single GL pass we don't interrupt.
        request.Cancellation.ThrowIfCancellationRequested();
        vm.Renderer.Render(vm.Camera, request.Width, request.Height);

        if (timings != null)
        {
            timings.SetupMs = MsBetween(tStart, tSetupDone);
            timings.BuildMs = MsBetween(tSetupDone, tBuildDone);
            timings.ResolveMs = _assets.ThreadResolveMs - resolveMsStart;
            timings.ParseMs = _previewCache.MeshBuilder.ThreadParseMs - parseMsStart;
            // += so a render-thread re-parse adds onto the prewarm worker's split
            // (captured in PrewarmAsync) rather than overwriting it.
            timings.LoadMs += _previewCache.MeshBuilder.ThreadLoadMs - loadMsStart;
            timings.BuildShapesMs += _previewCache.MeshBuilder.ThreadBuildMs - buildShapesMsStart;
            timings.InstallMs = MsBetween(tBuildDone, tInstallDone);
            timings.DecodeMs = _previewCache.ThreadDecodeMs - decodeMsStart;
            timings.DrawMs = MsBetween(tInstallDone, Stopwatch.GetTimestamp());
        }
    }

    private static double MsBetween(long startTimestamp, long endTimestamp)
        => (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Sets the VM camera's orbit parameters from the request's
    /// <see cref="CameraFraming"/>. Portrait mode auto-frames the head using
    /// the loaded scene's NPC base height; Fixed mode applies the explicit
    /// values directly. Both modes leave the renderer in a state where
    /// <c>vm.Renderer.Render(vm.Camera, w, h)</c> produces the framed image.</summary>
    private void ConfigureCamera(VM_CharacterViewer vm, OffscreenRenderRequest request)
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
                // ignored. FOV is camera-internal and currently fixed; if NPC2
                // needs custom FOV we can extend OrbitCamera later.
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
                // AsyncLocal flow-scoped capture (like NPC2's per-mugshot
                // _Mugshot.txt files) MUST snapshot their writer there
                // because this branch executes on the renderer's dedicated
                // render thread, which does not inherit the host's logical
                // call context. Fall back to _logger gated on _logGate.Verbose
                // for hosts that route framing diagnostics through their
                // shared verbose-log channel.
                Action<string>? log = request.DiagnosticLog
                    ?? ((_logGate != null && _logGate.Verbose && _logger != null)
                        ? (Action<string>)(msg => _logger.LogMessage(msg))
                        : null);
                MeshAwareCameraFitter.ApplyTo(vm, meshAware, request.Width, request.Height, log: log);
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

    /// <summary>Lazily creates / resizes the multisampled draw FBO and the
    /// single-sample resolve FBO. Called inside the render lock; both are
    /// reused across same-size requests.</summary>
    private void EnsureFbo(int width, int height)
    {
        if (_fboSize == (width, height) && _msaaFbo != -1) return;

        DestroyFboIfPresent();

        // ── Multisampled draw FBO ─────────────────────────────────────────
        _msaaFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

        _msaaColorRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColorRbo);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
            MsaaSamples, RenderbufferStorage.Rgba8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _msaaColorRbo);

        _msaaDepthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepthRbo);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
            MsaaSamples, RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _msaaDepthRbo);

        var msaaStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (msaaStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "Offscreen MSAA FBO incomplete: " + msaaStatus +
                " (size=" + width + "x" + height + ", samples=" + MsaaSamples + ")");
        }

        // ── Single-sample resolve FBO (readback target) ───────────────────
        _resolveFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

        _resolveColorTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _resolveColorTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _resolveColorTex, 0);

        _resolveDepthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _resolveDepthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _resolveDepthRbo);

        var resolveStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (resolveStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "Offscreen resolve FBO incomplete: " + resolveStatus +
                " (size=" + width + "x" + height + ")");
        }

        // Bind the MSAA FBO as the active draw target. Per-render code
        // assumes this is the bound FBO at the start of every render
        // (Pass 1's SAMPLE_ALPHA_TO_COVERAGE only smooths edges when MSAA
        // is the active target).
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

        _fboSize = (width, height);
    }

    private void DestroyFboIfPresent()
    {
        if (_msaaDepthRbo != -1) { GL.DeleteRenderbuffer(_msaaDepthRbo); _msaaDepthRbo = -1; }
        if (_msaaColorRbo != -1) { GL.DeleteRenderbuffer(_msaaColorRbo); _msaaColorRbo = -1; }
        if (_msaaFbo != -1) { GL.DeleteFramebuffer(_msaaFbo); _msaaFbo = -1; }
        if (_resolveDepthRbo != -1) { GL.DeleteRenderbuffer(_resolveDepthRbo); _resolveDepthRbo = -1; }
        if (_resolveColorTex != -1) { GL.DeleteTexture(_resolveColorTex); _resolveColorTex = -1; }
        if (_resolveFbo != -1) { GL.DeleteFramebuffer(_resolveFbo); _resolveFbo = -1; }
        _fboSize = (0, 0);
    }

    /// <summary>Reads the FBO's color attachment as RGBA8. Bound FBO must be
    /// the offscreen target before this is called.</summary>
    private static byte[] ReadPixelsRgba(int width, int height)
    {
        var buf = new byte[width * height * 4];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, buf);
        return buf;
    }

    /// <summary>Flips the byte buffer vertically in place. GL's coordinate
    /// origin is bottom-left; PNG / WPF expect top-left.</summary>
    private static void FlipVertical(byte[] rgba, int width, int height)
    {
        int rowBytes = width * 4;
        var tmp = new byte[rowBytes];
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * rowBytes;
            int bot = (height - 1 - y) * rowBytes;
            System.Buffer.BlockCopy(rgba, top, tmp, 0, rowBytes);
            System.Buffer.BlockCopy(rgba, bot, rgba, top, rowBytes);
            System.Buffer.BlockCopy(tmp, 0, rgba, bot, rowBytes);
        }
    }

    /// <summary>Reorders the RGBA pixel buffer in place to BGRA for callers
    /// that want to feed WPF's <c>WriteableBitmap.WritePixels</c> directly.</summary>
    private static byte[] RgbaToBgra(byte[] rgba)
    {
        // Swap R↔B in place; A stays.
        for (int i = 0; i < rgba.Length; i += 4)
        {
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        }
        return rgba;
    }

    // Mugshot PNG encoder, tuned for speed at negligible size/quality cost.
    // Two changes from the default:
    //  * ColorType=Rgb — RenderInternalCore stamps every pixel's alpha to 255
    //    before encode, so the alpha channel carries no information. Dropping it
    //    is lossless here and removes a quarter of the pixel data from the
    //    deflate stage.
    //  * FilterMethod=Paeth — the default Adaptive filter trials all five PNG
    //    filters on every scanline and keeps the smallest, which is the bulk of
    //    encode CPU. On smooth rendered faces a single Paeth filter is within a
    //    few percent of adaptive's output size for a fraction of the work.
    // CompressionLevel is left at the default so on-disk size stays comparable.
    // Immutable + stateless, so a single shared instance is safe (encode runs
    // only on the serialized render thread anyway).
    private static readonly PngEncoder MugshotPngEncoder = new()
    {
        ColorType = PngColorType.Rgb,
        FilterMethod = PngFilterMethod.Paeth,
    };

    private static byte[] EncodePngFromRgba(byte[] rgba, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.Save(ms, MugshotPngEncoder);
        return ms.ToArray();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        // Signal the render thread to drain pending jobs and exit. Jobs
        // queued before this point still run; new ones throw via the
        // EnqueueRender disposed-check / InvalidOperationException path.
        // The render thread tears down the GameWindow + FBO on its own
        // thread inside its finally block — GL resources must be released
        // by the thread that owns the context.
        try { _renderQueue.CompleteAdding(); } catch { /* already completed */ }

        // Bounded wait — don't block host shutdown if a render is wedged.
        // Skip Join when called from the render thread itself (a host
        // disposing from within an await continuation hijacked by the TCS,
        // which RunContinuationsAsynchronously normally prevents but is
        // worth defending against).
        if (Thread.CurrentThread != _renderThread)
        {
            try { _renderThread.Join(TimeSpan.FromSeconds(5)); } catch { /* best-effort */ }
        }

        return ValueTask.CompletedTask;
    }
}

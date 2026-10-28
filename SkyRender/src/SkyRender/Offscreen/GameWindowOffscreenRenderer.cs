using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SkyRender.Offscreen;

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
/// GL state can't leak between requests. The <see cref="AssetCache"/>
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

    // What each render draws comes from the scene builder; the renderer owns the
    // context, thread, framebuffers, asset scopes and texture residency.
    private readonly IOffscreenSceneBuilder _sceneBuilder;
    private readonly AssetCache _assetCache;
    private readonly GameAssetResolver _assets;
    private readonly RenderLogGate _logGate;
    private readonly IRenderLogger _logger;

    // Shared GL texture cache, created on the render thread once the context is
    // current and reused across every render so the same vanilla / mod-shared
    // textures upload to the GPU once instead of per render. Render-thread-only.
    private ResidentTextureCache? _residentTextures;

    // Dedicated render thread + queue. The thread owns the GL context for
    // its lifetime so we never migrate context across threads.
    private readonly Thread _renderThread;
    private readonly BlockingCollection<RenderJob> _renderQueue = new();

    // Per-request accumulator for data-folder-fallback hits, shared between the
    // prewarm flow (thread-pool worker) and the render flow (render thread) —
    // both push a resolver sink adding into the SAME set for a given request
    // instance, because a fully prewarmed render may consume cached parse/decode
    // results without re-resolving those paths. Weakly keyed so completed
    // requests don't accumulate.
    private readonly ConditionalWeakTable<OffscreenRenderRequest, ConcurrentDictionary<string, byte>>
        _dataFolderFallbackHits = new();

    // A job is either a render (Request set) or a maintenance action (Maintenance
    // set) — both run on the render thread with the context current, preserving
    // ordering so e.g. a cache clear can't race in-flight renders.
    private readonly record struct RenderJob(
        OffscreenRenderRequest Request,
        bool EncodeAsPng,
        TaskCompletionSource<byte[]> Tcs,
        Action? Maintenance = null);

    internal GameWindowOffscreenRenderer(
        IOffscreenSceneBuilder sceneBuilder,
        AssetCache assetCache,
        GameAssetResolver assets,
        RenderLogGate logGate,
        IRenderLogger logger)
    {
        _sceneBuilder = sceneBuilder;
        _assetCache = assetCache;
        _assets = assets;
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
            Name = "SkyRender.OffscreenRender",
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
                // Builder caches first (the NPC builder's resolved mesh paths), then the
                // shared asset cache: the order the combined preview-cache clear used.
                _sceneBuilder.InvalidateCaches();
                _assetCache.Clear();
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
        if (_disposed || request == null) return Task.CompletedTask;

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
                    request.VanillaLooseOverridesModLoose,
                    request.AllowLoadOrderFallback);
                // Capture data-folder-fallback hits made during the prewarm
                // parse/decode too — the render may hit the warm caches and
                // never re-resolve these paths (see _dataFolderFallbackHits).
                using var fallbackSink = PushDataFolderFallbackSinkIfRequested(request);

                _sceneBuilder.Prewarm(request, ct);
                if (request.PrewarmFallbackPathsOut != null &&
                    _dataFolderFallbackHits.TryGetValue(request, out var prewarmHits) && !prewarmHits.IsEmpty)
                {
                    var fallbackPaths = new List<string>(prewarmHits.Keys);
                    fallbackPaths.Sort(StringComparer.OrdinalIgnoreCase);
                    request.PrewarmFallbackPathsOut.AddRange(fallbackPaths);
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
        // ApplyTextureOverrides / ApplyShapeDeformer paths) still sees the right
        // scope chain. AdditionalScopes (1.2.0+) overrides AdditionalDataFolders
        // (1.1.0) when both are provided.
        using var scopes = _assets.PushScopes(
            request.AdditionalScopes,
            request.AdditionalDataFolders,
            request.VanillaLooseOverridesBsa,
            request.VanillaLooseOverridesModLoose,
            request.AllowLoadOrderFallback);
        // Same per-request accumulator as the prewarm flow's push — resolves
        // the prewarm didn't cover (evicted parts, override paths) land in
        // the same set and are copied out once below.
        using var fallbackSink = PushDataFolderFallbackSinkIfRequested(request);
        IOffscreenScene? scene = null;
        try
        {
            // Protect this render's textures from mid-render eviction.
            _residentTextures?.BeginRenderPass();
            scene = _sceneBuilder.Build(
                new OffscreenSceneContext(request.Width, request.Height, _residentTextures), request);
            DrawScene(scene, request);
            long tDrawDone = Stopwatch.GetTimestamp();

            // Copy the deduped data-folder-fallback hits (prewarm + render
            // flows combined) into the request's opt-in out-list. Sorted for
            // deterministic host-side presentation/persistence.
            if (request.DataFolderFallbackPathsOut != null &&
                _dataFolderFallbackHits.TryGetValue(request, out var fallbackHits) &&
                !fallbackHits.IsEmpty)
            {
                var fallbackPaths = new List<string>(fallbackHits.Keys);
                fallbackPaths.Sort(StringComparer.OrdinalIgnoreCase);
                request.DataFolderFallbackPathsOut.AddRange(fallbackPaths);
            }

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
            //     the WPF Image control that a host's gallery uses to render
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
            scene?.Dispose();
            // The using on `scopes` above pops the resolver state. Removed the
            // per-render ClearExtractedFiles call — it raced with concurrent
            // interactive previews (whose loads share the same extraction
            // directory). Hosts that want to flush the cache should call
            // GameAssetResolver.ClearExtractedFiles() at quiescence (e.g. on
            // shutdown) instead.
        }
    }

    /// <summary>Pushes a resolver sink that records data-folder-fallback hits
    /// into the request's shared accumulator (see <see cref="_dataFolderFallbackHits"/>).
    /// Null when the request didn't opt in via
    /// <see cref="OffscreenRenderRequest.DataFolderFallbackPathsOut"/> —
    /// callers hold the result in a <c>using var</c>, which tolerates null.</summary>
    private IDisposable? PushDataFolderFallbackSinkIfRequested(OffscreenRenderRequest request)
    {
        if (request.DataFolderFallbackPathsOut == null && request.PrewarmFallbackPathsOut == null) return null;
        var hits = _dataFolderFallbackHits.GetValue(request,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        return _assets.PushDataFolderFallbackSink(p => hits.TryAdd(p, 0));
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

    /// <summary>Draws a built scene into the multisampled FBO with the request's
    /// background and records the draw time.</summary>
    private void DrawScene(IOffscreenScene scene, OffscreenRenderRequest request)
    {
        long tDrawStart = Stopwatch.GetTimestamp();

        // Re-bind the multisampled draw FBO (the scene build's GL calls may have
        // unbound it) and clear before we render the new scene. MSAA on this FBO is
        // implicit from the multisampled attachments; the explicit Multisample
        // enable is defensive — some drivers leave it disabled by default.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        GL.Enable(EnableCap.Multisample);
        GL.Viewport(0, 0, request.Width, request.Height);
        // The renderer's Render() resets GL.ClearColor from its own ClearColor on
        // every frame, so the request's background goes there; setting
        // GL.ClearColor alone would be stomped.
        float r = request.BackgroundRgb.R / 255f;
        float g = request.BackgroundRgb.G / 255f;
        float b = request.BackgroundRgb.B / 255f;
        scene.Renderer.ClearColor = new Vector3(r, g, b);
        GL.ClearColor(r, g, b, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Last chance to bail before the draw + readback + encode. Past this
        // point the work is a single GL pass we don't interrupt.
        request.Cancellation.ThrowIfCancellationRequested();
        scene.Renderer.Render(scene.Camera, request.Width, request.Height);

        if (request.TimingsOut is { } timings)
            timings.DrawMs = MsBetween(tDrawStart, Stopwatch.GetTimestamp());
    }

    private static double MsBetween(long startTimestamp, long endTimestamp)
        => (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;

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

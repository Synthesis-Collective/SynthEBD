using System;
using System.Collections.Concurrent;
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

    // Dedicated render thread + queue. The thread owns the GL context for
    // its lifetime so we never migrate context across threads.
    private readonly Thread _renderThread;
    private readonly BlockingCollection<RenderJob> _renderQueue = new();

    private readonly record struct RenderJob(
        OffscreenRenderRequest Request,
        bool EncodeAsPng,
        TaskCompletionSource<byte[]> Tcs);

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
                    job.Request.Cancellation.ThrowIfCancellationRequested();
                    byte[] result = RenderInternalCore(job.Request, job.EncodeAsPng);
                    job.Tcs.TrySetResult(result);
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
            try { DestroyFboIfPresent(); } catch { /* best-effort */ }
            try { _gw?.Context.MakeNoneCurrent(); } catch { /* best-effort */ }
            try { _gw?.Close(); } catch { /* best-effort */ }
            try { _gw?.Dispose(); } catch { /* best-effort */ }
            _gw = null;
        }
    }

    private byte[] RenderInternalCore(OffscreenRenderRequest request, bool encodeAsPng)
    {
        if (_gw == null) throw new InvalidOperationException("GameWindow not initialized.");

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
            vm.AdditionalScopes = request.AdditionalScopes;
            vm.AdditionalDataFolders = request.AdditionalDataFolders;
            vm.VanillaLooseOverridesBsa = request.VanillaLooseOverridesBsa;
            vm.VanillaLooseOverridesModLoose = request.VanillaLooseOverridesModLoose;
            LoadAndRender(vm, request);

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

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            return encodeAsPng
                ? EncodePngFromRgba(pixels, request.Width, request.Height)
                : RgbaToBgra(pixels);
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

    private void LoadAndRender(VM_CharacterViewer vm, OffscreenRenderRequest request)
    {
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
        vm.EnableAmbientOcclusion = request.EnableAmbientOcclusion;
        vm.SsaoRadius = request.SsaoRadius;
        vm.SsaoBias = request.SsaoBias;
        vm.SsaoIntensity = request.SsaoIntensity;
        vm.EnableEyeCatchlight = request.EnableEyeCatchlight;
        vm.SubsurfaceStrength = request.SubsurfaceStrength;
        vm.SkinSaturationBoost = request.SkinSaturationBoost;
        vm.VignetteRadius = request.VignetteRadius;
        vm.VignetteIntensity = request.VignetteIntensity;

        // Synchronously load + drain. The marshaller is inline so LoadAsync's
        // scene-queue handoff runs on this thread; ProcessPendingSceneToCompletion
        // then flushes the install queue against the bound FBO.
        var identity = new NpcIdentity("offscreen", "offscreen");
        vm.LoadAsync(identity, request.MeshPaths, request.OverrideHeadMeshAbsolutePath,
            request.Cancellation).GetAwaiter().GetResult();
        vm.ProcessPendingSceneToCompletion();

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
        if (request.Morphs != null)
        {
            vm.ApplyMorphSet(request.Morphs, request.MorphWeight);
        }

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

        vm.Renderer.Render(vm.Camera, request.Width, request.Height);
    }

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

    private static byte[] EncodePngFromRgba(byte[] rgba, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
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

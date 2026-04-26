using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using NumericsVec3 = System.Numerics.Vector3;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Reference <see cref="IOffscreenRenderer"/> implementation. Owns a hidden
/// <see cref="GameWindow"/> for the GL context, an FBO for offscreen
/// rendering, and an ImageSharp encoder for PNG output. One renderer
/// instance handles many requests serially via an internal lock.
///
/// <para><b>Threading constraint:</b> GLFW (which OpenTK's
/// <see cref="GameWindow"/> wraps) requires its first call to come from the
/// process's main thread — that's where it installs its event hook. The
/// constructor must therefore run on the main thread (typical: the WPF
/// dispatcher thread). Subsequent <see cref="RenderToPngAsync"/> /
/// <see cref="RenderToBgra32Async"/> calls run their work via
/// <see cref="Task.Run(Action)"/>, taking the internal lock and calling
/// <c>MakeCurrent</c> per render — the GL context is portable across threads,
/// only GLFW initialization has the main-thread requirement.</para>
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
    private readonly object _lock = new();
    private GameWindow? _gw;

    private int _fboHandle = -1;
    private int _colorTex = -1;
    private int _depthRbo = -1;
    private (int W, int H) _fboSize = (0, 0);

    private bool _disposed;

    // VM dependencies — passed to each per-request VM instance.
    private readonly CharacterPreviewCache _previewCache;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdParser;
    private readonly BodyTriFileParser _triParser;
    private readonly GameAssetResolver _assets;
    private readonly ICharacterViewerSettings _settings;
    private readonly CharacterViewerLogGate _logGate;
    private readonly ICharacterViewerLogger _logger;

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
    }

    public Task<byte[]> RenderToPngAsync(OffscreenRenderRequest request)
        => Task.Run(() => RenderInternal(request, encodeAsPng: true));

    public Task<byte[]> RenderToBgra32Async(OffscreenRenderRequest request)
        => Task.Run(() => RenderInternal(request, encodeAsPng: false));

    private byte[] RenderInternal(OffscreenRenderRequest request, bool encodeAsPng)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GameWindowOffscreenRenderer));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Width <= 0 || request.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(request),
                "Width and Height must be positive.");

        request.Cancellation.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GameWindowOffscreenRenderer));
            if (_gw == null) throw new InvalidOperationException("GameWindow not initialized.");

            _gw.MakeCurrent();
            EnsureFbo(request.Width, request.Height);

            var vm = new VM_CharacterViewer(
                _bodySlideDeformer, _bsdParser, _triParser, _assets,
                _settings, _previewCache, _logGate, _logger
                /* renderThread defaults to InlineRenderThreadMarshaller */);
            try
            {
                LoadAndRender(vm, request);

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
            }
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

        // Synchronously load + drain. The marshaller is inline so LoadAsync's
        // scene-queue handoff runs on this thread; ProcessPendingSceneToCompletion
        // then flushes the install queue against the bound FBO.
        var identity = new NpcIdentity("offscreen", "offscreen");
        vm.LoadAsync(identity, request.MeshPaths, request.OverrideHeadMeshAbsolutePath,
            request.Cancellation).GetAwaiter().GetResult();
        vm.ProcessPendingSceneToCompletion();

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

        // Re-bind the FBO (VM's GL calls may have unbound it) and clear
        // before we render the new scene.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboHandle);
        GL.Viewport(0, 0, request.Width, request.Height);
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
    private static void ConfigureCamera(VM_CharacterViewer vm, OffscreenRenderRequest request)
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
                ConfigureMeshAwareCamera(vm, meshAware, request.Width, request.Height);
                break;
            }
        }
    }

    /// <summary>Computes the framing bbox from <paramref name="meshAware"/>'s
    /// shape selectors / filters / paddings, then sets the orbit camera's
    /// Distance + Target so the bbox fits inside the requested framing band
    /// at the framebuffer's aspect ratio.
    ///
    /// <para>Empty match (no shapes survived the selectors) leaves the camera
    /// at its current state — the renderer logs and renders without
    /// re-framing rather than throwing, so a host that misconfigures a
    /// selector doesn't lose a whole batch of mugshots.</para></summary>
    private static void ConfigureMeshAwareCamera(VM_CharacterViewer vm,
        CameraFraming.MeshAware meshAware, int width, int height)
    {
        var camera = vm.Camera;
        camera.Azimuth = meshAware.Yaw;
        camera.Elevation = meshAware.Pitch;

        var allMeshes = vm.Renderer.Meshes;
        if (allMeshes.Count == 0) return;

        // Collect per-FramingShape bboxes, then union.
        var unionMin = new NumericsVec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var unionMax = new NumericsVec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool anyContribution = false;

        foreach (var shape in meshAware.Shapes)
        {
            var matched = MatchShapes(allMeshes, shape.Selector);
            if (matched.Count == 0) continue;

            // Resolve the filter's reference Y bound, if any.
            float? minYBound = ResolveFilterMinY(shape.Filter, allMeshes);

            foreach (var mesh in matched)
            {
                var verts = mesh.CpuPositions;
                if (verts == null || verts.Length == 0) continue;

                NumericsVec3 mn = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                NumericsVec3 mx = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                bool meshHadVerts = false;

                for (int i = 0; i < verts.Length; i++)
                {
                    var v = verts[i];
                    if (minYBound.HasValue && v.Y < minYBound.Value) continue;
                    mn = NumericsVec3.Min(mn, v);
                    mx = NumericsVec3.Max(mx, v);
                    meshHadVerts = true;
                }
                if (!meshHadVerts) continue;

                if (shape.Padding > 0f)
                {
                    var pad = new NumericsVec3(shape.Padding, shape.Padding, shape.Padding);
                    mn -= pad;
                    mx += pad;
                }

                unionMin = NumericsVec3.Min(unionMin, mn);
                unionMax = NumericsVec3.Max(unionMax, mx);
                anyContribution = true;
            }
        }

        if (!anyContribution) return;

        // Camera target = bbox center on Y; X/Z stay 0 so the orbit revolves
        // around the character's vertical axis instead of an off-axis point.
        float centerY = (unionMin.Y + unionMax.Y) * 0.5f;
        camera.Target = new Vector3(0f, centerY, 0f);

        // Fit the bbox vertical extent into the framing band (top-bottom
        // fractions of the framebuffer). The band defines what fraction of
        // the FBO the bbox should occupy vertically; distance scales
        // inversely with band size.
        float bboxHeight = unionMax.Y - unionMin.Y;
        float bboxWidth = unionMax.X - unionMin.X;
        float band = MathF.Max(0.05f, meshAware.FrameTopFraction - meshAware.FrameBottomFraction);

        // Vertical fit: bboxHeight / band must equal 2 * D * tan(fov/2).
        float halfFovRad = MathHelper.DegreesToRadians(camera.FieldOfView * 0.5f);
        float tanHalfFov = MathF.Tan(halfFovRad);
        float distanceForHeight = (bboxHeight / band) / (2f * tanHalfFov);

        // Horizontal fit at this aspect: bboxWidth must fit in
        // 2 * D * tan(fov/2) * aspect. If horizontal would clip, push back.
        float aspect = (height > 0) ? (float)width / height : 1f;
        float distanceForWidth = (bboxWidth) / (2f * tanHalfFov * aspect);

        float distance = MathF.Max(distanceForHeight, distanceForWidth);
        camera.Distance = MathF.Max(camera.MinDistance, distance);
    }

    private static IReadOnlyList<GlMesh> MatchShapes(IReadOnlyList<GlMesh> all, FramingShapeSelector selector)
    {
        return selector switch
        {
            FramingShapeSelector.AllLoaded => all,

            FramingShapeSelector.PrimaryHead =>
                all.Where(m => m.IsPrimaryHeadShape).ToList(),

            FramingShapeSelector.HeadAccessories =>
                all.Where(m => string.Equals(m.BodyPart, "Head", StringComparison.OrdinalIgnoreCase)
                               && !m.IsPrimaryHeadShape).ToList(),

            FramingShapeSelector.BodyPart bp =>
                all.Where(m => string.Equals(m.BodyPart, bp.Name, StringComparison.OrdinalIgnoreCase)).ToList(),

            FramingShapeSelector.ShapeNameContains snc =>
                all.Where(m => m.ShapeName.Contains(snc.Substring, StringComparison.OrdinalIgnoreCase)
                               && (snc.InBodyPart == null
                                   || string.Equals(m.BodyPart, snc.InBodyPart, StringComparison.OrdinalIgnoreCase)))
                   .ToList(),

            _ => Array.Empty<GlMesh>(),
        };
    }

    private static float? ResolveFilterMinY(FramingShapeFilter? filter, IReadOnlyList<GlMesh> all)
    {
        switch (filter)
        {
            case null:
                return null;

            case FramingShapeFilter.AboveWorldY abs:
                return abs.Y;

            case FramingShapeFilter.AboveLowerYOfPrimaryHead:
            {
                var primary = all.FirstOrDefault(m => m.IsPrimaryHeadShape);
                return MinYOf(primary);
            }

            case FramingShapeFilter.AboveLowerYOfBodyPart bp:
            {
                float minY = float.PositiveInfinity;
                bool found = false;
                foreach (var m in all)
                {
                    if (!string.Equals(m.BodyPart, bp.BodyPart, StringComparison.OrdinalIgnoreCase)) continue;
                    var y = MinYOf(m);
                    if (y.HasValue && y.Value < minY) { minY = y.Value; found = true; }
                }
                return found ? minY : null;
            }

            default:
                return null;
        }
    }

    private static float? MinYOf(GlMesh? mesh)
    {
        if (mesh?.CpuPositions == null || mesh.CpuPositions.Length == 0) return null;
        float min = float.PositiveInfinity;
        foreach (var v in mesh.CpuPositions)
            if (v.Y < min) min = v.Y;
        return float.IsFinite(min) ? min : null;
    }

    /// <summary>Lazily creates / resizes the FBO. Called inside the render
    /// lock; the FBO is reused across same-size requests.</summary>
    private void EnsureFbo(int width, int height)
    {
        if (_fboSize == (width, height) && _fboHandle != -1) return;

        DestroyFboIfPresent();

        _fboHandle = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboHandle);

        _colorTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _colorTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTex, 0);

        _depthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
            RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depthRbo);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "Offscreen FBO incomplete: " + status + " (size=" + width + "x" + height + ")");
        }
        _fboSize = (width, height);
    }

    private void DestroyFboIfPresent()
    {
        if (_depthRbo != -1) { GL.DeleteRenderbuffer(_depthRbo); _depthRbo = -1; }
        if (_colorTex != -1) { GL.DeleteTexture(_colorTex); _colorTex = -1; }
        if (_fboHandle != -1) { GL.DeleteFramebuffer(_fboHandle); _fboHandle = -1; }
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
        lock (_lock)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            if (_gw != null)
            {
                try { _gw.MakeCurrent(); DestroyFboIfPresent(); } catch { /* best-effort */ }
                try { _gw.Close(); } catch { /* best-effort */ }
                _gw.Dispose();
                _gw = null;
            }
        }
        return ValueTask.CompletedTask;
    }
}

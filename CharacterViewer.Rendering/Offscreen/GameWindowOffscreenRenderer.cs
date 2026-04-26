using System;
using System.IO;
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
/// Phase D.1 implementation status:
///   * GL context creation, FBO allocation, clear-color render, glReadPixels,
///     vertical flip, and PNG encoding all wired and tested.
///   * Mesh rendering deferred to Phase D.2 — this MVP produces an image
///     of the requested background color at the requested resolution. This
///     proves the offscreen GL pipeline works (and that <see cref="GameWindow"/>
///     coexists with WPF's <c>GLWpfControl</c> in the same process) without
///     needing the full mesh-load + texture-apply refactor.
///   * NPC Plugin Chooser 2 can integrate against this API surface now;
///     when D.2 lands the rendered output starts including the actual
///     character mesh without any caller-side changes required.
/// </summary>
public sealed class GameWindowOffscreenRenderer : IOffscreenRenderer
{
    private readonly object _lock = new();
    private GameWindow? _gw;
    private bool _glInitialized;

    private int _fboHandle = -1;
    private int _colorTex = -1;
    private int _depthRbo = -1;
    private (int W, int H) _fboSize = (0, 0);

    private bool _disposed;

    internal GameWindowOffscreenRenderer()
    {
        // Construct the hidden GameWindow on the constructing thread (typical
        // behaviour expected by GLFW). It stays at 8×8 and never becomes
        // visible — the actual render target is the FBO, which can be any
        // size independent of the window.
        var nws = new NativeWindowSettings
        {
            ClientSize = new Vector2i(8, 8),
            StartVisible = false,
            APIVersion = new Version(4, 0),
            Profile = ContextProfile.Core,
            // Don't grab focus or steal input; this is purely a context host.
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

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboHandle);
            GL.Viewport(0, 0, request.Width, request.Height);

            float r = request.BackgroundRgb.R / 255f;
            float g = request.BackgroundRgb.G / 255f;
            float b = request.BackgroundRgb.B / 255f;
            GL.ClearColor(r, g, b, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // PHASE D.2 TODO: load meshes from request.MeshPaths via NifMeshBuilder,
            // upload to GL, apply textures + morphs, configure camera/lighting,
            // render via GlRenderer. For D.1 we just clear and read back.

            byte[] pixels = ReadPixelsRgba(request.Width, request.Height);
            FlipVertical(pixels, request.Width, request.Height);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            return encodeAsPng
                ? EncodePngFromRgba(pixels, request.Width, request.Height)
                : RgbaToBgra(pixels);
        }
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

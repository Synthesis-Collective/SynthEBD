using System;
using System.Threading.Tasks;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Renders an <see cref="OffscreenRenderRequest"/> to a PNG (or raw BGRA32
/// pixel buffer) without putting anything on screen. NPC Plugin Chooser 2
/// uses this in place of its C++ NPC Portrait Creator subprocess to
/// produce mugshots in-process.
///
/// Implementations are expected to own their own GL context (a hidden
/// <c>OpenTK.Windowing.Desktop.GameWindow</c> in the default
/// implementation) so they don't compete with WPF's <c>GLWpfControl</c>
/// for context state. Calls are serialized internally — one render at a
/// time per renderer instance.
///
/// Renderer instances are designed to be reused across many requests; the
/// GL context, shader programs, and FBO allocation are amortized. Dispose
/// when the host shuts down (or when no more renders are expected).
/// </summary>
public interface IOffscreenRenderer : IAsyncDisposable
{
    /// <summary>Renders the request and returns a PNG-encoded byte array
    /// suitable for writing directly to disk.</summary>
    Task<byte[]> RenderToPngAsync(OffscreenRenderRequest request);

    /// <summary>Renders the request and returns the raw BGRA32 pixel buffer
    /// (4 bytes per pixel, top-left origin). Useful for callers that want to
    /// skip the PNG encode and feed the pixels into their own image pipeline
    /// (e.g. WPF's <c>WriteableBitmap</c>).</summary>
    Task<byte[]> RenderToBgra32Async(OffscreenRenderRequest request);

    /// <summary>Drops cached assets the environment change may have invalidated
    /// (resolved disk paths / decoded pixels / uploaded GL textures). Call when
    /// the host rebuilds its environment (game path or load order changed). Safe
    /// from any thread — GL-side work is marshalled onto the renderer's own
    /// thread; a no-op on a disposed renderer.</summary>
    void InvalidateCaches();
}

using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>
/// A host-supplied overlay drawn on top of the rendered scene. Register instances in
/// <see cref="GlRenderer.Overlays"/>; <see cref="Draw"/> runs once per frame on the GL thread and
/// composes the renderer's overlay primitives (<see cref="GlRenderer.BeginOverlaySurfacePass"/>,
/// <see cref="GlRenderer.BeginOverlayLinePass"/> and the <c>DrawOverlay*</c> methods).
/// </summary>
public interface IRenderOverlay
{
    void Draw(GlRenderer renderer, in OverlayContext context);
}

/// <summary>Per-frame camera and viewport data handed to <see cref="IRenderOverlay.Draw"/>.</summary>
public readonly struct OverlayContext
{
    public OverlayContext(Matrix4 view, Matrix4 projection, int viewportWidth, int viewportHeight)
    {
        View = view;
        Projection = projection;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public Matrix4 View { get; }
    public Matrix4 Projection { get; }
    public int ViewportWidth { get; }
    public int ViewportHeight { get; }
}

/// <summary>One coloured line segment for <see cref="GlRenderer.DrawOverlayLines"/>, in the
/// renderer's pre-ModelScale mesh-local space.</summary>
public struct OverlayLineSegment
{
    public Vector3 A;
    public Vector3 B;
    public Vector3 Color;

    /// <summary>Optional host label (for example a hover tooltip). The renderer never reads it.</summary>
    public string? Label;
}

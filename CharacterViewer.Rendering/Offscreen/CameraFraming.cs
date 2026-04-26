using System;
using System.Collections.Generic;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// How the offscreen renderer positions its camera relative to the loaded
/// character. Three cases cover the common workflows:
///
///   * <see cref="Portrait"/> — quick auto-frame of the head using the NPC's
///     base height (no mesh introspection). Cheap, simple, ignores hair.
///   * <see cref="Fixed"/> — explicit camera placement. NPC2's existing
///     "Fixed" Portrait Creator mode maps directly here.
///   * <see cref="MeshAware"/> — frame around a bounding box computed from
///     selected loaded shapes, with optional vertex filters and padding.
///     This is the framing mode that mirrors NPC Portrait Creator's
///     hair-aware algorithm (and lets future hosts pick whatever shape
///     subset they care about).
/// </summary>
public abstract record CameraFraming
{
    /// <summary>Default portrait framing — the camera frames the head with
    /// a small bottom margin and a neutral azimuth/elevation.</summary>
    public static CameraFraming PortraitDefault { get; } = new Portrait(0.95f, 0.10f);

    /// <summary>Auto-frames the head using the NPC's base-height scale only;
    /// hair is not considered. <paramref name="HeadTopOffset"/> places the
    /// top of the head at this fraction of the framebuffer height (1.0 =
    /// top edge); <paramref name="HeadBottomOffset"/> places the bottom of
    /// the head at this fraction from the framebuffer bottom (0.0 = bottom
    /// edge). Cheap and reliable for plain humanoid heads. Use
    /// <see cref="MeshAware"/> when hair shouldn't be cropped.</summary>
    public sealed record Portrait(float HeadTopOffset, float HeadBottomOffset) : CameraFraming;

    /// <summary>Explicit eye-position camera placement. Yaw/Pitch/Roll are
    /// degrees; X/Y/Z is the camera position in world units; Fov is vertical
    /// field of view in degrees. The renderer hardcodes the orbit target to
    /// the head's world Y position and back-solves orbit params from the
    /// (X, Y, Z) eye offset, so this case is lossy if the desired target
    /// is anywhere except above-origin at head height.
    ///
    /// <para>For a manual-mode UI where the user pans + orbits + zooms
    /// directly via mouse interaction, prefer <see cref="OrbitState"/> —
    /// it carries the full orbit state losslessly so preview and saved
    /// PNG framing match exactly.</para>
    ///
    /// <para>Roll and per-render Fov overrides aren't currently honored by
    /// the orbit camera and are reserved for a future enhancement.</para></summary>
    public sealed record Fixed(
        float Yaw, float Pitch, float Roll,
        float X, float Y, float Z,
        float Fov) : CameraFraming;

    /// <summary>Direct orbit-camera state. Bypasses any auto-framing or
    /// eye-to-orbit conversion — the renderer writes the values onto
    /// <c>vm.Camera</c> verbatim. Used by hosts that drive the camera
    /// from interactive UI (drag-to-orbit, scroll-to-zoom, middle-drag
    /// to pan) and want the saved PNG to exactly match the preview's
    /// framing.
    ///
    /// <para><see cref="Distance"/> is the orbit radius (must be positive;
    /// clamped to the camera's MinDistance). <see cref="Azimuth"/> +
    /// <see cref="Elevation"/> are the orbit angles in degrees (180° / 0°
    /// = facing the character from the front). <see cref="TargetX"/> /
    /// <see cref="TargetY"/> / <see cref="TargetZ"/> is the world-space
    /// point the camera orbits around.</para></summary>
    public sealed record OrbitState(
        float Distance,
        float Azimuth,
        float Elevation,
        float TargetX,
        float TargetY,
        float TargetZ) : CameraFraming;

    /// <summary>
    /// Frames around a bounding box computed from selected loaded shapes
    /// (after mesh load, after morphs applied — uses CPU-side vertex
    /// positions). Each <see cref="FramingShape"/> contributes a per-shape
    /// bbox (with optional vertex filter and padding); the renderer
    /// unions them and sets the camera distance + target so the unioned
    /// bbox fits inside the framing band.
    ///
    /// <para>NPC Portrait Creator's algorithm — the head plus the hair
    /// portion above the head's lower bound — is expressible as:</para>
    /// <code>
    /// new MeshAware(new[]
    /// {
    ///     new FramingShape { Selector = FramingShapeSelector.PrimaryHead.Instance },
    ///     new FramingShape
    ///     {
    ///         Selector = FramingShapeSelector.HeadAccessories.Instance,
    ///         Filter   = new FramingShapeFilter.AboveLowerYOfPrimaryHead(),
    ///     },
    /// })
    /// </code>
    /// </summary>
    public sealed record MeshAware(
        IReadOnlyList<FramingShape> Shapes,
        float FrameTopFraction = 0.95f,
        float FrameBottomFraction = 0.10f,
        float Yaw = 180f,
        float Pitch = 0f) : CameraFraming;
}

/// <summary>
/// One contributor to <see cref="CameraFraming.MeshAware"/>'s framing bbox.
/// The renderer matches loaded shapes via <see cref="Selector"/>, applies
/// the optional <see cref="Filter"/> per-vertex, builds a bbox, adds
/// <see cref="Padding"/>, and unions with the other shapes' contributions.
/// </summary>
public sealed record FramingShape
{
    /// <summary>Which shapes to match. See <see cref="FramingShapeSelector"/>
    /// for the available patterns.</summary>
    public FramingShapeSelector Selector { get; init; } = FramingShapeSelector.AllLoaded.Instance;

    /// <summary>Optional vertex-level filter. Only vertices that pass
    /// contribute to the bbox.</summary>
    public FramingShapeFilter? Filter { get; init; }

    /// <summary>Padding (world units) added around this shape's bbox before
    /// unioning with the other framing shapes. Useful for guaranteeing the
    /// camera doesn't crop hard against the silhouette.</summary>
    public float Padding { get; init; }
}

/// <summary>
/// Pattern for matching loaded shapes by body part / shape name / role.
/// Maps to the <c>BodyPart</c>, <c>ShapeName</c>, and
/// <c>IsPrimaryHeadShape</c> properties on the rendering tier's GlMesh.
/// </summary>
public abstract record FramingShapeSelector
{
    /// <summary>Match every loaded shape regardless of body part.</summary>
    public sealed record AllLoaded : FramingShapeSelector
    {
        public static AllLoaded Instance { get; } = new();
    }

    /// <summary>Match every shape in the named body part (case-insensitive).
    /// Body parts the renderer uses today: <c>"Body"</c>, <c>"Hands"</c>,
    /// <c>"Feet"</c>, <c>"Head"</c>. The Head body part contains the face
    /// plus accessories (hair, eyes, brows, mouth) — use
    /// <see cref="PrimaryHead"/> / <see cref="HeadAccessories"/> to
    /// distinguish them.</summary>
    public sealed record BodyPart(string Name) : FramingShapeSelector;

    /// <summary>Match shapes whose <c>ShapeName</c> contains the given
    /// substring (case-insensitive). Optionally also constrained to a
    /// body part. Useful for matching hair shapes by name pattern when
    /// the shape isn't tagged as a head accessory by the NIF (rare).</summary>
    public sealed record ShapeNameContains(string Substring, string? InBodyPart = null) : FramingShapeSelector;

    /// <summary>Match the single shape the NIF parser flagged as the
    /// primary head — the face mesh, identified by partition flags +
    /// height heuristic. Empty match if no head NIF is loaded.</summary>
    public sealed record PrimaryHead : FramingShapeSelector
    {
        public static PrimaryHead Instance { get; } = new();
    }

    /// <summary>Match every shape in the Head body part EXCEPT the primary
    /// head — i.e. hair, eyes, brows, mouth, scars, etc.</summary>
    public sealed record HeadAccessories : FramingShapeSelector
    {
        public static HeadAccessories Instance { get; } = new();
    }
}

/// <summary>
/// Per-vertex filter applied during bbox computation. Lets a host narrow a
/// shape's contribution (e.g. include only the hair portion above the head's
/// chin so floor-length braids don't blow out the framing).
/// </summary>
public abstract record FramingShapeFilter
{
    /// <summary>Include only vertices with world-Y above the lower-Y bound
    /// of the primary head shape. Zero-padding equivalent of NPC Portrait
    /// Creator's "hair above head bottom" rule.</summary>
    public sealed record AboveLowerYOfPrimaryHead : FramingShapeFilter
    {
        public static AboveLowerYOfPrimaryHead Instance { get; } = new();
    }

    /// <summary>Include only vertices with world-Y above the lower-Y bound
    /// of all shapes in the named body part.</summary>
    public sealed record AboveLowerYOfBodyPart(string BodyPart) : FramingShapeFilter;

    /// <summary>Include only vertices with world-Y above this absolute
    /// world-space value.</summary>
    public sealed record AboveWorldY(float Y) : FramingShapeFilter;
}

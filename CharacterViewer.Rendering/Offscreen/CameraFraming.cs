namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// How the offscreen renderer positions its camera relative to the loaded
/// character. Two cases cover both NPC Plugin Chooser 2's existing Portrait
/// Creator camera modes (portrait + fixed) and any future programmatic
/// framing needs.
/// </summary>
public abstract record CameraFraming
{
    /// <summary>Default portrait framing — the camera frames the head with
    /// a small bottom margin and a neutral azimuth/elevation.</summary>
    public static CameraFraming PortraitDefault { get; } = new Portrait(0.95f, 0.10f);

    /// <summary>Auto-frames the head, with the top of the head at
    /// <paramref name="HeadTopOffset"/> of the framebuffer height (1.0 = top edge)
    /// and the head's bottom at <paramref name="HeadBottomOffset"/> from the
    /// framebuffer bottom (0.0 = bottom edge). Mirrors NPC Plugin Chooser 2's
    /// "Portrait" camera mode.</summary>
    public sealed record Portrait(float HeadTopOffset, float HeadBottomOffset) : CameraFraming;

    /// <summary>Explicit camera placement. Mirrors NPC Plugin Chooser 2's
    /// "Fixed" camera mode. Yaw/Pitch/Roll are degrees; X/Y/Z is the camera
    /// position in world units; Fov is vertical field of view in degrees.</summary>
    public sealed record Fixed(
        float Yaw, float Pitch, float Roll,
        float X, float Y, float Z,
        float Fov) : CameraFraming;
}

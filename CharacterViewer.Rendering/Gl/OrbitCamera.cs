using System;
using OpenTK.Mathematics;

namespace CharacterViewer.Rendering;

/// <summary>
/// Mouse-driven orbit camera for the 3D character viewer.
/// Orbits around a target point with configurable distance, azimuth, and elevation.
/// </summary>
public class OrbitCamera
{
    /// <summary>
    /// Raised whenever one of the five view-defining properties (<see cref="Azimuth"/>,
    /// <see cref="Elevation"/>, <see cref="Distance"/>, <see cref="Target"/>,
    /// <see cref="FieldOfView"/>) actually changes value.
    ///
    /// <para>Exists for the BodySlide Compare window's "Lock camera" mode, which mirrors one
    /// pane's view onto the other. Raising from the setters rather than from the mouse
    /// handlers means programmatic reframes — <c>MeshAwareCameraFitter.ApplyTo</c> after an
    /// NPC load, the FOV spinner — propagate too, which is what a locked pair should do.</para>
    ///
    /// <para>Mirror the change with <see cref="CopyViewFrom"/>, never by assigning the
    /// properties directly: it suppresses the event on the receiving camera, without which
    /// two locked cameras would echo each other into an infinite loop.</para>
    /// </summary>
    public event Action? ViewChanged;

    /// <summary>Non-zero while <see cref="CopyViewFrom"/> is applying a mirrored view; gates
    /// <see cref="RaiseViewChanged"/>. A counter rather than a bool so nested/re-entrant
    /// applies can't clear the guard early.</summary>
    private int _suppressViewChanged;

    private void RaiseViewChanged()
    {
        if (_suppressViewChanged > 0) return;
        ViewChanged?.Invoke();
    }

    private float _azimuth = 180f;
    private float _elevation = 15f;
    private float _distance = 200f;
    private Vector3 _target = new Vector3(0, 85, 0); // roughly chest height

    /// <summary>Horizontal angle in degrees. 180 = camera placed at -Z looking toward
    /// +Z, which shows the character's front (the character is oriented to face -Z_world
    /// after the NIF Z-up to Y-up R_X(-90) conversion).</summary>
    public float Azimuth
    {
        get => _azimuth;
        set { if (_azimuth == value) return; _azimuth = value; RaiseViewChanged(); }
    }

    /// <summary>Vertical angle in degrees. 0 = horizontal, positive = looking down.</summary>
    public float Elevation
    {
        get => _elevation;
        set { if (_elevation == value) return; _elevation = value; RaiseViewChanged(); }
    }

    /// <summary>Distance from the target point.</summary>
    public float Distance
    {
        get => _distance;
        set { if (_distance == value) return; _distance = value; RaiseViewChanged(); }
    }

    /// <summary>The point the camera orbits around (Y-up world space).</summary>
    public Vector3 Target
    {
        get => _target;
        set { if (_target == value) return; _target = value; RaiseViewChanged(); }
    }

    /// <summary>Minimum zoom distance.</summary>
    public float MinDistance { get; set; } = 5f;

    /// <summary>Maximum zoom distance.</summary>
    public float MaxDistance { get; set; } = 2000f;

    /// <summary>Near clip plane distance.</summary>
    public float NearPlane { get; set; } = 0.1f;

    /// <summary>Far clip plane distance.</summary>
    public float FarPlane { get; set; } = 10000f;

    private float _fieldOfView = 25f;

    /// <summary>Vertical field of view in degrees. Default 25° matches NPC
    /// Portrait Creator, which gives a flatter, more natural perspective for
    /// portrait framing than the wider 45° gameplay-style default.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set { if (_fieldOfView == value) return; _fieldOfView = value; RaiseViewChanged(); }
    }

    /// <summary>
    /// Copies the five view-defining properties from <paramref name="source"/> without
    /// raising <see cref="ViewChanged"/> on this camera. The suppression is the whole point:
    /// mirroring by plain assignment would make each locked camera re-notify its partner and
    /// spin forever. No-ops on a null or self source.
    /// </summary>
    public void CopyViewFrom(OrbitCamera source)
    {
        if (source == null || ReferenceEquals(source, this)) return;

        _suppressViewChanged++;
        try
        {
            Azimuth = source.Azimuth;
            Elevation = source.Elevation;
            Distance = source.Distance;
            Target = source.Target;
            FieldOfView = source.FieldOfView;
        }
        finally
        {
            _suppressViewChanged--;
        }
    }

    // Mouse interaction state
    private float _lastMouseX, _lastMouseY;
    private bool _isOrbiting;
    private bool _isPanning;

    public Matrix4 GetViewMatrix()
    {
        float azRad = MathHelper.DegreesToRadians(Azimuth);
        float elRad = MathHelper.DegreesToRadians(Elevation);

        float cosEl = MathF.Cos(elRad);
        float sinEl = MathF.Sin(elRad);
        float cosAz = MathF.Cos(azRad);
        float sinAz = MathF.Sin(azRad);

        // Camera position in spherical coordinates relative to target
        var offset = new Vector3(
            cosEl * sinAz,
            sinEl,
            cosEl * cosAz
        ) * Distance;

        var eye = Target + offset;
        return Matrix4.LookAt(eye, Target, Vector3.UnitY);
    }

    public Matrix4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FieldOfView),
            aspectRatio,
            NearPlane,
            FarPlane);
    }

    /// <summary>Gets the camera position in world space.</summary>
    public Vector3 GetEyePosition()
    {
        float azRad = MathHelper.DegreesToRadians(Azimuth);
        float elRad = MathHelper.DegreesToRadians(Elevation);
        var offset = new Vector3(
            MathF.Cos(elRad) * MathF.Sin(azRad),
            MathF.Sin(elRad),
            MathF.Cos(elRad) * MathF.Cos(azRad)
        ) * Distance;
        return Target + offset;
    }

    public void OnMouseDown(float x, float y, bool leftButton, bool middleButton)
    {
        _lastMouseX = x;
        _lastMouseY = y;
        _isOrbiting = leftButton;
        _isPanning = middleButton;
    }

    public void OnMouseUp()
    {
        _isOrbiting = false;
        _isPanning = false;
    }

    public void OnMouseMove(float x, float y)
    {
        float dx = x - _lastMouseX;
        float dy = y - _lastMouseY;
        _lastMouseX = x;
        _lastMouseY = y;

        if (_isOrbiting)
        {
            Azimuth -= dx * 0.5f;
            Elevation += dy * 0.5f;
            Elevation = Math.Clamp(Elevation, -89f, 89f);
        }
        else if (_isPanning)
        {
            // Pan perpendicular to the view direction
            float azRad = MathHelper.DegreesToRadians(Azimuth);
            var right = new Vector3(MathF.Cos(azRad), 0, -MathF.Sin(azRad));
            var up = Vector3.UnitY;

            float panScale = Distance * 0.002f;
            Target -= right * dx * panScale;
            Target += up * dy * panScale;
        }
    }

    public void OnMouseWheel(float delta)
    {
        // WPF sends delta in units of 120 per notch; normalize to ~0.1 per notch
        float normalized = delta / 120f;
        float zoomFactor = 1f - normalized * 0.1f;
        Distance = Math.Clamp(Distance * zoomFactor, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Converts a screen-space mouse position to a world-space ray (origin + direction)
    /// for hit testing. Mouse coordinates are in WPF logical units (top-left origin).
    /// </summary>
    public (Vector3 Origin, Vector3 Direction) ScreenPointToRay(
        float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        // Convert to NDC (-1..+1, with Y flipped for OpenGL)
        float ndcX = (2f * mouseX / viewportWidth) - 1f;
        float ndcY = 1f - (2f * mouseY / viewportHeight);

        float aspect = viewportWidth / viewportHeight;
        var projection = GetProjectionMatrix(aspect);
        var view = GetViewMatrix();

        // Unproject near/far points
        var invVP = Matrix4.Invert(view * projection);

        var nearNdc = new Vector4(ndcX, ndcY, -1f, 1f);
        var farNdc = new Vector4(ndcX, ndcY, 1f, 1f);

        var nearWorld = nearNdc * invVP;
        var farWorld = farNdc * invVP;

        nearWorld /= nearWorld.W;
        farWorld /= farWorld.W;

        var origin = nearWorld.Xyz;
        var direction = Vector3.Normalize(farWorld.Xyz - nearWorld.Xyz);

        return (origin, direction);
    }

    public void Reset()
    {
        Azimuth = 180f;
        Elevation = 15f;
        Distance = 200f;
        Target = new Vector3(0, 85, 0);
    }
}

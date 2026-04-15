using System;
using OpenTK.Mathematics;

namespace SynthEBD;

/// <summary>
/// Mouse-driven orbit camera for the 3D character viewer.
/// Orbits around a target point with configurable distance, azimuth, and elevation.
/// </summary>
public class OrbitCamera
{
    /// <summary>Horizontal angle in degrees. 180 = looking from front (-Z toward +Z, matching NIF character facing).</summary>
    public float Azimuth { get; set; } = 180f;

    /// <summary>Vertical angle in degrees. 0 = horizontal, positive = looking down.</summary>
    public float Elevation { get; set; } = 15f;

    /// <summary>Distance from the target point.</summary>
    public float Distance { get; set; } = 200f;

    /// <summary>The point the camera orbits around (Y-up world space).</summary>
    public Vector3 Target { get; set; } = new Vector3(0, 85, 0); // roughly chest height

    /// <summary>Minimum zoom distance.</summary>
    public float MinDistance { get; set; } = 5f;

    /// <summary>Maximum zoom distance.</summary>
    public float MaxDistance { get; set; } = 2000f;

    /// <summary>Near clip plane distance.</summary>
    public float NearPlane { get; set; } = 0.1f;

    /// <summary>Far clip plane distance.</summary>
    public float FarPlane { get; set; } = 10000f;

    /// <summary>Vertical field of view in degrees.</summary>
    public float FieldOfView { get; set; } = 45f;

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

    public void Reset()
    {
        Azimuth = 180f;
        Elevation = 15f;
        Distance = 200f;
        Target = new Vector3(0, 85, 0);
    }
}

using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>
/// Distance fog, the engine's form (Community Shaders' Lighting.hlsl and Water.hlsl, which keep
/// the vanilla shaders' fog): with d the distance from the camera,
/// <c>f = min(Max, saturate((d - Near) / (Far - Near)) ^ Power)</c>, the fog colour is
/// <c>lerp(NearColor, FarColor, f)</c>, and the surface colour moves toward it by <c>f</c>.
/// Distances are world units; colours are 0..1 RGB.
/// </summary>
public sealed record DistanceFog(Vector3 NearColor, Vector3 FarColor, float Near, float Far, float Power = 1f, float Max = 1f)
{
    /// <summary>The fog amount at distance <paramref name="distance"/>, as the shader computes it.</summary>
    public float Amount(float distance)
    {
        float range = MathF.Max(Far - Near, 1e-3f);
        float x = Math.Clamp(distance / range - Near / range, 0f, 1f);
        return MathF.Min(Max, MathF.Pow(x, Power));
    }
}

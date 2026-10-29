using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>
/// A point light (for a Skyrim cell, a placed LIGH reference), in the same pre-<c>ModelScale</c>
/// mesh space as the scene's geometry. <see cref="Color"/> is linear RGB already multiplied by the
/// light's fade; <see cref="Radius"/> is the distance at which it reaches zero. The engine's
/// falloff (Community Shaders' Lighting.hlsl, the vanilla point-light term):
/// <c>color x (1 - saturate(d / radius)^2)</c>, with Lambert diffuse on top.
/// </summary>
public readonly record struct PointLight(Vector3 Position, Vector3 Color, float Radius)
{
    /// <summary>The light casts shadows (an omnidirectional shadow cube map, out to its radius), as
    /// the engine's shadow-casting lights do. The first <see cref="GlRenderer.MaxShadowedPointLights"/>
    /// such lights in <see cref="GlRenderer.PointLights"/> get one; later ones draw unshadowed.</summary>
    public bool CastsShadow { get; init; }
}

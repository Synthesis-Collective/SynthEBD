using System.Numerics;

namespace SkyRender.World;

/// <summary>
/// A flat water surface over a rectangle of the world (usually one exterior cell), as plain data.
/// Skyrim world units, Z up. The host resolves the surface height (the cell's own water height or
/// the worldspace default) and the water type's colours.
/// </summary>
public sealed record WaterSurface(float Height, float X0, float Y0, float X1, float Y1, WaterMaterial Material)
{
    /// <summary>A label for logs and the mesh's shape name.</summary>
    public string Label { get; init; } = "water";
}

/// <summary>
/// How a water surface is coloured: the water type's shallow, deep and reflection colours (0..1),
/// its reflectivity and Fresnel F0, and its first noise texture, drawn as the wave normal map.
/// </summary>
public sealed record WaterMaterial(
    Vector3 ShallowColor,
    Vector3 DeepColor,
    Vector3 ReflectionColor,
    float Reflectivity,
    float Fresnel)
{
    /// <summary>Game-relative path of the noise texture used as the wave normal map, or null.</summary>
    public string? NoiseTexturePath { get; init; }

    /// <summary>World units per repeat of the noise texture; values near zero mean 1024.</summary>
    public float NoiseUvScale { get; init; } = 1024f;

    /// <summary>Depth (world units) over which shallow fades to deep and the surface closes up to
    /// opaque. 192 is about wading depth.</summary>
    public float ShallowBand { get; init; } = 192f;

    /// <summary>Alpha at zero depth. 1 (the default) draws opaque water, which is what the engine
    /// does; lower values blend a see-through shore margin.</summary>
    public float ShoreAlpha { get; init; } = 1f;

    /// <summary>Grid squares per side. The body colour follows depth to the ground at the grid
    /// vertices, so a shoreline needs vertices along it.</summary>
    public int GridResolution { get; init; } = 48;

    /// <summary>The reflection is the renderer's sky colour (<c>GlRenderer.SkyColor</c>, else its
    /// clear colour), as the engine's water shader reflects the sky when water reflections are on.
    /// Off (the default) reflects <see cref="ReflectionColor"/> times <see cref="Reflectivity"/>,
    /// the engine's term when reflections are off.</summary>
    public bool ReflectsSky { get; init; }

    /// <summary>The water type's noise falloff distance (world units). The engine blends the
    /// reflection over the body by Fresnel near the camera and by 1 (a mirror) far away, ramping
    /// between this distance and 8192. 0 (the default) keeps the Fresnel blend at every distance.</summary>
    public float NoiseFalloff { get; init; }

    /// <summary>The water type's above-water depth fog, or null. When set it replaces
    /// <see cref="ShallowBand"/> and <see cref="ShoreAlpha"/>: the body colour fades shallow to deep
    /// over the fog's far distance, and the surface's opacity follows the engine's refraction weight,
    /// so ground under shallow water shows through.</summary>
    public WaterDepthFog? DepthFog { get; init; }

    /// <summary>With <see cref="DepthFog"/>: measure the water depth per pixel along the view ray,
    /// against the renderer's depth prepass of the opaque scene, as the engine does, instead of
    /// vertically at the grid vertices. The body colour, the refraction weight and the bed's share
    /// of the pixel then follow that depth, and far away (past the noise falloff) the surface is all
    /// reflection. Costs one depth prepass per frame while such water is drawn. Off (the default)
    /// keeps the per-vertex path.</summary>
    public bool PerPixelDepth { get; init; }

    /// <summary>The water type's sun specular power (the exponent on the reflected view ray against
    /// the sun direction); 0 (the default) draws no sun specular. Used with <see cref="PerPixelDepth"/>.</summary>
    public float SunSpecularPower { get; init; }

    /// <summary>The water type's sun specular magnitude. Used with <see cref="PerPixelDepth"/>.</summary>
    public float SunSpecularMagnitude { get; init; }

    /// <summary>The water type's depth specular-lighting factor (0..1): 1 fades the sun specular out
    /// to nothing at zero depth, 0 leaves it at full strength everywhere. Used with
    /// <see cref="PerPixelDepth"/>.</summary>
    public float DepthSpecularLighting { get; init; }
}

/// <summary>A water type's above-water fog (WATR fog near / far / amount): how fast the water
/// closes up with depth. Distances are world units of water the view passes through.</summary>
public sealed record WaterDepthFog(float Near, float Far, float Amount)
{
    /// <summary>The engine's refraction weight for <paramref name="depth"/> units of water: 0 shows
    /// the ground under the water, 1 shows the water alone. <c>1 - saturate((far - min(depth, far)) /
    /// (far - near)) ^ amount</c> (Community Shaders' Water.hlsl, which keeps the vanilla shader's
    /// form; its fog range is read here as far - near).</summary>
    public float Opacity(float depth)
    {
        if (Far <= 0f) return 1f;
        float range = Far - Near;
        if (range <= 1e-3f) return depth >= Far ? 1f : 0f;
        float x = Math.Clamp((Far - Math.Min(Math.Max(depth, 0f), Far)) / range, 0f, 1f);
        return 1f - MathF.Pow(x, Math.Max(Amount, 0f));
    }

    /// <summary>How far shallow has faded to deep at <paramref name="depth"/>: depth over the far distance.</summary>
    public float DeepFraction(float depth) => Far > 0f ? Math.Clamp(depth / Far, 0f, 1f) : 1f;
}

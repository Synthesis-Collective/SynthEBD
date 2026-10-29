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
}

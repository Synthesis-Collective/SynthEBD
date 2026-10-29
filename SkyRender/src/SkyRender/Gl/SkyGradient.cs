using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>One stop of a <see cref="SkyGradient"/>: the sky's colour (0..1 RGB, before the image
/// space) at an elevation above the horizon, in degrees (0 = the horizon, 90 = the zenith).</summary>
public readonly record struct SkyGradientKey(float ElevationDegrees, Vector3 Color);

/// <summary>
/// The sky's colour as a function of elevation, supplied by a host, for what water reflects. When
/// <see cref="GlRenderer.SkyGradient"/> is set, a water surface that reflects the sky
/// (<c>WaterMaterial.ReflectsSky</c>) reflects the gradient's colour at the elevation of the
/// reflected view ray, per pixel, instead of the single <see cref="GlRenderer.SkyColor"/>.
/// Colours are interpolated linearly between keys and held at the first and last key beyond them.
/// The shader takes up to <see cref="MaxKeys"/> keys; further keys are dropped.
/// <para>
/// For a Skyrim exterior, the engine's sky dome (Community Shaders' Sky.hlsl, the vanilla sky
/// shader's port) colours each vertex of <c>meshes\sky\atmosphere.nif</c> as
/// <c>BlendColor[0] * c.r + BlendColor[1] * c.g + BlendColor[2] * c.b</c>, where the vertex colour is
/// a blend mask (red at the horizon ring, green around 18 degrees, blue at the zenith) and the three
/// blend colours are the weather's horizon, sky-lower and sky-upper colours, plus the image space's
/// sky scale as an additive term. A host builds the keys from those (see RENDERING_PIPELINE.md,
/// "Water", for the stops and what else the reflection carries).
/// </para>
/// </summary>
public sealed record SkyGradient(IReadOnlyList<SkyGradientKey> Keys)
{
    /// <summary>The most keys the shader reads.</summary>
    public const int MaxKeys = 16;

    /// <summary>The keys the shader reads: sorted by elevation, at most <see cref="MaxKeys"/>.</summary>
    public IReadOnlyList<SkyGradientKey> ShaderKeys
        => Keys.OrderBy(k => k.ElevationDegrees).Take(MaxKeys).ToArray();

    /// <summary>The colour at <paramref name="elevationDegrees"/>, as the shader computes it.</summary>
    public Vector3 At(float elevationDegrees)
    {
        var k = ShaderKeys;
        if (k.Count == 0) return Vector3.Zero;
        if (elevationDegrees <= k[0].ElevationDegrees) return k[0].Color;
        for (int i = 1; i < k.Count; i++)
        {
            if (elevationDegrees > k[i].ElevationDegrees) continue;
            float span = k[i].ElevationDegrees - k[i - 1].ElevationDegrees;
            float t = span > 1e-6f ? (elevationDegrees - k[i - 1].ElevationDegrees) / span : 1f;
            return Vector3.Lerp(k[i - 1].Color, k[i].Color, t);
        }
        return k[^1].Color;
    }
}

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

    /// <summary>The vanilla sky dome's blend masks by elevation (degrees above its rim): the vertex
    /// colours of <c>meshes\sky\atmosphere.nif</c> (Skyrim - Meshes0.bsa) averaged per ring. They
    /// weight the horizon, sky-lower and sky-upper colours, which the engine's sky shader sums per
    /// vertex (Community Shaders Sky.hlsl, the vanilla shader's port).</summary>
    private static readonly (float El, float H, float L, float U)[] DomeMasks =
    {
        (0f, 1f, 0f, 0f), (2.1f, 1f, 0f, 0f), (3.6f, 0.933f, 0.071f, 0f), (5.6f, 0.804f, 0.220f, 0f),
        (9.7f, 0.490f, 0.573f, 0f), (14.5f, 0.157f, 0.910f, 0f), (18.0f, 0.020f, 1f, 0.016f),
        (26.8f, 0f, 0.769f, 0.306f), (34.9f, 0f, 0.471f, 0.584f), (47.0f, 0f, 0.184f, 0.831f),
        (59.1f, 0f, 0.063f, 0.941f), (74.0f, 0f, 0.012f, 0.984f), (90f, 0f, 0f, 1f),
    };

    /// <summary>
    /// The sky of a Skyrim weather, one key per ring of the vanilla sky dome: the weather's horizon,
    /// sky-lower and sky-upper colours mixed by the ring's masks, seen through the far fog at its
    /// maximum (<c>(1 - fogMax) dome + fogMax fogFar</c>), plus the image space's sky scale (the sky
    /// shader adds it after the gradient). The fog over the sky is an in-game fit, not sourced: the
    /// bare dome is far darker and bluer than the game's sky. Pass a <paramref name="fogMax"/> of 0
    /// for the bare dome. <see cref="SceneEnvironment.VanillaClearDay"/> is built with it.
    /// </summary>
    public static SkyGradient FromSkyDome(Vector3 horizon, Vector3 skyLower, Vector3 skyUpper,
        Vector3 fogFar, float fogMax, float skyScale)
    {
        var keys = new SkyGradientKey[DomeMasks.Length];
        for (int i = 0; i < DomeMasks.Length; i++)
        {
            var m = DomeMasks[i];
            var dome = horizon * m.H + skyLower * m.L + skyUpper * m.U;
            var c = dome * (1f - fogMax) + fogFar * fogMax;
            keys[i] = new SkyGradientKey(m.El, c + new Vector3(skyScale));
        }
        return new SkyGradient(keys);
    }

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

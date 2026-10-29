using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>
/// The engine's image-space finish (its HDR tone map and cinematic grade), applied per pixel in
/// place of the renderer's own tone map. Off unless a host sets <see cref="GlRenderer.ImageSpace"/>.
/// The arithmetic is the retail <c>ISHDRTonemapBlendCinematic</c> pixel shader's (the Reinhard
/// branch; its constants as the engine packs them from the image space's HDR, cinematic and tint
/// data). With the scene colour <c>A</c> and <c>L = (0.2125, 0.7154, 0.0721)</c>:
/// <code>
/// Y = max(dot(A, L), 1e-5);  Z = Y * ExposureRatio
/// Q = Z * (1 + k * Z) / (1 + Z),  k = 1 / (White * WhiteScale)^2
/// rgb = A * Q / Y;  K = dot(rgb, L)
/// g = lerp(K, rgb, Saturation);  g = lerp(g, K * TintColor, TintAmount)
/// out = saturate(P + Contrast * (Brightness * g - P)) ^ OutputExponent,  P = AdaptedLuminance
/// </code>
/// Not modelled: bloom (the engine adds a blurred bright pass weighted by
/// <c>saturate(receiveBloomThreshold - Q)</c>), the filmic curve branch, and eye adaptation over
/// time. The engine's adaptation texture holds two luminances, both eased toward the frame's
/// average luminance; <see cref="ExposureRatio"/> is their ratio (1 once settled) and
/// <see cref="AdaptedLuminance"/> the first, which is also the contrast pivot. A host supplies
/// that luminance (for example a scene average it measured, or a fixed value) since a single
/// frame has no adaptation history. The image space's sunlight scale is not applied here: the
/// engine multiplies the sun's colour by it, which a host does when it sets the key light.
/// See RENDERING_PIPELINE.md, "Image space".
/// </summary>
public sealed record ImageSpaceFinish(float White, float Saturation, float Brightness, float Contrast,
                                Vector3 TintColor, float TintAmount, float AdaptedLuminance)
{
    /// <summary>The engine's Reinhard white scale (<c>fReinhardWhiteScale</c>, 1.1 by default).</summary>
    public float WhiteScale { get; init; } = 1.1f;
    /// <summary>The adaptation ratio applied before the curve (1 once adaptation has settled).</summary>
    public float ExposureRatio { get; init; } = 1f;
    /// <summary>The final exponent (the engine's <c>1 / fGamma</c>, 1 by default).</summary>
    public float OutputExponent { get; init; } = 1f;

    /// <summary>The curve coefficient the engine uploads: <c>1 / (White * WhiteScale)^2</c>.</summary>
    public float ReinhardCoefficient
    {
        get { float w = MathF.Max(White * WhiteScale, 1e-4f); return 1f / (w * w); }
    }

    /// <summary>The finish applied to one colour, as the shader computes it (a host can use it
    /// for anything it composes outside the renderer).</summary>
    public Vector3 Apply(Vector3 a)
    {
        var l = new Vector3(0.2125f, 0.7154f, 0.0721f);
        float y = MathF.Max(Vector3.Dot(a, l), 1e-5f);
        float z = y * ExposureRatio;
        float q = z * (1f + ReinhardCoefficient * z) / (1f + z);
        var rgb = a * (q / y);
        float k = Vector3.Dot(rgb, l);
        var g = Vector3.Lerp(new Vector3(k), rgb, Saturation);
        g = Vector3.Lerp(g, k * TintColor, TintAmount);
        var c = new Vector3(AdaptedLuminance) + Contrast * (Brightness * g - new Vector3(AdaptedLuminance));
        static float F(float v, float e) => MathF.Pow(Math.Clamp(v, 0f, 1f), e);
        return new Vector3(F(c.X, OutputExponent), F(c.Y, OutputExponent), F(c.Z, OutputExponent));
    }
}

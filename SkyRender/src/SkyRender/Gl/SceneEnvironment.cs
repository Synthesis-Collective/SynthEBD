using OpenTK.Mathematics;

namespace SkyRender;

/// <summary>
/// What a renderer draws for the parts of a frame a scene supplies rather than the meshes: the sky
/// (background, the colour water reflects, and the gradient by elevation), the sun, the ambient
/// light, distance fog and the image-space finish. <see cref="GlRenderer.ApplyEnvironment"/> copies
/// it onto a renderer; a <see cref="RenderProfile.Scene"/> renderer starts from
/// <see cref="VanillaClearDay"/>. A host with its own weather or cell lighting builds its own (or
/// sets the renderer's properties directly), which overrides the preset. See RENDERING_PIPELINE.md,
/// "Scene defaults".
/// </summary>
/// <param name="Background">The clear colour (the sky behind everything).</param>
/// <param name="SkyColor">The single sky colour water reflects when there is no gradient.</param>
/// <param name="SkyGradient">The sky by elevation, or null.</param>
/// <param name="SunAzimuthDegrees">The sun's azimuth, in <see cref="GlRenderer.SetKeyLight"/>'s convention.</param>
/// <param name="SunElevationDegrees">The sun's elevation above the horizon.</param>
/// <param name="SunColor">The sun's colour (0..1).</param>
/// <param name="SunIntensity">The sun's multiplier: the image space's HDR sunlight scale (the engine
/// multiplies the sun's colour by it).</param>
/// <param name="AmbientColor">The ambient light's colour (on an up-facing surface).</param>
/// <param name="Fog">Distance fog, or null.</param>
/// <param name="ImageSpace">The image-space finish, or null.</param>
public sealed record SceneEnvironment(
    Vector3 Background,
    Vector3 SkyColor,
    SkyGradient? SkyGradient,
    float SunAzimuthDegrees,
    float SunElevationDegrees,
    Vector3 SunColor,
    float SunIntensity,
    Vector3 AmbientColor,
    DistanceFog? Fog,
    ImageSpaceFinish? ImageSpace)
{
    private static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    // Update.esm SkyrimClear (WTHR) day slot, the winning record as read from a
    // vanilla load order. Colours are the record's 8-bit values.
    private static readonly Vector3 ClearSkyUpper = Rgb(21, 77, 117);
    private static readonly Vector3 ClearSkyLower = Rgb(60, 135, 183);
    private static readonly Vector3 ClearHorizon = Rgb(125, 163, 183);
    private static readonly Vector3 ClearFogNear = Rgb(35, 98, 124);
    private static readonly Vector3 ClearFogFar = Rgb(116, 168, 203);
    private const float ClearFogMax = 0.85f;
    private const float ClearSkyScale = 0.05f;   // ISSkyrimClearDAY HDR sky scale

    /// <summary>
    /// A clear summer noon in vanilla Skyrim, for a scene drawn without weather or cell data:
    /// Update.esm's SkyrimClear weather (day slot), its image space ISSkyrimClearDAY, and the sun of
    /// the SkyrimClimate climate at 12:00.
    /// <list type="bullet">
    /// <item>Fog: near colour (35, 98, 124), far colour (116, 168, 203), near 0, far 80000, power 0.4,
    /// max 0.85 (WTHR FNAM, day distances).</item>
    /// <item>Image space (ISSkyrimClearDAY): white 1, saturation 1.5, brightness 1.135, contrast 1.4,
    /// tint (228, 214, 197) x 0.42, HDR sunlight scale 2.8, sky scale 0.05; adapted luminance 0.367
    /// (the retail adaptation texture read at SkyrimClear noon; one frame has no adaptation history).</item>
    /// <item>Sky: horizon (125, 163, 183), sky-lower (60, 135, 183), sky-upper (21, 77, 117), mixed by
    /// the vanilla dome's masks, plus the sky scale, seen through the far fog at its maximum
    /// ((1 - 0.85) x dome + 0.85 x fog far). The fog over the sky is an in-game fit, not sourced: the
    /// bare dome is far darker and bluer than the game's sky. The background is the gradient at 20
    /// degrees; water reflects the gradient, or the sky-upper colour without one.</item>
    /// <item>Sun: colour (190, 181, 171) x 2.8, azimuth 106.5, elevation 72.9 (SkyrimClimate sunrise
    /// 7:30, sunset 18:30, on Community Shaders' vanilla sun path at 12:00).</item>
    /// <item>Ambient: (191, 214, 221), the directional ambient's Z- colour (the light travelling from
    /// the sky down, what an up-facing surface receives; matched in game).</item>
    /// </list>
    /// </summary>
    public static SceneEnvironment VanillaClearDay { get; } = BuildVanillaClearDay();

    /// <summary>
    /// The sun's direction at <paramref name="hour"/> under a climate's sunrise and sunset times
    /// (hours, as the CLMT record's timing gives them), as azimuth and elevation in
    /// <see cref="GlRenderer.SetKeyLight"/>'s convention, or null when the times leave no day.
    /// The vanilla sun path as Community Shaders reproduces it: sunrise is the mean of the sunrise
    /// begin and end less a quarter hour, sunset the mean of the sunset begin and end plus a quarter
    /// hour, <c>phi = pi (hour - sunrise) / (sunset - sunrise)</c>, and the direction
    /// <c>(cos phi, -sin phi cos 95, sin phi sin 95)</c> in Skyrim axes: east at sunrise and 5
    /// degrees off the zenith toward north at noon. <see cref="VanillaClearDay"/>'s sun is this at
    /// 12:00 for a sunrise of 7:30 and a sunset of 18:30.
    /// </summary>
    public static (float AzimuthDegrees, float ElevationDegrees)? SunDirection(
        float sunriseBegin, float sunriseEnd, float sunsetBegin, float sunsetEnd, float hour)
    {
        float rise = (sunriseBegin + sunriseEnd) * 0.5f - 0.25f, set = (sunsetBegin + sunsetEnd) * 0.5f + 0.25f;
        if (!(set > rise)) return null;
        double phi = Math.PI * (hour - rise) / (set - rise);
        double tilt = 95.0 * Math.PI / 180.0;
        double x = Math.Cos(phi), y = -Math.Sin(phi) * Math.Cos(tilt), z = Math.Sin(phi) * Math.Sin(tilt);
        double len = Math.Sqrt(x * x + y * y + z * z);
        x /= len; y /= len; z /= len;
        float elevation = (float)(Math.Asin(z) * 180.0 / Math.PI);
        float azimuth = (float)(Math.Atan2(x, -y) * 180.0 / Math.PI);
        return (azimuth, elevation);
    }

    private static SceneEnvironment BuildVanillaClearDay()
    {
        var gradient = SkyGradient.FromSkyDome(ClearHorizon, ClearSkyLower, ClearSkyUpper, ClearFogFar, ClearFogMax, ClearSkyScale);
        return new SceneEnvironment(
            Background: gradient.At(20f),
            SkyColor: ClearSkyUpper,
            SkyGradient: gradient,
            SunAzimuthDegrees: 106.5f,
            SunElevationDegrees: 72.9f,
            SunColor: Rgb(190, 181, 171),
            SunIntensity: 2.8f,
            AmbientColor: Rgb(191, 214, 221),
            Fog: new DistanceFog(ClearFogNear, ClearFogFar, 0f, 80000f, 0.4f, ClearFogMax),
            ImageSpace: new ImageSpaceFinish(1f, 1.5f, 1.135f, 1.4f, Rgb(228, 214, 197), 0.42f, 0.367f));
    }
}

/// <summary>
/// What a <see cref="GlRenderer"/> is set up to draw, which decides its defaults.
/// </summary>
public enum RenderProfile
{
    /// <summary>A scene or world (the default): full detail. The renderer starts from
    /// <see cref="SceneEnvironment.VanillaClearDay"/> (sky gradient, sun, ambient, fog, image space).</summary>
    Scene = 0,

    /// <summary>A character portrait or body: the studio rig (ambient + key + fill + rim), the
    /// renderer's own tone map and bloom, no fog, no image space, a grey background. The character
    /// tier uses it.</summary>
    Character = 1,
}

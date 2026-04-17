using System.Collections.Generic;
using OpenTK.Mathematics;

namespace SynthEBD;

/// <summary>
/// Placement + intensity of a 3-point lighting rig (ambient + key/fill/rim).
/// Colors are supplied separately by a <see cref="CharacterViewerLightingColorScheme"/>
/// so the two can be combined orthogonally in the UI.
///
/// Convention: azimuth=180° places the light in front of the character (camera side),
/// elevation positive places the light above.
/// </summary>
public class CharacterViewerLightingLayout
{
    public string Name { get; init; } = "";
    public double Ambient { get; init; }

    public double KeyAzimuth { get; init; }
    public double KeyElevation { get; init; }
    public double KeyIntensity { get; init; }

    public double FillAzimuth { get; init; }
    public double FillElevation { get; init; }
    public double FillIntensity { get; init; }

    public double RimAzimuth { get; init; }
    public double RimElevation { get; init; }
    public double RimIntensity { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// Per-light color tints for the 3-point rig. Linear 0..1 RGB.
/// Applied independently of the positional layout.
/// </summary>
public class CharacterViewerLightingColorScheme
{
    public string Name { get; init; } = "";
    public Vector3 KeyColor { get; init; } = Vector3.One;
    public Vector3 FillColor { get; init; } = Vector3.One;
    public Vector3 RimColor { get; init; } = Vector3.One;

    public override string ToString() => Name;
}

/// <summary>
/// Built-in layouts and color schemes for the character viewer's lighting controls.
/// The default pair ("Mugshot Natural" + "Cinematic Warm") targets the Natural
/// Lighting Mugshots reference look.
/// </summary>
public static class CharacterViewerLightingPresets
{
    // --- Shared color palette ---
    private static readonly Vector3 Neutral = new(1.00f, 1.00f, 1.00f);
    private static readonly Vector3 WarmKey = new(1.00f, 0.96f, 0.88f);
    private static readonly Vector3 CoolFill = new(0.90f, 0.93f, 1.00f);
    private static readonly Vector3 WarmRim = new(1.00f, 0.95f, 0.85f);
    private static readonly Vector3 Sunset = new(1.00f, 0.82f, 0.55f);
    private static readonly Vector3 CoolShade = new(0.55f, 0.70f, 1.00f);

    public static readonly IReadOnlyList<CharacterViewerLightingLayout> AllLayouts =
    [
        new()
        {
            Name = "Mugshot Natural",
            Ambient = 40,
            KeyAzimuth = 180, KeyElevation = 40, KeyIntensity = 100,
            FillAzimuth = 180, FillElevation = -20, FillIntensity = 35,
            RimAzimuth = 0, RimElevation = 45, RimIntensity = 55,
        },
        new()
        {
            Name = "Three-Point Classic",
            Ambient = 30,
            KeyAzimuth = 210, KeyElevation = 35, KeyIntensity = 90,
            FillAzimuth = 150, FillElevation = -5, FillIntensity = 40,
            RimAzimuth = 350, RimElevation = 40, RimIntensity = 45,
        },
        new()
        {
            Name = "Rembrandt",
            Ambient = 12,
            KeyAzimuth = 220, KeyElevation = 45, KeyIntensity = 100,
            FillAzimuth = 140, FillElevation = -5, FillIntensity = 15,
            RimAzimuth = 40, RimElevation = 30, RimIntensity = 35,
        },
        new()
        {
            Name = "Split",
            Ambient = 12,
            KeyAzimuth = 270, KeyElevation = 5, KeyIntensity = 100,
            FillAzimuth = 90, FillElevation = 0, FillIntensity = 5,
            RimAzimuth = 0, RimElevation = 25, RimIntensity = 40,
        },
        new()
        {
            Name = "Butterfly (Paramount)",
            Ambient = 20,
            KeyAzimuth = 180, KeyElevation = 60, KeyIntensity = 100,
            FillAzimuth = 180, FillElevation = -35, FillIntensity = 50,
            RimAzimuth = 0, RimElevation = 45, RimIntensity = 45,
        },
        new()
        {
            Name = "High Key Studio",
            Ambient = 55,
            KeyAzimuth = 180, KeyElevation = 35, KeyIntensity = 90,
            FillAzimuth = 200, FillElevation = 10, FillIntensity = 70,
            RimAzimuth = 0, RimElevation = 50, RimIntensity = 70,
        },
        new()
        {
            Name = "Overcast Daylight",
            Ambient = 60,
            KeyAzimuth = 180, KeyElevation = 75, KeyIntensity = 65,
            FillAzimuth = 0, FillElevation = 60, FillIntensity = 45,
            RimAzimuth = 90, RimElevation = 30, RimIntensity = 25,
        },
        new()
        {
            Name = "Cinematic Moody",
            Ambient = 8,
            KeyAzimuth = 210, KeyElevation = 25, KeyIntensity = 100,
            FillAzimuth = 140, FillElevation = -5, FillIntensity = 10,
            RimAzimuth = 45, RimElevation = 30, RimIntensity = 55,
        },
        new()
        {
            Name = "Warm Sunset",
            Ambient = 20,
            KeyAzimuth = 220, KeyElevation = 10, KeyIntensity = 100,
            FillAzimuth = 40, FillElevation = 20, FillIntensity = 35,
            RimAzimuth = 30, RimElevation = 30, RimIntensity = 50,
        },
        new()
        {
            Name = "Rim Hero",
            Ambient = 28,
            KeyAzimuth = 180, KeyElevation = 25, KeyIntensity = 70,
            FillAzimuth = 180, FillElevation = -10, FillIntensity = 40,
            RimAzimuth = 0, RimElevation = 30, RimIntensity = 100,
        },
    ];

    public static readonly IReadOnlyList<CharacterViewerLightingColorScheme> AllColorSchemes =
    [
        new() { Name = "Cinematic Warm",   KeyColor = WarmKey,  FillColor = CoolFill,  RimColor = WarmRim },
        new() { Name = "Neutral",          KeyColor = Neutral,  FillColor = Neutral,   RimColor = Neutral },
        new() { Name = "Rembrandt Warm",   KeyColor = WarmKey,  FillColor = CoolFill,  RimColor = Neutral },
        new() { Name = "Overcast",         KeyColor = CoolFill, FillColor = CoolFill,  RimColor = Neutral },
        new() { Name = "Sunset",           KeyColor = Sunset,   FillColor = CoolShade, RimColor = Sunset },
        new() { Name = "Warm Rim Accent",  KeyColor = Neutral,  FillColor = Neutral,   RimColor = WarmRim },
    ];

    public static CharacterViewerLightingLayout DefaultLayout => AllLayouts[0];
    public static CharacterViewerLightingColorScheme DefaultColorScheme => AllColorSchemes[0];

    public static CharacterViewerLightingLayout FindLayoutOrDefault(string name) =>
        string.IsNullOrEmpty(name)
            ? DefaultLayout
            : FindByName(AllLayouts, name, static l => l.Name) ?? DefaultLayout;

    public static CharacterViewerLightingColorScheme FindColorSchemeOrDefault(string name) =>
        string.IsNullOrEmpty(name)
            ? DefaultColorScheme
            : FindByName(AllColorSchemes, name, static c => c.Name) ?? DefaultColorScheme;

    private static T? FindByName<T>(IReadOnlyList<T> items, string name, System.Func<T, string> selector)
        where T : class
    {
        foreach (var item in items)
        {
            if (selector(item) == name) return item;
        }
        return null;
    }
}

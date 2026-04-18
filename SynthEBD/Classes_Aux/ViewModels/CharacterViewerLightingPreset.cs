using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    public string Name { get; set; } = "";
    public double Ambient { get; set; }

    public double KeyAzimuth { get; set; }
    public double KeyElevation { get; set; }
    public double KeyIntensity { get; set; }

    public double FillAzimuth { get; set; }
    public double FillElevation { get; set; }
    public double FillIntensity { get; set; }

    public double RimAzimuth { get; set; }
    public double RimElevation { get; set; }
    public double RimIntensity { get; set; }

    /// <summary>True for the hardcoded preset list; false for user-saved presets.
    /// Used by the UI to prevent overwriting built-ins.</summary>
    public bool IsBuiltIn { get; set; }

    public override string ToString() => Name;

    public CharacterViewerLightingLayout Clone() => new()
    {
        Name = Name,
        Ambient = Ambient,
        KeyAzimuth = KeyAzimuth, KeyElevation = KeyElevation, KeyIntensity = KeyIntensity,
        FillAzimuth = FillAzimuth, FillElevation = FillElevation, FillIntensity = FillIntensity,
        RimAzimuth = RimAzimuth, RimElevation = RimElevation, RimIntensity = RimIntensity,
        IsBuiltIn = IsBuiltIn,
    };
}

/// <summary>
/// Per-light color tints for the 3-point rig. Linear 0..1 RGB.
/// Applied independently of the positional layout.
/// </summary>
public class CharacterViewerLightingColorScheme
{
    public string Name { get; set; } = "";

    // Serialized as R/G/B triplets so the JSON round-trips without an
    // OpenTK.Vector3 converter. The in-memory Vector3 view is ignored by
    // the serializer.
    public float KeyR { get; set; } = 1f;
    public float KeyG { get; set; } = 1f;
    public float KeyB { get; set; } = 1f;

    public float FillR { get; set; } = 1f;
    public float FillG { get; set; } = 1f;
    public float FillB { get; set; } = 1f;

    public float RimR { get; set; } = 1f;
    public float RimG { get; set; } = 1f;
    public float RimB { get; set; } = 1f;

    [JsonIgnore]
    public Vector3 KeyColor
    {
        get => new(KeyR, KeyG, KeyB);
        set { KeyR = value.X; KeyG = value.Y; KeyB = value.Z; }
    }

    [JsonIgnore]
    public Vector3 FillColor
    {
        get => new(FillR, FillG, FillB);
        set { FillR = value.X; FillG = value.Y; FillB = value.Z; }
    }

    [JsonIgnore]
    public Vector3 RimColor
    {
        get => new(RimR, RimG, RimB);
        set { RimR = value.X; RimG = value.Y; RimB = value.Z; }
    }

    public bool IsBuiltIn { get; set; }

    public override string ToString() => Name;

    public CharacterViewerLightingColorScheme Clone() => new()
    {
        Name = Name,
        KeyR = KeyR, KeyG = KeyG, KeyB = KeyB,
        FillR = FillR, FillG = FillG, FillB = FillB,
        RimR = RimR, RimG = RimG, RimB = RimB,
        IsBuiltIn = IsBuiltIn,
    };
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

    public static readonly IReadOnlyList<CharacterViewerLightingLayout> BuiltInLayouts =
    [
        new()
        {
            Name = "Mugshot Natural", IsBuiltIn = true,
            Ambient = 40,
            KeyAzimuth = 180, KeyElevation = 40, KeyIntensity = 100,
            FillAzimuth = 180, FillElevation = -20, FillIntensity = 35,
            RimAzimuth = 0, RimElevation = 45, RimIntensity = 55,
        },
        new()
        {
            Name = "Three-Point Classic", IsBuiltIn = true,
            Ambient = 30,
            KeyAzimuth = 210, KeyElevation = 35, KeyIntensity = 90,
            FillAzimuth = 150, FillElevation = -5, FillIntensity = 40,
            RimAzimuth = 350, RimElevation = 40, RimIntensity = 45,
        },
        new()
        {
            Name = "Rembrandt", IsBuiltIn = true,
            Ambient = 12,
            KeyAzimuth = 220, KeyElevation = 45, KeyIntensity = 100,
            FillAzimuth = 140, FillElevation = -5, FillIntensity = 15,
            RimAzimuth = 40, RimElevation = 30, RimIntensity = 35,
        },
        new()
        {
            Name = "Split", IsBuiltIn = true,
            Ambient = 12,
            KeyAzimuth = 270, KeyElevation = 5, KeyIntensity = 100,
            FillAzimuth = 90, FillElevation = 0, FillIntensity = 5,
            RimAzimuth = 0, RimElevation = 25, RimIntensity = 40,
        },
        new()
        {
            Name = "Butterfly (Paramount)", IsBuiltIn = true,
            Ambient = 20,
            KeyAzimuth = 180, KeyElevation = 60, KeyIntensity = 100,
            FillAzimuth = 180, FillElevation = -35, FillIntensity = 50,
            RimAzimuth = 0, RimElevation = 45, RimIntensity = 45,
        },
        new()
        {
            Name = "High Key Studio", IsBuiltIn = true,
            Ambient = 55,
            KeyAzimuth = 180, KeyElevation = 35, KeyIntensity = 90,
            FillAzimuth = 200, FillElevation = 10, FillIntensity = 70,
            RimAzimuth = 0, RimElevation = 50, RimIntensity = 70,
        },
        new()
        {
            Name = "Overcast Daylight", IsBuiltIn = true,
            Ambient = 60,
            KeyAzimuth = 180, KeyElevation = 75, KeyIntensity = 65,
            FillAzimuth = 0, FillElevation = 60, FillIntensity = 45,
            RimAzimuth = 90, RimElevation = 30, RimIntensity = 25,
        },
        new()
        {
            Name = "Cinematic Moody", IsBuiltIn = true,
            Ambient = 8,
            KeyAzimuth = 210, KeyElevation = 25, KeyIntensity = 100,
            FillAzimuth = 140, FillElevation = -5, FillIntensity = 10,
            RimAzimuth = 45, RimElevation = 30, RimIntensity = 55,
        },
        new()
        {
            Name = "Warm Sunset", IsBuiltIn = true,
            Ambient = 20,
            KeyAzimuth = 220, KeyElevation = 10, KeyIntensity = 100,
            FillAzimuth = 40, FillElevation = 20, FillIntensity = 35,
            RimAzimuth = 30, RimElevation = 30, RimIntensity = 50,
        },
        new()
        {
            Name = "Rim Hero", IsBuiltIn = true,
            Ambient = 28,
            KeyAzimuth = 180, KeyElevation = 25, KeyIntensity = 70,
            FillAzimuth = 180, FillElevation = -10, FillIntensity = 40,
            RimAzimuth = 0, RimElevation = 30, RimIntensity = 100,
        },
    ];

    public static readonly IReadOnlyList<CharacterViewerLightingColorScheme> BuiltInColorSchemes =
    [
        MakeColorScheme("Cinematic Warm",   WarmKey,  CoolFill,  WarmRim),
        MakeColorScheme("Neutral",          Neutral,  Neutral,   Neutral),
        MakeColorScheme("Rembrandt Warm",   WarmKey,  CoolFill,  Neutral),
        MakeColorScheme("Overcast",         CoolFill, CoolFill,  Neutral),
        MakeColorScheme("Sunset",           Sunset,   CoolShade, Sunset),
        MakeColorScheme("Warm Rim Accent",  Neutral,  Neutral,   WarmRim),
    ];

    private static CharacterViewerLightingColorScheme MakeColorScheme(string name,
        Vector3 key, Vector3 fill, Vector3 rim) => new()
    {
        Name = name, IsBuiltIn = true,
        KeyR = key.X, KeyG = key.Y, KeyB = key.Z,
        FillR = fill.X, FillG = fill.Y, FillB = fill.Z,
        RimR = rim.X, RimG = rim.Y, RimB = rim.Z,
    };

    public static CharacterViewerLightingLayout DefaultLayout => BuiltInLayouts[0];
    public static CharacterViewerLightingColorScheme DefaultColorScheme => BuiltInColorSchemes[0];

    public static CharacterViewerLightingLayout FindLayoutOrDefault(string name,
        IEnumerable<CharacterViewerLightingLayout>? userLayouts = null)
    {
        if (string.IsNullOrEmpty(name)) return DefaultLayout;
        foreach (var l in BuiltInLayouts) if (l.Name == name) return l;
        if (userLayouts != null)
            foreach (var l in userLayouts) if (l.Name == name) return l;
        return DefaultLayout;
    }

    public static CharacterViewerLightingColorScheme FindColorSchemeOrDefault(string name,
        IEnumerable<CharacterViewerLightingColorScheme>? userSchemes = null)
    {
        if (string.IsNullOrEmpty(name)) return DefaultColorScheme;
        foreach (var c in BuiltInColorSchemes) if (c.Name == name) return c;
        if (userSchemes != null)
            foreach (var c in userSchemes) if (c.Name == name) return c;
        return DefaultColorScheme;
    }
}

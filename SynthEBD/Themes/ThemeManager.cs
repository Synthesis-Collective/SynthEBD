// Ported from NPC Plugin Chooser 2 (Themes/ThemeManager.cs), with an embedded-resource fallback
// for installs that lack the on-disk Themes folder (e.g. Synthesis-hosted runs).
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace SynthEBD;

/// <summary>
/// Loads and hot-swaps the application theme. Themes are loose <c>Themes\*.xaml</c> resource
/// dictionaries next to the executable (drop-in extensible: users can add their own), parsed with
/// <see cref="XamlReader"/> and merged last into the application resources so their implicit styles
/// win. Each theme file must be fully self-contained (all StaticResource references resolve within
/// the file) because it is parsed standalone at runtime.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// Fired whenever the active theme changes. The bool indicates whether the theme is dark.
    /// </summary>
    public static event Action<bool>? ThemeChanged;

    public const string DefaultThemeName = "Dark";

    private static readonly string ThemesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");

    /// <summary>
    /// Returns the display names (file names without extension) of all available themes. When the
    /// on-disk folder is missing, returns just the embedded default so a theme picker stays usable.
    /// </summary>
    public static List<string> GetAvailableThemes()
    {
        if (!Directory.Exists(ThemesFolder))
            return new List<string> { DefaultThemeName };

        var themes = Directory.GetFiles(ThemesFolder, "*.xaml")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return themes.Any() ? themes : new List<string> { DefaultThemeName };
    }

    /// <summary>
    /// Applies the theme with the given display name. Falls back to the on-disk default theme if
    /// the requested one is not found, then to the embedded copy of the default theme if the
    /// Themes folder itself is missing (Synthesis-hosted installs).
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        var themeDictionaries = Application.Current.Resources.MergedDictionaries;

        ResourceDictionary? newTheme = LoadThemeFromDisk(themeName)
            ?? LoadThemeFromDisk(DefaultThemeName)
            ?? LoadEmbeddedDefaultTheme();
        if (newTheme == null)
        {
            return; // No theme available at all; leave current resources in place.
        }

        // Remove any previously applied theme dictionary (tagged via our attached marker)
        var existing = themeDictionaries.FirstOrDefault(d => d["__ThemeManager_Applied"] != null);
        if (existing != null)
            themeDictionaries.Remove(existing);

        // Tag the dictionary so we can find and remove it later
        newTheme["__ThemeManager_Applied"] = true;

        themeDictionaries.Add(newTheme);

        bool isDark = IsDarkTheme(newTheme);

        // Repaint the theme-adaptive annotation brushes so descriptor/BodySlide status text stays
        // legible on light themes (the fixed Yellow/LightGreen/White washed out on white backgrounds).
        var foreground = (newTheme["PrimaryForeground"] as SolidColorBrush)?.Color
            ?? (isDark ? Colors.White : Colors.Black);
        AnnotationColors.ApplyTheme(isDark, foreground);

        ThemeChanged?.Invoke(isDark);
    }

    private static ResourceDictionary? LoadThemeFromDisk(string themeName)
    {
        var themeFile = Path.Combine(ThemesFolder, themeName + ".xaml");
        if (!File.Exists(themeFile))
            return null;

        try
        {
            using var stream = File.OpenRead(themeFile);
            return (ResourceDictionary)XamlReader.Load(stream);
        }
        catch (Exception)
        {
            return null; // Silently skip broken theme files
        }
    }

    /// <summary>The default theme's source file is also compiled in as an embedded resource so a
    /// themed UI survives installs where the loose Themes folder was not copied.</summary>
    private static ResourceDictionary? LoadEmbeddedDefaultTheme()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SynthEBD.Themes." + DefaultThemeName + ".xaml");
            if (stream == null)
                return null;
            return (ResourceDictionary)XamlReader.Load(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether a theme is "dark" by checking the luminance of PrimaryBackground.
    /// </summary>
    private static bool IsDarkTheme(ResourceDictionary theme)
    {
        if (theme["PrimaryBackground"] is SolidColorBrush brush)
        {
            var c = brush.Color;
            // Relative luminance approximation
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance < 0.5;
        }
        // Default to dark if we can't determine
        return true;
    }
}

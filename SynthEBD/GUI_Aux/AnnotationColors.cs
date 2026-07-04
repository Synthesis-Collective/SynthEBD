using System.Windows.Media;

namespace SynthEBD
{
    /// <summary>
    /// Theme-adaptive brushes for BodySlide/descriptor annotation state coloring. Unlike the fixed
    /// <see cref="CommonColors"/> palette, the colors here are repainted by
    /// <see cref="ThemeManager.ApplyTheme(string)"/> on every theme swap so they stay legible on both
    /// light and dark themes (the old fixed Yellow/LightGreen/White read as invisible washed-out text
    /// on the light themes). The brush <em>instances</em> are shared and long-lived — mutating each
    /// brush's <see cref="SolidColorBrush.Color"/> in place updates every live binding automatically,
    /// so no per-VM re-subscription is needed.
    /// </summary>
    public static class AnnotationColors
    {
        /// <summary>Default / "no annotation yet" text color. Tracks the active theme's primary foreground.</summary>
        public static readonly SolidColorBrush DefaultText = new(Colors.White);

        /// <summary>Unannotated indicator (border + text). Bright yellow on dark themes, dark goldenrod on light.</summary>
        public static readonly SolidColorBrush Unannotated = new(Colors.Yellow);

        /// <summary>Valid / manually-annotated indicator (border + text). Light green on dark themes, forest green on light.</summary>
        public static readonly SolidColorBrush Valid = new(Colors.LightGreen);

        /// <summary>Repaints the adaptive brushes for the active theme. Called from <see cref="ThemeManager.ApplyTheme(string)"/>.</summary>
        /// <param name="isDark">Whether the applied theme is dark.</param>
        /// <param name="primaryForeground">The applied theme's PrimaryForeground color (used for default text).</param>
        public static void ApplyTheme(bool isDark, Color primaryForeground)
        {
            DefaultText.Color = primaryForeground;
            Unannotated.Color = isDark ? Colors.Yellow : Color.FromRgb(0xB8, 0x86, 0x0B);   // DarkGoldenrod-ish, legible on white
            Valid.Color = isDark ? Colors.LightGreen : Color.FromRgb(0x2E, 0x7D, 0x32);      // forest green, legible on white
        }
    }
}

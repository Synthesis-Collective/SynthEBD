// Ported from NPC Plugin Chooser 2 (Themes/DwmHelper.cs).
using System.Runtime.InteropServices;

namespace SynthEBD;

internal static class DwmHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Darkens/lightens the native title bar of the given window. Returns false (without
    /// throwing) on Windows builds that lack the attribute — the failure is purely cosmetic.</summary>
    internal static bool UseImmersiveDarkMode(IntPtr handle, bool enabled)
    {
        try
        {
            int useImmersiveDarkMode = enabled ? 1 : 0;
            return DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }
}

namespace SynthEBD;

/// <summary>Thread-safe lazy singleton holding the ONE global <see cref="UiDisplayMode"/> that
/// governs progressive disclosure across every menu (modeled on <see cref="TooltipController"/>).
/// Views bind element/row visibility to <see cref="DisplayMode"/> through the UiMode converters;
/// the mode is persisted via <c>Settings_General</c>.</summary>
public sealed class UiModeController : VM
{
    private static UiModeController instance;
    private static object lockObj = new();

    private UiModeController() { }

    /// <summary>The active disclosure level. Defaults to <see cref="UiDisplayMode.Use"/> until
    /// settings are loaded (migration maps legacy settings to Customize/Troubleshoot).</summary>
    public UiDisplayMode DisplayMode { get; set; }

    /// <summary>When true, mode changes skip user-facing confirmation dialogs (set by the
    /// ui-screenshot harness so automated mode sweeps cannot block on a MessageWindow).</summary>
    public bool SuppressModeChangeWarnings { get; set; }

    /// <summary>The lazily created, thread-safe shared instance.</summary>
    public static UiModeController Instance
    {
        get
        {
            lock (lockObj)
            {
                if (instance == null)
                {
                    instance = new UiModeController();
                    instance.DisplayMode = UiDisplayMode.Use;
                }
            }
            return instance;
        }
    }
}

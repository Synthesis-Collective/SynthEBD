namespace SynthEBD;

/// <summary>Thread-safe lazy singleton that exposes a single bindable flag controlling whether the
/// application's tooltips are shown. Views bind their tooltip visibility to <see cref="DisplayToolTips"/>.</summary>
public sealed class TooltipController : VM
{
    private static TooltipController instance;
    private static object lockObj = new();

    private TooltipController() { }

    /// <summary>Whether tooltips should be displayed application-wide. Defaults to <c>true</c>.</summary>
    public bool DisplayToolTips { get; set; }
    /// <summary>The lazily created, thread-safe shared instance.</summary>
    public static TooltipController Instance
    {
        get
        {
            lock (lockObj)
            {
                if (instance == null)
                {
                    instance = new TooltipController();
                    instance.DisplayToolTips = true;
                }
            }
            return instance;
        }
    }
}
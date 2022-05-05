using Noggog.WPF;

namespace SynthEBD;

public sealed class TooltipController : ViewModel
{
    private static TooltipController instance;
    private static object lockObj = new();

    private TooltipController() { }

    public bool DisplayToolTips { get; set; }
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
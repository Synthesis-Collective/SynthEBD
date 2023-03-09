using Noggog;
using ReactiveUI;
using System.ComponentModel;
using System.Windows.Media;

namespace SynthEBD;

public class VM_StatusBar : VM
{
    private readonly Logger _logger;

    public VM_StatusBar(Logger logger)
    {
        _logger = logger;

        _logger.WhenAnyValue(x => x.StatusString).Subscribe(x => DispString = x).DisposeWith(this);
        _logger.WhenAnyValue(x => x.StatusColor).Subscribe(x => FontColor = x).DisposeWith(this);
    }

    public string DispString { get; set; } = "";
    public SolidColorBrush FontColor { get; set; } = new(Colors.Green);

    public int ProgressBarMax { get; set; } = 100;
    public int ProgressBarCurrent { get; set; } = 0;
    public string ProgressBarDisp { get; set; } = "";
    public bool IsPatching { get; set; } = false;
}
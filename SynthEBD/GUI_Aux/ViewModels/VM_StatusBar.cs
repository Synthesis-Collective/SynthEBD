using System.ComponentModel;
using System.Windows.Media;
using Noggog.WPF;

namespace SynthEBD;

public class VM_StatusBar : ViewModel
{
    public VM_StatusBar()
    {
        this.SubscribedLogger.PropertyChanged += RefreshDisp;
    }

    public string DispString { get; set; } = "";
    private Logger SubscribedLogger { get; set; } = Logger.Instance;
    public SolidColorBrush FontColor { get; set; } = new(Colors.Green);

    public int ProgressBarMax { get; set; } = 100;
    public int ProgressBarCurrent { get; set; } = 0;
    public string ProgressBarDisp { get; set; } = "";
    public bool IsPatching { get; set; } = false;

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = SubscribedLogger.StatusString;
        this.FontColor = SubscribedLogger.StatusColor;
    }
}
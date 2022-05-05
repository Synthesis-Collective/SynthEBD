using System.ComponentModel;
using System.Windows.Media;
using Noggog.WPF;

namespace SynthEBD;

public class VM_StatusBar : ViewModel
{
    public VM_StatusBar()
    {
        this.DispString = "";
        this.FontColor = new SolidColorBrush(Colors.Green);
        this.SubscribedLogger = Logger.Instance;
        this.SubscribedLogger.PropertyChanged += RefreshDisp;
        this.ProgressBarCurrent = 0;
        this.ProgressBarMax = 100;
        this.ProgressBarDisp = "";
        this.IsPatching = false;
    }

    public string DispString { get; set; }
    private Logger SubscribedLogger { get; set; }
    public SolidColorBrush FontColor { get; set; }

    public int ProgressBarMax { get; set; }
    public int ProgressBarCurrent { get; set; }
    public string ProgressBarDisp { get; set; }
    public bool IsPatching { get; set; }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = SubscribedLogger.StatusString;
        this.FontColor = SubscribedLogger.StatusColor;
    }
}
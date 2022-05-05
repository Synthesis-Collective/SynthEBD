using System.ComponentModel;
using System.Windows.Media;

namespace SynthEBD;

public class VM_StatusBar : INotifyPropertyChanged
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

    public string DispString
    {
        get { return _dispString; }
        set
        {
            if (value != _dispString)
            {
                _dispString = value;
                OnPropertyChanged("DispString");
            }
        }
    }
    private string _dispString;
    private Logger SubscribedLogger { get; set; }
    public SolidColorBrush FontColor { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    public int ProgressBarMax { get; set; }
    public int ProgressBarCurrent { get; set; }
    public string ProgressBarDisp { get; set; }
    public bool IsPatching { get; set; }

    protected void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        PropertyChangedEventHandler handler = PropertyChanged;
        if (handler != null)
            handler(this, e);
    }

    protected void OnPropertyChanged(string propertyName)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = SubscribedLogger.StatusString;
        this.FontColor = SubscribedLogger.StatusColor;
    }
}
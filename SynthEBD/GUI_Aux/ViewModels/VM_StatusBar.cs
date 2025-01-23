using Noggog;
using ReactiveUI;
using System.ComponentModel;
using System.Windows;
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

    // Or a method for incrementing
    public void IncrementProgressBarThreadSafe(int increment)
    {
        Interlocked.Add(ref _progressBarCurrent, increment);
    }

    public string DispString { get; set; } = "";
    public SolidColorBrush FontColor { get; set; } = new(Colors.Green);

    public int ProgressBarMax { get; set; } = 100;
    private int _progressBarCurrent;
    public int ProgressBarCurrent
    {
        get => _progressBarCurrent;
        set => Interlocked.Exchange(ref _progressBarCurrent, value);
    }
    private string _progressBarDisp;
    public string ProgressBarDisp
    {
        get => _progressBarDisp;
        set
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_progressBarDisp != value)
                {
                    _progressBarDisp = value;
                }
            });
        }
    }

    public bool IsPatching { get; set; } = false;
}
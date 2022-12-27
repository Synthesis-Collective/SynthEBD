using System.Windows;
using Autofac;

namespace SynthEBD;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
{
    public MainWindow()
    {
        InitializeComponent();

        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        var container = builder.Build();
        var logger = container.Resolve<Logger>();
        PatcherEnvironmentProvider.Instance = container.Resolve<PatcherEnvironmentProvider>();
        var mvm = container.Resolve<MainWindow_ViewModel>();
        this.DataContext = mvm;
        mvm.Init();

        //https://stackoverflow.com/questions/25426930/how-can-i-set-wpf-window-size-is-25-percent-of-relative-monitor-screen

        var toolbarHeight = SystemParameters.PrimaryScreenHeight - SystemParameters.FullPrimaryScreenHeight - SystemParameters.WindowCaptionHeight;

        this.Height = (System.Windows.SystemParameters.FullPrimaryScreenHeight) + SystemParameters.WindowCaptionHeight;
        this.Width = (System.Windows.SystemParameters.MaximizedPrimaryScreenWidth * 0.5);
    }
}
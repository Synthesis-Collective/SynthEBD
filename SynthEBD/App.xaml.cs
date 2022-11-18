using System.Text;
using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    void App_Startup(object sender, StartupEventArgs e)
    {
        // Application is running
        // Process command line args
        bool startMinimized = false;
        for (int i = 0; i != e.Args.Length; ++i)
        {
            if (e.Args[i] == "/StartMinimized")
            {
                startMinimized = true;
            }
        }

        // Create main application window, starting minimized if specified
        MainWindow mainWindow = new MainWindow();
        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }
        mainWindow.Show();
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        StringBuilder sb = new();
        sb.AppendLine("SynthEBD has crashed with the following error:");
        sb.AppendLine(ExceptionLogger.GetExceptionStack(e.Exception, ""));
        sb.AppendLine();
        sb.AppendLine("Patcher Settings Creation Log:");
        sb.AppendLine(PatcherSettingsProvider.SettingsLog.ToString());
        sb.AppendLine();
        sb.AppendLine("Patcher Environment Creation Log:");
        sb.AppendLine(PatcherEnvironmentProvider.Instance.EnvironmentLog.ToString());

        var errorMessage = sb.ToString();
        CustomMessageBox.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        var path = System.IO.Path.Combine(PatcherSettings.Paths.LogFolderPath, "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
        PatcherIO.WriteTextFile(path, errorMessage).Wait();

        e.Handled = true;
    }

}
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{
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
            MessageBox.Show("SynthEBD has crashed with the following error:" + Environment.NewLine + e.Exception.Message + Environment.NewLine + "See Logs\\Crash Logs for details", "Error");

            var path = System.IO.Path.Combine(PatcherSettings.Paths.LogFolderPath, "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
            PatcherIO.WriteTextFile(path, GetExceptionStack(e.Exception, ""));

            e.Handled = true;
        }

        private static string GetExceptionStack(Exception e, string error)
        {
            error += e.Message + Environment.NewLine + e.StackTrace + Environment.NewLine + Environment.NewLine;
            if (e.InnerException != null)
            {
                return GetExceptionStack(e.InnerException, error);
            }
            else
            {
                return error;
            }
        }
    }
}

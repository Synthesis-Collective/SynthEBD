using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public sealed class Logger : INotifyPropertyChanged
    {
        private static Logger instance;
        private static object lockObj = new Object();

        public event PropertyChangedEventHandler PropertyChanged;

        public VM_RunButton RunButton { get; set; }
        public string StatusString { get; set; }
        public string LogString { get; set; }

        public SolidColorBrush StatusColor { get; set; }

        public SolidColorBrush ReadyColor = new SolidColorBrush(Colors.Green);
        public SolidColorBrush WarningColor = new SolidColorBrush(Colors.Yellow);
        public SolidColorBrush ErrorColor = new SolidColorBrush(Colors.Red);
        public string ReadyString = "Ready To Patch";

        System.Windows.Threading.DispatcherTimer UpdateTimer { get; set; }
        System.Diagnostics.Stopwatch EllapsedTimer { get; set; }
        private Logger()
        {
            this.StatusColor = this.ReadyColor;
            this.StatusString = this.ReadyString;
            this.UpdateTimer = new System.Windows.Threading.DispatcherTimer();
            this.EllapsedTimer = new System.Diagnostics.Stopwatch();
        }

        public static Logger Instance
        {
            get
            {
                lock (lockObj)
                {
                    if (instance == null)
                    {
                        instance = new Logger();
                    }
                }
                return instance;
            }
        }

        public static void LogMessage(string message)
        {
            Instance.LogString += message + "\n";
        }

        public static void LogError(string error)
        {
            Instance.LogString += error + "\n";
        }

        public static void LogErrorWithStatusUpdate(string error, ErrorType type)
        {
            Instance.LogString += error + "\n";
            Instance.StatusString = error;
            switch (type)
            {
                case ErrorType.Warning: Instance.StatusColor = Instance.WarningColor; break;
                case ErrorType.Error: Instance.StatusColor = Instance.ErrorColor; break;
            }
        }

        public static void UpdateStatus(string message, bool triggerWarning)
        {
            Instance.StatusString = message;
            if (triggerWarning)
            {
                Instance.StatusColor = Instance.WarningColor;
            }
        }

        public static void TimedNotifyStatusUpdate(string error, ErrorType type, int durationSec)
        {
            LogErrorWithStatusUpdate(error, type);

            var t = Task.Factory.StartNew(() =>
            {
                Task.Delay(durationSec * 1000).Wait();
            });
            t.Wait();
            ClearStatusError();
        }

        public async Task CallTimedNotifyStatusUpdateAsync(string error, ErrorType type, int durationSec)
  => await TimedNotifyStatusUpdateAsync(error, type, durationSec);

        public static async Task TimedNotifyStatusUpdateAsync(string error, ErrorType type, int durationSec)
        {
            LogErrorWithStatusUpdate(error, type);

            // Await the Task to allow the UI thread to render the view
            // in order to show the changes     
            await Task.Delay(durationSec * 1000);

            ClearStatusError();
        }

        public static void ClearStatusError()
        {
            Instance.StatusString = Instance.ReadyString;
            Instance.StatusColor = Instance.ReadyColor;
        }
        public static void StartTimer()
        {
            Instance.UpdateTimer = new System.Windows.Threading.DispatcherTimer();
            Instance.EllapsedTimer = new System.Diagnostics.Stopwatch();
            Instance.UpdateTimer.Interval = TimeSpan.FromSeconds(1);
            Instance.UpdateTimer.Tick += new EventHandler((object s, EventArgs a) =>
            {
                UpdateStatus("Patching: " + GetEllapsedTime(), false);
            });
            Instance.UpdateTimer.Start();
            Instance.EllapsedTimer.Start();
        }

        public static string GetEllapsedTime()
        {
            TimeSpan ts = Instance.EllapsedTimer.Elapsed;
            return string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds);
        }
    }

    public enum ErrorType
    {
        Warning,
        Error
    }
}

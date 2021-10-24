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

        private Logger()
        {
            this.StatusColor = this.ReadyColor;
            this.StatusString = this.ReadyString;
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
        public static void ClearStatusError()
        {
            Instance.StatusString = Instance.ReadyString;
            Instance.StatusColor = Instance.ReadyColor;
        }
    }

    public enum ErrorType
    {
        Warning,
        Error
    }
}

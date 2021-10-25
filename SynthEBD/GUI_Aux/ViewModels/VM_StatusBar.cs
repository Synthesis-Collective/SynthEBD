using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_StatusBar : INotifyPropertyChanged
    {
        public VM_StatusBar()
        {
            this.DispString = "";
            this.FontColor = new SolidColorBrush(Colors.Green);
            this.SubscribedLogger = Logger.Instance;
            this.SubscribedLogger.PropertyChanged += RefreshDisp;
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
            string debugBreakHere = "";
        }
    }
}

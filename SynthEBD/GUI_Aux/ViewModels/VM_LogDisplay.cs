using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_LogDisplay : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private Logger SubscribedLogger { get; set; }

        public string DispString { get; set; }

        public VM_LogDisplay()
        {
            this.SubscribedLogger = Logger.Instance;
            this.DispString = "";

            this.SubscribedLogger.PropertyChanged += RefreshDisp;
        }

        public void RefreshDisp(object sender, PropertyChangedEventArgs e)
        {
            this.DispString = SubscribedLogger.LogString;
        }
    }
}

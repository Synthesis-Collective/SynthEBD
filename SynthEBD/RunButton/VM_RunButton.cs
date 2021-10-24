using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_RunButton : INotifyPropertyChanged
    {
        public VM_RunButton()
        {
            this.BackgroundColor = new SolidColorBrush(Colors.Green);
        }
        public SolidColorBrush BackgroundColor { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

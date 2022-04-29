
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace SynthEBD
{
    public class VM_PreviewImage : INotifyPropertyChanged
    {
        public VM_PreviewImage(Image image, string source)
        {
            Image = image;
            Source = source;
        }
        public Image Image { get; set; }
        public string Source { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

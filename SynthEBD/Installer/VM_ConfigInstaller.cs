using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_ConfigInstaller  : INotifyPropertyChanged
    {
        public VM_ConfigInstaller(Manifest manifest, Window_ConfigInstaller window)
        {
            SelectorMenu = new VM_ConfigSelector(manifest, window, this);
            SelectorMenu.SelectedOption = SelectorMenu; // shows the top-level choices
            DownloadMenu = null;
            DisplayedViewModel = SelectorMenu;

            Cancelled = false;
        }
        public Window_ConfigInstaller Window { get; set; }
        public object DisplayedViewModel { get; set; }
        public VM_ConfigSelector SelectorMenu { get; set; }
        public VM_DownloadCoordinator DownloadMenu { get; set; }
        public bool Cancelled { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }


}

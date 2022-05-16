using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public  class VM_MenuItem : VM // https://www.codeproject.com/Articles/37236/WPF-Menu-using-ViewModel-Part-1
    {
        public string Header { get; set; } = "";
        public string Alias { get; set; } = "";
        public ObservableCollection<VM_MenuItem> Children { get; set; } = new ObservableCollection<VM_MenuItem>();

        public RelayCommand Command { get; set; }

        public void Add(VM_MenuItem item)
        {
            Children.Add(item);
        }
    }
}

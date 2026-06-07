using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Backs a single (potentially nested) WPF menu item: a header/alias, an optional command, and child items.
    /// </summary>
    public  class VM_MenuItem : VM // https://www.codeproject.com/Articles/37236/WPF-Menu-using-ViewModel-Part-1
    {
        public string Header { get; set; } = "";
        public string Alias { get; set; } = "";
        public ObservableCollection<VM_MenuItem> Children { get; set; } = new ObservableCollection<VM_MenuItem>();

        public RelayCommand Command { get; set; }

        /// <summary>Adds a child menu item beneath this one.</summary>
        public void Add(VM_MenuItem item)
        {
            Children.Add(item);
        }
    }
}

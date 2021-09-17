using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_CollectionMemberString
    {
        public VM_CollectionMemberString(string content, ObservableCollection<VM_CollectionMemberString> parentCollection)
        {
            this.Content = content;
            this.ParentCollection = parentCollection;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }
        public string Content { get; set; }
        public ObservableCollection<VM_CollectionMemberString> ParentCollection { get; set; }
        public RelayCommand DeleteCommand { get; }
    }
}

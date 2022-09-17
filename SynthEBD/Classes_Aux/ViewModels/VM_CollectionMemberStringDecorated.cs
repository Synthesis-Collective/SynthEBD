using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_CollectionMemberStringDecorated : VM
    {
        public VM_CollectionMemberStringDecorated(string content, ObservableCollection<VM_CollectionMemberStringDecorated> parentCollection, Mode mode)
        {
            this.Content = content;
            this.ParentCollection = parentCollection;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
            switch(mode)
            {
                case Mode.TextBox: TextBox = true; TextBlock = false; break;
                case Mode.TextBlock: TextBlock = true; TextBox = false; break;
            }
        }
        public string Content { get; set; }
        public ObservableCollection<VM_CollectionMemberStringDecorated> ParentCollection { get; set; }
        public SolidColorBrush BorderColor { get; set; } = new SolidColorBrush(Colors.White);
        public RelayCommand DeleteCommand { get; }
        public bool TextBox { get; set; }
        public bool TextBlock { get; set; }
        public enum Mode
        {
            TextBox,
            TextBlock
        };

        public static ObservableCollection<VM_CollectionMemberStringDecorated> InitializeObservableCollectionFromICollection(ICollection<string> source)
        {
            ObservableCollection<VM_CollectionMemberStringDecorated> parentCollection = new ObservableCollection<VM_CollectionMemberStringDecorated>();
            foreach (string s in source)
            {
                var newCMS = new VM_CollectionMemberStringDecorated(s, parentCollection, Mode.TextBlock);
                parentCollection.Add(newCMS);
            }
            return parentCollection;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    /// <summary>View model for a string row with display decoration (text-box vs text-block mode, border/text colors) and a delete command.</summary>
    public class VM_CollectionMemberStringDecorated : VM
    {
        /// <summary>Creates the decorated row VM in the given display mode.</summary>
        /// <param name="content">The string value.</param>
        /// <param name="parentCollection">The collection this row belongs to.</param>
        /// <param name="mode">Whether the row renders as an editable text box or a static text block.</param>
        public VM_CollectionMemberStringDecorated(string content, ObservableCollection<VM_CollectionMemberStringDecorated> parentCollection, Mode mode)
        {
            Content = content;
            ParentCollection = parentCollection;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
            switch(mode)
            {
                case Mode.TextBox: TextBox = true; TextBlock = false; break;
                case Mode.TextBlock: TextBlock = true; TextBox = false; break;
            }
        }
        public string Content { get; set; }
        public ObservableCollection<VM_CollectionMemberStringDecorated> ParentCollection { get; set; }
        public SolidColorBrush BorderColor { get; set; } = CommonColors.White;
        public SolidColorBrush TextColor { get; set; } = CommonColors.White;
        public RelayCommand DeleteCommand { get; }
        public bool TextBox { get; set; }
        public bool TextBlock { get; set; }
        /// <summary>Whether a row renders as an editable text box or a read-only text block.</summary>
        public enum Mode
        {
            /// <summary>Editable text box.</summary>
            TextBox,
            /// <summary>Read-only text block.</summary>
            TextBlock
        };

        /// <summary>Builds a new observable collection of text-block-decorated rows from a source string collection.</summary>
        /// <param name="source">Source strings.</param>
        /// <returns>A new collection of decorated row view models.</returns>
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

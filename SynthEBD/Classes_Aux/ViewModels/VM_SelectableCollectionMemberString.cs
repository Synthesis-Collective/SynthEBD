using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SynthEBD
{
    public class VM_SelectableCollectionMemberString : INotifyPropertyChanged
    {
        public VM_SelectableCollectionMemberString(VM_CollectionMemberString subscribedString, ICollectionParent parentCollection)
        {
            this.SubscribedString = subscribedString;
            this.Parent = parentCollection;
            this.IsSelected = false;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => this.Parent.CollectionMemberStrings.Remove(this));

            this.PropertyChanged += refreshHeaderCaption;
        }
        public VM_CollectionMemberString SubscribedString { get; set; }
        public ICollectionParent Parent { get; set; }
        public bool IsSelected { get; set; }
        public RelayCommand DeleteCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void refreshHeaderCaption(object sender, PropertyChangedEventArgs e)
        {
            this.Parent.RebuildHeader();
        }
    }

    public interface ICollectionParent
    {
        ObservableCollection<VM_SelectableCollectionMemberString> CollectionMemberStrings { get; set; }
        string Header { get; set; }
        void RebuildHeader();
    }

    public class VM_CollectionMemberStringCheckboxList : INotifyPropertyChanged, ICollectionParent
    {
        public VM_CollectionMemberStringCheckboxList(ObservableCollection<VM_CollectionMemberString> collectionMemberStrings)
        {
            this.CollectionMemberStrings = new ObservableCollection<VM_SelectableCollectionMemberString>();
            foreach (var cms in collectionMemberStrings)
            {
                this.CollectionMemberStrings.Add(new VM_SelectableCollectionMemberString(cms, this));
            }

            RebuildHeader();
            this.SubscribedMasterList = collectionMemberStrings;
            this.SubscribedMasterList.CollectionChanged += RefreshCheckList;
        }

        public ObservableCollection<VM_CollectionMemberString> SubscribedMasterList { get; set; } // to fire the CollectionChanged event

        public event PropertyChangedEventHandler PropertyChanged;

        void RefreshCheckList(object sender, NotifyCollectionChangedEventArgs e)
        {
            var updatedMasterList = (ObservableCollection<VM_CollectionMemberString>)sender;
            var newCheckList = new VM_CollectionMemberStringCheckboxList(updatedMasterList);

            foreach (var originalItem in this.CollectionMemberStrings)
            {
                foreach (var newItem in newCheckList.CollectionMemberStrings)
                {
                    if (originalItem.SubscribedString.Content == newItem.SubscribedString.Content)
                    {
                        newItem.IsSelected = originalItem.IsSelected;
                    }
                }
            }

            this.CollectionMemberStrings = newCheckList.CollectionMemberStrings;
        }

        public ObservableCollection<VM_SelectableCollectionMemberString> CollectionMemberStrings { get; set; }
        public string Header { get; set; }

        public void RebuildHeader()
        {
            string header = "";
            foreach (var selection in this.CollectionMemberStrings)
            {
                if (selection.IsSelected)
                {
                    header += selection.SubscribedString.Content + ", ";
                }
            }

            if (header != "")
            {
                header = header.Remove(header.Length - 2, 2);
            }
            this.Header = header;
        }

        public void InitializeFromHashSet(HashSet<string> selectedStrings)
        {
            foreach (var cms in this.CollectionMemberStrings)
            {
                if (selectedStrings.Contains(cms.SubscribedString.Content))
                {
                    cms.IsSelected = true;
                }
                else
                {
                    cms.IsSelected = false;
                }    
            }
        }
    }
}

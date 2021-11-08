using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BodyGenMorphDescriptorSelectionMenu : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorSelectionMenu(VM_BodyGenMorphDescriptorMenu trackedMenu)
        {
            this.Header = "";
            this.TrackedMenu = trackedMenu;
            this.DescriptorShells = new ObservableCollection<VM_BodyGenMorphDescriptorShellSelector>();
            foreach (var Descriptor in TrackedMenu.TemplateDescriptors)
            {
                this.DescriptorShells.Add(new VM_BodyGenMorphDescriptorShellSelector(Descriptor, this));
            }
            this.CurrentlyDisplayedShell = new VM_BodyGenMorphDescriptorShellSelector(new VM_BodyGenMorphDescriptorShell(new ObservableCollection<VM_BodyGenMorphDescriptorShell>()), this);

            trackedMenu.TemplateDescriptors.CollectionChanged += UpdateShellList;
        }
        public string Header { get; set; }
        public VM_BodyGenMorphDescriptorMenu TrackedMenu { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptorShellSelector> DescriptorShells { get; set; }
        public VM_BodyGenMorphDescriptorShellSelector CurrentlyDisplayedShell { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public void UpdateShellList(object sender, NotifyCollectionChangedEventArgs e)
        {
            // remove deleted shells
            for (int i = 0; i < this.DescriptorShells.Count; i++)
            {
                bool found = false;
                foreach (var sourceShell in this.TrackedMenu.TemplateDescriptors)
                {
                    if (this.DescriptorShells[i].TrackedShell.Category == sourceShell.Category)
                    {
                        found = true;
                        break;
                    }
                }
                if (found == false)
                {
                    this.DescriptorShells.RemoveAt(i);
                    i--;
                }
            }

            // add new shells
            foreach (var sourceShell in this.TrackedMenu.TemplateDescriptors)
            {
                bool found = false;
                foreach (var destShell in this.DescriptorShells)
                {
                    if (destShell.TrackedShell.Category == sourceShell.Category)
                    {
                        found = true;
                        break;
                    }
                }
                if (found == false)
                {
                    this.DescriptorShells.Add(new VM_BodyGenMorphDescriptorShellSelector(sourceShell, this));
                }
            }

            this.UpdateHeader();
        }

        public static VM_BodyGenMorphDescriptorSelectionMenu InitializeFromHashSet(HashSet<BodyGenConfig.MorphDescriptor> morphDescriptors, VM_BodyGenMorphDescriptorMenu trackedMenu)
        {
            var menu = new VM_BodyGenMorphDescriptorSelectionMenu(trackedMenu);
            foreach (var descriptor in morphDescriptors)
            {
                bool keepLooking = true;
                foreach (var Descriptor in menu.DescriptorShells)
                {
                    foreach (var selectableDescriptor in Descriptor.DescriptorSelectors)
                    {
                        if (selectableDescriptor.TrackedDescriptor.DispString == descriptor.DispString)
                        {
                            selectableDescriptor.IsSelected = true;
                            keepLooking = false;
                            break;
                        }
                    }
                    if (keepLooking == false) { break; }
                }
            }
            menu.Header = BuildHeader(menu);
            return menu;
        }

        public static HashSet<BodyGenConfig.MorphDescriptor> DumpToHashSet(VM_BodyGenMorphDescriptorSelectionMenu viewModel)
        {
            HashSet<BodyGenConfig.MorphDescriptor> output = new HashSet<BodyGenConfig.MorphDescriptor>();
            foreach (var shell in viewModel.DescriptorShells)
            {
                output.UnionWith(shell.DescriptorSelectors.Where(x => x.IsSelected).Select(x => new BodyGenConfig.MorphDescriptor() { Category = shell.TrackedShell.Category, Value = x.Value, DispString = x.TrackedDescriptor.DispString}).ToHashSet());
            }

            return output;
        }

        public void UpdateHeader()
        {
            this.Header = BuildHeader(this);
        }

        static string BuildHeader(VM_BodyGenMorphDescriptorSelectionMenu menu)
        {
            string header = "";
            foreach (var Descriptor in menu.DescriptorShells)
            {
                string subHeader = "";
                string catHeader = Descriptor.TrackedShell.Category + ": ";
                foreach (var descriptor in Descriptor.DescriptorSelectors)
                {
                    if (descriptor.IsSelected)
                    {
                        subHeader += descriptor.Value + ", ";
                    }
                }
                if (subHeader.EndsWith(", "))
                {
                    subHeader = subHeader.Remove(subHeader.Length - 2);
                }
                if (subHeader != "")
                {
                    header += catHeader + subHeader + " | ";
                }
            }

            if (header.EndsWith(" | "))
            {
                header = header.Remove(header.Length - 3);
            }
            return header;
        }
    }

    public class VM_BodyGenMorphDescriptorShellSelector : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorShellSelector(VM_BodyGenMorphDescriptorShell trackedShell, VM_BodyGenMorphDescriptorSelectionMenu parentMenu)
        {
            this.TrackedShell = trackedShell;
            this.ParentMenu = parentMenu;
            this.DescriptorSelectors = new ObservableCollection<VM_BodyGenMorphDescriptorSelector>();
            foreach (var descriptor in this.TrackedShell.Descriptors)
            {
                this.DescriptorSelectors.Add(new VM_BodyGenMorphDescriptorSelector(descriptor, this.ParentMenu));
            }
            this.TrackedShell.Descriptors.CollectionChanged += UpdateDescriptorList;

        }
        public VM_BodyGenMorphDescriptorShell TrackedShell { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu ParentMenu { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptorSelector> DescriptorSelectors { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        void UpdateDescriptorList(object sender, NotifyCollectionChangedEventArgs e)
        {
            // remove deleted Descriptors
            for (int i = 0; i < this.DescriptorSelectors.Count; i++)
            {
                bool found = false;
                foreach (var sourceDescriptor in this.TrackedShell.Descriptors)
                {
                    if (this.DescriptorSelectors[i].TrackedDescriptor.Value == sourceDescriptor.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (found == false)
                {
                    this.DescriptorSelectors.RemoveAt(i);
                    i--;
                }
            }

            // add new Descriptors
            foreach (var sourceDescriptor in this.TrackedShell.Descriptors)
            {
                bool found = false;
                foreach (var destDescriptor in this.DescriptorSelectors)
                {
                    if (destDescriptor.TrackedDescriptor.Value == sourceDescriptor.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (found == false)
                {
                    this.DescriptorSelectors.Add(new VM_BodyGenMorphDescriptorSelector(sourceDescriptor, this.ParentMenu));
                }
            }
        }
    }

    public class VM_BodyGenMorphDescriptorSelector : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorSelector(VM_BodyGenMorphDescriptor trackedDescriptor, VM_BodyGenMorphDescriptorSelectionMenu parentMenu)
        {
            this.TrackedDescriptor = trackedDescriptor;
            this.ParentMenu = parentMenu;
            this.IsSelected = false;
            this.Value = TrackedDescriptor.Value;

            this.TrackedDescriptor.PropertyChanged += refreshLabelAndHeader;
            this.PropertyChanged += refreshHeader;
        }

        public VM_BodyGenMorphDescriptor TrackedDescriptor { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu ParentMenu { get; set; }
        public string Value { get; set; }
        public bool IsSelected { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void refreshLabelAndHeader(object sender, PropertyChangedEventArgs e)
        {
            this.Value = TrackedDescriptor.Value;
            this.ParentMenu.UpdateHeader();
        }
        public void refreshHeader(object sender, PropertyChangedEventArgs e)
        {
            this.ParentMenu.UpdateHeader();
        }
    }
}

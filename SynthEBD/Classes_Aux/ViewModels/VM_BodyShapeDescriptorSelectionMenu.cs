using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptorSelectionMenu : VM
{
    public VM_BodyShapeDescriptorSelectionMenu(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        this.TrackedMenu = trackedMenu;
        this.TrackedRaceGroupings = raceGroupingVMs;
        this.Parent = parentConfig;
        foreach (var Descriptor in TrackedMenu.TemplateDescriptors)
        {
            this.DescriptorShells.Add(new VM_BodyShapeDescriptorShellSelector(Descriptor, this));
        }
        this.CurrentlyDisplayedShell = new VM_BodyShapeDescriptorShellSelector(new VM_BodyShapeDescriptorShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), raceGroupingVMs, parentConfig), this);

        trackedMenu.TemplateDescriptors.CollectionChanged += UpdateShellList;
    }
    public string Header { get; set; } = "";
    public VM_BodyShapeDescriptorCreationMenu TrackedMenu { get; set; }
    public IHasAttributeGroupMenu Parent { get; set; }
    public ObservableCollection<VM_BodyShapeDescriptorShellSelector> DescriptorShells { get; set; } = new();
    ObservableCollection<VM_RaceGrouping>  TrackedRaceGroupings { get; set; }
    public VM_BodyShapeDescriptorShellSelector CurrentlyDisplayedShell { get; set; }

    public VM_BodyShapeDescriptorSelectionMenu Clone()
    {
        var modelDump = DumpToHashSet(this);
        return InitializeFromHashSet(modelDump, TrackedMenu, TrackedRaceGroupings, Parent);
    }

    public bool IsAnnotated()
    {
        foreach (var shell in DescriptorShells)
        {
            foreach (var descriptor in shell.DescriptorSelectors)
            {
                if (descriptor.IsSelected)
                {
                    return true;
                }
            }
        }
        return false;
    }

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
                this.DescriptorShells.Add(new VM_BodyShapeDescriptorShellSelector(sourceShell, this));
            }
        }

        this.UpdateHeader();
    }

    public static VM_BodyShapeDescriptorSelectionMenu InitializeFromHashSet(HashSet<BodyShapeDescriptor> BodyShapeDescriptors, VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        var menu = new VM_BodyShapeDescriptorSelectionMenu(trackedMenu, raceGroupingVMs, parentConfig);
        foreach (var descriptor in BodyShapeDescriptors)
        {
            bool keepLooking = true;
            foreach (var Descriptor in menu.DescriptorShells)
            {
                foreach (var selectableDescriptor in Descriptor.DescriptorSelectors)
                {
                    if (selectableDescriptor.TrackedDescriptor.Signature == descriptor.Signature)
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

    public static HashSet<BodyShapeDescriptor> DumpToHashSet(VM_BodyShapeDescriptorSelectionMenu viewModel)
    {
        HashSet<BodyShapeDescriptor> output = new HashSet<BodyShapeDescriptor>();
        if (viewModel is not null && viewModel.DescriptorShells is not null)
        {
            foreach (var shell in viewModel.DescriptorShells)
            {
                output.UnionWith(shell.DescriptorSelectors.Where(x => x.IsSelected).Select(x => new BodyShapeDescriptor() { Category = shell.TrackedShell.Category, Value = x.Value, Signature = x.TrackedDescriptor.Signature }).ToHashSet());
            }
        }
        return output;
    }

    public void UpdateHeader()
    {
        this.Header = BuildHeader(this);
    }

    static string BuildHeader(VM_BodyShapeDescriptorSelectionMenu menu)
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

public class VM_BodyShapeDescriptorShellSelector : VM
{
    public VM_BodyShapeDescriptorShellSelector(VM_BodyShapeDescriptorShell trackedShell, VM_BodyShapeDescriptorSelectionMenu parentMenu)
    {
        this.TrackedShell = trackedShell;
        this.ParentMenu = parentMenu;
        foreach (var descriptor in this.TrackedShell.Descriptors)
        {
            this.DescriptorSelectors.Add(new VM_BodyShapeDescriptorSelector(descriptor, this.ParentMenu));
        }
        this.TrackedShell.Descriptors.CollectionChanged += UpdateDescriptorList;
    }
    public VM_BodyShapeDescriptorShell TrackedShell { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public ObservableCollection<VM_BodyShapeDescriptorSelector> DescriptorSelectors { get; set; } = new();

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
                this.DescriptorSelectors.Add(new VM_BodyShapeDescriptorSelector(sourceDescriptor, this.ParentMenu));
            }
        }
    }
}

public class VM_BodyShapeDescriptorSelector : VM
{
    public VM_BodyShapeDescriptorSelector(VM_BodyShapeDescriptor trackedDescriptor, VM_BodyShapeDescriptorSelectionMenu parentMenu)
    {
        this.TrackedDescriptor = trackedDescriptor;
        this.ParentMenu = parentMenu;
        this.Value = TrackedDescriptor.Value;

        this.TrackedDescriptor.PropertyChanged += refreshLabelAndHeader;
        this.PropertyChanged += refreshHeader;
    }

    public VM_BodyShapeDescriptor TrackedDescriptor { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public string Value { get; set; }
    public bool IsSelected { get; set; } = false;

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
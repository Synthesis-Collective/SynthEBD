using DynamicData;
using DynamicData.Binding;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

public class VM_BodyShapeDescriptorSelectionMenu : VM
{
    private readonly Factory _selfFactory;
    public delegate VM_BodyShapeDescriptorSelectionMenu Factory(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode);
    public VM_BodyShapeDescriptorSelectionMenu(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, VM_BodyShapeDescriptorCreator descriptorCreator, VM_BodyShapeDescriptorSelectionMenu.Factory selfFactory)
    {
        _selfFactory = selfFactory;

        ShowMatchMode = showMatchMode;
        MatchMode = matchMode;
        TrackedMenu = trackedMenu;
        TrackedRaceGroupings = raceGroupingVMs;
        Parent = parentConfig;
        
        CurrentlyDisplayedShell = new VM_BodyShapeDescriptorShellSelector(descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), raceGroupingVMs, parentConfig), this);

        if (TrackedMenu != null)
        {
            foreach (var Descriptor in TrackedMenu.TemplateDescriptors)
            {
                DescriptorShells.Add(new VM_BodyShapeDescriptorShellSelector(Descriptor, this));
            }
            TrackedMenu.TemplateDescriptors.ToObservableChangeSet().Subscribe(_ => UpdateShellList()).DisposeWith(this);
        }

        DescriptorShells
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.NeedsRefresh)
                .Subscribe(_ => BuildHeader())
                .DisposeWith(this))
            .DisposeMany() // Dispose subscriptions related to removed attributes
            .Subscribe()  // Execute my instructions
            .DisposeWith(this);
        
    }
    public string Header { get; set; }
    public VM_BodyShapeDescriptorCreationMenu TrackedMenu { get; set; }
    public IHasAttributeGroupMenu Parent { get; set; }
    public ObservableCollection<VM_BodyShapeDescriptorShellSelector> DescriptorShells { get; set; } = new();
    ObservableCollection<VM_RaceGrouping>  TrackedRaceGroupings { get; set; }
    public VM_BodyShapeDescriptorShellSelector CurrentlyDisplayedShell { get; set; }
    public bool ShowMatchMode { get; set; } = false;
    public DescriptorMatchMode MatchMode { get; set; } = DescriptorMatchMode.All;
    public VM_BodyShapeDescriptorSelectionMenu Clone()
    {
        var modelDump = DumpToHashSet();
        return InitializeFromHashSet(modelDump, TrackedMenu, TrackedRaceGroupings, Parent, ShowMatchMode, MatchMode, _selfFactory);
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

    public void UpdateShellList()
    {
        // remove deleted shells
        for (int i = 0; i < DescriptorShells.Count; i++)
        {
            bool found = false;
            foreach (var sourceShell in TrackedMenu.TemplateDescriptors)
            {
                if (DescriptorShells[i].TrackedShell.Category == sourceShell.Category)
                {
                    found = true;
                    break;
                }
            }
            if (found == false)
            {
                DescriptorShells.RemoveAt(i);
                i--;
            }
        }

        // add new shells
        foreach (var sourceShell in TrackedMenu.TemplateDescriptors)
        {
            bool found = false;
            foreach (var destShell in DescriptorShells)
            {
                if (destShell.TrackedShell.Category == sourceShell.Category)
                {
                    found = true;
                    break;
                }
            }
            if (found == false)
            {
                DescriptorShells.Add(new VM_BodyShapeDescriptorShellSelector(sourceShell, this));
            }
        }
    }

    public static VM_BodyShapeDescriptorSelectionMenu InitializeFromHashSet(HashSet<BodyShapeDescriptor.LabelSignature> bodyShapeDescriptors, VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        var menu = descriptorSelectionFactory(trackedMenu, raceGroupingVMs, parentConfig, showMatchMode, matchMode);
        if (bodyShapeDescriptors != null)
        {
            foreach (var descriptor in bodyShapeDescriptors)
            {
                bool keepLooking = true;
                foreach (var Descriptor in menu.DescriptorShells)
                {
                    foreach (var selectableDescriptor in Descriptor.DescriptorSelectors)
                    {
                        if (selectableDescriptor.TrackedDescriptor.MapsTo(descriptor))
                        {
                            selectableDescriptor.IsSelected = true;
                            keepLooking = false;
                            break;
                        }
                    }
                    if (keepLooking == false) { break; }
                }
            }
        }
        return menu;
    }

    public HashSet<BodyShapeDescriptor.LabelSignature> DumpToHashSet()
    {
        HashSet<BodyShapeDescriptor.LabelSignature> output = new HashSet<BodyShapeDescriptor.LabelSignature>();
        if (this is not null && DescriptorShells is not null)
        {
            foreach (var shell in DescriptorShells)
            {
                output.UnionWith(shell.DescriptorSelectors.Where(x => x.IsSelected).Select(x => new BodyShapeDescriptor.LabelSignature() { Category = shell.TrackedShell.Category, Value = x.Value }).ToHashSet());
            }
        }
        return output;
    }

    public void BuildHeader()
    {
        string header = "";
        foreach (var Descriptor in DescriptorShells)
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
        Header = header;
    }
}

public class VM_BodyShapeDescriptorShellSelector : VM
{
    public VM_BodyShapeDescriptorShellSelector(VM_BodyShapeDescriptorShell trackedShell, VM_BodyShapeDescriptorSelectionMenu parentMenu)
    {
        TrackedShell = trackedShell;
        ParentMenu = parentMenu;
        foreach (var descriptor in TrackedShell.Descriptors)
        {
            DescriptorSelectors.Add(new VM_BodyShapeDescriptorSelector(descriptor, ParentMenu));
        }
        TrackedShell.Descriptors.ToObservableChangeSet().Subscribe(_ => UpdateDescriptorList()).DisposeWith(this);
        TrackedShell.Descriptors.ToObservableChangeSet()
            .QueryWhenChanged(x => x)
            .Subscribe(x =>
            {
                NeedsRefresh = DescriptorSelectors.Select(x => x.WhenAnyValue(x => x.IsSelected)).Merge().Unit();
            })
            .DisposeWith(this);
    }
    public VM_BodyShapeDescriptorShell TrackedShell { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public ObservableCollection<VM_BodyShapeDescriptorSelector> DescriptorSelectors { get; set; } = new();
    public IObservable<Unit> NeedsRefresh { get; set; }

    void UpdateDescriptorList()
    {
        // remove deleted Descriptors
        for (int i = 0; i < this.DescriptorSelectors.Count; i++)
        {
            bool found = false;
            foreach (var sourceDescriptor in TrackedShell.Descriptors)
            {
                if (DescriptorSelectors[i].TrackedDescriptor.Value == sourceDescriptor.Value)
                {
                    found = true;
                    break;
                }
            }
            if (found == false)
            {
                DescriptorSelectors.RemoveAt(i);
                i--;
            }
        }

        // add new Descriptors
        foreach (var sourceDescriptor in TrackedShell.Descriptors)
        {
            bool found = false;
            foreach (var destDescriptor in DescriptorSelectors)
            {
                if (destDescriptor.TrackedDescriptor.Value == sourceDescriptor.Value)
                {
                    found = true;
                    break;
                }
            }
            if (found == false)
            {
                DescriptorSelectors.Add(new VM_BodyShapeDescriptorSelector(sourceDescriptor, ParentMenu));
            }
        }
    }
}

public class VM_BodyShapeDescriptorSelector : VM
{
    public VM_BodyShapeDescriptorSelector(VM_BodyShapeDescriptor trackedDescriptor, VM_BodyShapeDescriptorSelectionMenu parentMenu)
    {
        TrackedDescriptor = trackedDescriptor;
        ParentMenu = parentMenu;
        Value = TrackedDescriptor.Value;

        TrackedDescriptor.WhenAnyValue(x => x.Value).Subscribe(_ => Value = TrackedDescriptor.Value).DisposeWith(this);
    }

    public VM_BodyShapeDescriptor TrackedDescriptor { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public string Value { get; set; }
    public bool IsSelected { get; set; } = false;
}
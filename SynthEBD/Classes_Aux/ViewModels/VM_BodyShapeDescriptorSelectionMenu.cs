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
    public delegate VM_BodyShapeDescriptorSelectionMenu Factory(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, bool showPriority);
    public VM_BodyShapeDescriptorSelectionMenu(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, bool showPriority, VM_BodyShapeDescriptorCreator descriptorCreator, VM_BodyShapeDescriptorSelectionMenu.Factory selfFactory)
    {
        _selfFactory = selfFactory;

        ShowMatchMode = showMatchMode;
        ShowPriority = showPriority;
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
            TrackedMenu.TemplateDescriptors.ToObservableChangeSet().Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler).Subscribe(_ => UpdateShellList()).DisposeWith(this);
        }

        DescriptorShells
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.NeedsRefresh)
                .Subscribe(_ => 
                { 
                    if (!_initializing)
                    {
                        AutoSelected = false;
                    }
                    BuildHeader();
                })
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
    public bool AutoSelected { get; set; } = false;
    private bool _initializing { get; set; } = false;
    public bool ShowPriority { get; set; } = false;

    public HashSet<BodyShapeDescriptor.LabelSignature> BackupStash { get; set; } = new(); // if a descriptor is present in the model but not present in the corresponding UI, stash here to write back to the model
    public HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> PrioritizedBackupStash { get; set; } = new(); // if a descriptor is present in the model but not present in the corresponding UI, stash here to write back to the model
    private VM_BodyShapeDescriptorSelectionMenu OppositeToggleMenu { get; set; } = null; // if this menu gets a selection, its opposite gets the same selection deselected
    public VM_BodyShapeDescriptorSelectionMenu Clone()
    {
        VM_BodyShapeDescriptorSelectionMenu clone = _selfFactory(TrackedMenu, TrackedRaceGroupings, Parent, ShowMatchMode, MatchMode, ShowPriority);
        if (ShowPriority)
        {
            HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> pModelDump = DumpToPrioritizedHashSet();            
            clone.CopyInFromHashSet(pModelDump);
        }
        else
        {
            var modelDump = DumpToHashSet();
            clone.CopyInFromHashSet(modelDump);
        }
        return clone;
    }

    public void SetOppositeToggleMenu(VM_BodyShapeDescriptorSelectionMenu opposite)
    {
        OppositeToggleMenu = opposite;
        if (OppositeToggleMenu != null)
        {
            foreach (var descriptorShell in DescriptorShells)
            {
                var oppositeShell = OppositeToggleMenu.DescriptorShells.Where(x => x.TrackedShell.Category == descriptorShell.TrackedShell.Category).FirstOrDefault();
                if (oppositeShell != null)
                {
                    foreach (var selector in descriptorShell.DescriptorSelectors)
                    {
                        var oppositeSelector = oppositeShell.DescriptorSelectors.Where(x => x.TrackedDescriptor.Value == selector.TrackedDescriptor.Value).FirstOrDefault();
                        if (oppositeSelector != null)
                        {
                            selector.WhenAnyValue(x => x.IsSelected).Subscribe(isSelected =>
                            {
                                if (isSelected)
                                {
                                    oppositeSelector.IsSelected = false;
                                }
                            }).DisposeWith(this);
                        }
                    }
                }
            }
        }
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

        if (OppositeToggleMenu != null)
        {
            SetOppositeToggleMenu(OppositeToggleMenu); // refresh toggles to make sure new ones get added
        }
    }

    public void CopyInFromHashSet(HashSet<BodyShapeDescriptor.LabelSignature> bodyShapeDescriptors)
    {
        _initializing = true;
        if (bodyShapeDescriptors != null)
        {
            foreach (var descriptor in bodyShapeDescriptors)
            {
                bool keepLooking = true;
                foreach (var Descriptor in DescriptorShells)
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
                if (keepLooking)
                {
                    BackupStash.Add(descriptor); // descriptor is no longer present in the UI
                }
            }
        }
        _initializing = false;
    }

    public void CopyInFromHashSet(HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> bodyShapeDescriptors)
    {
        _initializing = true;
        if (bodyShapeDescriptors != null)
        {
            foreach (var descriptor in bodyShapeDescriptors)
            {
                bool keepLooking = true;
                foreach (var Descriptor in DescriptorShells)
                {
                    foreach (var selectableDescriptor in Descriptor.DescriptorSelectors)
                    {
                        if (selectableDescriptor.TrackedDescriptor.MapsTo(descriptor))
                        {
                            selectableDescriptor.IsSelected = true;
                            selectableDescriptor.Priority = descriptor.Priority;
                            keepLooking = false;
                            break;
                        }
                    }
                    if (keepLooking == false) { break; }
                }
                if (keepLooking)
                {
                    BackupStash.Add(descriptor); // descriptor is no longer present in the UI
                }
            }
        }
        _initializing = false;
    }

    public HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> DumpToPrioritizedHashSet()
    {
        HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> output = new(PrioritizedBackupStash);
        if (this is not null && DescriptorShells is not null)
        {
            foreach (var shell in DescriptorShells)
            {
                output.UnionWith(shell.DescriptorSelectors.Where(x => x.Priority > 0).Select(x => new BodyShapeDescriptor.PrioritizedLabelSignature() { Category = shell.TrackedShell.Category, Value = x.Value, Priority = x.Priority }).ToHashSet());
            }
        }
        return output;
    }

    public HashSet<BodyShapeDescriptor.LabelSignature> DumpToHashSet()
    {
        HashSet<BodyShapeDescriptor.LabelSignature> output = new(BackupStash);
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
        List<string> categories = new();
        foreach (var Descriptor in DescriptorShells)
        {
            string catHeader = Descriptor.TrackedShell.Category + ": ";
            var selectedValues = Descriptor.DescriptorSelectors.Select(x => FormatSelection(x)).Where(x => x != string.Empty).ToArray();
            if (selectedValues.Any())
            {
                categories.Add(catHeader + string.Join(", ", selectedValues));
            }  
        }

        Header = string.Join(" | ", categories);
    }

    private string FormatSelection(VM_BodyShapeDescriptorSelector selection)
    {
        if(selection.ParentMenu.ShowPriority)
        {
            if(selection.Priority != 0)
            {
                return selection.Value + " (" + selection.Priority + ")";
            }
        }    
        else if(selection.IsSelected)
        {
            return selection.Value;
        }
        return string.Empty;
    }

    public void DeselectAll()
    {
        foreach (var shell in DescriptorShells)
        {
            foreach (var entry in shell.DescriptorSelectors)
            {
                entry.IsSelected = false;
            }
        }
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
                NeedsRefresh = DescriptorSelectors
                    .Select(x => x.WhenAnyValue(x => x.IsSelected, x => x.Priority))
                    .CombineLatest()
                    .Select(_ => Unit.Default);
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
    public int Priority { get; set; } = 0;
}
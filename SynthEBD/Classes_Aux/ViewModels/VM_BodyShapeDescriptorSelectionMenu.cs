using DynamicData;
using DynamicData.Binding;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography.Pkcs;
using System.Windows.Media;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

/// <summary>
/// View model for a descriptor-selection menu: mirrors a creation menu's category shells as selectable
/// rows (optionally with priority and match-mode), tracks an aggregate annotation state, supports an
/// "opposite" menu that auto-deselects conflicting picks, and round-trips selections to/from descriptor
/// signature sets (plain, prioritized, or annotated).
/// </summary>
public class VM_BodyShapeDescriptorSelectionMenu : VM
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing a selection menu.</summary>
    public delegate VM_BodyShapeDescriptorSelectionMenu Factory(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, bool showPriority);
    /// <summary>Creates the menu, building a selectable shell per tracked category, keeping it synced to the source menu, and recomputing the annotation state and header as selections change.</summary>
    public VM_BodyShapeDescriptorSelectionMenu(VM_BodyShapeDescriptorCreationMenu trackedMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, bool showMatchMode, DescriptorMatchMode matchMode, bool showPriority, VM_BodyShapeDescriptorCreator descriptorCreator, VM_BodyShapeDescriptorSelectionMenu.Factory selfFactory)
    {
        _selfFactory = selfFactory;

        ShowMatchMode = showMatchMode;
        ShowPriority = showPriority;
        MatchMode = matchMode;
        TrackedMenu = trackedMenu;
        TrackedRaceGroupings = raceGroupingVMs;
        Parent = parentConfig;
        
        CurrentlyDisplayedShell = new VM_BodyShapeDescriptorShellSelector(descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), raceGroupingVMs, parentConfig, null, null), this);

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
                        AnnotationState = AnnotationStateComputer.ComputeAnnotationState(DescriptorShells.Cast<IHasAnnotationState>().ToList());
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
    public BodyShapeAnnotationState AnnotationState { get; set; } = BodyShapeAnnotationState.None;
    private bool _initializing { get; set; } = false;
    public bool ShowPriority { get; set; } = false;

    public HashSet<BodyShapeDescriptor.LabelSignature> BackupStash { get; set; } = new(); // if a descriptor is present in the model but not present in the corresponding UI, stash here to write back to the model
    public HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> PrioritizedBackupStash { get; set; } = new(); // if a descriptor is present in the model but not present in the corresponding UI, stash here to write back to the model
    private VM_BodyShapeDescriptorSelectionMenu OppositeToggleMenu { get; set; } = null; // if this menu gets a selection, its opposite gets the same selection deselected
    /// <summary>Creates a copy of this menu with the same selections (prioritized or plain, per <see cref="ShowPriority"/>).</summary>
    /// <returns>The cloned menu.</returns>
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

    /// <summary>Links an "opposite" menu so selecting a descriptor here deselects the matching one there (e.g. Allowed vs Disallowed are mutually exclusive).</summary>
    /// <param name="opposite">The opposing selection menu.</param>
    public void SetOppositeToggleMenu(VM_BodyShapeDescriptorSelectionMenu opposite)
    {
        OppositeToggleMenu = opposite;
        if (OppositeToggleMenu != null)
        {
            foreach (var descriptorShell in DescriptorShells)
            {
                var oppositeShell = OppositeToggleMenu.DescriptorShells.FirstOrDefault(x => x.TrackedShell.Category == descriptorShell.TrackedShell.Category);
                if (oppositeShell != null)
                {
                    foreach (var selector in descriptorShell.DescriptorSelectors)
                    {
                        var oppositeSelector = oppositeShell.DescriptorSelectors.FirstOrDefault(x => x.TrackedDescriptor.Value == selector.TrackedDescriptor.Value);
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

    /// <summary>Whether any descriptor in the menu is selected.</summary>
    /// <returns><c>true</c> if at least one descriptor is selected.</returns>
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

    /// <summary>Syncs the selectable shells to the tracked menu's categories (adding new, removing deleted) and refreshes the opposite-toggle links.</summary>
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

    /// <summary>Selects the rows matching the given descriptor signatures (carrying over priority/annotation state), stashing any signature with no matching row so it survives the round-trip.</summary>
    /// <typeparam name="T">A descriptor label-signature type.</typeparam>
    /// <param name="bodyShapeDescriptors">The descriptor signatures to select.</param>
    public void CopyInFromHashSet<T>(HashSet<T> bodyShapeDescriptors)
        where T: BodyShapeDescriptor.LabelSignature
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
                            if (descriptor is AnnotatedDescriptorSignature annotated)
                            {
                                selectableDescriptor.AnnotationState = annotated.AnnotationState;
                            }
                            else if (descriptor is BodyShapeDescriptor.PrioritizedLabelSignature prioritized)
                            {
                                selectableDescriptor.Priority = prioritized.Priority;
                            }
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

    /// <summary>Dumps the prioritized (priority &gt; 0) descriptor selections as prioritized signatures, plus the prioritized backup stash.</summary>
    /// <returns>The prioritized descriptor signatures.</returns>
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

    /// <summary>Dumps the selected descriptors as plain signatures, plus the backup stash.</summary>
    /// <returns>The selected descriptor signatures.</returns>
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

    /// <summary>Dumps the selected descriptors as annotated signatures (carrying annotation state), as stored in OBody settings.</summary>
    /// <returns>The annotated descriptor signatures.</returns>
    public HashSet<AnnotatedDescriptorSignature> DumpToOBodySettingsHashSet()
    {
        HashSet<AnnotatedDescriptorSignature> output = new(BackupStash.Select(x => new AnnotatedDescriptorSignature(x)));
        if (this is not null && DescriptorShells is not null)
        {
            foreach (var shell in DescriptorShells)
            {
                output.UnionWith(shell.DescriptorSelectors.Where(x => x.IsSelected).Select(x => new AnnotatedDescriptorSignature(new BodyShapeDescriptor.LabelSignature() { Category = shell.TrackedShell.Category, Value = x.Value }, x.AnnotationState)).ToHashSet());
            }
        }
        return output;
    }

    /// <summary>Rebuilds the pipe-joined <see cref="Header"/> summarizing the selected descriptors grouped by category.</summary>
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

    /// <summary>Formats one selector for the header — the value, or "value (priority)" in priority mode — or empty when not selected.</summary>
    /// <param name="selection">The selector to format.</param>
    /// <returns>The formatted string, or empty.</returns>
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

    /// <summary>Deselects every descriptor in the menu.</summary>
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

    /// <summary>
    /// Replaces this menu's <see cref="BodyShapeAnnotationState.Classifier"/>-tagged selections
    /// with the supplied set. Used by <see cref="BodySlideMeasurementEvaluator"/> after a
    /// per-weight evaluation pass so the UI mirrors the merged model state.
    /// Manual / Library / RulesBased selections are left untouched -- they win every conflict.
    /// </summary>
    public void ApplyClassifierDescriptors(IEnumerable<AnnotatedDescriptorSignature> classifierDescriptors)
    {
        // Build a lookup of incoming (Category, Value) for fast hit checks.
        var incoming = new HashSet<(string Cat, string Val)>();
        if (classifierDescriptors != null)
        {
            foreach (var d in classifierDescriptors)
            {
                if (d == null || string.IsNullOrEmpty(d.Category) || string.IsNullOrEmpty(d.Value)) continue;
                incoming.Add((d.Category, d.Value));
            }
        }

        foreach (var shell in DescriptorShells)
        {
            string category = shell.TrackedShell?.Category ?? string.Empty;
            foreach (var selector in shell.DescriptorSelectors)
            {
                bool isCurrentlyClassifier = selector.IsSelected && selector.AnnotationState == BodyShapeAnnotationState.Classifier;
                bool shouldBeClassifier = incoming.Contains((category, selector.Value));

                if (shouldBeClassifier)
                {
                    // Don't trample a higher-priority source. The evaluator's MergeIntoSlot
                    // already filters Manual/Library/RulesBased, but the menu may still hold a
                    // non-Classifier selection that wasn't in the model yet -- leave it alone.
                    if (selector.IsSelected && selector.AnnotationState != BodyShapeAnnotationState.Classifier
                        && selector.AnnotationState != BodyShapeAnnotationState.None)
                    {
                        continue;
                    }
                    // Order matters: setting IsSelected fires the selector's reactive subscription
                    // that resets AnnotationState to Manual, so set state second.
                    if (!selector.IsSelected) selector.IsSelected = true;
                    selector.AnnotationState = BodyShapeAnnotationState.Classifier;
                }
                else if (isCurrentlyClassifier)
                {
                    selector.IsSelected = false;
                    // The IsSelected setter already resets AnnotationState to None via its subscription.
                }
            }
        }

        BuildHeader();
    }
}

/// <summary>Selectable view of one descriptor category shell: its selectable descriptor rows plus an aggregate annotation state and text color.</summary>
[DebuggerDisplay("{TrackedShell.Category} ({TrackedShell.Descriptors.Count})")]
public class VM_BodyShapeDescriptorShellSelector : VM, IHasAnnotationState
{
    /// <summary>Creates the shell selector, building a selector per descriptor and recomputing the aggregate annotation state/color as selections change.</summary>
    /// <param name="trackedShell">The category shell this mirrors.</param>
    /// <param name="parentMenu">The owning selection menu.</param>
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
                    .Select(x => x.WhenAnyValue(x => x.IsSelected, x => x.Priority, x => x.AnnotationState))
                    .CombineLatest()
                    .Select(_ => Unit.Default);
            })
            .DisposeWith(this);

        this.WhenAnyObservable(x => x.NeedsRefresh).Subscribe(_ =>
        {
            AnnotationState = AnnotationStateComputer.ComputeAnnotationState(DescriptorSelectors.Cast<IHasAnnotationState>().ToList());
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.AnnotationState).Subscribe(x => UpdateTextColor(x)).DisposeWith(this);
    }
    public VM_BodyShapeDescriptorShell TrackedShell { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public ObservableCollection<VM_BodyShapeDescriptorSelector> DescriptorSelectors { get; set; } = new();
    public IObservable<Unit> NeedsRefresh { get; set; }
    public BodyShapeAnnotationState AnnotationState { get; set; } = BodyShapeAnnotationState.None;
    public SolidColorBrush TextColor { get; set; } = AnnotationColors.DefaultText;

    /// <summary>Syncs the descriptor selectors to the tracked shell's descriptors (adding new, removing deleted).</summary>
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

    /// <summary>Updates the text color from the annotation state (with explicit overrides for some states).</summary>
    private void UpdateTextColor(BodyShapeAnnotationState annotationState)
    {
        TextColor = VM_BodySlideSetting.AnnotationToColor[annotationState];
        // some states have explicit differences vs. BodySlideSettings and BodySlidePlaceHolders
        if (annotationState == BodyShapeAnnotationState.Manual)
        {
            TextColor = AnnotationColors.DefaultText;
        }
    }
}

/// <summary>One selectable descriptor value: its selected state, optional priority, annotation state, and text color, tracking the underlying descriptor's value.</summary>
[DebuggerDisplay("{Value} {IsSelected ? \"(x)\" : \"(_)\";} Priority: {Priority}")]
public class VM_BodyShapeDescriptorSelector : VM, IHasAnnotationState
{
    /// <summary>Creates the selector, tracking the descriptor's value and updating annotation state/color on selection changes.</summary>
    /// <param name="trackedDescriptor">The descriptor this selector represents.</param>
    /// <param name="parentMenu">The owning selection menu.</param>
    public VM_BodyShapeDescriptorSelector(VM_BodyShapeDescriptor trackedDescriptor, VM_BodyShapeDescriptorSelectionMenu parentMenu)
    {
        TrackedDescriptor = trackedDescriptor;
        ParentMenu = parentMenu;
        Value = TrackedDescriptor.Value;

        TrackedDescriptor.WhenAnyValue(x => x.Value).Subscribe(_ => Value = TrackedDescriptor.Value).DisposeWith(this);
        this.WhenAnyValue(x => x.IsSelected).Subscribe(_ => AnnotationState = IsSelected ? BodyShapeAnnotationState.Manual : BodyShapeAnnotationState.None).DisposeWith(this);
        this.WhenAnyValue(x => x.AnnotationState).Subscribe(x => UpdateTextColor(x)).DisposeWith(this);
    }

    public VM_BodyShapeDescriptor TrackedDescriptor { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu ParentMenu { get; set; }
    public string Value { get; set; }
    public bool IsSelected { get; set; } = false;
    public int Priority { get; set; } = 0;
    public SolidColorBrush TextColor { get; set; } = AnnotationColors.DefaultText;
    public BodyShapeAnnotationState AnnotationState { get; set; } = BodyShapeAnnotationState.None;

    /// <summary>Updates the text color from the annotation state (with explicit overrides for some states).</summary>
    private void UpdateTextColor(BodyShapeAnnotationState annotationState)
    {
        TextColor = VM_BodySlideSetting.AnnotationToColor[annotationState];
        // some states have explicit differences vs. BodySlideSettings and BodySlidePlaceHolders
        if (annotationState == BodyShapeAnnotationState.None || annotationState == BodyShapeAnnotationState.Manual)
        {
            TextColor = AnnotationColors.DefaultText;
        }
    }
}

/// <summary>Aggregate annotation state of a descriptor selection, used to color the UI.</summary>
public enum BodyShapeAnnotationState
{
    /// <summary>Not annotated.</summary>
    None,
    /// <summary>Manually selected by the user.</summary>
    Manual,
    /// <summary>Applied by a rules-based pass.</summary>
    RulesBased,
    /// <summary>A mix of manual and rules-based selections.</summary>
    Mix_Manual_RulesBased,
    /// <summary>Imported from an annotation library.</summary>
    Library,
    /// <summary>Assigned by the ML BodySlide classifier.</summary>
    Classifier,
    /// <summary>A mix of multiple sources.</summary>
    Mixed
}

/// <summary>The source that produced a descriptor annotation (also its precedence for conflict resolution).</summary>
public enum BodyShapeAnnotationSource
{
    /// <summary>User-entered.</summary>
    Manual,
    /// <summary>From an annotation library.</summary>
    Library,
    /// <summary>From a rules-based pass.</summary>
    RulesBased,
    /// <summary>From the ML classifier.</summary>
    Classifier
}
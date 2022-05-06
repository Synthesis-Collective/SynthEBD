using System.Collections.ObjectModel;
using System.ComponentModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using ReactiveUI;
using GongSolutions.Wpf.DragDrop;
using Noggog.WPF;

namespace SynthEBD;

public class VM_Subgroup : ViewModel, ICloneable, IDropTarget, IHasSubgroupViewModels
{
    public VM_Subgroup(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
    {
        SubscribedRaceGroupings = raceGroupingVMs;
        SubscribedOBodyDescriptorMenu = OBodyDescriptorMenu;
        SetExplicitReferenceNPC = setExplicitReferenceNPC;
        ParentAssetPack = parentAssetPack;

        this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        if (parentAssetPack.TrackedBodyGenConfig != null)
        {
            this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
            this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
        }
        AllowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(SubscribedOBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);
        DisallowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(SubscribedOBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);

        _linkCache = PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .ToGuiProperty(this, nameof(LinkCache), default(ILinkCache))
            .DisposeWith(this);
        
        //UI-related
        this.ParentCollection = parentCollection;

        // must be set after Parent Asset Pack
        if (SetExplicitReferenceNPC)
        {
            this.PathsMenu = new VM_FilePathReplacementMenu(this, SetExplicitReferenceNPC, this.LinkCache);
        }
        else
        {
            this.PathsMenu = new VM_FilePathReplacementMenu(this, SetExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
            parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => this.PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache);
        }

        this.PathsMenu.WhenAnyValue(x => x.Paths).Subscribe(x => GetDDSPaths(this, this.ImagePaths));

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.AddKeywords.Add(new VM_CollectionMemberString("", this.AddKeywords))
        );

        AddPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.PathsMenu.Paths.Add(new VM_FilePathReplacement(this.PathsMenu))
        );

        AddSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Subgroups.Add(new VM_Subgroup(raceGroupingVMs, this.Subgroups, this.ParentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC))
        );

        DeleteMe = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.ParentCollection.Remove(this)
        );

        DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute:  x => { this.RequiredSubgroups.Remove((VM_Subgroup)x); RefreshListBoxLabel(this.RequiredSubgroups, SubgroupListBox.Required); }
        );

        DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => { this.ExcludedSubgroups.Remove((VM_Subgroup)x); RefreshListBoxLabel(this.ExcludedSubgroups, SubgroupListBox.Excluded); }
        );
    }

    public string ID { get; set; } = "";
    public string Name { get; set; } = "New";
    public bool Enabled { get; set; } = true;
    public bool DistributionEnabled { get; set; } = true;
    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public ObservableCollection<VM_Subgroup> RequiredSubgroups { get; set; } = new();
    public ObservableCollection<VM_Subgroup> ExcludedSubgroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;
    public VM_FilePathReplacementMenu PathsMenu { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    public NPCWeightRange WeightRange { get; set; } = new();
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();

    //UI-related
    private readonly ObservableAsPropertyHelper<ILinkCache> _linkCache;
    public ILinkCache LinkCache => _linkCache.Value;
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public string TopLevelSubgroupID { get; set; }

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public RelayCommand AddPath { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand DeleteRequiredSubgroup { get; }
    public RelayCommand DeleteExcludedSubgroup { get; }
    public bool SetExplicitReferenceNPC { get; set; }
    public HashSet<string> RequiredSubgroupIDs { get; set; } = new(); // temporary placeholder for RequiredSubgroups until all subgroups are loaded in
    public HashSet<string> ExcludedSubgroupIDs { get; set; } = new(); // temporary placeholder for ExcludedSubgroups until all subgroups are loaded in
    public string RequiredSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public string ExcludedSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";

    public ObservableCollection<VM_Subgroup> ParentCollection { get; set; }
    public VM_AssetPack ParentAssetPack { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }
    public VM_BodyShapeDescriptorCreationMenu SubscribedOBodyDescriptorMenu { get; set; }
    public ObservableCollection<Graphics.ImagePathWithSource> ImagePaths { get; set; } = new();

    public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
    {
        var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings, parentCollection, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC);

        viewModel.ID = model.ID;
        viewModel.Name = model.Name;
        viewModel.Enabled = model.Enabled;
        viewModel.DistributionEnabled = model.DistributionEnabled;
        viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        viewModel.AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, generalSettingsVM.RaceGroupings);
        viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        viewModel.DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, generalSettingsVM.RaceGroupings);
        viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true, null);
        viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false, null);
        foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
        viewModel.AllowUnique = model.AllowUnique;
        viewModel.AllowNonUnique = model.AllowNonUnique;
        viewModel.RequiredSubgroupIDs = model.RequiredSubgroups;
        viewModel.RequiredSubgroups = new ObservableCollection<VM_Subgroup>();
        viewModel.ExcludedSubgroupIDs = model.ExcludedSubgroups;
        viewModel.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>();
        viewModel.AddKeywords = VM_CollectionMemberString.InitializeCollectionFromHashSet(model.AddKeywords);
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.PathsMenu = VM_FilePathReplacementMenu.GetViewModelFromModels(model.Paths, viewModel, setExplicitReferenceNPC);
        viewModel.WeightRange = model.WeightRange;

        if (parentAssetPack.TrackedBodyGenConfig != null)
        {
            viewModel.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
            viewModel.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
        }

        viewModel.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodySlideDescriptors, OBodyDescriptorMenu, viewModel.SubscribedRaceGroupings, parentAssetPack);
        viewModel.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodySlideDescriptors, OBodyDescriptorMenu, viewModel.SubscribedRaceGroupings, parentAssetPack);

        foreach (var sg in model.Subgroups)
        {
            viewModel.Subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM, viewModel.Subgroups, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC));
        }

        //dds preview
        GetDDSPaths(viewModel, viewModel.ImagePaths);

        return viewModel;
    }

    private void RefreshListBoxLabel(ObservableCollection<VM_Subgroup> listSource, SubgroupListBox whichBox)
    {
        string label = "";
        if (listSource.Any())
        {
            label = "";
        }
        else
        {
            label = "Drag subgroups here from the tree view";
        }

        switch(whichBox)
        {
            case SubgroupListBox.Required: RequiredSubgroupsLabel = label; break;
            case SubgroupListBox.Excluded: ExcludedSubgroupsLabel = label; break;
        }
    }
    private enum SubgroupListBox
    {
        Required,
        Excluded
    }
    public static void GetDDSPaths(VM_Subgroup viewModel, ObservableCollection<Graphics.ImagePathWithSource> paths)
    {
        var ddsPaths = viewModel.PathsMenu.Paths.Where(x => x.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, x.Source)))
            .Select(x => x.Source)
            .Select(x => System.IO.Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, x))
            .ToHashSet();
        var source = Graphics.ImagePathWithSource.GetSource(viewModel);
        foreach (var path in ddsPaths)
        {
            var imagePathWithSource = new Graphics.ImagePathWithSource(path, source);
            if (!paths.Contains(imagePathWithSource))
            {
                paths.Add(imagePathWithSource);
            }
        }
        //paths.UnionWith(ddsPaths.Select(x => System.IO.Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, x)));
        foreach (var subgroup in viewModel.Subgroups)
        {
            GetDDSPaths(subgroup, paths);
        }
    }

    public void CallRefreshTrackedBodyShapeDescriptorsC(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshTrackedBodyShapeDescriptors();
    }

    public void CallRefreshTrackedBodyShapeDescriptorsP(object sender, PropertyChangedEventArgs e)
    {
        RefreshTrackedBodyShapeDescriptors();
    }

    public void RefreshTrackedBodyShapeDescriptors()
    {
        if (this.ParentAssetPack.TrackedBodyGenConfig != null)
        {
            this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack);
            this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack);
        }
    }

    public static AssetPack.Subgroup DumpViewModelToModel(VM_Subgroup viewModel)
    {
        var model = new AssetPack.Subgroup();

        model.ID = viewModel.ID;
        model.Name = viewModel.Name;
        model.Enabled = viewModel.Enabled;
        model.DistributionEnabled = viewModel.DistributionEnabled;
        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.AllowUnique;
        model.AllowNonUnique = viewModel.AllowNonUnique;
        model.RequiredSubgroups = viewModel.RequiredSubgroups.Select(x => x.ID).ToHashSet();
        model.ExcludedSubgroups = viewModel.ExcludedSubgroups.Select(x => x.ID).ToHashSet();
        model.AddKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.Paths = VM_FilePathReplacementMenu.DumpViewModelToModels(viewModel.PathsMenu);
        model.WeightRange = viewModel.WeightRange;

        model.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodyGenDescriptors);
        model.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodyGenDescriptors);
        model.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodySlideDescriptors);
        model.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodySlideDescriptors);

        foreach (var sg in viewModel.Subgroups)
        {
            model.Subgroups.Add(DumpViewModelToModel(sg));
        }

        return model;
    }

    public object Clone()
    {
        return this.Clone(this.ParentCollection);
    }

    public object Clone(ObservableCollection<VM_Subgroup> parentCollection)
    {
        var clone = new VM_Subgroup(this.SubscribedRaceGroupings, parentCollection, this.ParentAssetPack, this.SubscribedOBodyDescriptorMenu, this.SetExplicitReferenceNPC);
        clone.AddKeywords = new ObservableCollection<VM_CollectionMemberString>(this.AddKeywords);
        clone.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>(this.AllowedAttributes);
        clone.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>(this.DisallowedAttributes);
        if (this.AllowedBodyGenDescriptors is not null)
        {
            clone.AllowedBodyGenDescriptors = this.AllowedBodyGenDescriptors.Clone();
        }
        if (this.DisallowedBodyGenDescriptors is not null)
        {
            clone.DisallowedBodyGenDescriptors = this.DisallowedBodyGenDescriptors.Clone();
        }
        clone.AllowedBodySlideDescriptors = this.AllowedBodySlideDescriptors.Clone();
        clone.DisallowedBodySlideDescriptors = this.DisallowedBodySlideDescriptors.Clone();
        clone.AllowedRaceGroupings = this.AllowedRaceGroupings.Clone();
        clone.DisallowedRaceGroupings = this.DisallowedRaceGroupings.Clone();
        clone.AllowedRaces = new ObservableCollection<FormKey>(this.AllowedRaces);
        clone.DisallowedRaces = new ObservableCollection<FormKey>(this.DisallowedRaces);
        clone.AllowUnique = this.AllowUnique;
        clone.AllowNonUnique = this.AllowNonUnique;
        clone.DistributionEnabled = this.DistributionEnabled;
        clone.ID = this.ID;
        clone.Name = this.Name;
        clone.RequiredSubgroupIDs = new HashSet<string>(RequiredSubgroupIDs);
        clone.ExcludedSubgroupIDs = new HashSet<string>(ExcludedSubgroupIDs);
        clone.RequiredSubgroups = new ObservableCollection<VM_Subgroup>(this.RequiredSubgroups);
        clone.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>(this.ExcludedSubgroups);
        clone.WeightRange = new NPCWeightRange { Lower = this.WeightRange.Lower, Upper = this.WeightRange.Upper };
        clone.ProbabilityWeighting = this.ProbabilityWeighting;
        clone.PathsMenu = this.PathsMenu.Clone();
        GetDDSPaths(clone, clone.ImagePaths);

        clone.Subgroups.Clear();
        foreach (var subgroup in this.Subgroups)
        {
            clone.Subgroups.Add(subgroup.Clone(clone.Subgroups) as VM_Subgroup);
        }

        return clone;
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup && dropInfo.VisualTarget is ListBox)
        {
            var listBox = (ListBox)dropInfo.VisualTarget;
            var parentContextSubgroup = listBox.DataContext as VM_Subgroup;
            if (parentContextSubgroup != null)
            {
                var draggedSubgroup = (VM_Subgroup)dropInfo.Data;
                if (listBox.Name == "lbRequiredSubgroups")
                {
                    parentContextSubgroup.RequiredSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.RequiredSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.RequiredSubgroups, SubgroupListBox.Required);
                }
                else if (listBox.Name == "lbExcludedSubgroups")
                {
                    parentContextSubgroup.ExcludedSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.ExcludedSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.ExcludedSubgroups, SubgroupListBox.Excluded);
                }
            }
        }
    }
}
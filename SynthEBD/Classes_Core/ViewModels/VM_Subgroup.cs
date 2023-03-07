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
using DynamicData.Binding;
using System.Text.RegularExpressions;
using static SynthEBD.VM_NPCAttribute;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_Subgroup : VM, IDropTarget, IHasSubgroupViewModels
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Factory _selfFactory;
    private readonly VM_SubgroupPlaceHolder.Factory _placeHolderFactory;
    private readonly VM_FilePathReplacementMenu.Factory _filePathReplacementMenuFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly UpdateHandler _updateHandler;

    public delegate VM_Subgroup Factory(
        ObservableCollection<VM_RaceGrouping> raceGroupingVMs,
        VM_AssetPack parentAssetPack,
        VM_Subgroup parentSubgroup,
        bool setExplicitReferenceNPC);
    
    public VM_Subgroup(IEnvironmentStateProvider environmentProvider,
        Logger logger,
        SynthEBDPaths paths,
        ObservableCollection<VM_RaceGrouping> raceGroupingVMs,
        VM_AssetPack parentAssetPack, 
        VM_Subgroup parentSubgroup,
        bool setExplicitReferenceNPC,
        VM_SettingsOBody oBody,
        VM_NPCAttributeCreator attributeCreator,
        Factory selfFactory,
        VM_SubgroupPlaceHolder.Factory placeHolderFactory,
        VM_FilePathReplacementMenu.Factory filePathReplacementMenuFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory,
        UpdateHandler updateHandler)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
        _oBody = oBody;
        _attributeCreator = attributeCreator;
        _selfFactory = selfFactory;
        _placeHolderFactory = placeHolderFactory;
        _filePathReplacementMenuFactory = filePathReplacementMenuFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _updateHandler = updateHandler;

        SetExplicitReferenceNPC = setExplicitReferenceNPC;
        ParentAssetPack = parentAssetPack;
        SubscribedRaceGroupings = ParentAssetPack.RaceGroupingEditor.RaceGroupings;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);

        //RefreshBodyGenDescriptors();
        this.WhenAnyValue(x => x.ParentAssetPack.TrackedBodyGenConfig).Subscribe(_ => RefreshBodyGenDescriptors()).DisposeWith(this);
        AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.All);
        DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.Any);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);
        
        //UI-related
        RequiredSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required)).DisposeWith(this);
        ExcludedSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded)).DisposeWith(this);

        // must be set after Parent Asset Pack
        if (SetExplicitReferenceNPC)
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, LinkCache);
        }
        else
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
            parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache).DisposeWith(this);
        }

        Observable.CombineLatest(
                PathsMenu.Paths.ToObservableChangeSet(),
                this.WhenAnyValue(x => x.ParentAssetPack),
                this.WhenAnyValue(x => x.ParentSubgroup),
                (_, _, _) => { return 0; })
            .Subscribe(_ => {
                GetDDSPaths(ImagePaths);
            })
            .DisposeWith(this);

        AutoGenerateIDcommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedPlaceHolder?.AutoGenerateID(false, 0)
        );

        AutoGenerateID_Children_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedPlaceHolder?.AutoGenerateID(true, 1)
        );

        AutoGenerateID_All_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentAssetPack.AutoGenerateSubgroupIDs()
        );

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AddKeywords.Add(new VM_CollectionMemberString("", AddKeywords))
        );

        AddPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => PathsMenu.Paths.Add(filePathReplacementFactory(PathsMenu))
        );

        DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute:  x => { RequiredSubgroups.Remove((VM_SubgroupPlaceHolder)x); RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required); }
        );

        DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => { ExcludedSubgroups.Remove((VM_SubgroupPlaceHolder)x); RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded); }
        );
    }

    public VM_SubgroupPlaceHolder AssociatedPlaceHolder { get; set; }
    public string ID { get; set; } = "";
    public string Name { get; set; } = "New";
    public bool Enabled { get; set; } = true;
    public bool DistributionEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public ObservableCollection<VM_SubgroupPlaceHolder> RequiredSubgroups { get; set; } = new();
    public ObservableCollection<VM_SubgroupPlaceHolder> ExcludedSubgroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;
    public VM_FilePathReplacementMenu PathsMenu { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    public NPCWeightRange WeightRange { get; set; } = new();
    public ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; set; } = new();

    //UI-related
    public ILinkCache LinkCache { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public string TopLevelSubgroupID { get; set; }
    public RelayCommand AutoGenerateIDcommand { get; }
    public RelayCommand AutoGenerateID_Children_Command { get; }
    public RelayCommand AutoGenerateID_All_Command { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public RelayCommand AddPath { get; }
    public RelayCommand DeleteRequiredSubgroup { get; }
    public RelayCommand DeleteExcludedSubgroup { get; }
    public bool SetExplicitReferenceNPC { get; set; }
    public HashSet<string> RequiredSubgroupIDs { get; set; } = new(); // temporary placeholder for RequiredSubgroups until all subgroups are loaded in
    public HashSet<string> ExcludedSubgroupIDs { get; set; } = new(); // temporary placeholder for ExcludedSubgroups until all subgroups are loaded in
    public string RequiredSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public string ExcludedSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public VM_AssetPack ParentAssetPack { get; set; }
    public VM_SubgroupPlaceHolder ParentSubgroup { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }
    public ObservableCollection<ImagePreviewHandler.ImagePathWithSource> ImagePaths { get; set; } = new();

    public void CopyInViewModelFromModel(VM_SubgroupPlaceHolder subgroupPlaceholder)
    {
        if (subgroupPlaceholder == null)
        {
            _logger.LogMessage("Error: Can't read in subgroup VM from empty placeholder");
            return;
        }
        var model = subgroupPlaceholder.AssociatedModel;
        _logger.LogStartupEventStart("Copying in model for subgroup " + model.ID);
        AssociatedPlaceHolder = subgroupPlaceholder;
        ID = model.ID;
        Name = model.Name;
        Enabled = model.Enabled;
        DistributionEnabled = model.DistributionEnabled;
        Notes = model.Notes;
        AllowedRaces.AddRange(model.AllowedRaces);
        DisallowedRaces.AddRange(model.DisallowedRaces);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        AllowUnique = model.AllowUnique;
        AllowNonUnique = model.AllowNonUnique;
        
        RequiredSubgroupIDs = model.RequiredSubgroups;
        foreach (var reqID in model.RequiredSubgroups)
        {
            if (ParentAssetPack.TryGetSubgroupByID(reqID, out var reqSubgroup))
            {
                RequiredSubgroups.Add(reqSubgroup);
            }
        }

        ExcludedSubgroupIDs = model.ExcludedSubgroups;
        foreach (var exID in model.ExcludedSubgroups)
        {
            if (ParentAssetPack.TryGetSubgroupByID(exID, out var exSubgroup))
            {
                RequiredSubgroups.Add(exSubgroup);
            }
        }

        VM_CollectionMemberString.CopyInObservableCollectionFromICollection(model.AddKeywords, AddKeywords);
        ProbabilityWeighting = model.ProbabilityWeighting;
        _logger.LogStartupEventStart("Copying in pathsmenu for subgroup " + model.ID);
        PathsMenu.CopyInFromModels(model.Paths, _filePathReplacementFactory);
        _logger.LogStartupEventEnd("Copying in pathsmenu for subgroup " + model.ID);
        WeightRange = model.WeightRange;

        _logger.LogStartupEventStart("Copying in attributes for subgroup " + model.ID);
        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, false, null);
        _logger.LogStartupEventEnd("Copying in attributes for subgroup " + model.ID);
        _logger.LogStartupEventStart("Copying in race groupings for subgroup " + model.ID);
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        _logger.LogStartupEventEnd("Copying in race groupings for subgroup " + model.ID);
        _logger.LogStartupEventStart("Copying in bodyGen for subgroup " + model.ID);
        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors.CopyInFromHashSet(model.AllowedBodyGenDescriptors);
            DisallowedBodyGenDescriptors.CopyInFromHashSet(model.DisallowedBodyGenDescriptors);
        }
        _logger.LogStartupEventEnd("Copying in bodyGen for subgroup " + model.ID);
        _logger.LogStartupEventStart("Copying in bodySlide for subgroup " + model.ID);
        AllowedBodySlideDescriptors.CopyInFromHashSet(model.AllowedBodySlideDescriptors);
        DisallowedBodySlideDescriptors.CopyInFromHashSet(model.DisallowedBodySlideDescriptors);
        _logger.LogStartupEventEnd("Copying in bodySlide for subgroup " + model.ID);

        //dds preview
        _logger.LogStartupEventStart("Copying in DDS Paths for subgroup " + model.ID);
        GetDDSPaths(ImagePaths);
        _logger.LogStartupEventEnd("Copying in DDS Paths for subgroup " + model.ID);
        _logger.LogStartupEventEnd("Copying in model for subgroup " + model.ID);
    }
    public void RefreshListBoxLabel(ObservableCollection<VM_SubgroupPlaceHolder> listSource, SubgroupListBox whichBox)
    {
        _logger.LogStartupEventStart("Refreshing ListBox Label for subgroup " + ID);
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
        _logger.LogStartupEventEnd("Refreshing ListBox Label for subgroup " + ID);
    }
    public enum SubgroupListBox
    {
        Required,
        Excluded
    }
    public void GetDDSPaths(ObservableCollection<ImagePreviewHandler.ImagePathWithSource> paths)
    {
        if (AssociatedPlaceHolder == null) { return; }
        _logger.LogStartupEventStart("Getting DDS Paths for subgroup " + ID);
        var ddsPaths = AssociatedPlaceHolder.AssociatedModel.Paths.Where(x => x.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(_environmentProvider.DataFolderPath, x.Source)))
            .Select(x => x.Source)
            .Select(x => System.IO.Path.Combine(_environmentProvider.DataFolderPath, x))
            .ToHashSet() ?? new HashSet<string>();
        foreach (var path in ddsPaths)
        {
            var imagePathWithSource = new ImagePreviewHandler.ImagePathWithSource(path, AssociatedPlaceHolder);
            if (!paths.Contains(imagePathWithSource))
            {
                paths.Add(imagePathWithSource);
            }
        }

        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDDSPaths(paths);
        }
        _logger.LogStartupEventEnd("Getting DDS Paths for subgroup " + ID);
    }

    public AssetPack.Subgroup DumpViewModelToModel()
    {
        var model = new AssetPack.Subgroup();

        model.ID = ID;
        model.Name = Name;
        model.Enabled = Enabled;
        model.DistributionEnabled = DistributionEnabled;
        model.Notes = Notes;
        model.AllowedRaces = AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
        model.AllowUnique = AllowUnique;
        model.AllowNonUnique = AllowNonUnique;
        model.RequiredSubgroups = RequiredSubgroups.Select(x => x.ID).ToHashSet();
        model.ExcludedSubgroups = ExcludedSubgroups.Select(x => x.ID).ToHashSet();
        model.AddKeywords = AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = ProbabilityWeighting;
        model.Paths = VM_FilePathReplacementMenu.DumpViewModelToModels(PathsMenu);
        model.WeightRange = WeightRange;

        model.AllowedBodyGenDescriptors = AllowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.AllowedBodyGenMatchMode = AllowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.All;
        model.DisallowedBodyGenDescriptors = DisallowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.DisallowedBodyGenMatchMode = DisallowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.Any;
        model.AllowedBodySlideDescriptors = AllowedBodySlideDescriptors.DumpToHashSet();
        model.AllowedBodySlideMatchMode = AllowedBodySlideDescriptors.MatchMode;
        model.DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors.DumpToHashSet();
        model.DisallowedBodySlideMatchMode = DisallowedBodySlideDescriptors.MatchMode;

        /*
        foreach (var sg in Subgroups)
        {
            model.Subgroups.Add(sg.DumpViewModelToModel());
        }
        */

        return model;
    }

    /*
    public object Clone(ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack newParentAssetPack)
    {
        var clone = _selfFactory(SubscribedRaceGroupings, parentCollection, newParentAssetPack, this, SetExplicitReferenceNPC);
        clone.AddKeywords = new ObservableCollection<VM_CollectionMemberString>(AddKeywords);
        clone.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        foreach (var at in AllowedAttributes) { clone.AllowedAttributes.Add(at.Clone(clone.AllowedAttributes)); }
        clone.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        foreach (var at in DisallowedAttributes) { clone.DisallowedAttributes.Add(at.Clone(clone.DisallowedAttributes)); }
        if (AllowedBodyGenDescriptors is not null)
        {
            clone.AllowedBodyGenDescriptors = AllowedBodyGenDescriptors.Clone();
        }
        if (DisallowedBodyGenDescriptors is not null)
        {
            clone.DisallowedBodyGenDescriptors = DisallowedBodyGenDescriptors.Clone();
        }
        clone.AllowedBodySlideDescriptors = AllowedBodySlideDescriptors.Clone();
        clone.DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors.Clone();
        clone.AllowedRaceGroupings = AllowedRaceGroupings.Clone();
        clone.DisallowedRaceGroupings = DisallowedRaceGroupings.Clone();
        clone.AllowedRaces.AddRange(AllowedRaces);
        clone.DisallowedRaces.AddRange(DisallowedRaces);
        clone.AllowUnique = AllowUnique;
        clone.AllowNonUnique = AllowNonUnique;
        clone.DistributionEnabled = DistributionEnabled;
        clone.ID = ID;
        clone.Name = Name;
        clone.Notes = Notes;
        clone.RequiredSubgroupIDs = new HashSet<string>(RequiredSubgroupIDs);
        clone.ExcludedSubgroupIDs = new HashSet<string>(ExcludedSubgroupIDs);
        clone.RequiredSubgroups.AddRange(RequiredSubgroups);
        clone.ExcludedSubgroups.AddRange(ExcludedSubgroups);
        clone.WeightRange = WeightRange.Clone();
        clone.ProbabilityWeighting = ProbabilityWeighting;
        clone.PathsMenu = PathsMenu.Clone();
        clone.GetDDSPaths(clone.ImagePaths);

        clone.Subgroups.Clear();
        foreach (var subgroup in Subgroups)
        {
            clone.Subgroups.Add(subgroup.Clone(clone.Subgroups, newParentAssetPack) as VM_Subgroup);
        }

        return clone;
    }
    */

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_SubgroupPlaceHolder)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_SubgroupPlaceHolder && dropInfo.VisualTarget is ListBox)
        {
            var listBox = (ListBox)dropInfo.VisualTarget;
            var parentContextSubgroup = listBox.DataContext as VM_Subgroup;
            if (parentContextSubgroup != null)
            {
                var draggedSubgroup = (VM_SubgroupPlaceHolder)dropInfo.Data;
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

    public void RefreshBodyGenDescriptors()
    {
        _logger.LogStartupEventStart("Refreshing BodyGen Descriptors for subgroup " + ID);
        DescriptorMatchMode allowedMode = DescriptorMatchMode.All;
        DescriptorMatchMode disallowedMode = DescriptorMatchMode.Any;

        // keep existing settings if any
        if (AllowedBodyGenDescriptors != null)
        {
            allowedMode = AllowedBodyGenDescriptors.MatchMode;
        }
        if (DisallowedBodyGenDescriptors != null)
        {
            disallowedMode = DisallowedBodyGenDescriptors.MatchMode;
        }

        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, allowedMode);
            DisallowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, disallowedMode);
        }
        _logger.LogStartupEventEnd("Refreshing BodyGen Descriptors for subgroup " + ID);
    }
}
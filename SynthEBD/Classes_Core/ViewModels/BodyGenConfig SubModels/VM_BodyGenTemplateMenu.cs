using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ReactiveUI;
using System.Windows.Media;
using DynamicData.Binding;
using static SynthEBD.VM_NPCAttribute;
using System.Reactive.Linq;
using System.Diagnostics;

namespace SynthEBD;

public class VM_BodyGenTemplateMenu : VM
{
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_BodyGenTemplate.Factory _bodyGenTemplateFactory;
    public delegate VM_BodyGenTemplateMenu Factory(VM_BodyGenConfig parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);

    public VM_BodyGenTemplateMenu(VM_BodyGenConfig parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, SettingsIO_BodyGen bodyGenIO, VM_NPCAttributeCreator attributeCreator, VM_BodyGenTemplate.Factory bodyGenTemplateFactory)
    {
        _bodyGenIO = bodyGenIO;
        _attributeCreator = attributeCreator;
        _bodyGenTemplateFactory = bodyGenTemplateFactory;

        AddTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var placeHolder = new VM_BodyGenTemplatePlaceHolder(new BodyGenConfig.BodyGenTemplate(), Templates);
                Templates.Add(placeHolder);
                SelectedPlaceHolder = placeHolder;
            });

        RemoveTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: x => Templates.Remove((VM_BodyGenTemplatePlaceHolder)x)
        );

        ImportBodyGen = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (IO_Aux.SelectFile("", "INI files (*.ini)|*.ini", "Select the Templates.ini file", out string templatePath))
                {
                    if (System.IO.Path.GetFileName(templatePath).Equals("morphs.ini", StringComparison.OrdinalIgnoreCase) && !MessageWindow.DisplayNotificationYesNo("Confirm File Name", "Expecting templates.ini but this file is morphs.ini, which should be imported in the Specific NPC Assignments Menu. Are you sure you want to continue?"))
                    {
                        return;
                    }

                    var newTemplates = _bodyGenIO.LoadTemplatesINI(templatePath);
                    foreach (var template in newTemplates.Where(x => !Templates.Select(x => x.Label).Contains(x.Label)).ToArray())
                    {
                        /*
                        var templateVM = _bodyGenTemplateFactory(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, Templates, parentConfig);
                        templateVM.CopyInViewModelFromModel(template, parentConfig.DescriptorUI, raceGroupingVMs);
                        Templates.Add(templateVM);*/
                        var placeHolder = new VM_BodyGenTemplatePlaceHolder(template, Templates);
                        Templates.Add(placeHolder);
                    }
                }
            }
        );

        Alphabetizer = new(Templates, x => x.Label, new(Colors.MediumPurple));

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedViewModel.DumpViewModelToModel();
             }

             if (t.Current != null)
             {
                 CurrentlyDisplayedTemplate = _bodyGenTemplateFactory(t.Current, parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, parentConfig);
                 CurrentlyDisplayedTemplate.CopyInViewModelFromModel(parentConfig.DescriptorUI, raceGroupingVMs);
             }
         });
    }
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> Templates { get; set; } = new();
    public VM_BodyGenTemplatePlaceHolder SelectedPlaceHolder { get; set; }
    public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }

    public VM_Alphabetizer<VM_BodyGenTemplatePlaceHolder, string> Alphabetizer { get; set; }

    public RelayCommand AddTemplate { get; }
    public RelayCommand RemoveTemplate { get; }
    public RelayCommand ImportBodyGen { get; }

    private VM_BodyGenTemplatePlaceHolder _stashedPlaceHolder;

    public void StashAndNullDisplayedMorph()
    {
        if (SelectedPlaceHolder != null)
        {
            _stashedPlaceHolder = SelectedPlaceHolder;
            SelectedPlaceHolder = null;
        }
    }

    public void RestoreStashedMorph()
    {
        if (_stashedPlaceHolder != null)
        {
            SelectedPlaceHolder = _stashedPlaceHolder;
        }
    }
}

[DebuggerDisplay("{Label}")]
public class VM_BodyGenTemplatePlaceHolder : VM
{
    public VM_BodyGenTemplatePlaceHolder(BodyGenConfig.BodyGenTemplate model, ObservableCollection<VM_BodyGenTemplatePlaceHolder> parentCollection)
    {
        AssociatedModel = model;
        Label = model.Label;
        ParentCollection = parentCollection;
        if (!AssociatedModel.MemberOfTemplateGroups.Any())
        {
            BorderColor = CommonColors.Red;
        }
        else if (!AssociatedModel.BodyShapeDescriptors.Any())
        {
            BorderColor = CommonColors.Yellow;
        }
        else
        {
            BorderColor = CommonColors.Green;
        }

        this.WhenAnyValue(x => x.AssociatedViewModel.Label).Subscribe(y => Label = y).DisposeWith(this);
        this.WhenAnyValue(x => x.AssociatedViewModel.BorderColor).Subscribe(y => BorderColor = y).DisposeWith(this);
    }
    
    public string Label { get; set; }
    public SolidColorBrush BorderColor { get; set; }
    public BodyGenConfig.BodyGenTemplate AssociatedModel { get; set; }
    public VM_BodyGenTemplate? AssociatedViewModel { get; set; }
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> ParentCollection { get; set; }

}

[DebuggerDisplay("{Label}")]
public class VM_BodyGenTemplate : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    public delegate VM_BodyGenTemplate Factory(VM_BodyGenTemplatePlaceHolder associatedPlaceHolder, ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenConfig parentConfig);
    public VM_BodyGenTemplate(VM_BodyGenTemplatePlaceHolder associatedPlaceHolder, ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenConfig parentConfig, IEnvironmentStateProvider environmentProvider, VM_NPCAttributeCreator attributeCreator, Logger logger, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _logger = logger;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        SubscribedTemplateGroups = templateGroups;
        GroupSelectionCheckList = new VM_CollectionMemberStringCheckboxList(SubscribedTemplateGroups);
        DescriptorsSelectionMenu = descriptorSelectionFactory(BodyShapeDescriptors, raceGroupingVMs, parentConfig, false, DescriptorMatchMode.Any, false);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentConfig = parentConfig;
        SubscribedTemplateGroups.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection()).DisposeWith(this);
        GroupSelectionCheckList.CollectionMemberStrings.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection()).DisposeWith(this);

        this.WhenAnyValue(x => x.DescriptorsSelectionMenu.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);
        this.WhenAnyValue(x => x.GroupSelectionCheckList.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        UpdateStatusDisplay();

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddRequiredTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RequiredTemplates.Add(new VM_CollectionMemberString("", this.RequiredTemplates))
        );

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedPlaceHolder.ParentCollection.Remove(AssociatedPlaceHolder)
        );
    }

    public VM_BodyGenTemplatePlaceHolder AssociatedPlaceHolder { get; }
    public string Label { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Specs { get; set; } = ""; // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
    public VM_CollectionMemberStringCheckboxList GroupSelectionCheckList { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DescriptorsSelectionMenu { get; set; }
    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool bAllowUnique { get; set; } = true;
    public bool bAllowNonUnique { get; set; } = true;
    public bool bAllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public ObservableCollection<VM_CollectionMemberString> RequiredTemplates { get; set; } = new();
    public NPCWeightRange WeightRange { get; set; } = new();
    public string Caption_MemberOfTemplateGroups { get; set; } = "";
    public string Caption_BodyShapeDescriptors { get; set; } = "";

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddRequiredTemplate { get; }
    public RelayCommand DeleteMe { get; }

    public VM_BodyGenConfig ParentConfig { get; set; }
    public ObservableCollection<VM_CollectionMemberString> SubscribedTemplateGroups { get; set;}
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> OtherGroupsTemplateCollection { get; set; } = new();
    public SolidColorBrush BorderColor { get; set; }
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public void CopyInViewModelFromModel(VM_BodyShapeDescriptorCreationMenu descriptorMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        var model = AssociatedPlaceHolder.AssociatedModel;
        Label = model.Label;
        Notes = model.Notes;
        Specs = model.Specs;
        GroupSelectionCheckList.InitializeFromHashSet(model.MemberOfTemplateGroups);
        DescriptorsSelectionMenu.CopyInFromHashSet(model.BodyShapeDescriptors);
        AllowedRaces.AddRange(model.AllowedRaces);
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces.AddRange(model.DisallowedRaces);
        DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            
        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentConfig.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentConfig.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        VM_CollectionMemberString.CopyInObservableCollectionFromICollection(model.RequiredTemplates, RequiredTemplates);
        WeightRange = model.WeightRange.Clone();

        UpdateStatusDisplay();
    }

    public void DumpViewModelToModel()
    {
        var model = AssociatedPlaceHolder.AssociatedModel;
        model.Label = Label;
        model.Notes = Notes;
        model.Specs = Specs;
        model.MemberOfTemplateGroups = GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToHashSet();
        model.BodyShapeDescriptors = DescriptorsSelectionMenu.DumpToHashSet();
        model.AllowedRaces = AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
        model.AllowUnique = bAllowUnique;
        model.AllowNonUnique = bAllowNonUnique;
        model.AllowRandom = bAllowRandom;
        model.ProbabilityWeighting = ProbabilityWeighting;
        model.RequiredTemplates = RequiredTemplates.Select(x => x.Content).ToHashSet();
        model.WeightRange = WeightRange.Clone();
    }

    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> UpdateThisOtherGroupsTemplateCollection()
    {
        if (ParentConfig.IsLoadingFromViewModel)
        {
            return new(); // skip this when the parent BodyGen Config view model is being loaded in because every added Template will trigger this evaluation. 
        }

        var sameGroups = ParentConfig.TemplateMorphUI.Templates.Where(x => x.AssociatedModel.MemberOfTemplateGroups.Intersect(AssociatedPlaceHolder.AssociatedModel.MemberOfTemplateGroups).Any()).ToArray();
        var otherGroups = ParentConfig.TemplateMorphUI.Templates.Where(x => !x.AssociatedModel.MemberOfTemplateGroups.Intersect(AssociatedPlaceHolder.AssociatedModel.MemberOfTemplateGroups).Any()).ToArray();

        OtherGroupsTemplateCollection = new(otherGroups);

        return new(sameGroups);
    }

    public void UpdateStatusDisplay()
    {
        var belongsToGroup = false;
        foreach (var group in GroupSelectionCheckList.CollectionMemberStrings)
        {
            if (group.IsSelected)
            {
                belongsToGroup = true;
                break;
            }
        }

        if (!belongsToGroup)
        {
            BorderColor = CommonColors.Red;
            StatusHeader = "Warning:";
            StatusText = "Morph does not belong to any Morph Groups. Will not be assigned.";
            ShowStatus = true;
        }
        else if (!DescriptorsSelectionMenu.IsAnnotated())
        {
            BorderColor = CommonColors.Yellow;
            StatusHeader = "Warning:";
            StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
            ShowStatus = true;
        }
        else
        {
            BorderColor = CommonColors.Green;
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;
        }
    }
}
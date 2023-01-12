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
            execute: _ => Templates.Add(_bodyGenTemplateFactory(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, Templates, parentConfig))
        );

        RemoveTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: x => Templates.Remove((VM_BodyGenTemplate)x)
        );

        ImportBodyGen = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (IO_Aux.SelectFile("", "INI files (*.ini)|*.ini", "Select the Templates.ini file", out string templatePath))
                {
                    if (System.IO.Path.GetFileName(templatePath).Equals("morphs.ini", StringComparison.OrdinalIgnoreCase) && !CustomMessageBox.DisplayNotificationYesNo("Confirm File Name", "Expecting templates.ini but this file is morphs.ini, which should be imported in the Specific NPC Assignments Menu. Are you sure you want to continue?"))
                    {
                        return;
                    }

                    var newTemplates = _bodyGenIO.LoadTemplatesINI(templatePath);
                    foreach (var template in newTemplates.Where(x => !Templates.Select(x => x.Label).Contains(x.Label)))
                    {
                        var templateVM = _bodyGenTemplateFactory(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, Templates, parentConfig);
                        templateVM.CopyInViewModelFromModel(template, parentConfig.DescriptorUI, raceGroupingVMs);
                        Templates.Add(templateVM);
                    }
                }
            }
        );

        Alphabetizer = new(Templates, x => x.Label, new(Colors.MediumPurple));
    }
    public ObservableCollection<VM_BodyGenTemplate> Templates { get; set; } = new();

    public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }

    public VM_Alphabetizer<VM_BodyGenTemplate, string> Alphabetizer { get; set; }

    public RelayCommand AddTemplate { get; }
    public RelayCommand RemoveTemplate { get; }
    public RelayCommand ImportBodyGen { get; }
}


public class VM_BodyGenTemplate : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    public delegate VM_BodyGenTemplate Factory(ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodyGenTemplate> parentCollection, VM_BodyGenConfig parentConfig);
    public VM_BodyGenTemplate(ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodyGenTemplate> parentCollection, VM_BodyGenConfig parentConfig, IEnvironmentStateProvider environmentProvider, VM_NPCAttributeCreator attributeCreator, Logger logger, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _logger = logger;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        SubscribedTemplateGroups = templateGroups;
        GroupSelectionCheckList = new VM_CollectionMemberStringCheckboxList(SubscribedTemplateGroups);
        DescriptorsSelectionMenu = descriptorSelectionFactory(BodyShapeDescriptors, raceGroupingVMs, parentConfig);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentConfig = parentConfig;
        ParentCollection = parentCollection;

        ParentCollection.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection());
        SubscribedTemplateGroups.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection());
        GroupSelectionCheckList.CollectionMemberStrings.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection());

        this.WhenAnyValue(x => x.DescriptorsSelectionMenu.Header).Subscribe(x => UpdateStatusDisplay());
        this.WhenAnyValue(x => x.GroupSelectionCheckList.Header).Subscribe(x => UpdateStatusDisplay());

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
            execute: _ => ParentCollection.Remove(this)
        );
    }

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
    public ObservableCollection<VM_BodyGenTemplate> ParentCollection { get; set;}
    public ObservableCollection<VM_BodyGenTemplate> OtherGroupsTemplateCollection { get; set; } = new();
    public SolidColorBrush BorderColor { get; set; }
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public void CopyInViewModelFromModel(BodyGenConfig.BodyGenTemplate model, VM_BodyShapeDescriptorCreationMenu descriptorMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        Label = model.Label;
        Notes = model.Notes;
        Specs = model.Specs;
        GroupSelectionCheckList.InitializeFromHashSet(model.MemberOfTemplateGroups);
        DescriptorsSelectionMenu = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.BodyShapeDescriptors, descriptorMenu, raceGroupingVMs, ParentConfig, _descriptorSelectionFactory);
        AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            
        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        AllowedAttributes = _attributeCreator.GetViewModelsFromModels(model.AllowedAttributes, ParentConfig.AttributeGroupMenu.Groups, true, null);
        DisallowedAttributes = _attributeCreator.GetViewModelsFromModels(model.DisallowedAttributes, ParentConfig.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        RequiredTemplates = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.RequiredTemplates);
        WeightRange = model.WeightRange.Clone();

        UpdateStatusDisplay();
    }

    public static BodyGenConfig.BodyGenTemplate DumpViewModelToModel(VM_BodyGenTemplate viewModel)
    {
        BodyGenConfig.BodyGenTemplate model = new BodyGenConfig.BodyGenTemplate();
        model.Label = viewModel.Label;
        model.Notes = viewModel.Notes;
        model.Specs = viewModel.Specs;
        model.MemberOfTemplateGroups = viewModel.GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToHashSet();
        model.BodyShapeDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DescriptorsSelectionMenu);
        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.bAllowUnique;
        model.AllowNonUnique = viewModel.bAllowNonUnique;
        model.AllowRandom = viewModel.bAllowRandom;
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.RequiredTemplates = viewModel.RequiredTemplates.Select(x => x.Content).ToHashSet();
        model.WeightRange = viewModel.WeightRange.Clone();
        return model;
    }

    public ObservableCollection<VM_BodyGenTemplate> UpdateThisOtherGroupsTemplateCollection()
    {
        if (ParentConfig.IsLoadingFromViewModel)
        {
            return new(); // skip this when the parent BodyGen Config view model is being loaded in because every added Template will trigger this evaluation. 
        }

        var updatedCollection = new ObservableCollection<VM_BodyGenTemplate>();
        var excludedCollection = new ObservableCollection<VM_BodyGenTemplate>();

        foreach (var template in this.ParentCollection)
        {
            bool inGroup = false;
            foreach (var group in template.GroupSelectionCheckList.CollectionMemberStrings)
            {
                if (group.IsSelected == false) { continue; }

                foreach (var thisGroup in this.GroupSelectionCheckList.CollectionMemberStrings)
                {
                    if (thisGroup.IsSelected == false) { continue; }
                        
                    if (group.SubscribedString == thisGroup.SubscribedString)
                    {
                        inGroup = true;
                        break;
                    }
                }
                if (inGroup == true) { break; }
            }

            if (inGroup == false)
            {
                updatedCollection.Add(template);
            }
            else
            {
                excludedCollection.Add(template);
            }    
        }

        OtherGroupsTemplateCollection = updatedCollection;

        return excludedCollection;
    }

    public void UpdateStatusDisplay()
    {
        var belongsToGroup = false;
        foreach (var group in this.GroupSelectionCheckList.CollectionMemberStrings)
        {
            if (group.IsSelected)
            {
                belongsToGroup = true;
                break;
            }
        }

        if (!belongsToGroup)
        {
            BorderColor = new SolidColorBrush(Colors.Red);
            StatusHeader = "Warning:";
            StatusText = "Morph does not belong to any Morph Groups. Will not be assigned.";
            ShowStatus = true;
        }
        else if (!DescriptorsSelectionMenu.IsAnnotated())
        {
            BorderColor = new SolidColorBrush(Colors.Yellow);
            StatusHeader = "Warning:";
            StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
            ShowStatus = true;
        }
        else
        {
            BorderColor = new SolidColorBrush(Colors.LightGreen);
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;
        }
    }
}
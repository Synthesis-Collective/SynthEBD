using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using ReactiveUI;
using GongSolutions.Wpf.DragDrop;
using System.Windows.Controls;
using static SynthEBD.VM_NPCAttribute;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using static SynthEBD.AssetPack;

namespace SynthEBD;

public enum AssetPackMenuVisibility
{
    SubgroupEditor,
    DistributionRules,
    AssetReplacers,
    RecordTemplates,
    AttributeGroups
}

public class VM_AssetPack : VM, IHasAttributeGroupMenu, IDropTarget, IHasSubgroupViewModels
{
    private readonly IStateProvider _stateProvider;
    private readonly MainState _state;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_Settings_General _general;
    private readonly VM_BodyGenConfig.Factory _bodyGenConfigFactory;
    private readonly VM_AssetPackDirectReplacerMenu.Factory _assetPackDirectReplacerMenuFactory;
    private readonly VM_Subgroup.Factory _subgroupFactory;
    private readonly VM_ConfigDistributionRules.Factory _configDistributionRulesFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly AssetPackValidator _assetPackValidator;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly FileDialogs _fileDialogs;
    private readonly Factory _selfFactory;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly VM_NPCAttributeCreator _attributeCreator;

    public delegate VM_AssetPack Factory();

    public VM_AssetPack(
        IStateProvider stateProvider,
        MainState state,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsOBody oBody,
        VM_SettingsTexMesh texMesh,
        VM_Settings_General general,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_AssetPackDirectReplacerMenu.Factory assetPackDirectReplacerMenuFactory,
        VM_Subgroup.Factory subgroupFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_ConfigDistributionRules.Factory configDistributionRulesFactory,
        AssetPackValidator assetPackValidator,
        Logger logger,
        SynthEBDPaths paths,
        FileDialogs fileDialogs,
        SettingsIO_AssetPack assetPackIO,
        VM_NPCAttributeCreator attributeCreator,
        Factory selfFactory)
    {
        _stateProvider = stateProvider;
        _state = state;
        _oBody = oBody;
        _general = general;
        _bodyGenConfigFactory = bodyGenConfigFactory;
        _assetPackDirectReplacerMenuFactory = assetPackDirectReplacerMenuFactory;
        _subgroupFactory = subgroupFactory;
        _configDistributionRulesFactory = configDistributionRulesFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _assetPackValidator = assetPackValidator;
        _logger = logger;
        _paths = paths;
        _fileDialogs = fileDialogs;
        _selfFactory = selfFactory;
        _assetPackIO = assetPackIO;
        _attributeCreator = attributeCreator;

        ParentCollection = texMesh.AssetPacks;

        CurrentBodyGenSettings = bodyGen;
        switch (Gender)
        {
            case Gender.Female: AvailableBodyGenConfigs = CurrentBodyGenSettings.FemaleConfigs; break;
            case Gender.Male: AvailableBodyGenConfigs = CurrentBodyGenSettings.MaleConfigs; break;
        }

        PropertyChanged += RefreshTrackedBodyGenConfig;
        CurrentBodyGenSettings.PropertyChanged += RefreshTrackedBodyGenConfig;

        AttributeGroupMenu = new(general.AttributeGroupMenu, true, _attributeCreator, _logger);

        ReplacersMenu = assetPackDirectReplacerMenuFactory(this);

        DistributionRules = _configDistributionRulesFactory(general.RaceGroupings, this);

        BodyShapeMode = general.BodySelectionMode;
        general.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

        RecordTemplateLinkCache = state.RecordTemplateLinkCache;

        ParentMenuVM = texMesh;

        this.WhenAnyValue(x => x.Gender).Subscribe(x => SetDefaultRecordTemplate());

        this.WhenAnyValue(x => x.IsSelected).Subscribe(x => ParentMenuVM.RefreshDisplayedAssetPackString());

        AddSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { Subgroups.Add(subgroupFactory(general.RaceGroupings, Subgroups, this, null, false)); }
        );

        RemoveAssetPackConfigFile = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (_fileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"))
                {
                    ParentCollection.Remove(this);
                }
            }
        );

        AddAdditionalRecordTemplateAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { AdditionalRecordTemplateAssignments.Add(new VM_AdditionalRecordTemplate(_stateProvider, RecordTemplateLinkCache, AdditionalRecordTemplateAssignments)); }
        );

        AddRecordTemplateAdditionalRacesPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { DefaultRecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString("", DefaultRecordTemplateAdditionalRacesPaths)); }
        );

        MergeWithAssetPack = new RelayCommand(
            canExecute: _ => true,
            execute: _ => MergeInAssetPack(_paths.LogFolderPath)
        );

        SetDefaultTargetDestPaths = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { SetDefaultTargetPaths(); }
        );

        ValidateButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {

                // dump view models to models so that latest are available for validation
                BodyGenConfigs bgConfigs = new();
                bgConfigs.Male = bodyGen.MaleConfigs.Select(x => VM_BodyGenConfig.DumpViewModelToModel(x)).ToHashSet();
                bgConfigs.Female = bodyGen.FemaleConfigs.Select(x => VM_BodyGenConfig.DumpViewModelToModel(x)).ToHashSet();
                Settings_OBody oBodySettings = _oBody.DumpViewModelToModel();

                if (Validate(bgConfigs, oBodySettings, out List<string> errors))
                {
                    CustomMessageBox.DisplayNotificationOK("Validation", "No errors found.");
                }
                else
                {
                    _logger.LogError(String.Join(Environment.NewLine, errors));
                }
            }
        );

        SaveButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                _assetPackIO.SaveAssetPack(DumpViewModelToModel(this), out bool success);
                if (success)
                {
                    _logger.CallTimedNotifyStatusUpdateAsync(GroupName + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be saved.", ErrorType.Error, 3);
                }
            }
        );

        ListDisabledSubgroupsButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var disabledSubgroups = GetDisabledSubgroups();
                CustomMessageBox.DisplayNotificationOK("Disabled Subgroups", string.Join(Environment.NewLine, disabledSubgroups));
            }
        );

        DiscardButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var reloaded = _assetPackIO.LoadAssetPack(SourcePath, PatcherSettings.General.RaceGroupings, state.RecordTemplatePlugins, state.BodyGenConfigs, out bool success);
                if (!success)
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be reloaded from drive.", ErrorType.Error, 3);
                }

                var reloadedVM = _selfFactory();
                reloadedVM.CopyInViewModelFromModel(reloaded);
                this.IsSelected = reloadedVM.IsSelected;
                this.AttributeGroupMenu = reloadedVM.AttributeGroupMenu;
                this.AvailableBodyGenConfigs = reloadedVM.AvailableBodyGenConfigs;
                this.ConfigType = reloadedVM.ConfigType;
                this.CurrentBodyGenSettings = reloadedVM.CurrentBodyGenSettings;
                this.DefaultTemplateFK = reloadedVM.DefaultTemplateFK;
                this.DefaultRecordTemplateAdditionalRacesPaths = reloadedVM.DefaultRecordTemplateAdditionalRacesPaths;
                this.DistributionRules = reloadedVM.DistributionRules;
                this.Gender = reloadedVM.Gender;
                this.GroupName = reloadedVM.GroupName;
                this.ReplacersMenu = reloadedVM.ReplacersMenu;
                this.ShortName = reloadedVM.ShortName;
                this.SourcePath = reloadedVM.SourcePath;
                this.Subgroups = reloadedVM.Subgroups;
                this.TrackedBodyGenConfig = reloadedVM.TrackedBodyGenConfig;
                _logger.CallTimedNotifyStatusUpdateAsync("Discarded Changes", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
            }
        );

        CopyButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var copiedModel = DumpViewModelToModel(this);
                copiedModel.GroupName += " (2)";
                copiedModel.FilePath = String.Empty;
                var copiedVM = selfFactory();
                copiedVM.CopyInViewModelFromModel(copiedModel);
                texMesh.AssetPacks.Add(copiedVM);
                texMesh.AssetPresenterPrimary.AssetPack = copiedVM;
            }
        );

        SelectedSubgroupChanged = new RelayCommand(
            canExecute: _ => true,
            execute: x => this.DisplayedSubgroup = (VM_Subgroup)x
        );

        ViewSubgroupEditor = new RelayCommand(
            canExecute: _ => true,
            execute:
            x => DisplayedMenuType = AssetPackMenuVisibility.SubgroupEditor
        );

        ViewDistRulesEditor = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.DistributionRules
        );

        ViewDirectReplacersEditor = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.AssetReplacers
        );

        ViewRecordTemplatesEditor = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.RecordTemplates
        );

        ViewAttributeGroupsEditor = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.AttributeGroups
        );
    }

    public string GroupName { get; set; } = "New Asset Pack";
    public string ShortName { get; set; } = "NAP";
    public AssetPackType ConfigType { get; set; } = AssetPackType.Primary;
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();
    public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; } = new();
    public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
    public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
    public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }
    public ObservableCollection<VM_CollectionMemberString> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public bool IsSelected { get; set; } = true;
    public string SourcePath { get; set; } = "";
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public FormKey DefaultTemplateFK { get; set; } = new();
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public VM_AssetPackDirectReplacerMenu ReplacersMenu { get; set; }
    public VM_ConfigDistributionRules DistributionRules { get; set; }
    public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }
    public VM_Subgroup DisplayedSubgroup { get; set; }
    public RelayCommand RemoveAssetPackConfigFile { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand AddAdditionalRecordTemplateAssignment { get; }
    public RelayCommand AddRecordTemplateAdditionalRacesPath { get; }
    public RelayCommand MergeWithAssetPack { get; }
    public RelayCommand ValidateButton { get; }
    public RelayCommand ListDisabledSubgroupsButton { get; }
    public RelayCommand SaveButton { get; }
    public RelayCommand DiscardButton { get; }
    public RelayCommand CopyButton { get; }
    public RelayCommand SelectedSubgroupChanged { get; }
    public RelayCommand SetDefaultTargetDestPaths { get; }
    public BodyShapeSelectionMode BodyShapeMode { get; set; }
    public AssetPackMenuVisibility DisplayedMenuType { get; set; } = AssetPackMenuVisibility.SubgroupEditor;
    public RelayCommand ViewSubgroupEditor { get; }
    public RelayCommand ViewDistRulesEditor { get; }
    public RelayCommand ViewDirectReplacersEditor { get; }
    public RelayCommand ViewRecordTemplatesEditor { get; }
    public RelayCommand ViewAttributeGroupsEditor { get; }
    public VM_SettingsTexMesh ParentMenuVM { get; set; }
    public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
    {
        {Gender.Male, "Male"},
        {Gender.Female, "Female"},
    };

    public bool Validate(BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, out List<string> errors)
    {
        var model = DumpViewModelToModel(this);
        errors = new List<string>();
        return _assetPackValidator.Validate(model, errors, bodyGenConfigs, oBodySettings);
    }

    public static void GetViewModelsFromModels(
        List<AssetPack> assetPacks,
        VM_SettingsTexMesh texMesh,
        Settings_TexMesh texMeshSettings, 
        Factory assetPackFactory)
    {
        texMesh.AssetPacks.Clear();
        for (int i = 0; i < assetPacks.Count; i++)
        {
            var viewModel = assetPackFactory();
            viewModel.CopyInViewModelFromModel(assetPacks[i]);
            viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);
            texMesh.AssetPacks.Add(viewModel);
        }
    }
    
    public void CopyInViewModelFromModel(AssetPack model)
    {
        GroupName = model.GroupName;
        ShortName = model.ShortName;
        ConfigType = model.ConfigType;
        Gender = model.Gender;
        DisplayAlerts = model.DisplayAlerts;
        UserAlert = model.UserAlert;

        RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(_general.RaceGroupings);

        if (model.AssociatedBodyGenConfigName != "")
        {
            switch(Gender)
            {
                case Gender.Female:
                    TrackedBodyGenConfig = CurrentBodyGenSettings.FemaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                    break;
                case Gender.Male:
                    TrackedBodyGenConfig = CurrentBodyGenSettings.MaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                    break;
            }
        }
        else
        {
            TrackedBodyGenConfig = _bodyGenConfigFactory(new ObservableCollection<VM_BodyGenConfig>());
        }

        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);

        ReplacersMenu = _assetPackDirectReplacerMenuFactory(this);
        ReplacersMenu.CopyInViewModelFromModels(model.ReplacerGroups);

        DefaultTemplateFK = model.DefaultRecordTemplate;
        foreach(var additionalTemplateAssignment in model.AdditionalRecordTemplateAssignments)
        {
            var assignmentVM = new VM_AdditionalRecordTemplate(_stateProvider, _state.RecordTemplateLinkCache, AdditionalRecordTemplateAssignments);
            assignmentVM.RaceFormKeys = new ObservableCollection<FormKey>(additionalTemplateAssignment.Races);
            assignmentVM.TemplateNPC = additionalTemplateAssignment.TemplateNPC;
            assignmentVM.AdditionalRacesPaths = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(additionalTemplateAssignment.AdditionalRacesPaths);
            AdditionalRecordTemplateAssignments.Add(assignmentVM);
        }

        foreach (var path in model.DefaultRecordTemplateAdditionalRacesPaths)
        {
            DefaultRecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString(path, DefaultRecordTemplateAdditionalRacesPaths));
        }

        foreach (var sg in model.Subgroups)
        {
            var subVm = _subgroupFactory(_general.RaceGroupings, Subgroups, this, null, false);
            subVm.CopyInViewModelFromModel(sg, _general);
            Subgroups.Add(subVm);
        }

        // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
        ObservableCollection<VM_Subgroup> flattenedSubgroupList = FlattenSubgroupVMs(Subgroups, new ObservableCollection<VM_Subgroup>());
        LinkRequiredSubgroups(flattenedSubgroupList);
        LinkExcludedSubgroups(flattenedSubgroupList);

        DistributionRules = _configDistributionRulesFactory(_general.RaceGroupings, this);
        DistributionRules.CopyInViewModelFromModel(model.DistributionRules, _general.RaceGroupings, this);

        SourcePath = model.FilePath;
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
    {
        models.Clear();

        foreach (var vm in viewModels)
        {
            models.Add(DumpViewModelToModel(vm));
        }
    }

    public static AssetPack DumpViewModelToModel(VM_AssetPack viewModel)
    {
        AssetPack model = new AssetPack();
        model.GroupName = viewModel.GroupName;
        model.ShortName = viewModel.ShortName;
        model.ConfigType = viewModel.ConfigType;
        model.Gender = viewModel.Gender;
        model.DisplayAlerts = viewModel.DisplayAlerts;
        model.UserAlert = viewModel.UserAlert;

        if (viewModel.TrackedBodyGenConfig != null)
        {
            model.AssociatedBodyGenConfigName = viewModel.TrackedBodyGenConfig.Label;
        }

        model.DefaultRecordTemplate = viewModel.DefaultTemplateFK;
        model.AdditionalRecordTemplateAssignments = viewModel.AdditionalRecordTemplateAssignments.Select(x => VM_AdditionalRecordTemplate.DumpViewModelToModel(x)).ToHashSet();
        model.DefaultRecordTemplateAdditionalRacesPaths = viewModel.DefaultRecordTemplateAdditionalRacesPaths.Select(x => x.Content).ToHashSet();

        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);

        foreach (var svm in viewModel.Subgroups)
        {
            model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
        }

        model.ReplacerGroups = VM_AssetPackDirectReplacerMenu.DumpViewModelToModels(viewModel.ReplacersMenu);

        model.DistributionRules = VM_ConfigDistributionRules.DumpViewModelToModel(viewModel.DistributionRules);

        model.FilePath = viewModel.SourcePath;

        return model;
    }

    public static ObservableCollection<VM_Subgroup> FlattenSubgroupVMs(ObservableCollection<VM_Subgroup> currentLevelSGs, ObservableCollection<VM_Subgroup> flattened)
    {
        foreach(var sg in currentLevelSGs)
        {
            flattened.Add(sg);
            FlattenSubgroupVMs(sg.Subgroups, flattened);
        }
        return flattened;
    }

    public static void LinkRequiredSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
    {
        foreach (var sg in flattenedSubgroups)
        {
            foreach (string id in sg.RequiredSubgroupIDs)
            {
                foreach (var candidate in flattenedSubgroups)
                {
                    if (candidate.ID == id)
                    {
                        sg.RequiredSubgroups.Add(candidate);
                        break;
                    }
                }
            }
            sg.RefreshListBoxLabel(sg.RequiredSubgroups, VM_Subgroup.SubgroupListBox.Required);
        }
    }

    public static void LinkExcludedSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
    {
        foreach (var sg in flattenedSubgroups)
        {
            foreach (string id in sg.ExcludedSubgroupIDs)
            {
                foreach (var candidate in flattenedSubgroups)
                {
                    if (candidate.ID == id)
                    {
                        sg.ExcludedSubgroups.Add(candidate);
                        break;
                    }
                }
            }
            sg.RefreshListBoxLabel(sg.ExcludedSubgroups, VM_Subgroup.SubgroupListBox.Excluded);
        }
    }


    public void RemoveAssetPackDialog()
    {
        bool result = CustomMessageBox.DisplayNotificationYesNo("Confirm Deletion", "Are you sure you want to permanently delete this config file?");
            
        switch (result)
        {
            case true:
                if (File.Exists(this.SourcePath))
                {
                    try
                    {
                        File.Delete(this.SourcePath);
                    }
                    catch
                    {
                        _logger.LogError("Could not delete file at " + this.SourcePath);
                        _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete Asset Pack Config File", ErrorType.Error, 5);
                    }
                }
                    
                break;
            case false:
                break;
        }
    }

    public void RefreshTrackedBodyGenConfig(object sender, PropertyChangedEventArgs e)
    {
        switch (this.Gender)
        {
            case Gender.Female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
            case Gender.Male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
        }
    }

    public void MergeInAssetPack(string assetPackDirPath)
    {
        List<string> newSubgroupNames = new List<string>();

        if (IO_Aux.SelectFile(assetPackDirPath, "Config files (*.json)|*.json", "Select config file to merge in", out string path))
        {
            var newAssetPack = _assetPackIO.LoadAssetPack(path, PatcherSettings.General.RaceGroupings, _state.RecordTemplatePlugins, _state.BodyGenConfigs, out bool loadSuccess);
            if (loadSuccess)
            {
                var newAssetPackVM = _selfFactory();
                newAssetPackVM.CopyInViewModelFromModel(newAssetPack);
                    
                // first add completely new top-level subgroups if necessary
                foreach (var subgroup in newAssetPackVM.Subgroups)
                {
                    if (!this.Subgroups.Select(x => x .ID).Contains(subgroup.ID, StringComparer.OrdinalIgnoreCase))
                    {
                        var clone = subgroup.Clone() as VM_Subgroup;
                        clone.ParentAssetPack = this;
                        clone.ParentCollection = this.Subgroups;
                        this.Subgroups.Add(clone);
                        newSubgroupNames.Add(clone.ID + ": " + clone.Name);
                    }
                }

                // merge existing subgroups
                foreach (var subgroup in this.Subgroups)
                {
                    var matchedSubgroup = newAssetPackVM.Subgroups.Where(x => x.ID == subgroup.ID).FirstOrDefault();
                    if (matchedSubgroup != null)
                    {
                        MergeSubgroupLists(subgroup.Subgroups, matchedSubgroup.Subgroups, this, newSubgroupNames);
                    }
                }

                if (newSubgroupNames.Any())
                {
                    CustomMessageBox.DisplayNotificationOK("Config Merger", "The following subgroups were imported:" + Environment.NewLine + string.Join(Environment.NewLine, newSubgroupNames));
                }
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("Config Merger", "That file could not be parsed as a valid Asset Config Plugin File.");
            }
        }
    }

    public static void MergeSubgroupLists(ObservableCollection<VM_Subgroup> ListA, ObservableCollection<VM_Subgroup> ListB, VM_AssetPack parentAssetPack, List<string> newSubgroupNames)
    {
        foreach (VM_Subgroup candidateSubgroup in ListB)
        {
            var matchedSubgroup = ListA.Where(x => x.ID == candidateSubgroup.ID).FirstOrDefault();
            if (matchedSubgroup is null)
            {
                var clone = candidateSubgroup.Clone() as VM_Subgroup;
                clone.ParentAssetPack = parentAssetPack;
                clone.ParentCollection = ListA;
                ListA.Add(clone);
                newSubgroupNames.Add(clone.ID + ": " + clone.Name);
            }
            else
            {
                MergeSubgroupLists(matchedSubgroup.Subgroups, candidateSubgroup.Subgroups, parentAssetPack, newSubgroupNames);
            }
        }
    }

    public void SetDefaultRecordTemplate()
    {
        if (RecordTemplateLinkCache is null) { return; }

        switch(Gender)
        {
            case Gender.Male:
                if (RecordTemplateLinkCache.TryResolve<INpcGetter>("DefaultMale", out var defaultMaleRec))
                {
                    DefaultTemplateFK = defaultMaleRec.FormKey;
                }
                break;
            case Gender.Female:
                if (RecordTemplateLinkCache.TryResolve<INpcGetter>("DefaultFemale", out var defaultFemaleRec))
                {
                    DefaultTemplateFK = defaultFemaleRec.FormKey;
                }
                break;
        }
    }

    public void SetDefaultTargetPaths()
    {
        //first collect changed paths to warn user
        List<string> filePathWarnings = new List<string>();
        foreach (var subgroup in Subgroups)
        {
            GenerateFilePathWarningString(subgroup, filePathWarnings);
        }

        string warnStr = string.Join(Environment.NewLine, filePathWarnings);

        if (string.IsNullOrWhiteSpace(warnStr) || CustomMessageBox.DisplayNotificationYesNo("Replace Destination Paths?", "The following destinations will be modified:" + Environment.NewLine + warnStr))
        {
            foreach (var subgroup in Subgroups)
            {
                SetDefaultSubgroupFilePaths(subgroup);
            }

            bool hasBodyPath = false;
            bool hasFeetPath = false;
            foreach (var subgroup in Subgroups)
            {
                if (SubgroupHasDestinationPath(subgroup, "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)"))
                {
                    hasBodyPath = true;
                    break;
                }
            }
            foreach (var subgroup in Subgroups)
            {
                if (SubgroupHasDestinationPath(subgroup, "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)"))
                {
                    hasFeetPath = true;
                    break;
                }
            }

            if (hasBodyPath && !hasFeetPath) // duplicate body paths as feet
            {
                foreach (var subgroup in Subgroups)
                {
                    DuplicateBodyPathsAsFeet(subgroup);
                }
            }

            bool hasBeastTailPath = false;
            foreach (var subgroup in Subgroups)
            {
                if (SubgroupHasDestinationPath(subgroup, "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)"))
                {
                    hasBeastTailPath = true;
                    break;
                }
            }

            if (!hasBeastTailPath)
            {
                foreach (var subgroup in Subgroups)
                {
                    DuplicateBodyPathsAsTail(subgroup);
                }
            }
        }
    }

    public void DuplicateBodyPathsAsFeet(VM_Subgroup subgroup)
    {
        var newFeetPaths = new HashSet<VM_FilePathReplacement>();
        foreach (var path in subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath.Contains("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)")))
        {
            var newPath = _filePathReplacementFactory(path.ParentMenu);
            newPath.Source = path.Source;
            newPath.IntellisensedPath = path.IntellisensedPath.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)");
            newFeetPaths.Add(newPath);
        }

        foreach (var path in newFeetPaths)
        {
            subgroup.PathsMenu.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsFeet(sg);
        }
    }

    public void DuplicateBodyPathsAsTail(VM_Subgroup subgroup)
    {
        var newTailPaths = new HashSet<VM_FilePathReplacement>();
        var pathsNeedingTails = new HashSet<string>()
        {
            //male khajiit
            "bodymale.dds",
            "bodymale_msn.dds",
            "bodymale_s.dds",
            //male argonian
            "argonianmalebody.dds",
            "argonianmalebody_msn.dds", 
            "argonianmalebody_s.dds",
            //female khajiit
            "femalebody.dds", 
            "femalebody_msn.dds", 
            "femalebody_s.dds",
            //female argonian
            "argonianfemalebody.dds", 
            "argonianfemalebody_msn.dds",
            "argonianfemalebody_s.dds",
        };

        foreach (var path in subgroup.PathsMenu.Paths.Where(x => pathsNeedingTails.Contains(Path.GetFileName(x.Source), StringComparer.OrdinalIgnoreCase)))
        {
            var newPath = _filePathReplacementFactory(path.ParentMenu);
            newPath.Source = path.Source;
            newPath.IntellisensedPath = path.IntellisensedPath.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)");
            newTailPaths.Add(newPath);
        }

        foreach (var path in newTailPaths)
        {
            subgroup.PathsMenu.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsTail(sg);
        }
    }

    public static bool SubgroupHasDestinationPath(VM_Subgroup subgroup, string destinationPath)
    {
        if (subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath.Contains(destinationPath)).Any())
        {
            return true;
        }

        foreach (var sg in subgroup.Subgroups)
        {
            if (SubgroupHasDestinationPath(sg, destinationPath))
            {
                return true;
            }
        }
        return false;
    }

    public static void SetDefaultSubgroupFilePaths(VM_Subgroup subgroup)
    {
        foreach (var path in subgroup.PathsMenu.Paths.Where(x => !string.IsNullOrWhiteSpace(x.Source)))
        {
            var fileName = Path.GetFileName(path.Source);

            if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(fileName) && path.IntellisensedPath != FilePathDestinationMap.FileNameToDestMap[fileName])
            {
                path.IntellisensedPath = FilePathDestinationMap.FileNameToDestMap[fileName];
            }
        }

        foreach (var sg in subgroup.Subgroups)
        {
            SetDefaultSubgroupFilePaths(sg);
        }
    }

    public static bool SubgroupHasSourcePath(VM_Subgroup subgroup, string destinationPath)
    {
        if (subgroup.PathsMenu.Paths.Where(x => Path.GetFileName(x.Source).Equals(destinationPath, StringComparison.OrdinalIgnoreCase)).Any())
        {
            return true;
        }

        foreach (var sg in subgroup.Subgroups)
        {
            if (SubgroupHasDestinationPath(sg, destinationPath))
            {
                return true;
            }
        }
        return false;
    }

    public static void GenerateFilePathWarningString(VM_Subgroup subgroup, List<string> warnStrs)
    {
        string currentSubgroupWarnStr = "";
        foreach (var path in subgroup.PathsMenu.Paths.Where(x => !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.IntellisensedPath)))
        {
            var fileName = Path.GetFileName(path.Source);

            if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(fileName) && path.IntellisensedPath != FilePathDestinationMap.FileNameToDestMap[fileName])
            {
                currentSubgroupWarnStr += Environment.NewLine + fileName + ": " + path.IntellisensedPath + " -> " + FilePathDestinationMap.FileNameToDestMap[fileName];
            }
        }

        if(!string.IsNullOrWhiteSpace(currentSubgroupWarnStr))
        {
            warnStrs.Add(subgroup.ID + " (" + subgroup.Name + "):" + Environment.NewLine + currentSubgroupWarnStr);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            GenerateFilePathWarningString(sg, warnStrs);
        }
    }

    public bool ContainsSubgroupID(string id)
    {
        foreach (var subgroup in Subgroups)
        {
            if (subgroup.ContainsID(id))
            {
                return true;
            }    
        }
        return false;
    }

    public void AutoGenerateSubgroupIDs()
    {
        ClearSubgroupIDs();
        foreach (var subgroup in Subgroups)
        {
            subgroup.AutoGenerateID(true, 0);
        }
    }

    public void ClearSubgroupIDs()
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.ClearID(true);
        }
    }

    public List<string> GetDisabledSubgroups()
    {
        List<string> disabledSubgroups = new();
        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDisabledSubgroups(disabledSubgroups);
        }
        return disabledSubgroups;
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            var isTreeViewItem = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && dropInfo.VisualTargetItem is TreeViewItem; //https://github.com/punker76/gong-wpf-dragdrop/blob/d8545166eb08e4d71fc2d2aa67713ba7da70f92c/src/GongSolutions.WPF.DragDrop/DefaultDropHandler.cs#L150
            dropInfo.DropTargetAdorner = isTreeViewItem ? DropTargetAdorners.Highlight : DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
            if (dropInfo.KeyStates.HasFlag(DragDropKeyStates.RightMouseButton))
            {
                DropInitiatedRightClick = true;
            }
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            var draggedSubgroup = (VM_Subgroup)dropInfo.Data;
            if (dropInfo.TargetItem is VM_Subgroup)
            {
                VM_Subgroup dropTarget = (VM_Subgroup)dropInfo.TargetItem;

                if (draggedSubgroup.IsParentOf(dropTarget)) { return; } // prevent mis-click when user releases the click on treeview expander arrow slightly below where they initiated the click, simulating a drop into or before the child node and causing the parent to disappear into the abyss.

                var clone = (VM_Subgroup)draggedSubgroup.Clone(dropTarget.Subgroups);
                clone.ParentAssetPack = dropTarget.ParentAssetPack;

                if (dropInfo.DropTargetAdorner.Name == "DropTargetInsertionAdorner")
                {
                    int insertIndex = (dropInfo.InsertIndex != dropInfo.UnfilteredInsertIndex) ? dropInfo.UnfilteredInsertIndex : dropInfo.InsertIndex;
                    clone.ParentCollection = dropTarget.ParentCollection;
                    clone.ParentSubgroup = dropTarget.ParentSubgroup;
                    dropTarget.ParentCollection.Insert(insertIndex, clone);
                }
                else
                {
                    clone.ParentCollection = dropTarget.Subgroups;
                    clone.ParentSubgroup = dropTarget;
                    if (dropTarget.Name == draggedSubgroup.Name && dropTarget.ID == draggedSubgroup.ID) { return; }

                    dropTarget.Subgroups.Add(clone);
                }
            }
            else if (dropInfo.VisualTarget is TreeView)
            {
                var targetTV = (TreeView)dropInfo.VisualTarget;
                var dropTarget = (VM_AssetPack)targetTV.DataContext;
                if (targetTV.Name == "TVsubgroups" && dropTarget != null)
                {
                    var clone = (VM_Subgroup)draggedSubgroup.Clone(dropTarget.Subgroups);
                    clone.ParentCollection = dropTarget.Subgroups;
                    clone.ParentAssetPack = dropTarget;
                    clone.ParentSubgroup = null;
                    dropTarget.Subgroups.Add(clone);
                }
            }

            if (!DropInitiatedRightClick)
            {
                draggedSubgroup.ParentCollection.Remove(draggedSubgroup);
            }
        }

        DropInitiatedRightClick = false;
    }

    public bool DropInitiatedRightClick { get; set; }

    public bool CheckForVersionUpdate(Version version)
    {
        foreach (var subgroup in Subgroups)
        {
            if (subgroup.CheckForVersionUpdate(version))
            {
                return true;
            }
        }

        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                if (subgroup.CheckForVersionUpdate(version))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public void PerformVersionUpdate(Version version)
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.PerformVersionUpdate(version);
        }

        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                subgroup.PerformVersionUpdate(version);
            }
        }
    }
}

public interface IHasSubgroupViewModels
{
    ObservableCollection<VM_Subgroup> Subgroups { get; }
}
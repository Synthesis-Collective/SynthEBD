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
using Noggog.WPF;
using DynamicData;
using System.Reactive.Linq;

namespace SynthEBD;

public enum AssetPackMenuVisibility
{
    SubgroupEditor,
    DistributionRules,
    AssetReplacers,
    RecordTemplates,
    AttributeGroups,
    RaceGroupings,
    Misc
}

public class VM_AssetPack : VM, IHasAttributeGroupMenu, IDropTarget, IHasSubgroupViewModels, IHasRaceGroupingEditor
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_Settings_General _general;
    private readonly VM_SettingsModManager _modManager;
    private readonly VM_BodyGenConfig.Factory _bodyGenConfigFactory;
    private readonly VM_AssetPackDirectReplacerMenu.Factory _assetPackDirectReplacerMenuFactory;
    private readonly VM_AssetPackMiscMenu.Factory _miscMenuFactory;
    private readonly VM_Subgroup.Factory _subgroupFactory;
    private readonly VM_ConfigDistributionRules.Factory _configDistributionRulesFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly AssetPackValidator _assetPackValidator;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly IO_Aux _auxIO;
    private readonly FileDialogs _fileDialogs;
    private readonly Factory _selfFactory;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupMenuFactory;
    private readonly VM_RaceGroupingEditor.Factory _raceGroupingEditorFactory;

    public delegate VM_AssetPack Factory();

    public VM_AssetPack(
        IEnvironmentStateProvider environmentProvider,
        PatcherState state,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsOBody oBody,
        VM_SettingsTexMesh texMesh,
        VM_Settings_General general,
        VM_SettingsModManager modManager,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_AssetPackDirectReplacerMenu.Factory assetPackDirectReplacerMenuFactory,
        VM_AssetPackMiscMenu.Factory miscMenuFactory,
        VM_Subgroup.Factory subgroupFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_ConfigDistributionRules.Factory configDistributionRulesFactory,
        AssetPackValidator assetPackValidator,
        Logger logger,
        SynthEBDPaths paths,
        IO_Aux auxIO,
        FileDialogs fileDialogs,
        SettingsIO_AssetPack assetPackIO,
        VM_AttributeGroupMenu.Factory attributeGroupMenuFactory,
        VM_RaceGroupingEditor.Factory raceGroupingEditorFactory,
        Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _patcherState = state;
        _oBody = oBody;
        _general = general;
        _modManager = modManager;
        _bodyGenConfigFactory = bodyGenConfigFactory;
        _assetPackDirectReplacerMenuFactory = assetPackDirectReplacerMenuFactory;
        _miscMenuFactory = miscMenuFactory;
        _subgroupFactory = subgroupFactory;
        _configDistributionRulesFactory = configDistributionRulesFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _assetPackValidator = assetPackValidator;
        _logger = logger;
        _paths = paths;
        _auxIO = auxIO;
        _fileDialogs = fileDialogs;
        _selfFactory = selfFactory;
        _assetPackIO = assetPackIO;
        _attributeGroupMenuFactory = attributeGroupMenuFactory;
        _raceGroupingEditorFactory = raceGroupingEditorFactory;

        ParentCollection = texMesh.AssetPacks;

        CurrentBodyGenSettings = bodyGen;

        this.WhenAnyValue(x => x.Gender).Subscribe(x => {
            switch (Gender)
            {
                case Gender.Female: AvailableBodyGenConfigs = CurrentBodyGenSettings.FemaleConfigs; break;
                case Gender.Male: AvailableBodyGenConfigs = CurrentBodyGenSettings.MaleConfigs; break;
            }
        }).DisposeWith(this);

        AttributeGroupMenu = _attributeGroupMenuFactory(general.AttributeGroupMenu, true);

        RaceGroupingEditor = _raceGroupingEditorFactory(this, true);

        ReplacersMenu = assetPackDirectReplacerMenuFactory(this);

        DistributionRules = _configDistributionRulesFactory(RaceGroupingEditor.RaceGroupings, this);

        MiscMenu = _miscMenuFactory(this);

        BodyShapeMode = general.BodySelectionMode;
        general.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x).DisposeWith(this);

        RecordTemplateLinkCache = state.RecordTemplateLinkCache;

        ParentMenuVM = texMesh;

        this.WhenAnyValue(x => x.Gender).Subscribe(x => SetDefaultRecordTemplate()).DisposeWith(this);

        UpdateOrderingMenu = Observable.CombineLatest(
                this.WhenAnyValue(x => x.GroupName),
                this.WhenAnyValue(x => x.ConfigType),
                this.WhenAnyValue(x => x.IsSelected),
            (_, _, _) => { return 0; }).Unit();

        UpdateActiveHeader = Observable.CombineLatest(
                this.WhenAnyValue(x => x.ShortName),
                this.WhenAnyValue(x => x.IsSelected),
            (_, _) => { return 0; }).Unit();

        AddSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { Subgroups.Add(subgroupFactory(RaceGroupingEditor.RaceGroupings, Subgroups, this, null, false)); }
        );

        RemoveAssetPackConfigFile = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (_fileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"))
                {
                    DeleteAssetFiles(); // prompts user after collecting data
                    ParentCollection.Remove(this);
                }
            }
        );

        AddAdditionalRecordTemplateAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { AdditionalRecordTemplateAssignments.Add(new VM_AdditionalRecordTemplate(_environmentProvider, RecordTemplateLinkCache, AdditionalRecordTemplateAssignments)); }
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
                bgConfigs.Male = bodyGen.MaleConfigs.Select(x => x.DumpViewModelToModel()).ToHashSet();
                bgConfigs.Female = bodyGen.FemaleConfigs.Select(x => x.DumpViewModelToModel()).ToHashSet();
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
            execute: _ => SaveToModel(true)
        );

        ListDisabledSubgroupsButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var disabledSubgroups = GetDisabledSubgroups();
                CustomMessageBox.DisplayNotificationOK("Disabled Subgroups", string.Join(Environment.NewLine, disabledSubgroups));
            }
        );

        ListCustomRulesButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var rules = GetCustomRules();
                CustomMessageBox.DisplayNotificationOK("Custom Rules", string.Join(Environment.NewLine, rules));
            }
        );

        DiscardButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var reloaded = _assetPackIO.LoadAssetPack(SourcePath, _patcherState.GeneralSettings.RaceGroupings, state.RecordTemplatePlugins, state.BodyGenConfigs, out bool success);
                if (!success)
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be reloaded from drive.", ErrorType.Error, 3);
                }

                var reloadedVM = _selfFactory();
                reloadedVM.CopyInViewModelFromModel(reloaded, _general.RaceGroupingEditor.RaceGroupings);
                IsSelected = reloadedVM.IsSelected;
                AttributeGroupMenu = reloadedVM.AttributeGroupMenu;
                AvailableBodyGenConfigs = reloadedVM.AvailableBodyGenConfigs;
                ConfigType = reloadedVM.ConfigType;
                CurrentBodyGenSettings = reloadedVM.CurrentBodyGenSettings;
                DefaultTemplateFK = reloadedVM.DefaultTemplateFK;
                DefaultRecordTemplateAdditionalRacesPaths = reloadedVM.DefaultRecordTemplateAdditionalRacesPaths;
                DistributionRules = reloadedVM.DistributionRules;
                Gender = reloadedVM.Gender;
                GroupName = reloadedVM.GroupName;
                ReplacersMenu = reloadedVM.ReplacersMenu;
                ShortName = reloadedVM.ShortName;
                SourcePath = reloadedVM.SourcePath;
                Subgroups = reloadedVM.Subgroups;
                TrackedBodyGenConfig = reloadedVM.TrackedBodyGenConfig;
                _logger.CallTimedNotifyStatusUpdateAsync("Discarded Changes", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
            }
        );

        CopyButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var copiedModel = DumpViewModelToModel();
                copiedModel.GroupName += " (2)";
                copiedModel.FilePath = String.Empty;
                var copiedVM = selfFactory();
                copiedVM.CopyInViewModelFromModel(copiedModel, _general.RaceGroupingEditor.RaceGroupings);
                texMesh.AssetPacks.Add(copiedVM);
                texMesh.AssetPresenterPrimary.AssetPack = copiedVM;
            }
        );

        SelectedSubgroupChanged = new RelayCommand(
            canExecute: _ => true,
            execute: x => DisplayedSubgroup = (VM_Subgroup)x
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

        ViewRaceGroupingsEditor = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.RaceGroupings
        );

        ViewMiscMenu = new RelayCommand(
           canExecute: _ => true,
           execute: x => DisplayedMenuType = AssetPackMenuVisibility.Misc
        );
    }

    public string GroupName { get; set; } = "New Asset Pack";
    public string ShortName { get; set; } = "NAP";
    public AssetPackType ConfigType { get; set; } = AssetPackType.Primary;
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();
    public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
    public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
    public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }
    public ObservableCollection<VM_CollectionMemberString> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public bool IsSelected { get; set; } = true;
    public string SourcePath { get; set; } = "";
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public FormKey DefaultTemplateFK { get; set; } = new();
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public VM_AssetPackDirectReplacerMenu ReplacersMenu { get; set; }
    public VM_ConfigDistributionRules DistributionRules { get; set; }
    public VM_AssetPackMiscMenu MiscMenu { get; set; }
    public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }
    public VM_Subgroup DisplayedSubgroup { get; set; }
    public RelayCommand RemoveAssetPackConfigFile { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand AddAdditionalRecordTemplateAssignment { get; }
    public RelayCommand AddRecordTemplateAdditionalRacesPath { get; }
    public RelayCommand MergeWithAssetPack { get; }
    public RelayCommand ValidateButton { get; }
    public RelayCommand ListDisabledSubgroupsButton { get; }
    public RelayCommand ListCustomRulesButton { get; }
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
    public RelayCommand ViewRaceGroupingsEditor { get; }
    public RelayCommand ViewMiscMenu { get; }
    public VM_SettingsTexMesh ParentMenuVM { get; set; }
    public IObservable<System.Reactive.Unit> UpdateOrderingMenu { get; set; }
    public IObservable<System.Reactive.Unit> UpdateActiveHeader { get; set; }

    public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
    {
        {Gender.Male, "Male"},
        {Gender.Female, "Female"},
    };

    public bool Validate(BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, out List<string> errors)
    {
        var model = DumpViewModelToModel();
        errors = new List<string>();
        return _assetPackValidator.Validate(model, errors, bodyGenConfigs, oBodySettings);
    }

    public static void GetViewModelsFromModels(
        List<AssetPack> assetPacks,
        VM_SettingsTexMesh texMesh,
        Settings_TexMesh texMeshSettings, 
        Factory assetPackFactory,
        ObservableCollection<VM_RaceGrouping> mainRaceGroupings,
        Logger logger)
    {
        if (texMeshSettings == null)
        {
            return;
        }

        texMesh.AssetPacks.Clear();
        for (int i = 0; i < assetPacks.Count; i++)
        {
            logger.LogStartupEventStart("Loading UI for Asset Config File " + assetPacks[i].GroupName);
            logger.LogStartupEventStart("Creating new Asset Pack UI");
            var viewModel = assetPackFactory();
            logger.LogStartupEventEnd("Creating new Asset Pack UI");
            viewModel.CopyInViewModelFromModel(assetPacks[i], mainRaceGroupings);
            viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);
            texMesh.AssetPacks.Add(viewModel);
            logger.LogStartupEventEnd("Loading UI for Asset Config File " + assetPacks[i].GroupName);
        }
    }
    
    public void CopyInViewModelFromModel(AssetPack model, ObservableCollection<VM_RaceGrouping> mainRaceGroupings)
    {
        _logger.LogStartupEventStart("Loading UI for Asset Pack Main Settings");
        GroupName = model.GroupName;
        ShortName = model.ShortName;
        ConfigType = model.ConfigType;
        Gender = model.Gender;
        DisplayAlerts = model.DisplayAlerts;
        UserAlert = model.UserAlert;

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

        RaceGroupingEditor.CopyInFromModel(model.RaceGroupings, mainRaceGroupings);

        ReplacersMenu = _assetPackDirectReplacerMenuFactory(this);
        ReplacersMenu.CopyInViewModelFromModels(model.ReplacerGroups);

        MiscMenu.CopyInViewModelFromModel(model);

        DefaultTemplateFK = model.DefaultRecordTemplate;
        foreach(var additionalTemplateAssignment in model.AdditionalRecordTemplateAssignments)
        {
            var assignmentVM = new VM_AdditionalRecordTemplate(_environmentProvider, _patcherState.RecordTemplateLinkCache, AdditionalRecordTemplateAssignments);
            assignmentVM.RaceFormKeys = new ObservableCollection<FormKey>(additionalTemplateAssignment.Races);
            assignmentVM.TemplateNPC = additionalTemplateAssignment.TemplateNPC;
            assignmentVM.AdditionalRacesPaths = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(additionalTemplateAssignment.AdditionalRacesPaths);
            AdditionalRecordTemplateAssignments.Add(assignmentVM);
        }

        foreach (var path in model.DefaultRecordTemplateAdditionalRacesPaths)
        {
            DefaultRecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString(path, DefaultRecordTemplateAdditionalRacesPaths));
        }
        _logger.LogStartupEventEnd("Loading UI for Asset Pack Main Settings");

        _logger.LogStartupEventStart("Loading UI for Subgroups");
        foreach (var sg in model.Subgroups)
        {
            _logger.LogStartupEventStart("Creating UI for Subgroup " + sg.ID);
            var subVm = _subgroupFactory(RaceGroupingEditor.RaceGroupings, Subgroups, this, null, false);
            _logger.LogStartupEventEnd("Creating UI for Subgroup " + sg.ID);
            _logger.LogStartupEventStart("Loading UI for Subgroup " + sg.ID);
            subVm.CopyInViewModelFromModel(sg);
            _logger.LogStartupEventEnd("Loading UI for Subgroup " + sg.ID);
            Subgroups.Add(subVm);
        }
        _logger.LogStartupEventEnd("Loading UI for Subgroups");

        // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
        _logger.LogStartupEventStart("Linking required and excluded subgroups");
        ObservableCollection<VM_Subgroup> flattenedSubgroupList = FlattenSubgroupVMs(Subgroups, new ObservableCollection<VM_Subgroup>());
        LinkRequiredSubgroups(flattenedSubgroupList);
        LinkExcludedSubgroups(flattenedSubgroupList);
        _logger.LogStartupEventEnd("Linking required and excluded subgroups");

        _logger.LogStartupEventStart("Loading config distribution rules");
        DistributionRules = _configDistributionRulesFactory(RaceGroupingEditor.RaceGroupings, this);
        DistributionRules.CopyInViewModelFromModel(model.DistributionRules, RaceGroupingEditor.RaceGroupings, this);
        _logger.LogStartupEventEnd("Loading config distribution rules");

        SourcePath = model.FilePath;
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
    {
        models.Clear();

        foreach (var vm in viewModels)
        {
            models.Add(vm.DumpViewModelToModel());
        }
    }

    public AssetPack DumpViewModelToModel()
    {
        AssetPack model = new AssetPack();
        model.GroupName = GroupName;
        model.ShortName = ShortName;
        model.ConfigType = ConfigType;
        model.Gender = Gender;
        model.DisplayAlerts = DisplayAlerts;
        model.UserAlert = UserAlert;

        if (TrackedBodyGenConfig != null)
        {
            model.AssociatedBodyGenConfigName = TrackedBodyGenConfig.Label;
        }

        model.DefaultRecordTemplate = DefaultTemplateFK;
        model.AdditionalRecordTemplateAssignments = AdditionalRecordTemplateAssignments.Select(x => VM_AdditionalRecordTemplate.DumpViewModelToModel(x)).ToHashSet();
        model.DefaultRecordTemplateAdditionalRacesPaths = DefaultRecordTemplateAdditionalRacesPaths.Select(x => x.Content).ToHashSet();

        VM_AttributeGroupMenu.DumpViewModelToModels(AttributeGroupMenu, model.AttributeGroups);

        model.RaceGroupings = RaceGroupingEditor.DumpToModel();

        MiscMenu.MergeIntoModel(model);

        foreach (var svm in Subgroups)
        {
            model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
        }

        model.ReplacerGroups = VM_AssetPackDirectReplacerMenu.DumpViewModelToModels(ReplacersMenu);

        model.DistributionRules = VM_ConfigDistributionRules.DumpViewModelToModel(DistributionRules);

        model.FilePath = SourcePath;

        return model;
    }

    private bool SaveToModel(bool showToolBarNotification)
    {
        string savePath = _assetPackIO.SaveAssetPack(DumpViewModelToModel(), out bool success);
        if (success)
        {
            SourcePath = savePath;
        }

        if (showToolBarNotification)
        {
            if (success)
            {
                _logger.CallTimedNotifyStatusUpdateAsync(GroupName + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
            }
            else
            {
                _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be saved.", ErrorType.Error, 3);
            }
        }
        return success;
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

    public void MergeInAssetPack(string assetPackDirPath)
    {
        List<string> newSubgroupNames = new List<string>();

        if (IO_Aux.SelectFile(assetPackDirPath, "Config files (*.json)|*.json", "Select config file to merge in", out string path))
        {
            var newAssetPack = _assetPackIO.LoadAssetPack(path, _patcherState.GeneralSettings.RaceGroupings, _patcherState.RecordTemplatePlugins, _patcherState.BodyGenConfigs, out bool loadSuccess);
            if (loadSuccess)
            {
                var newAssetPackVM = _selfFactory();
                newAssetPackVM.CopyInViewModelFromModel(newAssetPack, _general.RaceGroupingEditor.RaceGroupings);
                    
                // first add completely new top-level subgroups if necessary
                foreach (var subgroup in newAssetPackVM.Subgroups)
                {
                    if (!Subgroups.Select(x => x .ID).Contains(subgroup.ID, StringComparer.OrdinalIgnoreCase))
                    {
                        var clone = subgroup.Clone() as VM_Subgroup;
                        clone.ParentAssetPack = this;
                        clone.ParentCollection = Subgroups;
                        Subgroups.Add(clone);
                        newSubgroupNames.Add(clone.ID + ": " + clone.Name);
                    }
                }

                // merge existing subgroups
                foreach (var subgroup in Subgroups)
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
        bool saveConfig = CustomMessageBox.DisplayNotificationYesNo("Save Config File?", "Save the config file before modifying destinations? (Recommended yes so you can use the Discard button to throw out incorrect changes).");
        if (saveConfig)
        {
            bool saved = SaveToModel(false);
            if (!saved)
            {
                CustomMessageBox.DisplayNotificationOK("Save Failure", "Config file could not be saved. Destination paths will not be modified.");
                return;
            }
        }

        List<string> modifications = new();

        foreach (var subgroup in Subgroups)
        {
            SetDefaultSubgroupFilePaths(subgroup, modifications);
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
                DuplicateBodyPathsAsFeet(subgroup, modifications);
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
                DuplicateBodyPathsAsTail(subgroup, modifications);
            }
        }

        if (modifications.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Summary", "The following modifications were made: " + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, modifications));
        }
        else
        {
            CustomMessageBox.DisplayNotificationOK("Summary", "No automatic modifications could be made based on the current Source and Destination paths.");
        }
    }

    public void DuplicateBodyPathsAsFeet(VM_Subgroup subgroup, List<string> modifications)
    {
        var newFeetPaths = new HashSet<VM_FilePathReplacement>();
        foreach (var path in subgroup.PathsMenu.Paths.Where(x => FilePathDestinationMap.MaleTorsoPaths.ContainsValue(x.IntellisensedPath) || FilePathDestinationMap.FemaleTorsoPaths.ContainsValue(x.IntellisensedPath)).ToArray())
        {
            var newPath = _filePathReplacementFactory(path.ParentMenu);
            newPath.Source = path.Source;
            newPath.IntellisensedPath = path.IntellisensedPath.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)");

            if (newPath.DestinationExists && !subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath == newPath.IntellisensedPath).Any())
            {
                newFeetPaths.Add(newPath);
                modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": Duplicated torso texture to feet: " + newPath.Source);
            }
        }

        foreach (var path in newFeetPaths)
        {
            subgroup.PathsMenu.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsFeet(sg, modifications);
        }
    }

    public void DuplicateBodyPathsAsTail(VM_Subgroup subgroup, List<string> modifications)
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

        foreach (var path in subgroup.PathsMenu.Paths.Where(x => pathsNeedingTails.Contains(Path.GetFileName(x.Source), StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            var newPath = _filePathReplacementFactory(path.ParentMenu);
            newPath.Source = path.Source;
            newPath.IntellisensedPath = path.IntellisensedPath.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)");

            if (newPath.DestinationExists && !subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath == newPath.IntellisensedPath).Any())
            {
                newTailPaths.Add(newPath);
                modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": Duplicated torso texture to tail: " + newPath.Source);
            }
        }

        foreach (var path in newTailPaths)
        {
            subgroup.PathsMenu.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsTail(sg, modifications);
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

    public static void SetDefaultSubgroupFilePaths(VM_Subgroup subgroup, List<string> modifications)
    {
        foreach (var path in subgroup.PathsMenu.Paths.Where(x => !string.IsNullOrWhiteSpace(x.Source)).ToArray())
        {
            var fileName = Path.GetFileName(path.Source);

            if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(fileName) && path.IntellisensedPath != FilePathDestinationMap.FileNameToDestMap[fileName])
            {
                var targetDestination = FilePathDestinationMap.FileNameToDestMap[fileName];
                var feetAlternateDestination = targetDestination.Replace("BipedObjectFlag.Body", "BipedObjectFlag.Feet");
                var tailAlternateDestination = targetDestination.Replace("BipedObjectFlag.Body", "BipedObjectFlag.Tail");

                // try assigning the default destination path if the subgroup doesn't already assign an asset to that path
                if (!subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath == targetDestination).Any())
                {
                    path.IntellisensedPath = FilePathDestinationMap.FileNameToDestMap[fileName];
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " --> " + path.IntellisensedPath);
                }

                else if (
                    (FilePathDestinationMap.MaleTorsoPaths.ContainsKey(fileName) || FilePathDestinationMap.FemaleTorsoPaths.ContainsKey(fileName))  && 
                    subgroup.PathsMenu.CandidateTargetPathExists(feetAlternateDestination) &&
                    !subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath == feetAlternateDestination).Any()
                    )
                {
                    path.IntellisensedPath = feetAlternateDestination;
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " (Duplicate) --> " + path.IntellisensedPath);
                }

                else if (
                    (FilePathDestinationMap.MaleTorsoPaths.ContainsKey(fileName) || FilePathDestinationMap.FemaleTorsoPaths.ContainsKey(fileName)) &&
                    subgroup.PathsMenu.CandidateTargetPathExists(tailAlternateDestination) &&
                    !subgroup.PathsMenu.Paths.Where(x => x.IntellisensedPath == tailAlternateDestination).Any()
                    )
                {
                    path.IntellisensedPath = tailAlternateDestination;
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " (Duplicate) --> " + path.IntellisensedPath);
                }
            }
        }

        foreach (var sg in subgroup.Subgroups)
        {
            SetDefaultSubgroupFilePaths(sg, modifications);
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

    public List<string> GetCustomRules()
    {
        List<string> rulesStrings = new();

        rulesStrings.AddRange(DistributionRules.GetRulesSummary());

        List<string> subgroupRules = new();
        foreach (var subgroup in Subgroups)
        {
            subgroupRules.AddRange(subgroup.GetRulesSummary());
        }
        if (subgroupRules.Any())
        {
            rulesStrings.Add("");
            rulesStrings.Add("Main Subgroups: ");
            rulesStrings.Add("");
            rulesStrings.AddRange(subgroupRules);
        }

        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            List<string> replacerRules = new();
            foreach (var subgroup in replacer.Subgroups)
            {
                replacerRules.AddRange(subgroup.GetRulesSummary());
            }

            if (replacerRules.Any())
            {
                rulesStrings.Add("");
                rulesStrings.Add("Direct Asset Replacers");
                rulesStrings.Add("");
                rulesStrings.AddRange(replacerRules);
            }
        }

        if (!rulesStrings.Any())
        {
            rulesStrings.Add("This config has no custom rules.");
        }

        return rulesStrings;
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

    public void AddFallBackRaceGroupings(AssetPack model, ObservableCollection<VM_RaceGrouping> existingGroupings, ObservableCollection<VM_RaceGrouping> fallBackGroupings)
    {
        HashSet<RaceGrouping> addedRaceGroups = new();

        HashSet<string> existingGroupNames = model.RaceGroupings.Select(x => x.Label).ToHashSet();
        HashSet<string> fallBackGroupNames = fallBackGroupings.Select(x => x.Label).ToHashSet();

        HashSet<string> groupingsToAdd = new();
        foreach (var subgroup in model.Subgroups)
        {
            subgroup.GetContainedRaceGroupingLabels(groupingsToAdd);
        }
        foreach (var replacer in model.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                subgroup.GetContainedRaceGroupingLabels(groupingsToAdd);
            }
        }

        foreach (string groupLabel in groupingsToAdd)
        {
            if (!existingGroupNames.Contains(groupLabel) && fallBackGroupNames.Contains(groupLabel))
            {
                existingGroupings.Add(fallBackGroupings.Where(x => x.Label == groupLabel).First());
            }
        }
    }

    private void DeleteAssetFiles()
    {
        HashSet<string> prefixes = new HashSet<string>();
        string modsFolderPath = "";
        if (_modManager.ModManagerType == ModManager.ModOrganizer2 && Directory.Exists(_modManager.MO2IntegrationVM.ModFolderPath))
        {
            modsFolderPath = _modManager.MO2IntegrationVM.ModFolderPath;
        }
        else if (_modManager.ModManagerType == ModManager.Vortex && Directory.Exists(_modManager.VortexIntegrationVM.StagingFolderPath))
        {
            modsFolderPath = _modManager.VortexIntegrationVM.StagingFolderPath;
        }

        if (!modsFolderPath.IsNullOrWhitespace())
        {
            GetPrefixes(prefixes);

            string currentModDir = "";
            foreach (var modDirectory in Directory.GetDirectories(modsFolderPath))
            {
                foreach (var subDirectory in Directory.GetDirectories(modDirectory))
                {
                    var candidatePrefixDirectories = Directory.GetDirectories(subDirectory).Select(x => new DirectoryInfo(x).Name).ToArray();
                    if(candidatePrefixDirectories.Where(x => prefixes.Contains(x)).Any())
                    {
                        currentModDir = modDirectory;
                    }
                }
            }

            if (!currentModDir.IsNullOrWhitespace())
            {
                if (CustomMessageBox.DisplayNotificationYesNo("", "Delete asset folder at " + currentModDir + "?"))
                {
                    _auxIO.TryDeleteDirectory(currentModDir, true);
                }
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("", "Could not find the Assets Folder for this config file in your mod manager. You will need to delete the installed asset files manually");
            }
        }

        else if (CustomMessageBox.DisplayNotificationYesNo("", "Delete this config file's assets in your data folder?"))
        {
            foreach (var subgroup in Subgroups)
            {
                DeleteSubgroupAssets(subgroup);
            }
            foreach (var replacer in ReplacersMenu.ReplacerGroups)
            {
                foreach (var subgroup in replacer.Subgroups)
                {
                    DeleteSubgroupAssets(subgroup);
                }
            }
        }
    }

    private void GetPrefixes(HashSet<string> prefixes)
    {
        foreach (var subgroup in Subgroups)
        {
            GetPrefixes(subgroup, prefixes);
        }
        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                GetPrefixes(subgroup, prefixes);
            }
        }
    }

    private void GetPrefixes(VM_Subgroup sg, HashSet<string> prefixes)
    {
        foreach (var ssg in sg.Subgroups)
        {
            GetPrefixes(ssg, prefixes);
        }
        foreach (var path in sg.PathsMenu.Paths)
        {
            string[] split = path.Source.Split(Path.DirectorySeparatorChar);
            if (split.Length >= 2 && !prefixes.Contains(split[1]))
            {
                prefixes.Add(split[1]);
            }
        }
    }

    private void DeleteSubgroupAssets(VM_Subgroup sg)
    {
        foreach (var ssg in sg.Subgroups)
        {
            DeleteSubgroupAssets(ssg);
        }
        foreach (var path in sg.PathsMenu.Paths)
        {
            if (path.Source == null) { continue; }
            string candidatePath = Path.Combine(_environmentProvider.DataFolderPath, path.Source);
            if(File.Exists(candidatePath))
            {
                _auxIO.TryDeleteFile(candidatePath);
            }

            var parentDir = Directory.GetParent(candidatePath);
            if (!Directory.EnumerateFileSystemEntries(parentDir.FullName).Any())
            {
                _auxIO.TryDeleteDirectory(parentDir.FullName, false);
            } 
        }
    }
}

public interface IHasSubgroupViewModels
{
    ObservableCollection<VM_Subgroup> Subgroups { get; }
}
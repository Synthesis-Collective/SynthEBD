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
using System.Diagnostics;
using static SynthEBD.FilePathDestinationMap;
using SynthEBD;
using Mutagen.Bethesda.WPF.Reflection.Fields;

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

[DebuggerDisplay("{ShortName}: {GroupName}")]
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
    private readonly VM_SubgroupPlaceHolder.Factory _subgroupPlaceHolderFactory;
    private readonly VM_ConfigDistributionRules.Factory _configDistributionRulesFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly AssetPackValidator _assetPackValidator;
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly IO_Aux _auxIO;
    private readonly FileDialogs _fileDialogs;
    private readonly VM_ConfigDrafter _configDrafter;
    private readonly Factory _selfFactory;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupMenuFactory;
    private readonly VM_RaceGroupingEditor.Factory _raceGroupingEditorFactory;
    private readonly VM_AdditionalRecordTemplate.Factory _additionalRecordTemplateFactory;
    private readonly VM_ConfigPathRemapper.Factory _pathRemapperFactory;

    public delegate VM_AssetPack Factory(AssetPack model);

    public VM_AssetPack(
        AssetPack model,
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
        VM_SubgroupPlaceHolder.Factory subgroupPlaceHolderFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_ConfigDistributionRules.Factory configDistributionRulesFactory,
        AssetPackValidator assetPackValidator,
        VM_AssetReplicateTextureRemover assetReplicateRemover,
        RecordPathParser recordPathParser,
        Logger logger,
        SynthEBDPaths paths,
        IO_Aux auxIO,
        FileDialogs fileDialogs,
        VM_ConfigDrafter configDrafter,
        SettingsIO_AssetPack assetPackIO,
        VM_AttributeGroupMenu.Factory attributeGroupMenuFactory,
        VM_RaceGroupingEditor.Factory raceGroupingEditorFactory,
        VM_AdditionalRecordTemplate.Factory additionalRecordTemplateFactory,
        VM_ConfigPathRemapper.Factory pathRemapperFactory,
        Factory selfFactory)
    {
        AssociatedModel = model;
        _environmentProvider = environmentProvider;
        _patcherState = state;
        _oBody = oBody;
        _general = general;
        _modManager = modManager;
        _bodyGenConfigFactory = bodyGenConfigFactory;
        _assetPackDirectReplacerMenuFactory = assetPackDirectReplacerMenuFactory;
        _miscMenuFactory = miscMenuFactory;
        _subgroupFactory = subgroupFactory;
        _subgroupPlaceHolderFactory = subgroupPlaceHolderFactory;
        _configDistributionRulesFactory = configDistributionRulesFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _assetPackValidator = assetPackValidator;
        _recordPathParser = recordPathParser;
        _logger = logger;
        _paths = paths;
        _auxIO = auxIO;
        _fileDialogs = fileDialogs;
        _configDrafter = configDrafter;
        _selfFactory = selfFactory;
        _assetPackIO = assetPackIO;
        _attributeGroupMenuFactory = attributeGroupMenuFactory;
        _raceGroupingEditorFactory = raceGroupingEditorFactory;
        _additionalRecordTemplateFactory = additionalRecordTemplateFactory;
        _pathRemapperFactory = pathRemapperFactory;

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

        this.WhenAnyValue(x => x.Gender).Skip(1).Subscribe(x => SetDefaultRecordTemplate()).DisposeWith(this); // Don't refresh until a model is loaded in or user changes gender in a new VM

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpViewModelToModel();
             }

             if (t.Current != null)
             {
                 DisplayedSubgroup = _subgroupFactory(t.Current, this, false);
                 DisplayedSubgroup.CopyInViewModelFromModel();
                 t.Current.GetDDSPaths();
             }
         }).DisposeWith(this);

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.SubgroupSearchText),
                this.WhenAnyValue(x => x.SubgroupSearchCaseSensitive),
                (searchText, caseSensitive) => { return (searchText, caseSensitive); })
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Subscribe(y => CheckSubgroupVisibility(y.searchText, y.caseSensitive))
            .DisposeWith(this);

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
            execute: _ =>
            {
                var newSubgroup = new AssetPack.Subgroup();
                var newPlaceHolder = _subgroupPlaceHolderFactory(newSubgroup, null, this, Subgroups);
                newPlaceHolder.AutoGenerateID(false, 0);
                Subgroups.Add(newPlaceHolder);
            });

        RemoveAssetPackConfigFile = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (_fileDialogs.ConfirmFileDeletion(SourcePath, "Asset Pack Config File"))
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
                    MessageWindow.DisplayNotificationOK("Validation", "No errors found.");
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
                MessageWindow.DisplayNotificationOK("Disabled Subgroups", string.Join(Environment.NewLine, disabledSubgroups));
            }
        );

        ListCustomRulesButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var rules = GetCustomRules();
                MessageWindow.DisplayNotificationOK("Custom Rules", string.Join(Environment.NewLine, rules));
            }
        );

        ImportTexturesButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _configDrafter.InitializeTo(this);
                var drafterWindow = new Window_ConfigDrafter();
                drafterWindow.DataContext = _configDrafter;
                drafterWindow.ShowDialog();
                if (_configDrafter.HasEtcTextures)
                {
                    switch (_configDrafter.SelectedBodyType)
                    {
                        case DrafterBodyType.CBBE_3BA:
                            ApplyCustomRecordTemplate("000801:Record Templates - 3BA - pamonha.esp", "000803:Record Templates - 3BA - pamonha.esp", "000805:Record Templates - 3BA - pamonha.esp", Gender.Female, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault);
                            break;
                        case DrafterBodyType.BHUNP:
                            ApplyCustomRecordTemplate("000801:Record Templates - BHUNP - pamonha.esp", "000803:Record Templates - BHUNP - pamonha.esp", "000805:Record Templates - BHUNP - pamonha.esp", Gender.Female, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault);
                            break;
                    }
                }
                else if (_configDrafter.HasTNGTextures)
                {
                    ApplyCustomRecordTemplate("000800:Record Templates - The New Gentleman.esp", "000802:Record Templates - The New Gentleman.esp", "000804:Record Templates - The New Gentleman.esp", Gender.Male, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault.And(VM_AdditionalRecordTemplate.AdditionalRacesPathsTNG));
                }
            }
        );

        RemapTexturesButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var remapperWindow = new Window_ConfigPathRemapper();
                var remapper = _pathRemapperFactory(this, remapperWindow);
                remapperWindow.DataContext = remapper;
                remapperWindow.ShowDialog();
            });

        DiscardButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var reloaded = _assetPackIO.LoadAssetPack(SourcePath, _patcherState.GeneralSettings.RaceGroupings, state.RecordTemplatePlugins, state.BodyGenConfigs, out bool success);
                if (!success)
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be reloaded from drive.", ErrorType.Error, 3);
                    return;
                }

                var reloadedVM = _selfFactory(reloaded);
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
                var copiedVM = selfFactory(copiedModel);
                copiedVM.CopyInViewModelFromModel(copiedModel, _general.RaceGroupingEditor.RaceGroupings);
                texMesh.AssetPacks.Add(copiedVM);
                texMesh.AssetPresenterPrimary.AssetPack = copiedVM;
            }
        );

        RemoveDuplicatesButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                Window_AssetReplicateTextureRemover replicatesWindow = new();
                replicatesWindow.DataContext = assetReplicateRemover;
                assetReplicateRemover.Initialize(this);
                bool needsReload = false;
                if (SelectedPlaceHolder != null && SelectedPlaceHolder.AssociatedViewModel != null)
                {
                    SelectedPlaceHolder.AssociatedViewModel.DumpViewModelToModel();
                    needsReload = true;
                }
                replicatesWindow.ShowDialog();
                if (needsReload && SelectedPlaceHolder != null && SelectedPlaceHolder.AssociatedViewModel != null)
                {
                    SelectedPlaceHolder.AssociatedViewModel.CopyInViewModelFromModel();
                }
            }
        );

        ClearBodyGenButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ClearBodyGen()
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

    public AssetPack AssociatedModel { get; }
    public static string _defaultGroupName = "New Asset Pack";
    public string GroupName { get; set; } = _defaultGroupName;
    public static string _defaultPrefix = "NAP";
    public string ShortName { get; set; } = _defaultPrefix;
    public AssetPackType ConfigType { get; set; } = AssetPackType.Primary;
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; set; } = new();
    public string SubgroupSearchText { get; set; }
    public bool SubgroupSearchCaseSensitive { get; set; } = false;
    public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
    public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
    public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }
    public ObservableCollection<VM_CollectionMemberString> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public bool IsSelected { get; set; } = true;
    public string SourcePath { get; set; } = "";
    public string InstallationToken { get; set; } = "";
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public FormKey DefaultTemplateFK { get; set; } = new();
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public IEnumerable<FormKey> AllReferenceNPCs => new FormKey[] { DefaultTemplateFK }.And(AdditionalRecordTemplateAssignments.Select(x=> x.TemplateNPC));
    public VM_AssetPackDirectReplacerMenu ReplacersMenu { get; set; }
    public VM_ConfigDistributionRules DistributionRules { get; set; }
    public VM_AssetPackMiscMenu MiscMenu { get; set; }
    public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }
    public VM_Subgroup DisplayedSubgroup { get; set; }
    public VM_SubgroupPlaceHolder SelectedPlaceHolder { get; set; }
    public RelayCommand RemoveAssetPackConfigFile { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand AddAdditionalRecordTemplateAssignment { get; }
    public RelayCommand AddRecordTemplateAdditionalRacesPath { get; }
    public RelayCommand MergeWithAssetPack { get; }
    public RelayCommand ValidateButton { get; }
    public RelayCommand ListDisabledSubgroupsButton { get; }
    public RelayCommand ListCustomRulesButton { get; }
    public RelayCommand SaveButton { get; }
    public RelayCommand ImportTexturesButton { get; }
    public RelayCommand RemapTexturesButton { get; }
    public RelayCommand DiscardButton { get; }
    public RelayCommand CopyButton { get; }
    public RelayCommand RemoveDuplicatesButton { get; }
    public RelayCommand SetDefaultTargetDestPaths { get; }
    public RelayCommand ClearBodyGenButton { get; }
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
            var viewModel = assetPackFactory(assetPacks[i]);
            viewModel.CopyInViewModelFromModel(assetPacks[i], mainRaceGroupings);
            viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);
            texMesh.AssetPacks.Add(viewModel);
        }
    }
    
    public void CopyInViewModelFromModel(AssetPack model, ObservableCollection<VM_RaceGrouping> mainRaceGroupings)
    {
        GroupName = model.GroupName;
        ShortName = model.ShortName;
        ConfigType = model.ConfigType;
        DisplayAlerts = model.DisplayAlerts;
        UserAlert = model.UserAlert;

        if (model.AssociatedBodyGenConfigName != "")
        {
            switch(model.Gender) // use the model's gender because the VM's gender is intentionally set last to simplify subscriptions.
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
        foreach (var sg in model.Subgroups)
        {
            Subgroups.Add(_subgroupPlaceHolderFactory(sg, null, this, Subgroups));
        }
        DistributionRules = _configDistributionRulesFactory(RaceGroupingEditor.RaceGroupings, this);
        DistributionRules.CopyInViewModelFromModel(model.DistributionRules, RaceGroupingEditor.RaceGroupings, this);

        InstallationToken = model.InstallationToken;
        SourcePath = model.FilePath;

        Gender = model.Gender; // setting Gender triggers a refresh of the VM's record templates, so only do this after the model's record templates are loaded to avoid adding duplicates to the list (avoids having to do another duplicate check here).
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

        if (DisplayedSubgroup != null)
        {
            DisplayedSubgroup.AssociatedPlaceHolder.AssociatedModel = DisplayedSubgroup.DumpViewModelToModel();
        }

        foreach (var svm in Subgroups)
        {
            svm.SaveToModel();
            model.Subgroups.Add(svm.AssociatedModel);
        }

        model.ReplacerGroups = VM_AssetPackDirectReplacerMenu.DumpViewModelToModels(ReplacersMenu);

        model.DistributionRules = DistributionRules.DumpViewModelToModel();

        model.InstallationToken = InstallationToken;
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
                _logger.CallTimedNotifyStatusUpdateAsync(GroupName + " Saved.", 2, CommonColors.Yellow);
            }
            else
            {
                _logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be saved.", ErrorType.Error, 3);
            }
        }
        return success;
    }

    // For UI Support
    public bool TryGetSubgroupByID(string ID, out VM_SubgroupPlaceHolder subgroup)
    {
        subgroup = VM_SubgroupPlaceHolder.GetSubgroupByID(Subgroups, ID);
        return subgroup != null;
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
        foreach (var subgroup in Subgroups)
        {
            subgroup.AutoGenerateSubgroupIDs();
        }
        foreach (var subgroupVM in Subgroups)
        {
            subgroupVM.Refresh();
        }
        
        if (DisplayedSubgroup.AssociatedPlaceHolder != null)
        {
            DisplayedSubgroup.ID = DisplayedSubgroup.AssociatedPlaceHolder.ID;
        }
    }

    public void RemoveAssetPackDialog()
    {
        bool result = MessageWindow.DisplayNotificationYesNo("Confirm Deletion", "Are you sure you want to permanently delete this config file?");
            
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
                var newAssetPackVM = _selfFactory(newAssetPack);
                newAssetPackVM.CopyInViewModelFromModel(newAssetPack, _general.RaceGroupingEditor.RaceGroupings);
                    
                // first add completely new top-level subgroups if necessary
                foreach (var subgroup in newAssetPackVM.Subgroups)
                {
                    if (!Subgroups.Select(x => x .ID).Contains(subgroup.ID, StringComparer.OrdinalIgnoreCase))
                    {
                        var clone = subgroup.Clone() as VM_SubgroupPlaceHolder;
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
                    MessageWindow.DisplayNotificationOK("Config Merger", "The following subgroups were imported:" + Environment.NewLine + string.Join(Environment.NewLine, newSubgroupNames));
                }
            }
            else
            {
                MessageWindow.DisplayNotificationOK("Config Merger", "That file could not be parsed as a valid Asset Config Plugin File.");
            }
        }
    }

    public static void MergeSubgroupLists(ObservableCollection<VM_SubgroupPlaceHolder> ListA, ObservableCollection<VM_SubgroupPlaceHolder> ListB, VM_AssetPack parentAssetPack, List<string> newSubgroupNames)
    {
        foreach (VM_SubgroupPlaceHolder candidateSubgroup in ListB)
        {
            var matchedSubgroup = ListA.Where(x => x.ID == candidateSubgroup.ID).FirstOrDefault();
            if (matchedSubgroup is null)
            {
                var clone = candidateSubgroup.Clone(parentAssetPack, ListA);
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
                UpdateDefaultRecordTemplate("DefaultMale", Gender.Male);
                UpdateDefaultRecordTemplateBeast("KhajiitMale", Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRaceVampire.FormKey }, Gender.Male);
                UpdateDefaultRecordTemplateBeast("ArgonianMale", Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRaceVampire.FormKey }, Gender.Male);
                RemovePreviousGenderRecordTemplates(Gender.Female);
                break;
            case Gender.Female:
                UpdateDefaultRecordTemplate("DefaultFemale", Gender.Female);
                UpdateDefaultRecordTemplateBeast("KhajiitFemale", Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRaceVampire.FormKey }, Gender.Female);
                UpdateDefaultRecordTemplateBeast("ArgonianFemale", Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRaceVampire.FormKey }, Gender.Female);
                RemovePreviousGenderRecordTemplates(Gender.Male);
                break;
        }

        foreach (var additionalRacesPath in VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault)
        {
            if (!DefaultRecordTemplateAdditionalRacesPaths.Select(x => x.Content).Contains(additionalRacesPath))
            {
                DefaultRecordTemplateAdditionalRacesPaths.Add(new(additionalRacesPath, DefaultRecordTemplateAdditionalRacesPaths));
            }
        }
    }

    public void UpdateDefaultRecordTemplate(string defaultTemplateEditorID, Gender gender)
    {
        if (RecordTemplateLinkCache.TryResolve<INpcGetter>(defaultTemplateEditorID, out var defaultMaleRec))
        {
            if (DefaultTemplateFK == null || DefaultTemplateFK.IsNull || (RecordTemplateLinkCache.TryResolve<INpcGetter>(DefaultTemplateFK, out var defaultTemplate) && NPCInfo.GetGender(defaultTemplate) != gender))
            {
                DefaultTemplateFK = defaultMaleRec.FormKey;
            }
        }
    }

    public void UpdateDefaultRecordTemplateBeast(string defaultTemplateEditorID, FormKey defaultTemplateRaceFormKey, List<FormKey> raceFormKeys, Gender gender)
    {
        if (RecordTemplateLinkCache.TryResolve<INpcGetter>(defaultTemplateEditorID, out var defaultBeastRecordTemplate))
        {
            if (!AdditionalRecordTemplateAssignments.Where(x =>
                RecordTemplateLinkCache.TryResolve<INpcGetter>(x.TemplateNPC, out var templateNPCGetter) &&
                NPCInfo.GetGender(templateNPCGetter) == gender &&
                templateNPCGetter.Race.FormKey.Equals(defaultTemplateRaceFormKey)).Any())
            {
                var additionalBeast = _additionalRecordTemplateFactory(RecordTemplateLinkCache, AdditionalRecordTemplateAssignments);
                additionalBeast.TemplateNPC = defaultBeastRecordTemplate.FormKey;
                Noggog.ListExt.AddRange(additionalBeast.RaceFormKeys, raceFormKeys);
                foreach (var additionalRacesPath in VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault.And(VM_AdditionalRecordTemplate.AdditionalRacesPathsBeast))
                {
                    additionalBeast.AdditionalRacesPaths.Add(new(additionalRacesPath, additionalBeast.AdditionalRacesPaths));
                }
                AdditionalRecordTemplateAssignments.Add(additionalBeast);
            }
        }        
    }

    public void RemovePreviousGenderRecordTemplates(Gender previousGender)
    {
        var wrongGenderTemplates = AdditionalRecordTemplateAssignments.Where(x =>
                RecordTemplateLinkCache.TryResolve<INpcGetter>(x.TemplateNPC, out var templateNPCGetter) &&
                NPCInfo.GetGender(templateNPCGetter) == previousGender).ToArray();

        foreach (var template in wrongGenderTemplates)
        {
            AdditionalRecordTemplateAssignments.Remove(template);
        }
    }

    public void SetDefaultTargetPaths()
    {
        bool saveConfig = MessageWindow.DisplayNotificationYesNo("Save Config File?", "Save the config file before modifying destinations? (Recommended yes so you can use the Discard button to throw out incorrect changes).");
        if (saveConfig)
        {
            bool saved = SaveToModel(false);
            if (!saved)
            {
                MessageWindow.DisplayNotificationOK("Save Failure", "Config file could not be saved. Destination paths will not be modified.");
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
            MessageWindow.DisplayNotificationOK("Summary", "The following modifications were made: " + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, modifications));
        }
        else
        {
            MessageWindow.DisplayNotificationOK("Summary", "No automatic modifications could be made based on the current Source and Destination paths.");
        }
    }

    public void DuplicateBodyPathsAsFeet(VM_SubgroupPlaceHolder subgroup, List<string> modifications)
    {
        var newFeetPaths = new HashSet<FilePathReplacement>();
        foreach (var path in subgroup.AssociatedModel.Paths.Where(x => FilePathDestinationMap.MaleTorsoPaths.ContainsValue(x.Destination) || FilePathDestinationMap.FemaleTorsoPaths.ContainsValue(x.Destination)).ToArray())
        {
            var newPath = new FilePathReplacement();
            newPath.Source = path.Source;
            newPath.Destination = path.Destination.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)");
            
            bool newDestinationExists = VM_FilePathReplacement.DestinationPathExists(newPath.Destination, RecordTemplateLinkCache, AllReferenceNPCs, _recordPathParser, _logger);

            if (newDestinationExists && !subgroup.AssociatedModel.Paths.Where(x => x.Destination == newPath.Destination).Any())
            {
                newFeetPaths.Add(newPath);
                modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": Duplicated torso texture to feet: " + newPath.Source);
            }
        }

        foreach (var path in newFeetPaths)
        {
            subgroup.AssociatedModel.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsFeet(sg, modifications);
        }
    }

    public void DuplicateBodyPathsAsTail(VM_SubgroupPlaceHolder subgroup, List<string> modifications)
    {
        var newTailPaths = new HashSet<FilePathReplacement>();
        var pathsNeedingTails = new HashSet<string>()
        {
            //male khajiit
            Source_TorsoDiffuseKhajiitMale,
            Source_TorsoNormalKhajiitMale,
            Source_TorsoSpecularKhajiitMale,
            //male argonian
            Source_TorsoDiffuseArgonianMale,
            Source_TorsoNormalArgonianMale,
            Source_TorsoSpecularArgonianMale,
            //female khajiit
            Source_TorsoDiffuseKhajiitFemale,
            Source_TorsoNormalKhajiitFemale,
            Source_TorsoSpecularKhajiitFemale,
            //female argonian
            Source_TorsoDiffuseArgonianFemale,
            Source_TorsoNormalArgonianFemale,
            Source_TorsoSpecularArgonianFemale,
        };

        foreach (var path in subgroup.AssociatedModel.Paths.Where(x => pathsNeedingTails.Contains(Path.GetFileName(x.Source), StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            var newPath = new FilePathReplacement();
            newPath.Source = path.Source;
            newPath.Destination = path.Destination.Replace("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)");

            bool newDestinationExists = VM_FilePathReplacement.DestinationPathExists(newPath.Destination, RecordTemplateLinkCache, AllReferenceNPCs, _recordPathParser, _logger);

            if (newDestinationExists && !subgroup.AssociatedModel.Paths.Where(x => x.Destination == newPath.Destination).Any())
            {
                newTailPaths.Add(newPath);
                modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": Duplicated torso texture to tail: " + newPath.Source);
            }
        }

        foreach (var path in newTailPaths)
        {
            subgroup.AssociatedModel.Paths.Add(path);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DuplicateBodyPathsAsTail(sg, modifications);
        }
    }

    public static bool SubgroupHasDestinationPath(VM_SubgroupPlaceHolder subgroup, string destinationPath)
    {
        if (subgroup.AssociatedModel.Paths.Where(x => x.Destination.Contains(destinationPath)).Any())
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

    public void SetDefaultSubgroupFilePaths(VM_SubgroupPlaceHolder subgroup, List<string> modifications)
    {
        foreach (var path in subgroup.AssociatedModel.Paths.Where(x => !string.IsNullOrWhiteSpace(x.Source)).ToArray())
        {
            var fileName = Path.GetFileName(path.Source);

            if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(fileName) && path.Destination != FilePathDestinationMap.FileNameToDestMap[fileName])
            {
                var targetDestination = FilePathDestinationMap.FileNameToDestMap[fileName];
                var feetAlternateDestination = targetDestination.Replace("BipedObjectFlag.Body", "BipedObjectFlag.Feet");
                var tailAlternateDestination = targetDestination.Replace("BipedObjectFlag.Body", "BipedObjectFlag.Tail");

                // try assigning the default destination path if the subgroup doesn't already assign an asset to that path
                if (!subgroup.AssociatedModel.Paths.Where(x => x.Destination == targetDestination).Any())
                {
                    path.Destination = FilePathDestinationMap.FileNameToDestMap[fileName];
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " --> " + path.Destination);
                }

                else if (
                    (FilePathDestinationMap.MaleTorsoPaths.ContainsKey(fileName) || FilePathDestinationMap.FemaleTorsoPaths.ContainsKey(fileName))  && 
                    CandidateTargetPathExists(feetAlternateDestination) &&
                    !subgroup.AssociatedModel.Paths.Where(x => x.Destination == feetAlternateDestination).Any()
                    )
                {
                    path.Destination = feetAlternateDestination;
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " (Duplicate) --> " + path.Destination);
                }

                else if (
                    (FilePathDestinationMap.MaleTorsoPaths.ContainsKey(fileName) || FilePathDestinationMap.FemaleTorsoPaths.ContainsKey(fileName)) &&
                    CandidateTargetPathExists(tailAlternateDestination) &&
                    !subgroup.AssociatedModel.Paths.Where(x => x.Destination == tailAlternateDestination).Any()
                    )
                {
                    path.Destination = tailAlternateDestination;
                    modifications.Add(Logger.GetSubgroupIDString(subgroup) + ": " + path.Source + " (Duplicate) --> " + path.Destination);
                }
            }
        }

        foreach (var sg in subgroup.Subgroups)
        {
            SetDefaultSubgroupFilePaths(sg, modifications);
        }
    }

    public bool CandidateTargetPathExists(string candidate)
    {
        List<FormKey> candidateRecordTemplates = new();
        if (DefaultTemplateFK != null)
        {
            candidateRecordTemplates.Add(DefaultTemplateFK);
        }
        candidateRecordTemplates.AddRange(AdditionalRecordTemplateAssignments.Where(x => x.TemplateNPC != null).Select(x => x.TemplateNPC).ToArray());

        foreach (var referenceNPCformkey in candidateRecordTemplates)
        {
            if (RecordTemplateLinkCache != null && referenceNPCformkey != null && RecordTemplateLinkCache.TryResolve<INpcGetter>(referenceNPCformkey, out var refNPC) && _recordPathParser.GetObjectAtPath(refNPC, refNPC, candidate, new Dictionary<string, dynamic>(), RecordTemplateLinkCache, true, _logger.GetNPCLogNameString(refNPC), out var objAtPath) && objAtPath is not null && objAtPath.GetType() == typeof(string))
            {
                return true;
            }
        }
        return false;
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
        if (dropInfo.Data is VM_SubgroupPlaceHolder)
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
        if (dropInfo.Data is VM_SubgroupPlaceHolder)
        {
            var draggedSubgroup = (VM_SubgroupPlaceHolder)dropInfo.Data;
            if (dropInfo.TargetItem is VM_SubgroupPlaceHolder)
            {
                VM_SubgroupPlaceHolder dropTarget = (VM_SubgroupPlaceHolder)dropInfo.TargetItem;

                if (draggedSubgroup.IsParentOf(dropTarget)) { return; } // prevent mis-click when user releases the click on treeview expander arrow slightly below where they initiated the click, simulating a drop into or before the child node and causing the parent to disappear into the abyss.

                var clone = draggedSubgroup.Clone(dropTarget.ParentAssetPack, dropTarget.Subgroups);
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
                if ((targetTV.Name == "TVsubgroups" || targetTV.Name == "ReplacerTV") && dropTarget != null)
                {
                    var clone = draggedSubgroup.Clone(dropTarget, dropTarget.Subgroups);
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

        CheckSubgroupVisibility(SubgroupSearchText, SubgroupSearchCaseSensitive);
    }

    public bool DropInitiatedRightClick { get; set; }

    public bool VersionUpdate(Version version, UpdateMode updateAction)
    {
        foreach (var subgroup in Subgroups)
        {
            var bUpdate = subgroup.VersionUpdate(version, updateAction);
            if (bUpdate && updateAction == UpdateMode.Check)
            {
                return true;
            }
        }

        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                var bUpdate = subgroup.VersionUpdate(version, updateAction);
                if (bUpdate && updateAction == UpdateMode.Check)
                {
                    return true;
                }
            }
        }
        return false;
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
        string currentModDir = "";
        string dispMessage = "";

        if (_modManager.ModManagerType == ModManager.ModOrganizer2 && Directory.Exists(_modManager.MO2IntegrationVM.ModFolderPath))
        {
            modsFolderPath = _modManager.MO2IntegrationVM.ModFolderPath;
        }
        else if (_modManager.ModManagerType == ModManager.Vortex && Directory.Exists(_modManager.VortexIntegrationVM.StagingFolderPath))
        {
            modsFolderPath = _modManager.VortexIntegrationVM.StagingFolderPath;
        }

        // new asset deletion strategy (v1.0.1.9 or newer, using matched tokens)
        if (!InstallationToken.IsNullOrWhitespace())
        {
            // delete prefix folders if no mod manager used
            if (_modManager.ModManagerType == ModManager.None)
            {
                List<string> deletePrefixPaths = new();
                List<string> keepPrefixPaths = new();
                foreach (var dataSubDir in Directory.GetDirectories(_environmentProvider.DataFolderPath))
                {
                    foreach (var secondSubDir in Directory.GetDirectories(dataSubDir)) // expected to be the prefix directory
                    {
                        var tokenFile = Path.Combine(secondSubDir, ConfigInstaller.SynthEBDInstallationTokenFileName);
                        if (File.Exists(tokenFile))
                        {
                            try
                            {
                                var installationTokens = JSONhandler<List<string>>.LoadJSONFile(tokenFile, out bool readSuccess, out _);
                                if (readSuccess && installationTokens.Contains(InstallationToken))
                                {
                                    if (installationTokens.Count == 1)
                                    {
                                        deletePrefixPaths.Add(secondSubDir);
                                    }
                                    else
                                    {
                                        foreach (string token in installationTokens.Where(x => x != InstallationToken && x.Contains("|")))
                                        {
                                            keepPrefixPaths.Add(token.Split('|').First() + ':' + secondSubDir);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                }

                if (keepPrefixPaths.Any())
                {
                    dispMessage += "The following asset directories are being used by other config files and will not be deleted:" + Environment.NewLine + String.Join(Environment.NewLine, keepPrefixPaths);
                }

                if (deletePrefixPaths.Any())
                {
                    if (deletePrefixPaths.Count == 1)
                    {
                        dispMessage += "Delete asset folder at " + deletePrefixPaths.First() + "?";
                    }
                    else
                    {
                        dispMessage += "Asset folders found at: " + Environment.NewLine + String.Join(Environment.NewLine, deletePrefixPaths) + Environment.NewLine + "Delete these folders?";
                    }

                    if (MessageWindow.DisplayNotificationYesNo("", dispMessage))
                    {
                        foreach (var directory in deletePrefixPaths)
                        {
                            _auxIO.TryDeleteDirectory(directory, true);
                        }
                    }
                }
            }

            // delete mod folder if a mod manager is used
            else
            {
                foreach (var modDirectory in Directory.GetDirectories(modsFolderPath))
                {
                    var tokenFile = Path.Combine(modDirectory, ConfigInstaller.SynthEBDInstallationTokenFileName);
                    if (File.Exists(tokenFile))
                    {
                        try
                        {
                            var installationTokens = JSONhandler<List<string>>.LoadJSONFile(tokenFile, out bool readSuccess, out _);
                            if (readSuccess && installationTokens.Contains(InstallationToken))
                            {
                                currentModDir = modDirectory;
                                if (installationTokens.Count == 1)
                                {
                                    if (MessageWindow.DisplayNotificationYesNo("", "Delete asset folder at " + currentModDir + "?"))
                                    {
                                        _auxIO.TryDeleteDirectory(currentModDir, true);
                                    }
                                }
                                else
                                {
                                    dispMessage = "The mod folder referenced by this config file is also referenced by the following other config files:" + Environment.NewLine + Environment.NewLine;
                                    foreach (string token in installationTokens.Where(x => x != InstallationToken && x.Contains("|")))
                                    {
                                        dispMessage += Environment.NewLine + token.Split('|').First();
                                    }
                                }
                                break;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
        }

        // original asset deletion strategy
        else if (!modsFolderPath.IsNullOrWhitespace())
        {
            List<string> candidateAssetDirs = new();
            GetAssetPackPrefixes(prefixes);

            foreach (var modDirectory in Directory.GetDirectories(modsFolderPath))
            {
                foreach (var subDirectory in Directory.GetDirectories(modDirectory))
                {
                    var candidatePrefixDirectories = Directory.GetDirectories(subDirectory).Select(x => new DirectoryInfo(x).Name).ToArray();
                    if (candidatePrefixDirectories.Where(x => prefixes.Contains(x)).Any())
                    {
                        candidateAssetDirs.Add(modDirectory);
                    }
                }
            }

            if (candidateAssetDirs.Count == 1)
            {
                currentModDir = candidateAssetDirs.First();
                dispMessage = "This config file was installed on a version of SynthEBD < 1.0.1.9 (or was generated using the Config Drafter), so there is no record of where assets were installed. Based on the paths in this config file, SynthEBD predicts they are in " + currentModDir + ". Do you want to delete this folder?";
                if (MessageWindow.DisplayNotificationYesNo("PLEASE READ CAREFULLY", dispMessage))
                {
                    _auxIO.TryDeleteDirectory(currentModDir, true);
                }
            }
            else if (candidateAssetDirs.Count > 1)
            {
                dispMessage = "SynthEBD could not determine which of the following mod folders corresponds to this config file. If you want to delete the corresponding assets, you will need to do it manually from your mod manager." + Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine, candidateAssetDirs);
                MessageWindow.DisplayNotificationOK("", dispMessage);
            }
            else
            {
                MessageWindow.DisplayNotificationOK("", "Could not find the Assets Folder for this config file in your mod manager. If you want to delete the corresponding assets, you will need to do it manually from your mod manager.");
            }
        }

        // no mod manager - delete from data folder
        else
        {
            var containedPaths = GetContainedFileRelativePaths()
                .Where(x => IsValidSynthEBDInstalledAsset(x))
                .Select(x => Path.Combine(_environmentProvider.DataFolderPath, x))
                .Distinct()
                .ToList();

            dispMessage = "The following assets are detected to be associated with this config file. Please read the following list carefully. If all files are from SynthEBD, press Yes to delete. If any of them are native game files, please press No and delete the SynthEBD files manually" + Environment.NewLine + Environment.NewLine;
            dispMessage += string.Join(Environment.NewLine, containedPaths);

            if (containedPaths.Any() && MessageWindow.DisplayNotificationYesNo("PLEASE READ CAREFULLY", dispMessage))
            {
                foreach (var path in containedPaths)
                {
                    _auxIO.TryDeleteFile(path);
                    var dir = Path.GetDirectoryName(path);
                    if (dir != null)
                    {
                        _auxIO.DeleteDirectoryChainIfEmpty(dir);
                    }
                }
            }
                
        }
    }

    private void GetAssetPackPrefixes(HashSet<string> prefixes)
    {
        foreach (var subgroup in Subgroups)
        {
            GetSubgroupPrefixes(subgroup, prefixes);
        }
        foreach (var replacer in ReplacersMenu.ReplacerGroups)
        {
            foreach (var subgroup in replacer.Subgroups)
            {
                GetSubgroupPrefixes(subgroup, prefixes);
            }
        }
    }

    private void GetSubgroupPrefixes(VM_SubgroupPlaceHolder sg, HashSet<string> prefixes)
    {
        foreach (var ssg in sg.Subgroups)
        {
            GetSubgroupPrefixes(ssg, prefixes);
        }
        foreach (var path in sg.AssociatedModel.Paths)
        {
            string[] split = path.Source.Split(Path.DirectorySeparatorChar);
            if (IsValidSynthEBDInstalledAsset(path.Source))
            {
                prefixes.Add(split[1]);
            }
        }
    }

    private bool IsValidSynthEBDInstalledAsset(string path)
    {
        string[] split = path.Split(Path.DirectorySeparatorChar);
        return split.Length >= 2 &&
            !split[0].EndsWith(".esm", StringComparison.OrdinalIgnoreCase) && // don't try to get a prefix for a file coming from a .bsa (referenced as the first part of the filename ending with 'bsaname.esm')
            !split[1].Equals("actors", StringComparison.OrdinalIgnoreCase); // try to get rid of file paths that are pointing at default (non-modded) file paths. This is hard to do but most often in the case of SynthEBD it would be textures\actors or meshes\actors
    }

    private List<string> GetContainedFileRelativePaths()
    {
        List<string> paths = new();
        foreach (var subgroup in Subgroups)
        {
            paths.AddRange(subgroup.GetContainedAssetRelativePaths());
        }
        return paths;
    }

    public void DeleteMissingDescriptors()
    {
        foreach (var subgroup in Subgroups)
        {
            DeletedMissingDescriptors(subgroup.AssociatedModel, _patcherState.OBodySettings, TrackedBodyGenConfig?.DumpViewModelToModel() ?? new BodyGenConfig());
        }
        if (DisplayedSubgroup != null)
        {
            DisplayedSubgroup.CopyInViewModelFromModel();
        }

        _logger.CallTimedNotifyStatusUpdateAsync("Deleted Missing Descriptors", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
    }

    private static void DeletedMissingDescriptors(Subgroup subgroup, Settings_OBody oBodySettings, BodyGenConfig? bodyGenConfig)
    {
        var allowedDescriptorsBG = subgroup.AllowedBodyGenDescriptors.ToList();
        for (int i = 0; i < allowedDescriptorsBG.Count; i++)
        {
            var descriptor = allowedDescriptorsBG[i];
            if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
            {
                subgroup.AllowedBodyGenDescriptors.Remove(descriptor);
                allowedDescriptorsBG.RemoveAt(i);
                i--;
            }
        }

        var disallowedDescriptorsBG = subgroup.DisallowedBodyGenDescriptors.ToList();
        for (int i = 0; i < disallowedDescriptorsBG.Count; i++)
        {
            var descriptor = disallowedDescriptorsBG[i];
            if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
            {
                subgroup.DisallowedBodyGenDescriptors.Remove(descriptor);
                disallowedDescriptorsBG.RemoveAt(i);
                i--;
            }
        }

        var allowedDescriptorsBS = subgroup.AllowedBodySlideDescriptors.ToList();
        for (int i = 0; i < allowedDescriptorsBS.Count; i++)
        {
            var descriptor = allowedDescriptorsBS[i];
            if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors))
            {
                subgroup.AllowedBodySlideDescriptors.Remove(descriptor);
                allowedDescriptorsBS.RemoveAt(i);
                i--;
            }
        }

        var disallowedDescriptorsBS = subgroup.DisallowedBodySlideDescriptors.ToList();
        for (int i = 0; i < disallowedDescriptorsBS.Count; i++)
        {
            var descriptor = disallowedDescriptorsBS[i];
            if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors))
            {
                subgroup.DisallowedBodySlideDescriptors.Remove(descriptor);
                disallowedDescriptorsBS.RemoveAt(i);
                i--;
            }
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DeletedMissingDescriptors(sg, oBodySettings, bodyGenConfig);
        }
    }

    private void ApplyCustomRecordTemplate(string newDefaultTemplateFormKeyStr, string newKhajiitFormKeyStr, string newArgonianFormKeyStr, Gender gender, IEnumerable<string> defaultAdditionalRacesPaths)
    {
        if (RecordTemplateLinkCache.TryResolve<INpcGetter>(newDefaultTemplateFormKeyStr, out var defaultTemplate))
        {
            DefaultTemplateFK = defaultTemplate.FormKey;
        }
        else if (FormKey.TryFactory(newDefaultTemplateFormKeyStr, out var defaultTemplatefk))
        {
            DefaultTemplateFK = defaultTemplatefk;
        }
        foreach (var additionalArmaStr in defaultAdditionalRacesPaths)
        {
            if (!DefaultRecordTemplateAdditionalRacesPaths.Select(x => x.Content).Contains(additionalArmaStr))
            {
                DefaultRecordTemplateAdditionalRacesPaths.Add(new(additionalArmaStr, DefaultRecordTemplateAdditionalRacesPaths));
            }
        }
  
        ApplyBeastTemplate(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, gender, newKhajiitFormKeyStr, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRaceVampire.FormKey }, defaultAdditionalRacesPaths); //khajiit template
        ApplyBeastTemplate(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, gender, newArgonianFormKeyStr, new List<FormKey>() { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace.FormKey, Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRaceVampire.FormKey }, defaultAdditionalRacesPaths); //khajiit template
    }

    private void ApplyBeastTemplate(FormKey defaultTemplateRaceFormKey, Gender gender, string newBeastFormKeyStr, List<FormKey> additionalRacesFormKeys, IEnumerable<string> defaultAdditionalRacesPaths)
    {
        var currentBeastTemplate = AdditionalRecordTemplateAssignments.Where(x =>
                RecordTemplateLinkCache.TryResolve<INpcGetter>(x.TemplateNPC, out var templateNPCGetter) &&
                NPCInfo.GetGender(templateNPCGetter) == gender &&
                templateNPCGetter.Race.FormKey.Equals(defaultTemplateRaceFormKey)).FirstOrDefault();

        if (currentBeastTemplate == null)
        {
            currentBeastTemplate = _additionalRecordTemplateFactory(RecordTemplateLinkCache, AdditionalRecordTemplateAssignments);
            AdditionalRecordTemplateAssignments.Add(currentBeastTemplate);
        }

        FormKey.TryFactory(newBeastFormKeyStr, out var newBeastTemplateNPCFormKey);
        currentBeastTemplate.TemplateNPC = newBeastTemplateNPCFormKey;

        Noggog.ListExt.AddRange(currentBeastTemplate.RaceFormKeys, additionalRacesFormKeys.Where(x => !currentBeastTemplate.RaceFormKeys.Contains(x)));
        foreach (var additionalArmaStr in defaultAdditionalRacesPaths.And(VM_AdditionalRecordTemplate.AdditionalRacesPathsBeast))
        {
            if (!currentBeastTemplate.AdditionalRacesPaths.Select(x => x.Content).Contains(additionalArmaStr))
            {
                currentBeastTemplate.AdditionalRacesPaths.Add(new(additionalArmaStr, DefaultRecordTemplateAdditionalRacesPaths));
            }
        }
    }

    public int BulkRenameSubgroups(string from, string to)
    {
        int renamedCount = 0;
        foreach (var subgroup in Subgroups)
        {
            renamedCount += RenameSubgroupsRecursive(subgroup, from, to);
        }
        return renamedCount;
    }

    private int RenameSubgroupsRecursive(VM_SubgroupPlaceHolder subgroup, string from, string to)
    {
        int renamedCount = 0;
        if (subgroup.Name.Contains(from))
        {
            subgroup.Name = subgroup.Name.Replace(from, to);
            subgroup.AutoGenerateID(true, 0);
            subgroup.AssociatedModel.Name = subgroup.Name;
            subgroup.AssociatedModel.ID = subgroup.ID;
            if (subgroup.AssociatedViewModel != null)
            {
                subgroup.AssociatedViewModel.Name = subgroup.Name;
                subgroup.AssociatedViewModel.ID = subgroup.ID;
            }
            renamedCount++;
        }

        foreach (var sg in subgroup.Subgroups)
        {
            renamedCount += RenameSubgroupsRecursive(sg, from, to);
        }

        return renamedCount;
    }

    private void ClearBodyGen()
    {
        if (DisplayedSubgroup != null)
        {
            foreach (var x in DisplayedSubgroup.AllowedBodyGenDescriptors.DescriptorShells)
            {
                foreach (var y in x.DescriptorSelectors)
                {
                    y.IsSelected = false;
                }
            }

            foreach (var x in DisplayedSubgroup.DisallowedBodyGenDescriptors.DescriptorShells)
            {
                foreach (var y in x.DescriptorSelectors)
                {
                    y.IsSelected = false;
                }
            }
        }

        foreach (var subgroup in Subgroups)
        {
            subgroup.ClearBodyGenRecursive();
        }

        TrackedBodyGenConfig = null;
    }

    private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.CheckVisibilityConfigVM(searchText, caseSensitive, false);
        }
    }

    public HashSet<VM_SubgroupPlaceHolder> GetAllSubgroups()
    {
        HashSet<VM_SubgroupPlaceHolder> subgroups = new();
        foreach (var topLevel in Subgroups)
        {
            var allAtIndex = topLevel.GetChildren();
            subgroups.Add(topLevel);
            subgroups.Add(allAtIndex);
        }

        return subgroups;
    }
}

public interface IHasSubgroupViewModels
{
    ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; }
}
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

/// <summary>
/// Identifies which sub-editor pane of the asset pack UI is currently displayed
/// (subgroup tree, distribution rules, direct replacers, record templates, attribute groups,
/// race groupings, or miscellaneous settings).
/// </summary>
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

/// <summary>
/// View model of an <see cref="AssetPack"/> (a texture/mesh "config plugin"). Backs the top-level
/// asset pack editor in the Textures &amp; Meshes settings tab: its subgroup tree, distribution rules,
/// direct asset replacers, record templates, attribute groups, race groupings, and miscellaneous
/// settings panes, plus the toolbar commands (save/discard/copy/merge/validate/import/remap, etc.).
/// Inherits reactive/INotifyPropertyChanged plumbing from <see cref="VM"/> (PropertyChanged.Fody + ReactiveUI)
/// and acts as a gong-wpf-dragdrop drop target for reorganizing the subgroup tree.
/// </summary>
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

    /// <summary>Autofac factory delegate for constructing a <see cref="VM_AssetPack"/> around an <see cref="AssetPack"/> model.</summary>
    public delegate VM_AssetPack Factory(AssetPack model);

    /// <summary>
    /// Wires up all toolbar/menu <see cref="RelayCommand"/>s, the reactive subscriptions that keep
    /// derived state in sync (gender-driven BodyGen config list and record-template refresh, body-shape
    /// mode mirroring, subgroup search throttling, ordering/header refresh observables, and the
    /// selected-placeholder buffer that dumps the previously displayed subgroup and loads the new one),
    /// and seeds the child menu view models (attribute groups, race groupings, replacers, distribution rules, misc).
    /// </summary>
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
    /// <summary>The default record-template NPC plus all additional (per-race) template NPCs, used as reference NPCs when validating destination paths.</summary>
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

    /// <summary>Display-name map for the <see cref="Gender"/> enum, bound by the XAML gender selector.</summary>
    public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
    {
        {Gender.Male, "Male"},
        {Gender.Female, "Female"},
    };

    /// <summary>Dumps this VM to a model and runs <see cref="AssetPackValidator"/> against it, returning whether it is valid and collecting any errors.</summary>
    public bool Validate(BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, out List<string> errors)
    {
        var model = DumpViewModelToModel();
        errors = new List<string>();
        return _assetPackValidator.Validate(model, errors, bodyGenConfigs, oBodySettings);
    }

    /// <summary>Models -&gt; VMs: rebuilds the Textures &amp; Meshes asset pack list, creating and populating a VM per model and marking each selected per the saved settings.</summary>
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
    
    /// <summary>
    /// Model -&gt; VM: populates this view model's fields, child menus, subgroup placeholders, and record-template
    /// assignments from the given <paramref name="model"/>. <see cref="Gender"/> is set last so the gender-driven
    /// record-template refresh subscription does not duplicate the templates loaded here.
    /// </summary>
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

    /// <summary>VMs -&gt; models: clears <paramref name="models"/> and refills it by dumping each view model in <paramref name="viewModels"/>.</summary>
    public static void DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
    {
        models.Clear();

        foreach (var vm in viewModels)
        {
            models.Add(vm.DumpViewModelToModel());
        }
    }

    /// <summary>
    /// VM -&gt; model: builds a fresh <see cref="AssetPack"/> from this view model's current state, including
    /// child menus, the currently displayed subgroup (flushed back to its placeholder first), all subgroup
    /// placeholders, replacer groups, and distribution rules.
    /// </summary>
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

    /// <summary>Dumps this VM to a model and writes it to disk via <see cref="SettingsIO_AssetPack"/>, updating <see cref="SourcePath"/> and optionally showing a toolbar status notification. Returns whether the save succeeded.</summary>
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
    /// <summary>Looks up a subgroup placeholder by its ID anywhere in the subgroup tree; returns whether one was found.</summary>
    public bool TryGetSubgroupByID(string ID, out VM_SubgroupPlaceHolder subgroup)
    {
        subgroup = VM_SubgroupPlaceHolder.GetSubgroupByID(Subgroups, ID);
        return subgroup != null;
    }

    /// <summary>Returns whether any subgroup in the tree (at any depth) uses the given ID.</summary>
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

    /// <summary>Regenerates IDs for every subgroup in the tree, refreshes their displayed IDs, and syncs the currently displayed subgroup's ID to its placeholder.</summary>
    public void AutoGenerateSubgroupIDs()
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.AutoGenerateSubgroupIDs();
        }
        foreach (var subgroupVM in Subgroups)
        {
            subgroupVM.RefreshID(true);
        }
        
        if (DisplayedSubgroup.AssociatedPlaceHolder != null)
        {
            DisplayedSubgroup.ID = DisplayedSubgroup.AssociatedPlaceHolder.ID;
        }
    }

    /// <summary>Prompts the user to confirm, then permanently deletes this asset pack's config file from disk (logging on failure).</summary>
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

    /// <summary>
    /// Prompts the user to pick another config JSON, loads it, and merges its subgroups into this asset pack:
    /// brand-new top-level subgroups are cloned in, and subgroups sharing an ID are merged recursively via
    /// <see cref="MergeSubgroupLists"/>. Reports the imported subgroups when done.
    /// </summary>
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

    /// <summary>Recursively merges <paramref name="ListB"/> into <paramref name="ListA"/>: subgroups absent from A (by ID) are cloned in; matching IDs recurse into their children. Records newly added subgroup names.</summary>
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

    /// <summary>
    /// Picks default record templates appropriate to the current <see cref="Gender"/>: sets the default human
    /// template, adds Khajiit/Argonian beast templates, removes any leftover templates for the opposite gender,
    /// and ensures the default additional-races paths are present.
    /// </summary>
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

    /// <summary>Sets <see cref="DefaultTemplateFK"/> to the template with the given editor ID if no default is set yet or the current default is for the wrong gender.</summary>
    public void UpdateDefaultRecordTemplate(string defaultTemplateEditorID, Gender gender)
    {
        if (RecordTemplateLinkCache.TryResolve<INpcGetter>(defaultTemplateEditorID, out var defaultMaleRec))
        {
            if (DefaultTemplateFK.IsNull || (RecordTemplateLinkCache.TryResolve<INpcGetter>(DefaultTemplateFK, out var defaultTemplate) && NPCInfo.GetGender(defaultTemplate) != gender))
            {
                DefaultTemplateFK = defaultMaleRec.FormKey;
            }
        }
    }

    /// <summary>Adds an additional beast-race record-template assignment (for the given editor ID and races) if one for that race/gender does not already exist.</summary>
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

    /// <summary>Removes all additional record-template assignments whose template NPC is of the given (no-longer-relevant) gender.</summary>
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

    /// <summary>
    /// Auto-assigns destination paths across all subgroups based on source file names (optionally saving first so the
    /// user can discard): runs <see cref="SetDefaultSubgroupFilePaths"/>, then duplicates body torso paths to feet
    /// (when feet paths are absent) and to beast tails, and finally reports the modifications made.
    /// </summary>
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

    /// <summary>Recursively copies each torso (Body) texture path in the subgroup tree to the equivalent Feet destination when that destination exists on a reference NPC and isn't already assigned.</summary>
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

    /// <summary>Recursively copies each beast (Khajiit/Argonian) torso texture path in the subgroup tree to the equivalent Tail destination when that destination exists and isn't already assigned.</summary>
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

    /// <summary>Returns whether the subgroup or any of its descendants assigns a path whose destination contains the given fragment.</summary>
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

    /// <summary>
    /// Recursively assigns each path's destination from the file-name-to-destination map: prefers the canonical
    /// destination, otherwise (for torso textures) falls back to a Feet or Tail alternate destination if it exists
    /// on a reference NPC and isn't already taken. Records every change.
    /// </summary>
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

    /// <summary>Returns whether the given record path resolves to a string-valued field on any of this asset pack's reference NPC record templates.</summary>
    public bool CandidateTargetPathExists(string candidate)
    {
        List<FormKey> candidateRecordTemplates = new();
        if (!DefaultTemplateFK.IsNull)
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

    /// <summary>Collects display strings for every disabled subgroup across the tree (for the "List Disabled Subgroups" report).</summary>
    public List<string> GetDisabledSubgroups()
    {
        List<string> disabledSubgroups = new();
        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDisabledSubgroups(disabledSubgroups);
        }
        return disabledSubgroups;
    }

    /// <summary>Builds a human-readable summary of all custom distribution rules: config-level rules, then per-subgroup rules, then direct-replacer rules (for the "List Custom Rules" report).</summary>
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

    /// <summary>gong-wpf-dragdrop handler: while dragging a subgroup placeholder, chooses the highlight vs. insert adorner and flags a right-mouse drag (copy instead of move).</summary>
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

    /// <summary>
    /// gong-wpf-dragdrop handler: drops a dragged subgroup placeholder into the tree by cloning it under or
    /// before the drop target (or at the tree root), guarding against dropping a parent into its own descendant,
    /// removing the original unless it was a right-click copy, and re-applying the subgroup search filter.
    /// </summary>
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

    /// <summary>Applies (or, in <see cref="UpdateMode.Check"/> mode, merely detects) version-migration updates across all main and replacer subgroups; returns true if an update was applied/needed.</summary>
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

    /// <summary>Ensures any race grouping referenced by the model's subgroups/replacers but missing from <paramref name="existingGroupings"/> is pulled in from the supplied fallback groupings.</summary>
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

    /// <summary>
    /// Locates and (after user confirmation) deletes the installed asset files/folders associated with this config.
    /// Uses the installation-token strategy (v1.0.1.9+) to find prefix/mod folders shared with other configs when a
    /// token is present, falls back to prefix-directory matching for older installs, and otherwise deletes individual
    /// SynthEBD-installed files directly from the Data folder. Honors the active mod manager (MO2/Vortex) staging path.
    /// </summary>
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

    /// <summary>Collects the set of install-prefix folder names used by all main and replacer subgroup source paths.</summary>
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

    /// <summary>Recursively adds the install-prefix folder name (second path segment) of each valid SynthEBD-installed source path in the subgroup tree.</summary>
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

    /// <summary>Heuristic filter for whether a relative asset path is a SynthEBD-installed file (excludes .bsa-sourced files and default game actor paths) and thus a candidate for deletion/prefix extraction.</summary>
    private bool IsValidSynthEBDInstalledAsset(string path)
    {
        string[] split = path.Split(Path.DirectorySeparatorChar);
        return split.Length >= 2 &&
            !split[0].EndsWith(".esm", StringComparison.OrdinalIgnoreCase) && // don't try to get a prefix for a file coming from a .bsa (referenced as the first part of the filename ending with 'bsaname.esm')
            !split[1].Equals("actors", StringComparison.OrdinalIgnoreCase); // try to get rid of file paths that are pointing at default (non-modded) file paths. This is hard to do but most often in the case of SynthEBD it would be textures\actors or meshes\actors
    }

    /// <summary>Collects the relative source paths of all assets referenced by the top-level subgroups (and their descendants).</summary>
    private List<string> GetContainedFileRelativePaths()
    {
        List<string> paths = new();
        foreach (var subgroup in Subgroups)
        {
            paths.AddRange(subgroup.GetContainedAssetRelativePaths());
        }
        return paths;
    }

    /// <summary>Removes BodyGen/BodySlide descriptor selections from every subgroup that no longer correspond to a descriptor defined in the current OBody/BodyGen configs, then refreshes the displayed subgroup.</summary>
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

    /// <summary>Recursively prunes a subgroup model's allowed/disallowed BodyGen and BodySlide descriptors that are no longer present in the supplied configs.</summary>
    private static void DeletedMissingDescriptors(Subgroup subgroup, Settings_OBody oBodySettings, BodyGenConfig? bodyGenConfig)
    {
        var allowedDescriptorsBG = subgroup.AllowedBodyGenDescriptors.ToList();
        for (int i = 0; i < allowedDescriptorsBG.Count; i++)
        {
            var descriptor = allowedDescriptorsBG[i];
            if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors.Flatten()))
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
            if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors.Flatten()))
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
            if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors.Flatten()))
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
            if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors.Flatten()))
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

    /// <summary>Applies a predefined record-template set (used by the Config Drafter for a chosen body type): sets the default human template plus Khajiit and Argonian beast templates and their additional-races paths.</summary>
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

    /// <summary>Creates or updates the additional record-template assignment for a beast race/gender, setting its template NPC and merging in the given race form keys and additional-races paths.</summary>
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

    /// <summary>Replaces the substring <paramref name="from"/> with <paramref name="to"/> in every matching subgroup name across the tree, regenerating IDs; returns the number of subgroups renamed.</summary>
    public int BulkRenameSubgroups(string from, string to)
    {
        int renamedCount = 0;
        foreach (var subgroup in Subgroups)
        {
            renamedCount += RenameSubgroupsRecursive(subgroup, from, to);
        }
        return renamedCount;
    }

    /// <summary>Recursively renames a subgroup and its descendants whose names contain <paramref name="from"/>, syncing the new name/ID to the placeholder, model, and any open child VM; returns the count renamed.</summary>
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

    /// <summary>Clears all BodyGen descriptor selections on the displayed subgroup and recursively across the whole tree, and detaches the tracked BodyGen config.</summary>
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

    /// <summary>Applies the subgroup search text/case-sensitivity filter to update each top-level subgroup's (and its descendants') tree visibility.</summary>
    private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.CheckVisibilityConfigVM(searchText, caseSensitive, false);
        }
    }

    /// <summary>Returns a flattened set of every subgroup placeholder in the tree (top-level plus all descendants).</summary>
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

/// <summary>Implemented by view models (such as <see cref="VM_AssetPack"/>) that own a tree of subgroup placeholders, exposing it for shared subgroup-tree UI and logic.</summary>
public interface IHasSubgroupViewModels
{
    ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; }
}
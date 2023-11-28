using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ReactiveUI;
using DynamicData.Binding;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive;
using System.Windows;
using static SynthEBD.AssetPack;

namespace SynthEBD;

public class VM_SpecificNPCAssignment : VM, IHasForcedAssets, IHasSynthEBDGender, IHasHeadPartAssignments
{
    public delegate VM_SpecificNPCAssignment Factory(VM_SpecificNPCAssignmentPlaceHolder associatedPlaceHolder);

    private IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_Settings_General _generalSettings;
    private readonly VM_SettingsTexMesh _texMeshSettings;
    private readonly VM_SettingsBodyGen _bodyGenSettings;
    private readonly VM_SettingsOBody _oBodySettings;
    private readonly VM_Settings_Headparts _headPartSettings;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    private readonly VM_BodySlidePlaceHolder.Factory _bodySlidePlaceHolderFactory;
    private readonly VM_HeadPartAssignment.Factory _headPartFactory;
    private readonly Converters _converters;

    public VM_SpecificNPCAssignment(
        VM_SpecificNPCAssignmentPlaceHolder associatedPlaceHolder,
        IEnvironmentStateProvider environmentProvider,
        Logger logger, 
        SynthEBDPaths paths,
        VM_Settings_General general,
        VM_SettingsOBody oBody,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsTexMesh texMesh,
        VM_Settings_Headparts headParts,
        VM_SpecificNPCAssignmentsUI parentUI,
        VM_AssetPack.Factory assetPackFactory,
        VM_BodySlidePlaceHolder.Factory bodySlidePlaceHolderFactory,
        VM_HeadPartAssignment.Factory headPartFactory,
        Converters converters)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
        _generalSettings = general;
        _texMeshSettings = texMesh;
        _bodyGenSettings = bodyGen;
        _oBodySettings = oBody;
        _headPartSettings = headParts;
        _assetPackFactory = assetPackFactory;
        _bodySlidePlaceHolderFactory = bodySlidePlaceHolderFactory;
        _headPartFactory = headPartFactory;
        _converters = converters;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        SubscribedGeneralSettings = general;
        SubscribedOBodySettings = oBody;
        SubscribedBodyGenSettings = bodyGen;
        SubscribedHeadPartSettings = headParts;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AssetOrderingMenu = new(texMesh);

        SubscribedAssetPacks = texMesh.AssetPacks;

        this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => RefreshAll()).DisposeWith(this);

        SubscribedAssetPacks.ToObservableChangeSet().Subscribe(x => RefreshAssets()).DisposeWith(this);

        DynamicData.ObservableListEx
            .Transform(SubscribedAssetPacks.ToObservableChangeSet(), x => x.WhenAnyValue(y => y.IsSelected)
                .Subscribe(_ => RefreshAssets())
                .DisposeWith(this))
            .Subscribe()
            .DisposeWith(this);

        DynamicData.ObservableListEx
            .Transform(SubscribedAssetPacks.ToObservableChangeSet(), x => x.WhenAnyValue(y => y.ConfigType)
                .Subscribe(_ => RefreshAssets())
                .DisposeWith(this))
            .Subscribe()
            .DisposeWith(this);

        DynamicData.ObservableListEx
            .Transform(SubscribedAssetPacks.ToObservableChangeSet(), x => x.WhenAnyValue(y => y.Gender)
                .Subscribe(_ => RefreshAssets())
                .DisposeWith(this))
            .Subscribe()
            .DisposeWith(this);

        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
        {
            if (x != null && x.IsSelected)
            {
                UpdateAvailableSubgroups(this);
                ShowSubgroupAssignments = true;
                CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
            }
            else
            {
                ShowSubgroupAssignments = false;
            }
            
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ForcedAssetPack.IsSelected).Subscribe(b =>
        {
            if (b)
            {
                ShowSubgroupAssignments = true;
                CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
            }
            else
            {
                ShowSubgroupAssignments = false;
            }
        }).DisposeWith(this);

        ForcedSubgroups.ToObservableChangeSet().Subscribe(_ => {
            UpdateAvailableSubgroups(this);
            CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
        }).DisposeWith(this);

        ForcedBodyGenMorphs.ToObservableChangeSet().Subscribe(_ => UpdateAvailableMorphs(this)).DisposeWith(this);

        Observable.CombineLatest(
            this.WhenAnyValue(x => x.SubscribedBodyGenSettings),
            this.WhenAnyValue(x => x.ForcedAssetPack),
            ForcedBodyGenMorphs.ToObservableChangeSet(),
            SubscribedBodyGenSettings.MaleConfigs.ToObservableChangeSet(),
            SubscribedBodyGenSettings.FemaleConfigs.ToObservableChangeSet(),
            SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentMaleConfig),
            SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentFemaleConfig),
            (a, b, c, d, e, f, g) => { return Unit.Default; })
            .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
            //.Subscribe(_ => UpdateAvailableMorphs(this))
            .Subscribe(_ => MessageBox.Show("Combined Subscription"))
            .DisposeWith(this);

        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
        {
            ForcedAssetReplacements.Remove(ForcedAssetReplacements.Where(x => x.ParentAssetPack != ForcedAssetPack));
        }).DisposeWith(this);

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.NameSearchStr),
                this.WhenAnyValue(x => x.NameSearchCaseSensitive),
                (searchText, caseSensitive) => { return (searchText, caseSensitive); })
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Subscribe(y => CheckSubgroupVisibility(y.searchText, y.caseSensitive))
            .DisposeWith(this);

        HeadParts = new()
        {
            { HeadPart.TypeEnum.Eyebrows, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Eyebrows, this, this) },
            { HeadPart.TypeEnum.Eyes, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Eyes, this, this) },
            { HeadPart.TypeEnum.Face, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Face, this, this) },
            { HeadPart.TypeEnum.FacialHair, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.FacialHair, this, this) },
            { HeadPart.TypeEnum.Hair, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Hair, this, this) },
            { HeadPart.TypeEnum.Misc, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Misc, this, this) },
            { HeadPart.TypeEnum.Scars, _headPartFactory(null, SubscribedHeadPartSettings, HeadPart.TypeEnum.Scars, this, this) }
        };

        DeleteForcedAssetPack = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                ForcedSubgroups.Clear();
                ForcedAssetPack = null;
            }
        );
        DeleteForcedSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedSubgroups.Remove((VM_SubgroupPlaceHolder)x)
        );

        DeleteForcedMorph = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedBodyGenMorphs.Remove((VM_BodyGenTemplatePlaceHolder)x)
        );

        AddForcedMixIn = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedMixIns.Add(new VM_MixInSpecificAssignment(this, assetPackFactory))
        );

        AddForcedReplacer = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
        );

        DeleteForcedMixInSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                var toDelete = (VM_SubgroupPlaceHolder)x;
                foreach (var mixin in ForcedMixIns)
                {
                    if (mixin.ForcedSubgroups.Contains(toDelete))
                    {
                        mixin.ForcedSubgroups.Remove(toDelete);
                    }
                }
            }
        );

        SyncThisAssetOrder = new RelayCommand(
            canExecute: _ => true,
            execute: x => AssociatedPlaceHolder.SyncAssetOrderFromMain()
        );

        SyncAllAssetOrders = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in parentUI.Assignments)
                {
                    assignment.SyncAssetOrderFromMain();
                }
            }
        );

        UpdateAvailableAssetPacks(this);
        UpdateAvailableBodySlides();
    }

    // Caption
    public string DispName { get; set; } = "New Assignment";

    //User-editable
    public FormKey NPCFormKey { get; set; } = new();
    public VM_AssetPack ForcedAssetPack { get; set; }
    public bool ShowSubgroupAssignments { get; set; } = false;
    public ObservableCollection<VM_SubgroupPlaceHolder> ForcedSubgroups { get; set; } = new();
    public ObservableCollection<VM_MixInSpecificAssignment> ForcedMixIns { get; set; } = new();
    public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
    public string ForcedHeight { get; set; } = "";
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> ForcedBodyGenMorphs { get; set; } = new();
    public string ForcedBodySlide { get; set; } = "";
    public Dictionary<HeadPart.TypeEnum, VM_HeadPartAssignment> HeadParts { get; set; } = new();

    // UI Styling
    public string NameSearchStr { get; set; }
    public bool NameSearchCaseSensitive { get; set; } = false;

    //Needed by UI
    public VM_SpecificNPCAssignmentPlaceHolder AssociatedPlaceHolder { get; set; }
    public ObservableCollection<VM_AssetPack> AvailableAssetPacks { get; set; } = new();
    public ObservableCollection<VM_AssetPack> SubscribedAssetPacks { get; set; }

    public ObservableCollection<VM_SubgroupPlaceHolder> AvailableSubgroups { get; set; } = new();

    public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; } = new();
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> AvailableMorphs { get; set; } = new();
    public VM_SettingsBodyGen SubscribedBodyGenSettings { get; set; }
    public ObservableCollection<VM_BodySlidePlaceHolder> SubscribedBodySlides { get; set; }
    public ObservableCollection<VM_BodySlidePlaceHolder> AvailableBodySlides { get; set; }
    public VM_BodyGenTemplate SelectedTemplate { get; set; }

    public Gender Gender { get; set; }
    public VM_AssetOrderingMenu AssetOrderingMenu { get; set; }

    public VM_Settings_General SubscribedGeneralSettings { get; set; }
    public VM_SettingsOBody SubscribedOBodySettings { get; set; }
    public VM_Settings_Headparts SubscribedHeadPartSettings { get; set; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand DeleteForcedAssetPack { get; set; }
    public RelayCommand DeleteForcedSubgroup { get; set; }
    public RelayCommand DeleteForcedMorph { get; set; }
    public RelayCommand AddForcedMixIn { get; set; }
    public RelayCommand AddForcedReplacer { get; set; }
    public RelayCommand DeleteForcedMixInSubgroup { get; set; }
    public RelayCommand AddHeadPart { get; set; }
    public RelayCommand SyncThisAssetOrder { get; set; }
    public RelayCommand SyncAllAssetOrders { get; set; }
    public void CopyInFromModel(NPCAssignment model)
    {
        NPCFormKey = model.NPCFormKey;

        if (NPCFormKey.IsNull)
        {
            return;
        }

        var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

        if (!npcFormLink.TryResolve(_environmentProvider.LinkCache, out var npcRecord))
        {
            _logger.LogError("Warning: the target NPC of the Specific NPC Assignment with FormKey " + NPCFormKey.ToString() + " was not found in the current load order.");
        }

        Gender = GetGender(NPCFormKey, _logger, _environmentProvider);

        if (model.AssetPackName.Length != 0)
        {
            LinkAssetPackToForcedAssignment(model, this, model.AssetPackName, _texMeshSettings.AssetPacks, _logger);
        }

        CopyInMixInViewModels(model.MixInAssignments);

        AssetOrderingMenu.CopyInFromModel(model.AssetOrder);

        if (model.Height != null)
        {
            ForcedHeight = model.Height.ToString();
        }
        else
        {
            ForcedHeight = "";
        }

        ObservableCollection<VM_BodyGenTemplatePlaceHolder> templates = new();
        switch (Gender)
        {
            case Gender.Male:
                if (_bodyGenSettings.CurrentMaleConfig != null)
                {
                    templates = _bodyGenSettings.CurrentMaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplatePlaceHolder>();
                }
                break;
            case Gender.Female:
                if (_bodyGenSettings.CurrentFemaleConfig != null)
                {
                    templates = _bodyGenSettings.CurrentFemaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplatePlaceHolder>();
                }
                break;
        }

        foreach (var forcedMorph in model.BodyGenMorphNames)
        {
            bool morphFound = false;
            foreach (var morph in templates)
            {
                if (morph.Label == forcedMorph)
                {
                    ForcedBodyGenMorphs.Add(morph);
                    morphFound = true;
                    break; ;
                }
            }
            if (morphFound == false)
            {
                _logger.LogError("Warning: The forced BodyGen morph " + forcedMorph + " for NPC " + DispName + " no longer exists.");
            }
        }

        foreach (var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = _texMeshSettings.AssetPacks.Where(x => x.GroupName == replacer.AssetPackName).FirstOrDefault();
            if (parentAssetPack != null)
            {
                VM_AssetReplacementAssignment subVm = new VM_AssetReplacementAssignment(parentAssetPack, ForcedAssetReplacements);
                subVm.CopyInViewModelFromModel(replacer);
                ForcedAssetReplacements.Add(subVm);
            }
            else
            {
                _logger.LogError("Warning: The forced Asset Replacer " + replacer.AssetPackName + " for NPC " + DispName + " no longer exists.");
            }
        }

        ForcedBodySlide = model.BodySlidePreset;

        foreach (var headPartType in HeadParts.Keys)
        {
            if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }
            else
            {
                HeadParts[headPartType].CopyInFromModel(model.HeadParts[headPartType], headPartType, _headPartSettings, this, this, _environmentProvider);
            }
        }

        DispName = _converters.CreateNPCDispNameFromFormKey(NPCFormKey);
    }

    private static bool LinkAssetPackToForcedAssignment(NPCAssignment model, IHasForcedAssets viewModel, string assetPackName, ObservableCollection<VM_AssetPack> assetPacks, Logger logger)
    {
        bool assetPackFound = false;
        foreach (var ap in assetPacks)
        {
            if (ap.GroupName == assetPackName)
            {
                viewModel.ForcedAssetPack = ap;
                assetPackFound = true;

                foreach (var id in model.SubgroupIDs)
                {
                    if (ap.TryGetSubgroupByID(id, out var foundSubgroup))
                    {
                        viewModel.ForcedSubgroups.Add(foundSubgroup);
                        continue;
                    }
                    else
                    {
                        logger.LogError("Warning: The forced Subgroup " + id + " for NPC " + model.DispName + " no longer exists.");
                    }
                }
            }
        }

        if (!assetPackFound)
        {
            logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + model.DispName + " no longer exists.");
        }

        return assetPackFound;
    }

    private static bool LinkMixInToForcedAssignment(NPCAssignment.MixInAssignment model, IHasForcedAssets viewModel, string assetPackName, ObservableCollection<VM_AssetPack> assetPacks, string npcName, Logger logger)
    {
        bool assetPackFound = false;
        foreach (var ap in assetPacks)
        {
            if (ap.GroupName == assetPackName)
            {
                viewModel.ForcedAssetPack = ap;
                assetPackFound = true;

                foreach (var id in model.SubgroupIDs)
                {
                    if (ap.TryGetSubgroupByID(id, out var foundSubgroup))
                    {
                        viewModel.ForcedSubgroups.Add(foundSubgroup);
                        continue;
                    }
                    else
                    {
                        logger.LogError("Warning: The forced Subgroup " + id + " for NPC " + npcName + " no longer exists.");
                    }
                }
            }
        }

        if (!assetPackFound)
        {
            logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + npcName + " no longer exists.");
        }

        return assetPackFound;
    }

    public NPCAssignment DumpViewModelToModel()
    {
        NPCAssignment model = new NPCAssignment();
        model.DispName = DispName;

        if (ForcedAssetPack != null)
        {
            model.AssetPackName = ForcedAssetPack.GroupName;
            model.SubgroupIDs = ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

            model.AssetReplacerAssignments.Clear();
            foreach (var replacer in ForcedAssetReplacements)
            {
                model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
            }
        }
        else
        {
            model.AssetPackName = String.Empty;
            model.SubgroupIDs = new();
        }

        foreach (var mixin in ForcedMixIns.Where(x => x.ForcedAssetPack != null && !x.ForcedAssetPack.GroupName.IsNullOrWhitespace()).ToArray())
        {
            if (!model.MixInAssignments.Select(x => x.AssetPackName).Contains(mixin.ForcedAssetPack.GroupName))
            {
                model.MixInAssignments.Add(VM_MixInSpecificAssignment.DumpViewModelToModel(mixin));
            }
        }

        model.AssetOrder = AssetOrderingMenu.DumpToModel();

        if (ForcedHeight == "")
        {
            model.Height = null;
        }
        else if (float.TryParse(ForcedHeight, out var height))
        {
            model.Height = height;
        }
        else
        {
            _logger.LogError("Error parsing Specific NPC Assignment " + DispName + ". Cannot parse height: " + ForcedHeight);
        }
            
        model.BodyGenMorphNames = ForcedBodyGenMorphs.Select(morph => morph.Label).ToList();
        model.BodySlidePreset = ForcedBodySlide;
        model.NPCFormKey = NPCFormKey;

        foreach (var headPartType in HeadParts.Keys)
        {
            model.HeadParts[headPartType] = HeadParts[headPartType].DumpToModel();
        }

        return model;
    }

    public void UpdateAvailableAssetPacks(VM_SpecificNPCAssignment assignment)
    {
        var availablePrimaryAssetPacks = assignment.SubscribedAssetPacks.Where(x => x.IsSelected && x.Gender == assignment.Gender && x.ConfigType == AssetPackType.Primary).ToArray();
        var availableMixInAssetPacks = assignment.SubscribedAssetPacks.Where(x => x.IsSelected && x.Gender == assignment.Gender && x.ConfigType == AssetPackType.MixIn).ToArray();

        assignment.AvailableAssetPacks.AddRange(availablePrimaryAssetPacks.Where(x => !assignment.AvailableAssetPacks.Contains(x)));
        assignment.AvailableMixInAssetPacks.AddRange(availableMixInAssetPacks.Where(x => !assignment.AvailableMixInAssetPacks.Contains(x)));

        // I first tried this with Linq RemoveWhere but it seems to fail "under the hood".
        // With RemoveWhere, even if assignment.ForcedAssetPack exists in availablePrimaryAssetPacks, it seems to get removed and then re-added, causing assignment.ForcedAssetPack to change to null and clear out.
        for (int i = 0; i < assignment.AvailableAssetPacks.Count; i++)
        {
            if (!availablePrimaryAssetPacks.Contains(assignment.AvailableAssetPacks[i]))
            {
                assignment.AvailableAssetPacks.RemoveAt(i);
                i--;
            }
        }

        for (int i = 0; i < assignment.AvailableMixInAssetPacks.Count; i++)
        {
            if (!availableMixInAssetPacks.Contains(assignment.AvailableMixInAssetPacks[i]))
            {
                assignment.AvailableMixInAssetPacks.RemoveAt(i);
                i--;
            }
        }

        assignment.AvailableAssetPacks.Sort(x => x.GroupName, false);
        assignment.AvailableMixInAssetPacks.Sort(x => x.GroupName, false);
    }

    public static void UpdateAvailableSubgroups(IHasForcedAssets assignment)
    {
        assignment.AvailableSubgroups.Clear();
        if (assignment.ForcedAssetPack == null) { return; }
        foreach (var topLevelSubgroup in assignment.ForcedAssetPack.Subgroups)
        {
            bool topLevelTaken = false;
            foreach (var forcedSubgroup in assignment.ForcedSubgroups)
            {
                if (topLevelSubgroup.ID == forcedSubgroup.ID || ContainsSubgroupID(topLevelSubgroup.Subgroups, forcedSubgroup.ID))
                {
                    topLevelTaken = true;
                    break;
                }
            }
            if (topLevelTaken == false)
            {
                assignment.AvailableSubgroups.Add(topLevelSubgroup);
            }
        }
    }

    public static bool ContainsSubgroupID(ObservableCollection<VM_SubgroupPlaceHolder> subgroups, string id)
    {
        foreach(var sg in subgroups)
        {
            if (sg.ID == id) { return true; }
            else
            {
                if (ContainsSubgroupID(sg.Subgroups, id) == true) { return true; }
            }
        }
        return false;
    }

    public static void UpdateAvailableMorphs(VM_SpecificNPCAssignment assignment)
    {
        // clear available morphs besides the ones that are forced (removing those from the available morph list also clears their combobox selection)
        assignment.AvailableMorphs.Clear();

        var allTemplateList = new ObservableCollection<VM_BodyGenTemplatePlaceHolder>();
        if (assignment.ForcedAssetPack != null && assignment.ForcedAssetPack.TrackedBodyGenConfig != null && assignment.ForcedAssetPack.TrackedBodyGenConfig.TemplateMorphUI != null)
        {
            allTemplateList = assignment.ForcedAssetPack.TrackedBodyGenConfig.TemplateMorphUI.Templates;
        }
        else
        {
            switch (assignment.Gender)
            {
                case Gender.Male:
                    if (assignment.SubscribedBodyGenSettings.CurrentMaleConfig != null && assignment.SubscribedBodyGenSettings.CurrentMaleConfig.TemplateMorphUI != null)
                    {
                        allTemplateList = assignment.SubscribedBodyGenSettings.CurrentMaleConfig.TemplateMorphUI.Templates;
                    }
                    break;
                case Gender.Female:
                    if (assignment.SubscribedBodyGenSettings.CurrentFemaleConfig != null && assignment.SubscribedBodyGenSettings.CurrentFemaleConfig.TemplateMorphUI != null)
                    {
                        allTemplateList = assignment.SubscribedBodyGenSettings.CurrentFemaleConfig.TemplateMorphUI.Templates;
                    }
                    break;
            }
        }

        foreach (var candidateMorph in allTemplateList)
        {
            if (assignment.ForcedBodyGenMorphs.Contains(candidateMorph))
            {
                continue;
            }

            bool groupOccupied = false;

            var candidateGroups = candidateMorph.AssociatedModel.MemberOfTemplateGroups;

            foreach (var alreadyForcedMorph in assignment.ForcedBodyGenMorphs)
            {
                var forcedGroups = alreadyForcedMorph.AssociatedModel.MemberOfTemplateGroups;

                if (candidateGroups.Intersect(forcedGroups).ToArray().Length > 0)
                {
                    groupOccupied = true;
                    break;
                }
            }

            if (groupOccupied == false)
            {
                assignment.AvailableMorphs.Add(candidateMorph);
            }
        }
    }

    public void UpdateAvailableBodySlides()
    {
        switch(Gender)
        {
            case Gender.Male: SubscribedBodySlides = _oBodySettings.BodySlidesUI.BodySlidesMale; break;
            case Gender.Female: SubscribedBodySlides = _oBodySettings.BodySlidesUI.BodySlidesFemale; break;
        }
        AvailableBodySlides = new() { _bodySlidePlaceHolderFactory(new BodySlideSetting(), AvailableBodySlides) }; // blank entry
        AvailableBodySlides.AddRange(SubscribedBodySlides);
    }

    public void RefreshAll()
    {
        if (NPCFormKey.IsNull)
        {
            return;
        }

        DispName = _converters.CreateNPCDispNameFromFormKey(NPCFormKey);
        Gender = GetGender(NPCFormKey, _logger, _environmentProvider);

        UpdateAvailableAssetPacks(this);
        UpdateAvailableSubgroups(this);
        UpdateAvailableMorphs(this);
        UpdateAvailableBodySlides();
    }
        
    public void RefreshAssets()
    {
        UpdateAvailableAssetPacks(this);
        UpdateAvailableSubgroups(this);
    }

    public static Gender GetGender (FormKey NPCFormKey, Logger logger, IEnvironmentStateProvider environmentProvider)
    {
        var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

        if (npcFormLink.TryResolve(environmentProvider.LinkCache, out var npcRecord))
        {
            if (npcRecord.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
            {
                return Gender.Female;
            }
            else
            {
                return Gender.Male;
            }
        }

        if (!NPCFormKey.IsNull)
        {
            logger.LogError("Could not resolve gender of NPC with FormKey " + NPCFormKey.ToString() + " because it does not exist in the current load order.");
        }
        return Gender.Male;
    }

    public void CopyInMixInViewModels(List<NPCAssignment.MixInAssignment> models)
    {
        ForcedMixIns.Clear();
        foreach (var model in models)
        {
            var viewModel = new VM_MixInSpecificAssignment(this, _assetPackFactory);
            if(LinkMixInToForcedAssignment(model, viewModel, model.AssetPackName, _texMeshSettings.AssetPacks, DispName, _logger))
            {
                viewModel.Decline = model.DeclinedAssignment;
                ForcedMixIns.Add(viewModel);
            }
        }
    }

    private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
    {
        foreach (var subgroup in AvailableSubgroups)
        {
            subgroup.CheckVisibilitySpecificVM(searchText, caseSensitive, false);
        }
    }

    public class VM_MixInSpecificAssignment : VM, IHasForcedAssets
    {
        public delegate VM_MixInSpecificAssignment Factory(VM_SpecificNPCAssignment parent);
        public VM_MixInSpecificAssignment(VM_SpecificNPCAssignment parent, VM_AssetPack.Factory assetPackFactory)
        {
            Parent = parent;
            ParentCollection = Parent.ForcedMixIns;

            AvailableMixInAssetPacks = Parent.AvailableMixInAssetPacks;
            ForcedAssetPack = assetPackFactory(new AssetPack());

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x => { 
                UpdateAvailableSubgroups(this); 
                CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
            }).DisposeWith(this);

            ForcedSubgroups.ToObservableChangeSet().Subscribe(_ => {
                UpdateAvailableSubgroups(this);
                CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
            }).DisposeWith(this);

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
            {
                if (x != null && x.IsSelected)
                {
                    UpdateAvailableSubgroups(this);
                    ShowSubgroupAssignments = true;
                    CheckSubgroupVisibility(NameSearchStr, NameSearchCaseSensitive);
                }
                else
                {
                    ShowSubgroupAssignments = false;
                }

            }).DisposeWith(this);

            this.WhenAnyValue(x => x.Decline).Subscribe(y =>
            {
                switch (y)
                {
                    case true: ShowSubgroupAssignments = false; break;
                    case false: ShowSubgroupAssignments = true; break;
                }
            }).DisposeWith(this);

            DeleteCommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    ParentCollection.Remove(this);
                }
            );

            DeleteForcedMixInSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => ForcedSubgroups.Remove((VM_SubgroupPlaceHolder)x)
            );

            AddForcedReplacer = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
            );

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
            {
                foreach (var replacer in ForcedAssetReplacements)
                {
                    replacer.ParentAssetPack = ForcedAssetPack;
                }
            }).DisposeWith(this);

            Observable.CombineLatest(
                this.WhenAnyValue(x => x.NameSearchStr),
                this.WhenAnyValue(x => x.NameSearchCaseSensitive),
                (searchText, caseSensitive) => { return (searchText, caseSensitive); })
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Subscribe(y => CheckSubgroupVisibility(y.searchText, y.caseSensitive))
            .DisposeWith(this);
        }
        public VM_AssetPack ForcedAssetPack { get; set; }
        public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; }
        public bool Decline { get; set; } = false;
        public bool ShowSubgroupAssignments { get; set; }
        public ObservableCollection<VM_SubgroupPlaceHolder> ForcedSubgroups { get; set; } = new();
        public ObservableCollection<VM_SubgroupPlaceHolder> AvailableSubgroups { get; set; } = new();
        public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
        public ObservableCollection<VM_MixInSpecificAssignment> ParentCollection { get; set; }
        public VM_SpecificNPCAssignment Parent { get; set; }

        public RelayCommand DeleteCommand { get; set; }
        public RelayCommand DeleteForcedMixInSubgroup { get; set; }
        public RelayCommand AddForcedReplacer { get; set; }
       
        // UI Styling
        public string NameSearchStr { get; set; }
        public bool NameSearchCaseSensitive { get; set; } = false;

        public static NPCAssignment.MixInAssignment DumpViewModelToModel(VM_MixInSpecificAssignment viewModel)
        {
            NPCAssignment.MixInAssignment model = new NPCAssignment.MixInAssignment();
            model.DeclinedAssignment = viewModel.Decline;
            model.AssetPackName = viewModel.ForcedAssetPack.GroupName;
            model.SubgroupIDs = viewModel.ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

            model.AssetReplacerAssignments.Clear();
            foreach (var replacer in viewModel.ForcedAssetReplacements)
            {
                model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
            }
            return model;
        }

        private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
        {
            foreach (var subgroup in AvailableSubgroups)
            {
                subgroup.CheckVisibilitySpecificVM(searchText, caseSensitive, false);
            }
        }
    }
}

public interface IHasForcedAssets
{
    public VM_AssetPack ForcedAssetPack { get; set; }
    ObservableCollection<VM_SubgroupPlaceHolder> ForcedSubgroups { get; set; }
    public ObservableCollection<VM_SubgroupPlaceHolder> AvailableSubgroups { get; set; }
}
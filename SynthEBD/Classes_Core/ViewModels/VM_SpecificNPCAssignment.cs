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
using System.Threading.Tasks;
using System.Windows;
using static SynthEBD.AssetPack;

namespace SynthEBD;

/// <summary>
/// View model behind the "Specific NPC Assignments" editor: the forced appearance
/// overrides (asset pack and subgroups, height, BodyGen morphs, BodySlide preset,
/// head parts, mix-ins, and asset replacers) for a single chosen NPC. Its backing
/// model is <see cref="NPCAssignment"/>; it also drives a live <see cref="VM_CharacterViewer"/>
/// preview of the assignment.
/// </summary>
public class VM_SpecificNPCAssignment : VM, IHasForcedAssets, IHasSynthEBDGender, IHasHeadPartAssignments
{
    /// <summary>Autofac factory delegate that creates a view model for the given placeholder.</summary>
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
    private readonly SubgroupTextureMapper _subgroupTextureMapper;

    /// <summary>
    /// Wires up the assignment editor: seeds the subscribed settings VMs and asset
    /// pack list, builds the seven head-part assignment sub-VMs, registers all
    /// <see cref="RelayCommand"/>s (add/delete subgroups, morphs, mix-ins, replacers,
    /// asset-order sync), and sets up the ReactiveUI chains that recompute available
    /// asset packs/subgroups/morphs and drive the live character viewer (textures,
    /// BodySlide, BodyGen, height, and FaceGen re-bake) as the user edits.
    /// </summary>
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
        Converters converters,
        SubgroupTextureMapper subgroupTextureMapper,
        VM_CharacterViewer characterViewer)
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
        _subgroupTextureMapper = subgroupTextureMapper;
        CharacterViewer = characterViewer;
        CharacterViewer.DisposeWith(this);

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
            .Subscribe(_ => UpdateAvailableMorphs(this))
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

        // Character Viewer: reload NPC mesh when NPCFormKey changes.
        // Subscribed below in a merged stream alongside the head-part FormKey
        // observables so all initial-value emissions on screen load share a
        // single throttle window — preventing concurrent RefreshViewerNpcAsync
        // calls that would race FaceGenPatcher's temp extraction.

        // Character Viewer: reapply BodySlide when ForcedBodySlide changes
        this.WhenAnyValue(x => x.ForcedBodySlide)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerBodySlide())
            .DisposeWith(this);

        // Character Viewer: push the NPC-height scale override when ForcedHeight changes
        this.WhenAnyValue(x => x.ForcedHeight)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerHeight())
            .DisposeWith(this);

        // Character Viewer: reapply texture overrides when ForcedAssetPack or ForcedSubgroups change
        this.WhenAnyValue(x => x.ForcedAssetPack)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        ForcedSubgroups.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        // Character Viewer: reapply texture overrides when MixIns change (add/remove)
        ForcedMixIns.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        // Character Viewer: per-MixIn — refresh on AssetPack swap or inner subgroup edits
        DynamicData.ObservableListEx
            .Transform(ForcedMixIns.ToObservableChangeSet(), mixIn =>
            {
                mixIn.WhenAnyValue(y => y.ForcedAssetPack)
                    .Subscribe(_ => RefreshViewerTextures())
                    .DisposeWith(this);
                mixIn.ForcedSubgroups.ToObservableChangeSet()
                    .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
                    .Subscribe(_ => RefreshViewerTextures())
                    .DisposeWith(this);
                return mixIn;
            })
            .Subscribe()
            .DisposeWith(this);

        // Character Viewer: reapply texture overrides when Asset Replacer assignments change
        ForcedAssetReplacements.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        // Character Viewer: per-Replacer — refresh on group swap or subgroup-ID edits
        DynamicData.ObservableListEx
            .Transform(ForcedAssetReplacements.ToObservableChangeSet(), replacer =>
            {
                replacer.WhenAnyValue(y => y.ReplacerName)
                    .Subscribe(_ => RefreshViewerTextures())
                    .DisposeWith(this);
                replacer.SubgroupIDs.ToObservableChangeSet()
                    .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
                    .Subscribe(_ => RefreshViewerTextures())
                    .DisposeWith(this);
                return replacer;
            })
            .Subscribe()
            .DisposeWith(this);

        // Character Viewer: reapply BodyGen morphs on collection add/remove
        ForcedBodyGenMorphs.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerBodyGen())
            .DisposeWith(this);

        // Character Viewer: re-bake FaceGen when the NPC FormKey OR any head-part
        // assignment's FormKey changes. All viewer-NPC-refresh triggers share a
        // single throttle stream: per-subscription throttling would let every
        // head-part type's initial-value emission AND the NPC FormKey's initial-
        // value emission all race through after the same 300ms window expires —
        // producing 7+ concurrent RefreshViewerNpcAsync calls on screen load.
        // Those then race FaceGenPatcher's per-NPC temp extraction at
        // S:\Temp\<plugin>_<formId>_facegen.nif.
        var npcRefreshTriggers = new List<IObservable<Unit>>();
        npcRefreshTriggers.Add(
            this.WhenAnyValue(x => x.NPCFormKey)
                .Where(fk => !fk.IsNull && lk != null)
                .Select(_ => Unit.Default));
        npcRefreshTriggers.AddRange(
            HeadParts.Values
                .Where(hp => hp != null)
                .Select(hp => hp.WhenAnyValue(x => x.FormKey).Select(_ => Unit.Default)));
        Observable.Merge(npcRefreshTriggers)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => { var _t = RefreshViewerNpcAsync(); })
            .DisposeWith(this);

        CharacterViewer.Mode = ViewerMode.Full;

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
    public VM_CharacterViewer CharacterViewer { get; }
    /// <summary>
    /// Populates this view model from a saved <see cref="NPCAssignment"/> model:
    /// resolves the NPC and gender, links the forced asset pack/subgroups, mix-ins,
    /// asset order, height, BodyGen morphs, asset replacers, BodySlide preset, and
    /// head parts, logging warnings for any referenced item that no longer exists.
    /// </summary>
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
                    break;
                }
            }
            if (morphFound == false)
            {
                _logger.LogError("Warning: The forced BodyGen morph " + forcedMorph + " for NPC " + DispName + " no longer exists.");
            }
        }

        foreach (var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = _texMeshSettings.AssetPacks.FirstOrDefault(x => x.GroupName == replacer.AssetPackName);
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
            // Backward-compat shim (verified-not-a-bug B53): ensure every head-part type exists on the model. The
            // patcher indexes SpecificNPCAssignment.HeadParts[type] directly (HeadPartSelector, no ContainsKey
            // guard), so an older assignment missing a newer type (e.g. Scars) must be backfilled or the patcher
            // would throw KeyNotFoundException. When the type is present the VM loads it; when absent it keeps its default.
            if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }
            else
            {
                HeadParts[headPartType].CopyInFromModel(model.HeadParts[headPartType], headPartType, _headPartSettings, this, this, _environmentProvider);
            }
        }

        DispName = _converters.CreateNPCDispNameFromFormKey(NPCFormKey);
    }

    /// <summary>
    /// Finds the asset pack named <paramref name="assetPackName"/> in <paramref name="assetPacks"/>,
    /// assigns it as the view model's forced pack, and resolves the model's subgroup IDs
    /// into forced subgroups (logging warnings for any missing pack or subgroup).
    /// Returns whether the asset pack was found.
    /// </summary>
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
                break; // asset-pack GroupNames are matched as the first hit; stop so a duplicate name does not re-add subgroups
            }
        }

        if (!assetPackFound)
        {
            logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + model.DispName + " no longer exists.");
        }

        return assetPackFound;
    }

    /// <summary>
    /// Mix-in counterpart of <see cref="LinkAssetPackToForcedAssignment"/>: links the
    /// named asset pack and its subgroup IDs onto a mix-in view model from its
    /// <see cref="NPCAssignment.MixInAssignment"/> model. Returns whether the pack was found.
    /// </summary>
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
                break; // asset-pack GroupNames are matched as the first hit; stop so a duplicate name does not re-add subgroups
            }
        }

        if (!assetPackFound)
        {
            logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + npcName + " no longer exists.");
        }

        return assetPackFound;
    }

    /// <summary>
    /// Serializes this view model back into a fresh <see cref="NPCAssignment"/> model:
    /// forced asset pack/subgroup IDs, asset replacers, mix-ins (deduplicated by pack
    /// name), asset order, parsed height, BodyGen morph labels, BodySlide preset,
    /// NPC FormKey, and per-type head parts.
    /// </summary>
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

    /// <summary>
    /// Recomputes the available primary and mix-in asset pack lists for the assignment,
    /// keeping only selected packs matching the NPC's gender. Adds newly-eligible packs
    /// and removes no-longer-eligible ones in place (deliberately avoiding LINQ
    /// RemoveWhere, which would transiently clear the forced selection — see code note).
    /// </summary>
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

    /// <summary>
    /// Rebuilds the assignment's available top-level subgroups from its forced asset
    /// pack, excluding any top-level group already represented (directly or via a
    /// descendant) among the currently forced subgroups.
    /// </summary>
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

    /// <summary>Recursively tests whether the subgroup tree contains a subgroup with the given ID.</summary>
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

    /// <summary>
    /// Rebuilds the assignment's available BodyGen morph list from the forced asset
    /// pack's tracked config (or, failing that, the gender-appropriate current config),
    /// excluding morphs already forced and any whose template groups collide with an
    /// already-forced morph's groups.
    /// </summary>
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

    /// <summary>
    /// Selects the gender-appropriate subscribed BodySlide collection and rebuilds the
    /// available list with a leading blank entry followed by those presets.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    //  CHARACTER VIEWER REFRESH
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reloads the viewer's NPC meshes for the current FormKey — baking a FaceGen
    /// preview NIF when any head-part overrides are active, otherwise a plain load —
    /// then reapplies the active texture, BodySlide, and BodyGen overrides.
    /// </summary>
    private async Task RefreshViewerNpcAsync()
    {
        if (NPCFormKey.IsNull || lk == null)
        {
            return;
        }

        // If any head part overrides are active, reload the NPC with a baked FaceGen
        // preview NIF so the swapped parts are visible. Otherwise, plain LoadNpcAsync.
        var hpAssignments = HeadParts
            .Where(kv => kv.Value != null && !kv.Value.FormKey.IsNull)
            .ToDictionary(kv => kv.Key, kv => kv.Value.FormKey);

        if (hpAssignments.Count > 0)
        {
            await CharacterViewer.ApplyHeadPartsAsync(NPCFormKey, lk, hpAssignments);
        }
        else
        {
            await CharacterViewer.LoadNpcAsync(NPCFormKey, lk);
        }

        // After loading meshes, apply current overrides if any
        RefreshViewerTextures();
        RefreshViewerBodySlide();
        RefreshViewerBodyGen();
    }

    /// <summary>
    /// Collects all texture/mesh path overrides from the forced subgroups, mix-in
    /// subgroups, and asset-replacer subgroups (resolved by ID), and applies them to
    /// the loaded viewer meshes. Each contributing subgroup's paths are resolved against
    /// <em>its own</em> asset pack's record template (via <see cref="SubgroupTextureMapper"/>),
    /// so the target body part / slots come from the real armature and worn-armor
    /// AlternateTextures land on their named sub-shape — and auxiliary armatures (e.g. a
    /// slot-52 mesh) are synthesized as mesh overrides, which the flat texture path could not do.
    /// </summary>
    private void RefreshViewerTextures()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0)
        {
            return;
        }

        INpcGetter? previewNpc = null;
        if (!NPCFormKey.IsNull && lk != null)
        {
            lk.TryResolve<INpcGetter>(NPCFormKey, out previewNpc);
        }

        var textureOverrides = new Dictionary<SubgroupTextureMapper.OverrideKey, TextureOverride>();
        var meshOverrides = new Dictionary<int, MeshOverride>();

        void AddSubgroup(VM_SubgroupPlaceHolder sg)
        {
            if (sg?.AssociatedModel?.Paths == null) return;
            var ctx = SubgroupTextureMapper.BuildContext(sg.ParentAssetPack, previewNpc, lk);
            foreach (var kv in _subgroupTextureMapper.MapPathsTextures(sg.AssociatedModel.Paths, ctx))
            {
                textureOverrides[kv.Key] = kv.Value;
            }
            foreach (var mo in _subgroupTextureMapper.MapPathsMeshOverrides(sg.AssociatedModel.Paths, ctx))
            {
                meshOverrides[mo.BipedSlots] = mo;
            }
        }

        // Primary forced subgroups
        foreach (var sg in ForcedSubgroups)
        {
            AddSubgroup(sg);
        }

        // Mix-In forced subgroups
        foreach (var mixIn in ForcedMixIns)
        {
            if (mixIn?.ForcedSubgroups == null) continue;
            foreach (var sg in mixIn.ForcedSubgroups)
            {
                AddSubgroup(sg);
            }
        }

        // Asset Replacer subgroups — resolve each ID against the replacer group's subgroup tree
        foreach (var replacer in ForcedAssetReplacements)
        {
            if (replacer?.SubscribedReplacerGroup?.Subgroups == null) continue;
            foreach (var idMember in replacer.SubgroupIDs)
            {
                if (string.IsNullOrEmpty(idMember?.Content)) continue;
                var sg = VM_SubgroupPlaceHolder.GetSubgroupByID(replacer.SubscribedReplacerGroup.Subgroups, idMember.Content);
                AddSubgroup(sg);
            }
        }

        if (textureOverrides.Count > 0)
        {
            CharacterViewer.ApplyTextureOverrides(textureOverrides.Values);
        }
        CharacterViewer.ApplyMeshOverrides(meshOverrides.Values);
    }

    /// <summary>Pushes the parsed positive <see cref="ForcedHeight"/> as the viewer's height scale override, or clears it.</summary>
    private void RefreshViewerHeight()
    {
        if (!string.IsNullOrWhiteSpace(ForcedHeight) && float.TryParse(ForcedHeight, out var h) && h > 0f)
        {
            CharacterViewer.HeightOverride = h;
        }
        else
        {
            CharacterViewer.HeightOverride = null;
        }
    }

    /// <summary>Looks up the forced BodySlide preset by label among the available presets and applies it to the viewer.</summary>
    private void RefreshViewerBodySlide()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0 || string.IsNullOrEmpty(ForcedBodySlide))
        {
            return;
        }

        // Look up the BodySlideSetting from the available body slides by label
        var matchingPreset = AvailableBodySlides?
            .FirstOrDefault(bs => bs.AssociatedModel?.Label == ForcedBodySlide)
            ?.AssociatedModel;

        if (matchingPreset != null)
        {
            CharacterViewer.ApplyBodySlide(matchingPreset, CharacterViewer.NpcWeight);
        }
    }

    /// <summary>Applies the forced BodyGen morph templates to the viewer using the gender-appropriate preview slider group.</summary>
    private void RefreshViewerBodyGen()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0) return;
        if (ForcedBodyGenMorphs == null || ForcedBodyGenMorphs.Count == 0) return;

        string sliderGroup = Gender == Gender.Female
            ? (_bodyGenSettings?.PreviewSliderGroupFemale ?? string.Empty)
            : (_bodyGenSettings?.PreviewSliderGroupMale ?? string.Empty);

        var templates = ForcedBodyGenMorphs
            .Where(m => m?.AssociatedModel != null)
            .Select(m => m.AssociatedModel)
            .ToList();
        if (templates.Count == 0) return;

        CharacterViewer.ApplyBodyGen(templates, sliderGroup, CharacterViewer.NpcWeight);
    }

    /// <summary>
    /// Refreshes display name and gender from the current NPC FormKey and rebuilds all
    /// dependent lists (asset packs, subgroups, morphs, BodySlides). No-op if no NPC is set.
    /// </summary>
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
        
    /// <summary>Rebuilds the available asset packs and subgroups (subset of <see cref="RefreshAll"/>) after an asset-pack change.</summary>
    public void RefreshAssets()
    {
        UpdateAvailableAssetPacks(this);
        UpdateAvailableSubgroups(this);
    }

    /// <summary>
    /// Resolves the NPC's gender from its FormKey via the load order, returning
    /// <see cref="Gender.Female"/> or <see cref="Gender.Male"/>. Defaults to Male and
    /// logs an error if a non-null FormKey cannot be resolved.
    /// </summary>
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

    /// <summary>
    /// Rebuilds <see cref="ForcedMixIns"/> from the model's mix-in assignments, linking
    /// each to its asset pack/subgroups and carrying over its declined flag; mix-ins
    /// whose asset pack is missing are dropped.
    /// </summary>
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

    /// <summary>Applies the name search filter to each available subgroup's visibility (outer assignment).</summary>
    private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
    {
        foreach (var subgroup in AvailableSubgroups)
        {
            subgroup.CheckVisibilitySpecificVM(searchText, caseSensitive, false);
        }
    }

    /// <summary>
    /// View model for a single forced mix-in asset pack assignment within a
    /// <see cref="VM_SpecificNPCAssignment"/>: a chosen mix-in pack with its forced
    /// subgroups, asset replacers, and a "decline" toggle. Backed by
    /// <see cref="NPCAssignment.MixInAssignment"/>.
    /// </summary>
    public class VM_MixInSpecificAssignment : VM, IHasForcedAssets
    {
        /// <summary>Autofac factory delegate creating a mix-in assignment under the given parent.</summary>
        public delegate VM_MixInSpecificAssignment Factory(VM_SpecificNPCAssignment parent);
        /// <summary>
        /// Wires up the mix-in editor: seeds the available mix-in pack list from the
        /// parent, registers delete/add-replacer commands, and sets up the reactive
        /// chains that recompute available subgroups, sync replacer parent packs,
        /// toggle subgroup visibility on decline, and apply the name-search filter.
        /// </summary>
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

            DeleteCommand = new RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    ParentCollection.Remove(this);
                }
            );

            DeleteForcedMixInSubgroup = new RelayCommand(
                canExecute: _ => true,
                execute: x => ForcedSubgroups.Remove((VM_SubgroupPlaceHolder)x)
            );

            AddForcedReplacer = new RelayCommand(
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

        /// <summary>
        /// Serializes a mix-in view model into a <see cref="NPCAssignment.MixInAssignment"/>
        /// model: declined flag, asset pack name, forced subgroup IDs, and asset replacers.
        /// </summary>
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

        /// <summary>Applies the name search filter to each available subgroup's visibility (mix-in).</summary>
        private void CheckSubgroupVisibility(string searchText, bool caseSensitive)
        {
            foreach (var subgroup in AvailableSubgroups)
            {
                subgroup.CheckVisibilitySpecificVM(searchText, caseSensitive, false);
            }
        }
    }
}

/// <summary>
/// Contract for view models that carry a forced asset pack plus its forced and
/// available subgroup collections, letting the shared subgroup-linking helpers
/// operate uniformly over primary assignments and mix-ins.
/// </summary>
public interface IHasForcedAssets
{
    public VM_AssetPack ForcedAssetPack { get; set; }
    ObservableCollection<VM_SubgroupPlaceHolder> ForcedSubgroups { get; set; }
    public ObservableCollection<VM_SubgroupPlaceHolder> AvailableSubgroups { get; set; }
}
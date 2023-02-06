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

namespace SynthEBD;

public class VM_SpecificNPCAssignment : VM, IHasForcedAssets, IHasSynthEBDGender, IHasHeadPartAssignments
{
    public delegate VM_SpecificNPCAssignment Factory();

    private IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_Settings_General _generalSettings;
    private readonly VM_SettingsTexMesh _texMeshSettings;
    private readonly VM_SettingsBodyGen _bodyGenSettings;
    private readonly VM_SettingsOBody _oBodySettings;
    private readonly VM_Settings_Headparts _headPartSettings;
    private readonly VM_SpecificNPCAssignment.Factory _specificNPCAssignmentFactory;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    private readonly VM_BodySlideSetting.Factory _bodySlideFactory;
    private readonly VM_HeadPartAssignment.Factory _headPartFactory;
    private readonly Converters _converters;

    public VM_SpecificNPCAssignment(
        IEnvironmentStateProvider environmentProvider,
        Logger logger, 
        SynthEBDPaths paths,
        VM_Settings_General general,
        VM_SettingsOBody oBody,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsTexMesh texMesh,
        VM_Settings_Headparts headParts,
        VM_AssetPack.Factory assetPackFactory,
        VM_BodySlideSetting.Factory bodySlideFactory,
        VM_HeadPartAssignment.Factory headPartFactory,
        VM_SpecificNPCAssignment.Factory specificNPCAssignmentFactory,
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
        _bodySlideFactory = bodySlideFactory;
        _headPartFactory = headPartFactory;
        _specificNPCAssignmentFactory = specificNPCAssignmentFactory;
        _converters = converters;
        SubscribedGeneralSettings = general;
        SubscribedOBodySettings = oBody;
        SubscribedBodyGenSettings = bodyGen;
        SubscribedHeadPartSettings = headParts;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        //this.ForcedAssetPack = assetPackFactory();

        this.SubscribedAssetPacks = texMesh.AssetPacks;

        this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => RefreshAll()).DisposeWith(this);

        this.SubscribedAssetPacks.ToObservableChangeSet().Subscribe(x => RefreshAssets()).DisposeWith(this);
        DynamicData.ObservableListEx
            .Transform(this.SubscribedAssetPacks.ToObservableChangeSet(), x => x.WhenAnyValue(y => y.IsSelected)
                .Subscribe(_ => RefreshAssets())
                .DisposeWith(this))
            .Subscribe()
            .DisposeWith(this);

        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x => UpdateAvailableSubgroups(this)).DisposeWith(this);
        this.ForcedSubgroups.ToObservableChangeSet().Subscribe(x => UpdateAvailableSubgroups(this)).DisposeWith(this);

        this.WhenAnyValue(x => x.SubscribedBodyGenSettings).Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);
        this.ForcedBodyGenMorphs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);
        this.SubscribedBodyGenSettings.MaleConfigs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);
        this.SubscribedBodyGenSettings.FemaleConfigs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);
        this.SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentMaleConfig).Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);
        this.SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentFemaleConfig).Subscribe(x => UpdateAvailableMorphs(this)).DisposeWith(this);

        this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => UpdateAvailableBodySlides()).DisposeWith(this);
        SubscribedOBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesFemale).Subscribe(x => UpdateAvailableBodySlides()).DisposeWith(this);
        SubscribedOBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesMale).Subscribe(x => UpdateAvailableBodySlides()).DisposeWith(this);

        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
        {
            ForcedAssetReplacements.Remove(ForcedAssetReplacements.Where(x => x.ParentAssetPack != ForcedAssetPack));
        }).DisposeWith(this);

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
            execute: x => ForcedSubgroups.Remove((VM_Subgroup)x)
        );

        DeleteForcedMorph = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedBodyGenMorphs.Remove((VM_BodyGenTemplate)x)
        );

        AddForcedMixIn = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedMixIns.Add(new VM_MixInSpecificAssignment(this, assetPackFactory, ForcedMixIns))
        );

        AddForcedReplacer = new RelayCommand(
            canExecute: _ => true,
            execute: x => ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
        );

        DeleteForcedMixInSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                var toDelete = (VM_Subgroup)x;
                foreach (var mixin in ForcedMixIns)
                {
                    if (mixin.ForcedSubgroups.Contains(toDelete))
                    {
                        mixin.ForcedSubgroups.Remove(toDelete);
                    }
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
    public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; } = new();
    public ObservableCollection<VM_MixInSpecificAssignment> ForcedMixIns { get; set; } = new();
    public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
    public string ForcedHeight { get; set; } = "";
    public ObservableCollection<VM_BodyGenTemplate> ForcedBodyGenMorphs { get; set; } = new();
    public string ForcedBodySlide { get; set; } = "";
    public Dictionary<HeadPart.TypeEnum, VM_HeadPartAssignment> HeadParts { get; set; } = new();

    //Needed by UI
    public ObservableCollection<VM_AssetPack> AvailableAssetPacks { get; set; } = new();
    public ObservableCollection<VM_AssetPack> SubscribedAssetPacks { get; set; }

    public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; } = new();

    public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; } = new();
    public ObservableCollection<VM_BodyGenTemplate> AvailableMorphs { get; set; } = new();
    public VM_SettingsBodyGen SubscribedBodyGenSettings { get; set; }
    public ObservableCollection<VM_BodySlideSetting> SubscribedBodySlides { get; set; }
    public ObservableCollection<VM_BodySlideSetting> AvailableBodySlides { get; set; }
    public VM_BodyGenTemplate SelectedTemplate { get; set; }

    public Gender Gender { get; set; }

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
    public static VM_SpecificNPCAssignment GetViewModelFromModel(
        NPCAssignment model, 
        VM_AssetPack.Factory assetPackFactory, 
        VM_SettingsTexMesh texMesh,
        VM_SettingsBodyGen bodyGen,
        VM_Settings_Headparts headParts,
        Factory specificNpcAssignmentFactory, 
        Logger logger,
        Converters converters,
        IEnvironmentStateProvider environmentProvider)
    {
        var viewModel = specificNpcAssignmentFactory();
        viewModel.NPCFormKey = model.NPCFormKey;

        if (viewModel.NPCFormKey.IsNull)
        {
            return null;
        }

        var npcFormLink = new FormLink<INpcGetter>(viewModel.NPCFormKey);

        if (!npcFormLink.TryResolve(environmentProvider.LinkCache, out var npcRecord))
        {
            logger.LogError("Warning: the target NPC of the Specific NPC Assignment with FormKey " + viewModel.NPCFormKey.ToString() + " was not found in the current load order.");
        }

        viewModel.Gender = GetGender(viewModel.NPCFormKey, logger, environmentProvider);

        if (model.AssetPackName.Length != 0)
        {
            LinkAssetPackToForcedAssignment(model, viewModel, model.AssetPackName, texMesh.AssetPacks, logger);
        }

        foreach (var forcedMixIn in model.MixInAssignments)
        {
            viewModel.ForcedMixIns.Add(VM_MixInSpecificAssignment.GetViewModelFromModel(forcedMixIn, viewModel, assetPackFactory, texMesh, viewModel.ForcedMixIns, logger));
        }

        if (model.Height != null)
        {
            viewModel.ForcedHeight = model.Height.ToString();
        }
        else
        {
            viewModel.ForcedHeight = "";
        }

        ObservableCollection<VM_BodyGenTemplate> templates = new ObservableCollection<VM_BodyGenTemplate>();
        switch (viewModel.Gender)
        {
            case Gender.Male:
                if (bodyGen.CurrentMaleConfig != null)
                {
                    templates = bodyGen.CurrentMaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplate>();
                }
                break;
            case Gender.Female:
                if (bodyGen.CurrentFemaleConfig != null)
                {
                    templates = bodyGen.CurrentFemaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplate>();
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
                    viewModel.ForcedBodyGenMorphs.Add(morph);
                    morphFound = true;
                    break; ;
                }
            }
            if (morphFound == false)
            {
                logger.LogError("Warning: The forced BodyGen morph " + forcedMorph + " for NPC " + viewModel.DispName + " no longer exists.");
            }
        }


        foreach (var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = texMesh.AssetPacks.Where(x => x.GroupName == replacer.AssetPackName).FirstOrDefault();
            if (parentAssetPack != null)
            {
                VM_AssetReplacementAssignment subVm = new VM_AssetReplacementAssignment(parentAssetPack, viewModel.ForcedAssetReplacements);
                subVm.CopyInViewModelFromModel(replacer);
                viewModel.ForcedAssetReplacements.Add(subVm);
            }
            else
            {
                logger.LogError("Warning: The forced Asset Replacer " + replacer.AssetPackName + " for NPC " + viewModel.DispName + " no longer exists.");
            }
        }

        viewModel.ForcedBodySlide = model.BodySlidePreset;

        foreach (var headPartType in viewModel.HeadParts.Keys)
        {
            if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }
            else
            {
                viewModel.HeadParts[headPartType] = VM_HeadPartAssignment.GetViewModelFromModel(model.HeadParts[headPartType], headPartType, headParts, viewModel, viewModel, environmentProvider);
            }
        }

        viewModel.DispName = converters.CreateNPCDispNameFromFormKey(viewModel.NPCFormKey);

        return viewModel;
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
                    var foundSubgroup = GetSubgroupByID(ap.Subgroups, id);
                    if (foundSubgroup != null)
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

    private static bool LinkAssetPackToForcedAssignment(NPCAssignment.MixInAssignment model, IHasForcedAssets viewModel, string assetPackName, ObservableCollection<VM_AssetPack> assetPacks, string npcName, Logger logger)
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
                    var foundSubgroup = GetSubgroupByID(ap.Subgroups, id);
                    if (foundSubgroup != null)
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

        foreach (var mixin in ForcedMixIns)
        {
            if (!model.MixInAssignments.Select(x => x.AssetPackName).Contains(mixin.ForcedAssetPack.GroupName))
            {
                model.MixInAssignments.Add(VM_MixInSpecificAssignment.DumpViewModelToModel(mixin));
            }
        }

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
        assignment.AvailableAssetPacks.Clear();
        assignment.AvailableMixInAssetPacks.Clear();
        foreach (var assetPack in assignment.SubscribedAssetPacks.Where(x => x.IsSelected))
        {
            if (assetPack.Gender == assignment.Gender)
            {
                if (assetPack.ConfigType == AssetPackType.Primary)
                {
                    assignment.AvailableAssetPacks.Add(assetPack);
                }
                else if (assetPack.ConfigType == AssetPackType.MixIn)
                {
                    assignment.AvailableMixInAssetPacks.Add(assetPack);
                }
            }
        }
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

    public static bool ContainsSubgroupID(ObservableCollection<VM_Subgroup> subgroups, string id)
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

    public static VM_Subgroup GetSubgroupByID(ObservableCollection<VM_Subgroup> subgroups, string id)
    {
        foreach (var sg in subgroups)
        {
            if (sg.ID == id) { return sg; }
            else
            {
                var candidate = GetSubgroupByID(sg.Subgroups, id);
                if (candidate != null) { return candidate; }
            }
        }
        return null;
    }

    public static void UpdateAvailableMorphs(VM_SpecificNPCAssignment assignment)
    {
        // clear available morphs besides the ones that are forced (removing those from the available morph list also clears their combobox selection)
        assignment.AvailableMorphs.Clear();

        var allTemplateList = new ObservableCollection<VM_BodyGenTemplate>();
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

        foreach (var candidateMorph in allTemplateList)
        {
            if (assignment.ForcedBodyGenMorphs.Contains(candidateMorph))
            {
                continue;
            }

            bool groupOccupied = false;

            var candidateGroups = candidateMorph.GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToArray();

            foreach (var alreadyForcedMorph in assignment.ForcedBodyGenMorphs)
            {
                var forcedGroups = alreadyForcedMorph.GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToArray();

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
        AvailableBodySlides = new ObservableCollection<VM_BodySlideSetting>() { _bodySlideFactory(_oBodySettings.DescriptorUI, _generalSettings.RaceGroupingEditor.RaceGroupings, AvailableBodySlides) }; // blank entry
        AvailableBodySlides.AddRange(SubscribedBodySlides);
    }
    /*
    public void TriggerDispNameUpdate(object sender, PropertyChangedEventArgs e)
    {
        if (this.NPCFormKey.IsNull == false)
        {
            this.DispName = Converters.CreateNPCDispNameFromFormKey(this.NPCFormKey);
        }
    }*/

    public void RefreshAll()
    {
        if (this.NPCFormKey.IsNull)
        {
            return;
        }

        this.DispName = _converters.CreateNPCDispNameFromFormKey(this.NPCFormKey);
        this.Gender = GetGender(this.NPCFormKey, _logger, _environmentProvider);

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

    /*
    public void TriggerGenderUpdate(object sender, PropertyChangedEventArgs e)
    {
        this.Gender = GetGender(this.NPCFormKey);
    }*/

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

    public class VM_MixInSpecificAssignment : VM, IHasForcedAssets
    {
        public VM_MixInSpecificAssignment(VM_SpecificNPCAssignment parent, VM_AssetPack.Factory assetPackFactory, ObservableCollection<VM_MixInSpecificAssignment> parentCollection)
        {
            ParentCollection = parentCollection;
            Parent = parent;

            this.AvailableMixInAssetPacks = Parent.AvailableMixInAssetPacks;
            this.ForcedAssetPack = assetPackFactory();

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x => UpdateAvailableSubgroups(this)).DisposeWith(this);
            this.ForcedSubgroups.CollectionChanged += TriggerAvailableSubgroupsUpdate;

            DeleteCommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    ParentCollection.Remove(this);
                }
            );

            DeleteForcedSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ForcedSubgroups.Remove((VM_Subgroup)x)
            );

            AddForcedReplacer = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
            );

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
            {
                foreach (var replacer in ForcedAssetReplacements)
                {
                    replacer.ParentAssetPack = ForcedAssetPack;
                }
            }).DisposeWith(this);
        }
        public VM_AssetPack ForcedAssetPack { get; set; }
        public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; }
        public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; } = new();
        public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; } = new();
        public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
        public ObservableCollection<VM_MixInSpecificAssignment> ParentCollection { get; set; }
        public VM_SpecificNPCAssignment Parent { get; set; }

        public RelayCommand DeleteCommand { get; set; }
        public RelayCommand DeleteForcedSubgroup { get; set; }
        public RelayCommand AddForcedReplacer { get; set; }

        public void TriggerAvailableSubgroupsUpdate(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateAvailableSubgroups(this);
        }

        public static VM_MixInSpecificAssignment GetViewModelFromModel(
            NPCAssignment.MixInAssignment model, 
            VM_SpecificNPCAssignment parent,
            VM_AssetPack.Factory assetPackFactory, 
            VM_SettingsTexMesh texMesh,
            ObservableCollection<VM_MixInSpecificAssignment> parentCollection,
            Logger logger)
        {
            var viewModel = new VM_MixInSpecificAssignment(parent, assetPackFactory, parentCollection);
            LinkAssetPackToForcedAssignment(model, viewModel, model.AssetPackName, texMesh.AssetPacks, parent.DispName, logger);
            return viewModel;
        }
        
        public static NPCAssignment.MixInAssignment DumpViewModelToModel(VM_MixInSpecificAssignment viewModel)
        {
            NPCAssignment.MixInAssignment model = new NPCAssignment.MixInAssignment();
            model.AssetPackName = viewModel.ForcedAssetPack.GroupName;
            model.SubgroupIDs = viewModel.ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

            model.AssetReplacerAssignments.Clear();
            foreach (var replacer in viewModel.ForcedAssetReplacements)
            {
                model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
            }
            return model;
        }
    }
}

public interface IHasForcedAssets
{
    public VM_AssetPack ForcedAssetPack { get; set; }
    ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; }
    public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; }
}
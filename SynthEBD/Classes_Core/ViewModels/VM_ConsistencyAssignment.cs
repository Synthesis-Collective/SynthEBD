using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using ReactiveUI;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Editor view model of an <see cref="NPCAssignment"/> as stored in the consistency record: the
/// previously assigned asset pack/subgroups, mix-in and replacer assignments, BodyGen morphs,
/// BodySlide preset, height, and per-type head parts for one NPC. Drives a read-only character
/// viewer that previews the consistency selections as they change.
/// </summary>
public class VM_ConsistencyAssignment : VM, IHasSynthEBDGender
{
    private readonly VM_SettingsTexMesh _texMeshUI;
    private readonly VM_SettingsOBody _oBodySettings;
    private readonly VM_SettingsBodyGen _bodyGenSettings;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for constructing a <see cref="VM_ConsistencyAssignment"/>.</summary>
    public delegate VM_ConsistencyAssignment Factory(NPCAssignment model);
    /// <summary>
    /// Seeds the model and the read-only character viewer, follows the environment link cache,
    /// tracks the assigned flags, wires the delete-asset-pack/bodyslide/height commands, and
    /// registers the throttled subscriptions that refresh the viewer's NPC, textures, BodySlide, and
    /// height as the consistency selections change.
    /// </summary>
    public VM_ConsistencyAssignment(
        NPCAssignment model,
        VM_SettingsTexMesh texMeshUI,
        VM_SettingsOBody oBodySettings,
        VM_SettingsBodyGen bodyGenSettings,
        IEnvironmentStateProvider environmentProvider,
        Logger logger,
        VM_CharacterViewer characterViewer)
    {
        AssociatedModel = model;
        _texMeshUI = texMeshUI;
        _oBodySettings = oBodySettings;
        _bodyGenSettings = bodyGenSettings;
        _environmentProvider = environmentProvider;
        _logger = logger;

        CharacterViewer = characterViewer;
        CharacterViewer.DisposeWith(this);
        CharacterViewer.Mode = ViewerMode.ReadOnly;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.AssetPackName).Subscribe(x => AssetPackAssigned = AssetPackName != null && AssetPackName.Any()).DisposeWith(this);
        this.WhenAnyValue(x => x.BodySlidePreset).Subscribe(x => BodySlideAssigned = BodySlidePreset != null && BodySlidePreset.Any()).DisposeWith(this);
        this.WhenAnyValue(x => x.Height).Subscribe(x => HeightAssigned = Height != null && Height.Any()).DisposeWith(this);

        DeleteAssetPackCommand = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                AssetPackName = "";
                Subgroups.Clear();
            }
        );

        DeleteBodySlideCommand = new RelayCommand(
            canExecute: _ => true,
            execute: x => this.BodySlidePreset = ""
        );

        DeleteHeightCommand = new RelayCommand(
            canExecute: _ => true,
            execute: x => this.Height = ""
        );

        // ───────────────────────────────────────────────────────────
        // Character Viewer refresh subscriptions
        // ───────────────────────────────────────────────────────────
        this.WhenAnyValue(x => x.NPCFormKey)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Where(fk => !fk.IsNull && lk != null)
            .Subscribe(fk => _ = RefreshViewerNpcAsync())
            .DisposeWith(this);

        this.WhenAnyValue(x => x.BodySlidePreset)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerBodySlide())
            .DisposeWith(this);

        this.WhenAnyValue(x => x.Height)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerHeight())
            .DisposeWith(this);

        this.WhenAnyValue(x => x.AssetPackName)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        Subgroups.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        MixInAssignments.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);

        AssetReplacements.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshViewerTextures())
            .DisposeWith(this);
    }

    public string AssetPackName { get; set; }
    public ObservableCollection<VM_ConsistencySubgroupAssignment> Subgroups { get; set; } = new();
    public ObservableCollection<VM_MixInConsistencyAssignment> MixInAssignments { get; set; } = new();
    public ObservableCollection<VM_AssetReplacementAssignment> AssetReplacements { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> BodyGenMorphNames { get; set; } = new();
    public string BodySlidePreset { get; set; } = "";
    public string Height { get; set; }
    public Dictionary<HeadPart.TypeEnum, VM_HeadPartConsistency> HeadParts { get; set; } = new()
    {
        { HeadPart.TypeEnum.Eyebrows, null },
        { HeadPart.TypeEnum.Eyes, null },
        { HeadPart.TypeEnum.Face, null },
        { HeadPart.TypeEnum.FacialHair, null },
        { HeadPart.TypeEnum.Hair, null },
        { HeadPart.TypeEnum.Misc, null },
        { HeadPart.TypeEnum.Scars, null }
    };
    public string DispName { get; set; }
    public FormKey NPCFormKey { get; set; }

    public NPCAssignment AssociatedModel { get; set; }

    public RelayCommand DeleteAssetPackCommand { get; set; }
    public RelayCommand DeleteBodySlideCommand { get; set; }
    public RelayCommand DeleteHeightCommand { get; set; }

    public bool AssetPackAssigned { get; set; } = false;
    public bool BodySlideAssigned { get; set; } = false;
    public bool HeightAssigned { get; set; } = false;
    public Gender Gender { get; set; }
    public ILinkCache lk { get; private set; }
    public VM_CharacterViewer CharacterViewer { get; }

    /// <summary>Resolves an asset pack name + subgroup ID to a human-readable "->"-joined name chain, or "Not Loaded" if unresolved.</summary>
    private string GetSubgroupNameChain(string assetPackName, string subgroupID)
    {
        string subgroupName = "Not Loaded";

        var assetPack = _texMeshUI.AssetPacks.Where(x => x.GroupName == assetPackName).FirstOrDefault();
        if (assetPack != null && assetPack.TryGetSubgroupByID(subgroupID, out var subgroup))
        {
            subgroupName = subgroup.GetNameChain(" -> ");
        }

        return subgroupName;
    }

    /// <summary>Model → view model: loads all consistency selections (asset pack/subgroups, mix-ins, replacers, morphs, BodySlide, height, head parts, NPC identity) from an <see cref="NPCAssignment"/>.</summary>
    public void GetViewModelFromModel(NPCAssignment model)
    {
        AssetPackName = model.AssetPackName;
        Subgroups.Clear();
        if (model.SubgroupIDs != null)
        {
            foreach (var id in model.SubgroupIDs)
            {
                var subgroupEntry = new VM_ConsistencySubgroupAssignment(Subgroups);
                subgroupEntry.SubgroupID = id;
                subgroupEntry.DispString = GetSubgroupNameChain(AssetPackName, id);
                Subgroups.Add(subgroupEntry);
            }
        }
        MixInAssignments.Clear();
        foreach (var mixIn in model.MixInAssignments)
        {
            var mixInVM = new VM_MixInConsistencyAssignment(MixInAssignments) { AssetPackName = mixIn.AssetPackName};
            foreach (var id in mixIn.SubgroupIDs)
            {
                var subgroupEntry = new VM_ConsistencySubgroupAssignment(mixInVM.Subgroups);
                subgroupEntry.SubgroupID = id;
                subgroupEntry.DispString = GetSubgroupNameChain(mixIn.AssetPackName, id);
                mixInVM.Subgroups.Add(subgroupEntry);
            }
            mixInVM.DeclinedAssignment = mixIn.DeclinedAssignment;
            MixInAssignments.Add(mixInVM);
        }
        AssetReplacements.Clear();
        foreach(var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = _texMeshUI.AssetPacks.Where(x => x.GroupName == replacer.AssetPackName).FirstOrDefault();
            if (parentAssetPack != null)
            {
                VM_AssetReplacementAssignment subVm = new VM_AssetReplacementAssignment(parentAssetPack, AssetReplacements);
                subVm.CopyInViewModelFromModel(replacer);
                AssetReplacements.Add(subVm);
            }
        }
        BodyGenMorphNames.Clear();
        if (model.BodyGenMorphNames != null)
        {
            foreach (var morph in model.BodyGenMorphNames)
            {
                BodyGenMorphNames.Add(new VM_CollectionMemberString(morph, BodyGenMorphNames));
            }
        }
        BodySlidePreset = model.BodySlidePreset;
        if (model.Height != null)
        {
            Height = model.Height.ToString();
        }
        else
        {
            Height = "";
        }

        foreach (var headPartType in HeadParts.Keys)
        {
            if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }
            else
            {
                HeadParts[headPartType] = VM_HeadPartConsistency.GetViewModelFromModel(model.HeadParts[headPartType]);
            }
        }

        DispName = model.DispName;
        NPCFormKey = model.NPCFormKey;
        Gender = VM_SpecificNPCAssignment.GetGender(NPCFormKey, _logger, _environmentProvider);
    }

    /// <summary>View model → model: writes all consistency selections back into the associated <see cref="NPCAssignment"/>, parsing height and logging an error if it cannot be parsed.</summary>
    public void DumpViewModelToModel()
    {
        AssociatedModel.AssetPackName = AssetPackName;
        AssociatedModel.SubgroupIDs = Subgroups.Select(x => x.SubgroupID).ToList();
        if (AssociatedModel.SubgroupIDs.Count == 0) { AssociatedModel.SubgroupIDs = null; }
        AssociatedModel.MixInAssignments.Clear();
        foreach (var mixInVM in MixInAssignments)
        {
            AssociatedModel.MixInAssignments.Add(new NPCAssignment.MixInAssignment() { AssetPackName = mixInVM.AssetPackName, SubgroupIDs = mixInVM.Subgroups.Select(x => x.SubgroupID).ToList(), DeclinedAssignment = mixInVM.DeclinedAssignment });
        }
        AssociatedModel.AssetReplacerAssignments.Clear();
        foreach (var replacer in AssetReplacements)
        {
            AssociatedModel.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
        }
        AssociatedModel.BodyGenMorphNames = BodyGenMorphNames.Select(x => x.Content).ToList();
        if (AssociatedModel.BodyGenMorphNames.Count == 0) { AssociatedModel.BodyGenMorphNames = null; }
        AssociatedModel.BodySlidePreset = BodySlidePreset;
        if (Height == "")
        {
            AssociatedModel.Height = null;
        }
        else if (float.TryParse(Height, out var height))
        {
            AssociatedModel.Height = height;
        }
        else
        {
            _logger.LogError("Error parsing consistency assignment " + DispName + ". Cannot parse height: " + Height);
        }

        foreach (var headPartType in HeadParts.Keys)
        {
            if (!AssociatedModel.HeadParts.ContainsKey(headPartType))
            {
                AssociatedModel.HeadParts.Add(headPartType, new());
            }
            AssociatedModel.HeadParts[headPartType] = HeadParts[headPartType].DumpToModel();
        }

        AssociatedModel.DispName = DispName;
        AssociatedModel.NPCFormKey = NPCFormKey;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CHARACTER VIEWER REFRESH
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Loads the assigned NPC into the character viewer (applying any consistency head parts) and then refreshes textures, BodySlide, and BodyGen.</summary>
    private async Task RefreshViewerNpcAsync()
    {
        if (NPCFormKey.IsNull || lk == null)
        {
            return;
        }

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

        RefreshViewerTextures();
        RefreshViewerBodySlide();
        RefreshViewerBodyGen();
    }

    /// <summary>Resolves the stored BodyGen morph names to their gendered template specs and applies them to the character viewer.</summary>
    private void RefreshViewerBodyGen()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0) return;
        if (BodyGenMorphNames == null || BodyGenMorphNames.Count == 0) return;

        var configs = Gender == Gender.Female ? _bodyGenSettings.FemaleConfigs : _bodyGenSettings.MaleConfigs;
        if (configs == null) return;

        // Resolve each stored morph name to its template's Specs. BodyGen templates are
        // unique by Label within a gender's config database, so a simple flat lookup works.
        var resolved = new List<BodyGenConfig.BodyGenTemplate>();
        foreach (var name in BodyGenMorphNames)
        {
            if (string.IsNullOrWhiteSpace(name?.Content)) continue;
            foreach (var config in configs)
            {
                var match = config.TemplateMorphUI?.Templates
                    .FirstOrDefault(t => string.Equals(t.Label, name.Content, StringComparison.OrdinalIgnoreCase));
                if (match?.AssociatedModel != null)
                {
                    resolved.Add(match.AssociatedModel);
                    break;
                }
            }
        }

        if (resolved.Count == 0) return;

        string sliderGroup = Gender == Gender.Female
            ? (_bodyGenSettings.PreviewSliderGroupFemale ?? string.Empty)
            : (_bodyGenSettings.PreviewSliderGroupMale ?? string.Empty);

        CharacterViewer.ApplyBodyGen(resolved, sliderGroup, CharacterViewer.NpcWeight);
    }

    /// <summary>Collects texture/mesh path overrides from the primary, mix-in, and replacer subgroup selections and applies them to the character viewer.</summary>
    private void RefreshViewerTextures()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0)
        {
            return;
        }

        var overrides = new List<FilePathReplacement>();

        // Primary asset-pack subgroups
        var primaryPack = _texMeshUI.AssetPacks.FirstOrDefault(p => p.GroupName == AssetPackName);
        if (primaryPack != null)
        {
            foreach (var entry in Subgroups)
            {
                if (entry?.SubgroupID != null
                    && primaryPack.TryGetSubgroupByID(entry.SubgroupID, out var sg)
                    && sg.AssociatedModel?.Paths != null)
                {
                    overrides.AddRange(sg.AssociatedModel.Paths);
                }
            }
        }

        // MixIn asset-pack subgroups (MixIn packs live alongside primary in _texMeshUI.AssetPacks)
        foreach (var mixIn in MixInAssignments)
        {
            if (mixIn == null || string.IsNullOrEmpty(mixIn.AssetPackName)) continue;
            var mixPack = _texMeshUI.AssetPacks.FirstOrDefault(p => p.GroupName == mixIn.AssetPackName);
            if (mixPack == null) continue;
            foreach (var entry in mixIn.Subgroups)
            {
                if (entry?.SubgroupID != null
                    && mixPack.TryGetSubgroupByID(entry.SubgroupID, out var sg)
                    && sg.AssociatedModel?.Paths != null)
                {
                    overrides.AddRange(sg.AssociatedModel.Paths);
                }
            }
        }

        // AssetReplacer subgroups — look up within the replacer group's subgroup tree
        foreach (var replacer in AssetReplacements)
        {
            if (replacer?.SubscribedReplacerGroup?.Subgroups == null) continue;
            foreach (var id in replacer.SubgroupIDs)
            {
                if (id?.Content == null) continue;
                var match = VM_SubgroupPlaceHolder.GetSubgroupByID(replacer.SubscribedReplacerGroup.Subgroups, id.Content);
                if (match?.AssociatedModel?.Paths != null)
                {
                    overrides.AddRange(match.AssociatedModel.Paths);
                }
            }
        }

        if (overrides.Count > 0)
        {
            CharacterViewer.ApplyTextureOverrides(overrides);
        }
    }

    /// <summary>Applies the parsed height to the character viewer's height override, or clears it when height is blank/invalid.</summary>
    private void RefreshViewerHeight()
    {
        if (!string.IsNullOrWhiteSpace(Height) && float.TryParse(Height, out var h) && h > 0f)
        {
            CharacterViewer.HeightOverride = h;
        }
        else
        {
            CharacterViewer.HeightOverride = null;
        }
    }

    /// <summary>Looks up the assigned BodySlide preset among the gendered available presets and applies it to the character viewer.</summary>
    private void RefreshViewerBodySlide()
    {
        if (CharacterViewer.Renderer.Meshes.Count == 0 || string.IsNullOrEmpty(BodySlidePreset))
        {
            return;
        }

        var availableBodySlides = Gender switch
        {
            Gender.Male   => _oBodySettings.BodySlidesUI.BodySlidesMale,
            Gender.Female => _oBodySettings.BodySlidesUI.BodySlidesFemale,
            _ => null
        };
        if (availableBodySlides == null) return;

        var match = availableBodySlides
            .FirstOrDefault(bs => bs.AssociatedModel?.Label == BodySlidePreset)
            ?.AssociatedModel;

        if (match != null)
        {
            CharacterViewer.ApplyBodySlide(match, CharacterViewer.NpcWeight);
        }
    }

    /// <summary>View model for a single mix-in asset pack consistency assignment (its pack name, chosen subgroups, and declined flag) within a <see cref="VM_ConsistencyAssignment"/>.</summary>
    public class VM_MixInConsistencyAssignment
    {
        /// <summary>Seeds the parent collection and wires the DeleteCommand to remove this mix-in from it.</summary>
        public VM_MixInConsistencyAssignment(ObservableCollection<VM_MixInConsistencyAssignment> parentCollection)
        {
            ParentCollection = parentCollection;

            DeleteCommand = new RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    ParentCollection.Remove(this);
                }
            );
        }
        public string AssetPackName { get; set; }
        public ObservableCollection<VM_ConsistencySubgroupAssignment> Subgroups { get; set; } = new();
        public ObservableCollection<VM_MixInConsistencyAssignment> ParentCollection { get; set; }
        public bool DeclinedAssignment { get; set; } = false;
        public RelayCommand DeleteCommand { get; set; }
    }

    /// <summary>View model for a single consistency subgroup selection: its subgroup ID plus a resolved display string.</summary>
    public class VM_ConsistencySubgroupAssignment
    {
        /// <summary>Seeds the parent collection and wires the DeleteCommand to remove this entry from it.</summary>
        public VM_ConsistencySubgroupAssignment(ObservableCollection<VM_ConsistencySubgroupAssignment> parentCollection)
        {
            ParentCollection = parentCollection;

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }
        public string SubgroupID { get; set; }
        public string DispString { get; set; }
        public ObservableCollection<VM_ConsistencySubgroupAssignment> ParentCollection { get; set; } = new();
        public RelayCommand DeleteCommand { get; }
    }
}

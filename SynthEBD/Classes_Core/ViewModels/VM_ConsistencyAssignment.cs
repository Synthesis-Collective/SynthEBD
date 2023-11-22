using Mutagen.Bethesda.Plugins;
using System.Collections.ObjectModel;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

public class VM_ConsistencyAssignment : VM, IHasSynthEBDGender
{
    private readonly VM_SettingsTexMesh _texMeshUI;
    private readonly Logger _logger;
    public delegate VM_ConsistencyAssignment Factory(NPCAssignment model);
    public VM_ConsistencyAssignment(NPCAssignment model, VM_SettingsTexMesh texMeshUI, Logger logger)
    {
        AssociatedModel = model;
        _texMeshUI = texMeshUI;
        _logger = logger;

        this.WhenAnyValue(x => x.AssetPackName).Subscribe(x => AssetPackAssigned = AssetPackName != null && AssetPackName.Any()).DisposeWith(this);
        this.WhenAnyValue(x => x.BodySlidePreset).Subscribe(x => BodySlideAssigned = BodySlidePreset != null && BodySlidePreset.Any()).DisposeWith(this);
        this.WhenAnyValue(x => x.Height).Subscribe(x => HeightAssigned = Height != null && Height.Any()).DisposeWith(this);

        DeleteAssetPackCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => 
            {
                AssetPackName = "";
                Subgroups.Clear();
            }
        );

        DeleteBodySlideCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.BodySlidePreset = ""
        );

        DeleteHeightCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.Height = ""
        );
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
    public Gender Gender { get; set; } // only needs to satisfy the HeadPart assignment view model.

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

    public void GetViewModelFromModel(NPCAssignment model)
    {
        AssetPackName = model.AssetPackName;
        Subgroups = new ObservableCollection<VM_ConsistencySubgroupAssignment>();
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
        foreach (var mixIn in model.MixInAssignments)
        {
            var mixInVM = new VM_MixInConsistencyAssignment(MixInAssignments) { AssetPackName = mixIn.AssetPackName};
            foreach (var id in mixIn.SubgroupIDs)
            {
                var subgroupEntry = new VM_ConsistencySubgroupAssignment(mixInVM.Subgroups);
                subgroupEntry.SubgroupID = id;
                subgroupEntry.DispString = GetSubgroupNameChain(mixIn.AssetPackName, id);
                Subgroups.Add(subgroupEntry);
            }
            mixInVM.DeclinedAssignment = mixIn.DeclinedAssignment;
            MixInAssignments.Add(mixInVM);
        }
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
        BodyGenMorphNames = new ObservableCollection<VM_CollectionMemberString>();
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
    }

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

    public class VM_MixInConsistencyAssignment
    {
        public VM_MixInConsistencyAssignment(ObservableCollection<VM_MixInConsistencyAssignment> parentCollection)
        {
            ParentCollection = parentCollection;

            DeleteCommand = new SynthEBD.RelayCommand(
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

    public class VM_ConsistencySubgroupAssignment
    {
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
using Mutagen.Bethesda.Plugins;
using System.Collections.ObjectModel;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class VM_ConsistencyAssignment : VM, IHasSynthEBDGender
{
    private readonly Logger _logger;
    public VM_ConsistencyAssignment(Logger logger)
    {
        _logger = logger;

        this.WhenAnyValue(x => x.AssetPackName).Subscribe(x => AssetPackAssigned = AssetPackName != null && AssetPackName.Any());
        this.WhenAnyValue(x => x.BodySlidePreset).Subscribe(x => BodySlideAssigned = BodySlidePreset != null && BodySlidePreset.Any());
        this.WhenAnyValue(x => x.Height).Subscribe(x => HeightAssigned = Height != null && Height.Any());

        DeleteAssetPackCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => 
            {
                this.AssetPackName = "";
                this.SubgroupIDs.Clear();
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
    public ObservableCollection<VM_CollectionMemberString> SubgroupIDs { get; set; } = new();
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

    public RelayCommand DeleteAssetPackCommand { get; set; }
    public RelayCommand DeleteBodySlideCommand { get; set; }
    public RelayCommand DeleteHeightCommand { get; set; }

    public bool AssetPackAssigned { get; set; } = false;
    public bool BodySlideAssigned { get; set; } = false;
    public bool HeightAssigned { get; set; } = false;
    public Gender Gender { get; set; } // only needs to satisfy the HeadPart assignment view model.

    public static VM_ConsistencyAssignment GetViewModelFromModel(NPCAssignment model, ObservableCollection<VM_AssetPack> AssetPackVMs, Logger logger)
    {
        VM_ConsistencyAssignment viewModel = new VM_ConsistencyAssignment(logger);
        viewModel.AssetPackName = model.AssetPackName;
        viewModel.SubgroupIDs = new ObservableCollection<VM_CollectionMemberString>();
        if (model.SubgroupIDs != null)
        {
            foreach (var id in model.SubgroupIDs)
            {
                viewModel.SubgroupIDs.Add(new VM_CollectionMemberString(id, viewModel.SubgroupIDs));
            }
        }
        foreach (var mixIn in model.MixInAssignments)
        {
            var mixInVM = new VM_MixInConsistencyAssignment(viewModel.MixInAssignments) { AssetPackName = mixIn.AssetPackName};
            foreach (var id in mixIn.SubgroupIDs)
            {
                mixInVM.SubgroupIDs.Add(new VM_CollectionMemberString(id, mixInVM.SubgroupIDs));
            }
            viewModel.MixInAssignments.Add(mixInVM);
        }
        foreach(var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = AssetPackVMs.Where(x => x.GroupName == replacer.AssetPackName).FirstOrDefault();
            if (parentAssetPack != null)
            {
                VM_AssetReplacementAssignment subVm = new VM_AssetReplacementAssignment(parentAssetPack, viewModel.AssetReplacements);
                subVm.CopyInViewModelFromModel(replacer);
                viewModel.AssetReplacements.Add(subVm);
            }
        }
        viewModel.BodyGenMorphNames = new ObservableCollection<VM_CollectionMemberString>();
        if (model.BodyGenMorphNames != null)
        {
            foreach (var morph in model.BodyGenMorphNames)
            {
                viewModel.BodyGenMorphNames.Add(new VM_CollectionMemberString(morph, viewModel.BodyGenMorphNames));
            }
        }
        viewModel.BodySlidePreset = model.BodySlidePreset;
        if (model.Height != null)
        {
            viewModel.Height = model.Height.ToString();
        }
        else
        {
            viewModel.Height = "";
        }

        foreach (var headPartType in viewModel.HeadParts.Keys)
        {
            if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }
            else
            {
                viewModel.HeadParts[headPartType] = VM_HeadPartConsistency.GetViewModelFromModel(model.HeadParts[headPartType]);
            }
        }

        viewModel.DispName = model.DispName;
        viewModel.NPCFormKey = model.NPCFormKey;
        return viewModel;
    }

    public NPCAssignment DumpViewModelToModel()
    {
        NPCAssignment model = new NPCAssignment();
        model.AssetPackName = AssetPackName;
        model.SubgroupIDs = SubgroupIDs.Select(x => x.Content).ToList();
        if (model.SubgroupIDs.Count == 0) { model.SubgroupIDs = null; }
        model.MixInAssignments.Clear();
        foreach (var mixInVM in MixInAssignments)
        {
            model.MixInAssignments.Add(new NPCAssignment.MixInAssignment() { AssetPackName = mixInVM.AssetPackName, SubgroupIDs = mixInVM.SubgroupIDs.Select(x => x.Content).ToList() });
        }
        model.AssetReplacerAssignments.Clear();
        foreach (var replacer in AssetReplacements)
        {
            model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
        }
        model.BodyGenMorphNames = BodyGenMorphNames.Select(x => x.Content).ToList();
        if (model.BodyGenMorphNames.Count == 0) { model.BodyGenMorphNames = null; }
        model.BodySlidePreset = BodySlidePreset;
        if (Height == "")
        {
            model.Height = null;
        }
        else if (float.TryParse(Height, out var height))
        {
            model.Height = height;
        }
        else
        {
            _logger.LogError("Error parsing consistency assignment " + DispName + ". Cannot parse height: " + Height);
        }

        foreach (var headPartType in HeadParts.Keys)
        {
            model.HeadParts[headPartType] = HeadParts[headPartType].DumpToModel();
        }

        model.DispName = DispName;
        model.NPCFormKey = NPCFormKey;
        return model;
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
        public ObservableCollection<VM_CollectionMemberString> SubgroupIDs { get; set; } = new();
        public ObservableCollection<VM_MixInConsistencyAssignment> ParentCollection { get; set; }

        public RelayCommand DeleteCommand { get; set; }
    }
}
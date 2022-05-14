using Mutagen.Bethesda.Plugins;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConsistencyAssignment : VM
{
    public VM_ConsistencyAssignment()
    {
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
    public string DispName { get; set; }
    public FormKey NPCFormKey { get; set; }

    public RelayCommand DeleteAssetPackCommand { get; set; }
    public RelayCommand DeleteBodySlideCommand { get; set; }
    public RelayCommand DeleteHeightCommand { get; set; }

    public bool AssetPackAssigned { get; set; } = false;
    public bool BodySlideAssigned { get; set; } = false;
    public bool HeightAssigned { get; set; } = false;

    public static VM_ConsistencyAssignment GetViewModelFromModel(NPCAssignment model, ObservableCollection<VM_AssetPack> AssetPackVMs)
    {
        VM_ConsistencyAssignment viewModel = new VM_ConsistencyAssignment();
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
                viewModel.AssetReplacements.Add(VM_AssetReplacementAssignment.GetViewModelFromModel(replacer, parentAssetPack, viewModel.AssetReplacements));
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
            
        viewModel.DispName = model.DispName;
        viewModel.NPCFormKey = model.NPCFormKey;
        return viewModel;
    }

    public static NPCAssignment DumpViewModelToModel(VM_ConsistencyAssignment viewModel)
    {
        NPCAssignment model = new NPCAssignment();
        model.AssetPackName = viewModel.AssetPackName;
        model.SubgroupIDs = viewModel.SubgroupIDs.Select(x => x.Content).ToList();
        if (model.SubgroupIDs.Count == 0) { model.SubgroupIDs = null; }
        model.MixInAssignments.Clear();
        foreach (var mixInVM in viewModel.MixInAssignments)
        {
            model.MixInAssignments.Add(new NPCAssignment.MixInAssignment() { AssetPackName = mixInVM.AssetPackName, SubgroupIDs = mixInVM.SubgroupIDs.Select(x => x.Content).ToList() });
        }
        model.AssetReplacerAssignments.Clear();
        foreach (var replacer in viewModel.AssetReplacements)
        {
            model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
        }
        model.BodyGenMorphNames = viewModel.BodyGenMorphNames.Select(x => x.Content).ToList();
        if (model.BodyGenMorphNames.Count == 0) { model.BodyGenMorphNames = null; }
        model.BodySlidePreset = viewModel.BodySlidePreset;
        if (viewModel.Height == "")
        {
            model.Height = null;
        }
        else if (float.TryParse(viewModel.Height, out var height))
        {
            model.Height = height;
        }
        else
        {
            Logger.LogError("Error parsing consistency assignment " + viewModel.DispName + ". Cannot parse height: " + viewModel.Height);
        }
            
        model.DispName = viewModel.DispName;
        model.NPCFormKey = viewModel.NPCFormKey;
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
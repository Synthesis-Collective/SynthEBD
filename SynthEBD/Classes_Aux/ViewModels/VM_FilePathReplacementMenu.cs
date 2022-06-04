using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;
using ReactiveUI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using DynamicData.Binding;
using DynamicData;

namespace SynthEBD;

public class VM_FilePathReplacementMenu : VM
{
    public VM_FilePathReplacementMenu(VM_Subgroup parent, bool setExplicitReferenceNPC, ILinkCache refLinkCache)
    {
        this.ParentSubgroup = parent;

        this.ReferenceLinkCache = refLinkCache;
        this.SetExplicitReferenceNPC = setExplicitReferenceNPC;

        Paths.ToObservableChangeSet().Subscribe(x => RefreshHasContents());
    }
    public ObservableCollection<VM_FilePathReplacement> Paths { get; set; } = new();

    public VM_Subgroup ParentSubgroup { get; set; }
    public bool SetExplicitReferenceNPC { get; set; }
    public FormKey ReferenceNPCFK { get; set; } = new();
    public ILinkCache ReferenceLinkCache { get; set; }
    public IEnumerable<Type> NPCType { get; set; } = typeof(INpcGetter).AsEnumerable();
    public bool HasContents { get; set; }

    public VM_FilePathReplacementMenu Clone()
    {
        VM_FilePathReplacementMenu clone = new VM_FilePathReplacementMenu(ParentSubgroup, ParentSubgroup.SetExplicitReferenceNPC, ReferenceLinkCache);
        clone.HasContents = this.HasContents;
        clone.Paths = new ObservableCollection<VM_FilePathReplacement>() { this.Paths.Select(x => x.Clone(clone)) };
        return clone;
    }

    public static VM_FilePathReplacementMenu GetViewModelFromModels(HashSet<FilePathReplacement> models, VM_Subgroup parentSubgroup, bool setExplicitReferenceNPC)
    {
        VM_FilePathReplacementMenu viewModel = null;

        if (setExplicitReferenceNPC)
        {
            viewModel = new VM_FilePathReplacementMenu(parentSubgroup, setExplicitReferenceNPC, parentSubgroup.LinkCache);
        }
        else
        {
            viewModel = new VM_FilePathReplacementMenu(parentSubgroup, setExplicitReferenceNPC, parentSubgroup.ParentAssetPack.RecordTemplateLinkCache);
        }

        foreach (var model in models)
        {
            var subVm = new VM_FilePathReplacement(viewModel);
            subVm.CopyInViewModelFromModel(model);
            viewModel.Paths.Add(subVm);
        }
        return viewModel;
    }

    public static HashSet<FilePathReplacement> DumpViewModelToModels(VM_FilePathReplacementMenu viewModel)
    {
        return viewModel.Paths.Select(x => new FilePathReplacement() { Source = x.Source, Destination = x.IntellisensedPath }).ToHashSet();
    }

    public void RefreshHasContents()
    {
        if (Paths.Any()) { HasContents = true; }
        else { HasContents = false; }
    }
}
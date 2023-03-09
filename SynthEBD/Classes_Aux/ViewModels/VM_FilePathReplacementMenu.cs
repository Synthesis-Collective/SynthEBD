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
    private readonly Logger _logger;
    private readonly RecordPathParser _recordPathParser;
    private readonly VM_FilePathReplacementMenu.Factory _selfFactory;
    public delegate VM_FilePathReplacementMenu Factory(VM_Subgroup parent, bool setExplicitReferenceNPC, ILinkCache refLinkCache);
    public VM_FilePathReplacementMenu(VM_Subgroup parent, bool setExplicitReferenceNPC, ILinkCache refLinkCache, RecordPathParser recordPathParser, VM_FilePathReplacementMenu.Factory selfFactory, Logger logger)
    {
        _logger = logger;
        _recordPathParser = recordPathParser;
        _selfFactory = selfFactory;

        ParentSubgroup = parent;
        ReferenceLinkCache = refLinkCache;
        SetExplicitReferenceNPC = setExplicitReferenceNPC;

        Paths.ToObservableChangeSet().Subscribe(x => RefreshHasContents()).DisposeWith(this);
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
        VM_FilePathReplacementMenu clone = _selfFactory(ParentSubgroup, ParentSubgroup.SetExplicitReferenceNPC, ReferenceLinkCache);
        clone.HasContents = this.HasContents;
        clone.Paths = new ObservableCollection<VM_FilePathReplacement>() { this.Paths.Select(x => x.Clone(clone)) };
        return clone;
    }

    public void CopyInFromModels(HashSet<FilePathReplacement> models, VM_FilePathReplacement.Factory filePathReplacementFactory)
    {
        foreach (var model in models)
        {
            var subVm = filePathReplacementFactory(this);
            subVm.CopyInViewModelFromModel(model);
            Paths.Add(subVm);
        }
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
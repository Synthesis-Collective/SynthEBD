using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;
using ReactiveUI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using DynamicData.Binding;
using DynamicData;

namespace SynthEBD;

/// <summary>View model for a subgroup's list of file-path (texture/mesh) replacements, tracking whether it currently has any entries.</summary>
public class VM_FilePathReplacementMenu : VM
{
    private readonly Logger _logger;
    private readonly RecordPathParser _recordPathParser;
    private readonly VM_FilePathReplacementMenu.Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing the menu under a subgroup.</summary>
    public delegate VM_FilePathReplacementMenu Factory(VM_Subgroup parent, bool setExplicitReferenceNPC, ILinkCache refLinkCache);
    /// <summary>Creates the menu and refreshes <see cref="HasContents"/> whenever the path list changes.</summary>
    /// <param name="parent">The owning subgroup VM.</param>
    /// <param name="setExplicitReferenceNPC">Whether the menu lets the user pick an explicit reference NPC.</param>
    /// <param name="refLinkCache">Reference link cache for record-path resolution.</param>
    /// <param name="recordPathParser">Parser used by child path VMs.</param>
    /// <param name="selfFactory">Factory used by <see cref="Clone"/>.</param>
    /// <param name="logger">Logger for diagnostics.</param>
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

    /// <summary>Creates a deep copy of this menu, cloning each path entry against the new menu.</summary>
    /// <returns>The cloned menu.</returns>
    public VM_FilePathReplacementMenu Clone()
    {
        VM_FilePathReplacementMenu clone = _selfFactory(ParentSubgroup, ParentSubgroup.SetExplicitReferenceNPC, ReferenceLinkCache);
        clone.HasContents = this.HasContents;
        clone.Paths = new ObservableCollection<VM_FilePathReplacement>() { this.Paths.Select(x => x.Clone(clone)) };
        return clone;
    }

    /// <summary>Populates the path list from a set of <see cref="FilePathReplacement"/> models.</summary>
    /// <param name="models">The replacement models to load.</param>
    /// <param name="filePathReplacementFactory">Factory for the child path VMs.</param>
    public void CopyInFromModels(HashSet<FilePathReplacement> models, VM_FilePathReplacement.Factory filePathReplacementFactory)
    {
        foreach (var model in models)
        {
            var subVm = filePathReplacementFactory(this);
            subVm.CopyInViewModelFromModel(model);
            Paths.Add(subVm);
        }
    }

    /// <summary>Projects the path list back into a set of <see cref="FilePathReplacement"/> models.</summary>
    /// <param name="viewModel">The menu to project.</param>
    /// <returns>The replacement models.</returns>
    public static HashSet<FilePathReplacement> DumpViewModelToModels(VM_FilePathReplacementMenu viewModel)
    {
        return viewModel.Paths.Select(x => new FilePathReplacement() { Source = x.Source, Destination = x.IntellisensedPath }).ToHashSet();
    }

    /// <summary>Updates <see cref="HasContents"/> based on whether any paths are present.</summary>
    public void RefreshHasContents()
    {
        if (Paths.Any()) { HasContents = true; }
        else { HasContents = false; }
    }
}
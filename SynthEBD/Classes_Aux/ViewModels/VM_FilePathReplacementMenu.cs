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

    public static VM_FilePathReplacementMenu GetViewModelFromModels(HashSet<FilePathReplacement> models, VM_Subgroup parentSubgroup, bool setExplicitReferenceNPC, VM_FilePathReplacementMenu.Factory menuFactory, VM_FilePathReplacement.Factory filePathReplacementFactory)
    {
        VM_FilePathReplacementMenu viewModel = null;

        if (setExplicitReferenceNPC)
        {
            viewModel = menuFactory(parentSubgroup, setExplicitReferenceNPC, parentSubgroup.LinkCache);
        }
        else
        {
            viewModel = menuFactory(parentSubgroup, setExplicitReferenceNPC, parentSubgroup.ParentAssetPack.RecordTemplateLinkCache);
        }

        foreach (var model in models)
        {
            var subVm = filePathReplacementFactory(viewModel);
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

    public bool CandidateTargetPathExists(string candidate)
    {
        List<FormKey> candidateRecordTemplates = new();
        if (ParentSubgroup.ParentAssetPack.DefaultTemplateFK != null)
        {
            candidateRecordTemplates.Add(ParentSubgroup.ParentAssetPack.DefaultTemplateFK);
        }
        candidateRecordTemplates.AddRange(ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments.Where(x => x.TemplateNPC != null).Select(x => x.TemplateNPC).ToArray());

        foreach (var referenceNPCformkey in candidateRecordTemplates)
        {
            if (ReferenceLinkCache != null && referenceNPCformkey != null && ReferenceLinkCache.TryResolve<INpcGetter>(referenceNPCformkey, out var refNPC) && _recordPathParser.GetObjectAtPath(refNPC, refNPC, candidate, new Dictionary<string, dynamic>(), ReferenceLinkCache, true, _logger.GetNPCLogNameString(refNPC), out var objAtPath) && objAtPath is not null && objAtPath.GetType() == typeof(string))
            {
                return true;
            }
        }
        return false;
    }
}
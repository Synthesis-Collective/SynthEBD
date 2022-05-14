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

        if (!setExplicitReferenceNPC)
        {
            ParentSubgroup.AllowedRaces.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.AllowedRaceGroupings.WhenAnyValue(x => x.HeaderCaption).Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.DisallowedRaces.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.DisallowedRaceGroupings.WhenAnyValue(x => x.HeaderCaption).Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.DefaultTemplateFK).Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => this.ReferenceLinkCache = this.ParentSubgroup.ParentAssetPack.RecordTemplateLinkCache);
            ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => RefreshReferenceNPC());
            GetReferenceNPCFromRecordTemplates();
        }
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
            viewModel.Paths.Add(VM_FilePathReplacement.GetViewModelFromModel(model, viewModel));
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

    public void RefreshReferenceNPC()
    {
        if (SetExplicitReferenceNPC) { return; }
        else
        {
            GetReferenceNPCFromRecordTemplates();
        }
    }

    public void GetReferenceNPCFromRecordTemplates()
    {
        var templateFormKey = new FormKey();
        bool raceMatched = false;

        var disallowedRaces = RaceGrouping.MergeRaceAndGroupingList(ParentSubgroup.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet(), PatcherSettings.General.RaceGroupings, ParentSubgroup.DisallowedRaces.ToHashSet());
        var allowedRaces = RaceGrouping.MergeRaceAndGroupingList(ParentSubgroup.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet(), PatcherSettings.General.RaceGroupings, ParentSubgroup.AllowedRaces.ToHashSet());
        allowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(allowedRaces, disallowedRaces);

        foreach (var allowedRace in allowedRaces)
        {
            if (FormKeyHashSetComparer.Contains(disallowedRaces, allowedRace)) { continue; }
            foreach (var templateRace in ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments)
            {
                if (FormKeyHashSetComparer.Contains(templateRace.RaceFormKeys.ToHashSet(), allowedRace))
                {
                    raceMatched = true;
                    templateFormKey = templateRace.TemplateNPC;
                    break;
                }
            }
            if (raceMatched) { break; }
        }
        if (!raceMatched)
        {
            templateFormKey = ParentSubgroup.ParentAssetPack.DefaultTemplateFK;
        }

        if (!templateFormKey.IsNull && ReferenceLinkCache != null && ReferenceLinkCache.TryResolve<INpcGetter>(templateFormKey, out _))
        {
            ReferenceNPCFK = templateFormKey;
        }
    }
}
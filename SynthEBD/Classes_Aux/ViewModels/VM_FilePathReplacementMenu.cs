using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using DynamicData.Binding;
using DynamicData;

namespace SynthEBD
{
    public class VM_FilePathReplacementMenu : INotifyPropertyChanged
    {
        public VM_FilePathReplacementMenu(VM_Subgroup parent, bool setExplicitReferenceNPC, ILinkCache refLinkCache)
        {
            this.Paths = new ObservableCollection<VM_FilePathReplacement>();
            this.ParentSubgroup = parent;

            this.ReferenceNPC = null;
            this.ReferenceNPCFK = new FormKey();
            this.ReferenceLinkCache = refLinkCache;
            this.SetExplicitReferenceNPC = setExplicitReferenceNPC;

            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.NPCType = typeof(INpcGetter).AsEnumerable();

            Paths.ToObservableChangeSet().Subscribe(x => RefreshHasContents());

            ParentSubgroup.allowedRaces.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.disallowedRaces.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            this.WhenAnyValue(x => x.ReferenceNPCFK).Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.DefaultTemplateFK).Subscribe(x => RefreshReferenceNPC());
            ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments.ToObservableChangeSet().Subscribe(x => RefreshReferenceNPC());
            if (!setExplicitReferenceNPC)
            {
                ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => this.ReferenceLinkCache = this.ParentSubgroup.ParentAssetPack.RecordTemplateLinkCache);
            }
            ParentSubgroup.ParentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => RefreshReferenceNPC());
        }
        public ObservableCollection<VM_FilePathReplacement> Paths { get; set; }

        public VM_Subgroup ParentSubgroup { get; set; }
        public INpcGetter ReferenceNPC { get; set; }
        public bool SetExplicitReferenceNPC { get; set; }
        public FormKey ReferenceNPCFK { get; set; }
        public ILinkCache ReferenceLinkCache { get; set; }
        public IEnumerable<Type> NPCType { get; set; }
        public ILinkCache lk { get; set; }
        public bool HasContents { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_FilePathReplacementMenu GetViewModelFromModels(HashSet<FilePathReplacement> models, VM_Subgroup parentSubgroup, bool setExplicitReferenceNPC)
        {
            VM_FilePathReplacementMenu viewModel = null;

            if (setExplicitReferenceNPC)
            {
                viewModel = new VM_FilePathReplacementMenu(parentSubgroup, setExplicitReferenceNPC, parentSubgroup.lk);
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
            if (SetExplicitReferenceNPC && ReferenceNPCFK.IsNull) { ReferenceNPC = null; }

            var templateFormKey = new FormKey();

            if (SetExplicitReferenceNPC)
            {
                templateFormKey = this.ReferenceNPCFK;
            }

            else
            {
                bool raceMatched = false;

                var disallowedRaces = ParentSubgroup.disallowedRaces.ToHashSet();
                foreach (var allowedRace in ParentSubgroup.allowedRaces)
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
            }

            if (!templateFormKey.IsNull && ReferenceLinkCache != null && ReferenceLinkCache.TryResolve<INpcGetter>(templateFormKey, out var templateNPC))
            {
                ReferenceNPC = templateNPC;
            }
        }
    }
}

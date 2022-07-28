using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD
{
    public class VM_HeadPartImport : VM
    {
        public VM_HeadPartImport(VM_Settings_Headparts parentMenu)
        {
            ParentMenu = parentMenu;

            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

            this.WhenAnyValue(
                x => x.bImportMale,
                x => x.bImportFemale,
                x => x.bImportPlayableOnly,
                x => x.bImportExtraParts,
                x => x.bImportEyebrows,
                x => x.bImportEyes,
                x => x.bImportFace,
                x => x.bImportFacialHair,
                x => x.bImportHair,
                x => x.bImportMisc,
                x => x.ModtoImport,
                // Just pass along the signal, don't care about the triggering values
                (_, _, _, _, _, _, _, _, _, _, _) => Unit.Default)
            .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateSelections());

            Import = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ImportSelections()
            );
        }
        public bool bImportMale { get; set; } = true;
        public bool bImportFemale { get; set; } = true;
        public bool bImportPlayableOnly { get; set; } = true;
        public bool bImportExtraParts { get; set; } = false;
        public bool bImportEyebrows { get; set; } = true;
        public bool bImportEyes { get; set; } = true;
        public bool bImportFace { get; set; } = true;
        public bool bImportFacialHair { get; set; } = true;
        public bool bImportHair { get; set; } = true;
        public bool bImportMisc { get; set; } = true;
        public bool bImportScar { get; set; } = true;

        public ObservableCollection<FormKey> EyebrowImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> EyeImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> FaceImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> FacialHairImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> HairImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> MiscImports { get; set; } = new ObservableCollection<FormKey>();
        public ObservableCollection<FormKey> ScarImports { get; set; } = new ObservableCollection<FormKey>();

        public RelayCommand Import { get; }

        public VM_Settings_Headparts ParentMenu { get; set; }
        public IEnumerable<Type> HeadPartType { get; set; } = typeof(IHeadPartGetter).AsEnumerable();
        public ModKey ModtoImport { get; set; }
        public Mutagen.Bethesda.Plugins.Order.ILoadOrder<Mutagen.Bethesda.Plugins.Order.IModListing<ISkyrimModGetter>> LoadOrder { get; private set; }
        public ILinkCache lk { get; private set; }

        public void UpdateSelections()
        {
            ClearSelections();
            var mod = PatcherEnvironmentProvider.Instance.Environment.LoadOrder.ListedOrder.Where(x => x.ModKey.Equals(ModtoImport)).FirstOrDefault();
            if (mod != null)
            {
                foreach (var headpart in mod.Mod.HeadParts)
                {
                    if (!bImportMale && headpart.Flags.HasFlag(Mutagen.Bethesda.Skyrim.HeadPart.Flag.Male)) { continue; }
                    if (!bImportFemale && headpart.Flags.HasFlag(Mutagen.Bethesda.Skyrim.HeadPart.Flag.Female)) { continue; }
                    if (bImportPlayableOnly && !headpart.Flags.HasFlag(Mutagen.Bethesda.Skyrim.HeadPart.Flag.Playable)) { continue; }
                    if (!bImportExtraParts && headpart.Flags.HasFlag(Mutagen.Bethesda.Skyrim.HeadPart.Flag.IsExtraPart)) { continue; }
                    if (!bImportEyebrows && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyebrows) { continue; }
                    if (!bImportEyes && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyes) { continue; }
                    if (!bImportFace && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Face) { continue; }
                    if (!bImportFacialHair && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.FacialHair) { continue; }
                    if (!bImportHair && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Hair) { continue; }
                    if (!bImportMisc && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Misc) { continue; }
                    if (!bImportScar && headpart.Type.Value == Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Scars) { continue; }
                    if (!headpart.Type.HasValue) { continue; }

                    switch (headpart.Type.Value)
                    {
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyebrows: EyebrowImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyes: EyeImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Face: FaceImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.FacialHair: FacialHairImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Hair: HairImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Misc: MiscImports.Add(headpart.FormKey); break;
                        case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Scars: ScarImports.Add(headpart.FormKey); break;
                    }
                }
            }
        }

        public void ClearSelections()
        {
            EyebrowImports.Clear();
            EyeImports.Clear();
            FaceImports.Clear();
            FacialHairImports.Clear();
            HairImports.Clear();
            MiscImports.Clear();
            ScarImports.Clear();
        }

        public void ImportSelections()
        {
            foreach (var headPartFK in EyebrowImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Eyebrows.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Eyebrows, ParentMenu));
                }
            }
            foreach (var headPartFK in EyeImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Eyes.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Eyes, ParentMenu));
                }
            }
            foreach (var headPartFK in FaceImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Faces.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Faces, ParentMenu));
                }
            }
            foreach (var headPartFK in FacialHairImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.FacialHairs.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.FacialHairs, ParentMenu));
                }
            }
            foreach (var headPartFK in HairImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Hairs.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Hairs, ParentMenu));
                }
            }
            foreach (var headPartFK in MiscImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Misc.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Misc, ParentMenu));
                }
            }
            foreach (var headPartFK in ScarImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Scars.Add(new VM_HeadPart(ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Scars, ParentMenu));
                }
            }
        }
    }
}

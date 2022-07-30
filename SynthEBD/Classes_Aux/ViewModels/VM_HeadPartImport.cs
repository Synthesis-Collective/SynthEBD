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
using DynamicData.Binding;
using System.Windows.Media;

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

            this.EyebrowImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyebrows));
            this.EyeImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyes));
            this.FaceImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Face));
            this.FacialHairImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.FacialHair));
            this.HairImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Hair));
            this.MiscImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Misc));
            this.ScarImports.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Scars));

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
        public SolidColorBrush EyebrowBorderColor { get; set; }
        public string EyebrowStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> EyeImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush EyeBorderColor { get; set; }
        public string EyeStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> FaceImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush FaceBorderColor { get; set; }
        public string FaceStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> FacialHairImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush FacialHairBorderColor { get; set; }
        public string FacialHairStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> HairImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush HairBorderColor { get; set; }
        public string HairStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> MiscImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush MiscBorderColor { get; set; }
        public string MiscStatusString { get; set; } = string.Empty;
        public ObservableCollection<FormKey> ScarImports { get; set; } = new ObservableCollection<FormKey>();
        public SolidColorBrush ScarBorderColor { get; set; }
        public string ScarStatusString { get; set; } = string.Empty;

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

        public void ValidateNewSelection(HeadPart.TypeEnum type)
        {
            List<string> invalidEditorIDs = new List<string>();
            switch (type)
            {
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyebrows: 
                    EyebrowBorderColor = GetBorderColor(EyebrowImports, type, invalidEditorIDs);  
                    if (invalidEditorIDs.Any())
                    {
                        EyebrowStatusString = "The following invalid eyebrows will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        EyebrowStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Eyes: 
                    EyeBorderColor = GetBorderColor(EyeImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        EyeStatusString = "The following invalid eyes will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        EyeStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Face: 
                    FaceBorderColor = GetBorderColor(FaceImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        FaceStatusString = "The following invalid face parts will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        FaceStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.FacialHair: 
                    FacialHairBorderColor = GetBorderColor(FacialHairImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        FacialHairStatusString = "The following invalid facial hairs will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        FacialHairStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Hair: 
                    HairBorderColor = GetBorderColor(HairImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        HairStatusString = "The following invalid hairs will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        HairStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Misc: 
                    MiscBorderColor = GetBorderColor(MiscImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        MiscStatusString = "The following invalid misc parts will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        MiscStatusString = String.Empty;
                    }
                    break;
                case Mutagen.Bethesda.Skyrim.HeadPart.TypeEnum.Scars: 
                    ScarBorderColor = GetBorderColor(ScarImports, type, invalidEditorIDs);
                    if (invalidEditorIDs.Any())
                    {
                        ScarStatusString = "The following invalid scars will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
                    }
                    else
                    {
                        ScarStatusString = String.Empty;
                    }
                    break;
            }
        }

        public static SolidColorBrush GetBorderColor(ObservableCollection<FormKey> collection, HeadPart.TypeEnum type, List<string> invalidEditorIDs)
        {
            invalidEditorIDs.Clear();
            for (int i = 0; i < collection.Count; i++)
            {
                var headPartFK = collection[i];
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    if (!headpart.Type.HasValue)
                    {
                        invalidEditorIDs.Add(headpart.EditorID);
                    }
                    else if (headpart.Type.Value != type)
                    {
                        invalidEditorIDs.Add(headpart.EditorID);
                    }
                }
                else
                {
                    invalidEditorIDs.Add(headpart.EditorID);
                }
            }

            if (invalidEditorIDs.Any())
            {
                return new SolidColorBrush(Colors.Red);
            }
            if (collection.Any())
            {
                return new SolidColorBrush(Colors.Green);
            }
            else
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }

        public void ImportSelections()
        {
            int importCount = 0;
            foreach (var headPartFK in EyebrowImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Eyebrows.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Eyebrows.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in EyeImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Eyes.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Eyes.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in FaceImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Faces.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Faces.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in FacialHairImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.FacialHairs.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.FacialHairs.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in HairImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Hairs.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Hairs.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in MiscImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Misc.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Misc.DisplayedList, ParentMenu));
                    importCount++;
                }
            }
            foreach (var headPartFK in ScarImports)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                {
                    ParentMenu.Scars.DisplayedList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Scars.DisplayedList, ParentMenu));
                    importCount++;
                }
            }

            Logger.CallTimedNotifyStatusUpdateAsync("Imported " + importCount + " head parts.", 5);
        }

        public static VM_HeadPart ImportHeadPart(IHeadPartGetter headPart, VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_HeadPart> parentCollection, VM_Settings_Headparts parentConfig)
        {
            return new VM_HeadPart(headPart.FormKey, bodyShapeDescriptors, raceGroupingVMs, parentCollection, parentConfig) { Label = headPart.EditorID };
        }
    }
}

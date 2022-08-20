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

            this.Imports[HeadPart.TypeEnum.Eyebrows].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyebrows));
            this.Imports[HeadPart.TypeEnum.Eyes].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyes));
            this.Imports[HeadPart.TypeEnum.Face].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Face));
            this.Imports[HeadPart.TypeEnum.FacialHair].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.FacialHair));
            this.Imports[HeadPart.TypeEnum.Hair].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Hair));
            this.Imports[HeadPart.TypeEnum.Misc].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Misc));
            this.Imports[HeadPart.TypeEnum.Scars].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Scars));

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

        public Dictionary<HeadPart.TypeEnum, HeadPartImportContainer> Imports { get; set; } = new()
        {
            { HeadPart.TypeEnum.Eyebrows, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.Eyes, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.Face, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.FacialHair, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.Hair, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.Misc, new HeadPartImportContainer() },
            { HeadPart.TypeEnum.Scars, new HeadPartImportContainer() }
        };

        public RelayCommand Import { get; }

        public VM_Settings_Headparts ParentMenu { get; set; }
        public IEnumerable<Type> HeadPartType { get; set; } = typeof(IHeadPartGetter).AsEnumerable();
        public ModKey ModtoImport { get; set; }
        public Mutagen.Bethesda.Plugins.Order.ILoadOrder<Mutagen.Bethesda.Plugins.Order.IModListing<ISkyrimModGetter>> LoadOrder { get; private set; }
        public ILinkCache lk { get; private set; }

        public class HeadPartImportContainer : VM
        {
            public ObservableCollection<FormKey> FormKeys { get; set; } = new();
            public SolidColorBrush BorderColor { get; set; } = new();
            public string StatusString { get; set; } = String.Empty;
        }

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

                    Imports[headpart.Type.Value].FormKeys.Add(headpart.FormKey);
                }
            }
        }

        public void ClearSelections()
        {
            foreach (var value in Imports.Values)
            {
                value.FormKeys.Clear();
            }
        }

        public void ValidateNewSelection(HeadPart.TypeEnum type)
        {
            List<string> invalidEditorIDs = new List<string>();

            Imports[type].BorderColor = GetBorderColor(Imports[type].FormKeys, type, invalidEditorIDs);
            
            if (invalidEditorIDs.Any())
            {
                Imports[type].StatusString = "The following invalid eyebrows will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
            }
            else
            {
                Imports[type].StatusString = String.Empty;
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
                        invalidEditorIDs.Add(EditorIDHandler.GetEditorIDSafely(headpart));
                    }
                    else if (headpart.Type.Value != type)
                    {
                        invalidEditorIDs.Add(EditorIDHandler.GetEditorIDSafely(headpart));
                    }
                }
                else
                {
                    invalidEditorIDs.Add(EditorIDHandler.GetEditorIDSafely(headpart));
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
            List<string> skippedImports = new();
            foreach (var entry in Imports)
            {
                foreach (var headPartFK in entry.Value.FormKeys)
                {
                    if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                    {
                        if (!ParentMenu.Types[entry.Key].HeadPartList.Where(x => x.FormKey.Equals(headPartFK)).Any())
                        {
                            ParentMenu.Types[entry.Key].HeadPartList.Add(ImportHeadPart(headpart, ParentMenu.OBodyDescriptors, ParentMenu.RaceGroupings, ParentMenu.Types[entry.Key].HeadPartList, ParentMenu));
                            importCount++;
                        }
                        else
                        {
                            skippedImports.Add(EditorIDHandler.GetEditorIDSafely(headpart));
                        }
                    }
                }
            }    

            if (skippedImports.Any())
            {
                CustomMessageBox.DisplayNotificationOK("Duplicate Imports", "The following head parts were previously imported and will be skipped: " + Environment.NewLine + String.Join(Environment.NewLine, skippedImports));
            }

            Logger.CallTimedNotifyStatusUpdateAsync("Imported " + importCount + " head parts.", 5);
        }

        public static VM_HeadPart ImportHeadPart(IHeadPartGetter headPart, VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_HeadPart> parentCollection, VM_Settings_Headparts parentConfig)
        {
            return new VM_HeadPart(headPart.FormKey, bodyShapeDescriptors, raceGroupingVMs, parentCollection, parentConfig) { Label = EditorIDHandler.GetEditorIDSafely(headPart), bAllowMale = headPart.Flags.HasFlag(HeadPart.Flag.Male), bAllowFemale = headPart.Flags.HasFlag(HeadPart.Flag.Female) };
        }
    }
}

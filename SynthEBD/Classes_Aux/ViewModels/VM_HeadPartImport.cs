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
    /// <summary>
    /// View model for the head-part import tool: scans a selected plugin's HeadPart records (filtered by
    /// sex/type/playable/etc.), validates the selections per type, and imports them into the head-parts
    /// settings, mapping each part's valid races to a race grouping where possible.
    /// </summary>
    public class VM_HeadPartImport : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly VM_HeadPartPlaceHolder.Factory _placeHolderFactory;
        /// <summary>Creates the import tool, wiring load-order/link-cache tracking, debounced re-selection on filter changes, per-type validation, and the import command.</summary>
        /// <param name="parentMenu">The head-parts settings VM imported into.</param>
        /// <param name="logger">Logger for status updates.</param>
        /// <param name="environmentProvider">Supplies the load order and link cache.</param>
        /// <param name="placeholderFactory">Factory for head-part placeholder VMs.</param>
        public VM_HeadPartImport(VM_Settings_Headparts parentMenu, Logger logger, IEnvironmentStateProvider environmentProvider, VM_HeadPartPlaceHolder.Factory placeholderFactory)
        {
            ParentMenu = parentMenu;
            _environmentProvider = environmentProvider;
            _logger = logger;
            _placeHolderFactory = placeholderFactory;

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

            _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x.Where(y => y.Value != null && y.Value.Enabled).Select(x => x.Value.ModKey)).DisposeWith(this);

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
            .Subscribe(_ => UpdateSelections())
            .DisposeWith(this);

            Imports[HeadPart.TypeEnum.Eyebrows].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyebrows)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.Eyes].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Eyes)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.Face].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Face)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.FacialHair].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.FacialHair)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.Hair].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Hair)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.Misc].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Misc)).DisposeWith(this);
            Imports[HeadPart.TypeEnum.Scars].FormKeys.ToObservableChangeSet().Subscribe(x => ValidateNewSelection(HeadPart.TypeEnum.Scars)).DisposeWith(this);
            
            Import = new RelayCommand(
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
        public bool bRespectHeadPartRaces { get; set; } = true;

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
        public IEnumerable<ModKey> LoadOrder { get; private set; }
        public ILinkCache lk { get; private set; }

        /// <summary>Per-type import bucket: the selected head-part FormKeys plus a validity border color and status string.</summary>
        public class HeadPartImportContainer : VM
        {
            public ObservableCollection<FormKey> FormKeys { get; set; } = new();
            public SolidColorBrush BorderColor { get; set; } = new();
            public string StatusString { get; set; } = String.Empty;
        }

        /// <summary>Repopulates the per-type selections from the chosen mod, applying the sex / type / playable / extra-part filters.</summary>
        public void UpdateSelections()
        {
            ClearSelections();
            var mod = _environmentProvider.LoadOrder.ListedOrder.Where(x => x.ModKey.Equals(ModtoImport)).FirstOrDefault();
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

        /// <summary>Clears all per-type selections.</summary>
        public void ClearSelections()
        {
            foreach (var value in Imports.Values)
            {
                value.FormKeys.Clear();
            }
        }

        /// <summary>Revalidates one type's selections, updating its border color and listing any entries of the wrong type (which won't be imported).</summary>
        /// <param name="type">The head-part type to validate.</param>
        public void ValidateNewSelection(HeadPart.TypeEnum type)
        {
            List<string> invalidEditorIDs = new List<string>();

            Imports[type].BorderColor = GetBorderColor(Imports[type].FormKeys, type, invalidEditorIDs);
            
            if (invalidEditorIDs.Any())
            {
                Imports[type].StatusString = "The following invalid " + type.ToString() + " will not be imported:" + Environment.NewLine + String.Join(Environment.NewLine, invalidEditorIDs);
            }
            else
            {
                Imports[type].StatusString = String.Empty;
            }
        }

        /// <summary>Returns the border color for a selection set (red on any invalid/wrong-type entry, green if non-empty and valid, grey if empty) and collects the invalid EditorIDs.</summary>
        /// <param name="collection">The selected head-part FormKeys.</param>
        /// <param name="type">The expected head-part type.</param>
        /// <param name="invalidEditorIDs">Receives the EditorIDs of invalid/wrong-type entries.</param>
        /// <returns>The status border color.</returns>
        public SolidColorBrush GetBorderColor(ObservableCollection<FormKey> collection, HeadPart.TypeEnum type, List<string> invalidEditorIDs)
        {
            invalidEditorIDs.Clear();
            for (int i = 0; i < collection.Count; i++)
            {
                var headPartFK = collection[i];
                if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
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
                    invalidEditorIDs.Add(EditorIDHandler.GetEditorIDSafely<IHeadPartGetter>(headPartFK, _environmentProvider.LinkCache));
                }
            }

            if (invalidEditorIDs.Any())
            {
                return CommonColors.Red;
            }
            if (collection.Any())
            {
                return CommonColors.Green;
            }
            else
            {
                return CommonColors.LightSlateGrey;
            }
        }

        /// <summary>Imports the selected head parts into the settings (skipping any already present), notifies about duplicates, and reports the imported count.</summary>
        public void ImportSelections()
        {
            int importCount = 0;
            List<string> skippedImports = new();
            foreach (var entry in Imports)
            {
                foreach (var headPartFK in entry.Value.FormKeys)
                {
                    if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(headPartFK, out var headpart))
                    {
                        if (!ParentMenu.Types[entry.Key].HeadPartList.Where(x => x.AssociatedModel.HeadPartFormKey.Equals(headPartFK)).Any())
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
                MessageWindow.DisplayNotificationOK("Duplicate Imports", "The following head parts were previously imported and will be skipped: " + Environment.NewLine + String.Join(Environment.NewLine, skippedImports));
            }

            _logger.CallTimedNotifyStatusUpdateAsync("Imported " + importCount + " head parts.", 5);
        }

        /// <summary>Builds a head-part setting/placeholder from a record, copying sex flags and (when enabled) mapping the record's valid races to a race grouping, falling back to an explicit race set.</summary>
        /// <param name="headPart">The head-part record to import.</param>
        /// <param name="bodyShapeDescriptors">Descriptor menu (available for rule setup).</param>
        /// <param name="raceGroupingVMs">Race groupings used to match the record's valid races.</param>
        /// <param name="parentCollection">The head-part list the placeholder is added to.</param>
        /// <param name="parentConfig">The head-parts settings VM (for the respect-races option).</param>
        /// <returns>The created placeholder VM.</returns>
        public VM_HeadPartPlaceHolder ImportHeadPart(IHeadPartGetter headPart, VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_HeadPartPlaceHolder> parentCollection, VM_Settings_Headparts parentConfig)
        {
            var imported = new HeadPartSetting() { HeadPartFormKey = headPart.FormKey};
            var placeHolder = _placeHolderFactory(imported, parentCollection);
            imported.EditorID = EditorIDHandler.GetEditorIDSafely(headPart);
            imported.bAllowMale = headPart.Flags.HasFlag(HeadPart.Flag.Male);
            imported.bAllowFemale = headPart.Flags.HasFlag(HeadPart.Flag.Female);

            if (parentConfig.ImportMenu.bRespectHeadPartRaces && _environmentProvider.LinkCache.TryResolve<IFormListGetter>(headPart.ValidRaces.FormKey, out var raceFormList) && raceFormList.Items.Any())
            {
                var races = raceFormList.Items.Select(x => x.FormKey).ToHashSet();
                var matchedGroupings = raceGroupingVMs.Where(g => g.Races.ToHashSet().SetEquals(races)).Select(x => x.Label).ToHashSet();
                if (matchedGroupings.Any())
                {
                    imported.AllowedRaceGroupings = matchedGroupings;
                }
                else
                {
                    imported.AllowedRaces = races;
                }
            }

            return placeHolder;
        }
    }
}

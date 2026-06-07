using DynamicData;
using Mutagen.Bethesda.Synthesis.States.DI;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD
{
    /// <summary>
    /// View model for the BodySlide import/export exchange window: exports the referenced BodySlide
    /// annotations (plus the attribute groups, race groupings, and descriptors they reference) to a JSON
    /// file, or imports such a file back into the OBody settings (with backup and annotation-count matching).
    /// </summary>
    public class VM_BodySlideExchange
    {
        /// <summary>Autofac factory delegate for constructing the exchange in import or export mode.</summary>
        public delegate VM_BodySlideExchange Factory(ExchangeMode mode, Window_BodySlideExchange window);
        /// <summary>Creates the exchange VM in the given mode and wires the import/export action command.</summary>
        /// <param name="mode">Whether the window imports or exports.</param>
        /// <param name="window">The hosting window (closed on success).</param>
        /// <param name="oBodyUI">The OBody settings VM (source/target of BodySlides).</param>
        /// <param name="generalUI">General settings VM (race groupings).</param>
        /// <param name="placeHolderFactory">Factory for BodySlide placeholder VMs.</param>
        /// <param name="attributeGroupFactory">Factory for attribute-group VMs.</param>
        /// <param name="raceGroupingFactory">Factory for race-grouping VMs.</param>
        /// <param name="descriptorSelectionFactory">Factory for descriptor-selection menus.</param>
        public VM_BodySlideExchange(ExchangeMode mode, Window_BodySlideExchange window, VM_SettingsOBody oBodyUI, VM_Settings_General generalUI, VM_BodySlidePlaceHolder.Factory placeHolderFactory, VM_AttributeGroup.Factory attributeGroupFactory, VM_RaceGrouping.Factory raceGroupingFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            Mode = mode;
            _oBodyUI = oBodyUI;
            _generalUI = generalUI;
            _placeHolderFactory = placeHolderFactory;
            _attributeGroupFactory = attributeGroupFactory;
            _raceGroupingFactory = raceGroupingFactory;
            _decriptorSelectionFactory = descriptorSelectionFactory;

            ActionCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    switch (Mode)
                    {
                        case ExchangeMode.Import:
                            if (Import())
                            {
                                window.Close();
                            }
                            break;
                        case ExchangeMode.Export:
                            if (Export())
                            {
                                window.Close();
                            }
                            break;
                    }
                }
            );
        }

        public ExchangeMode Mode { get; }
        private readonly VM_SettingsOBody _oBodyUI;
        private readonly VM_Settings_General _generalUI;
        private readonly VM_BodySlidePlaceHolder.Factory _placeHolderFactory;
        private readonly VM_AttributeGroup.Factory _attributeGroupFactory;
        private readonly VM_RaceGrouping.Factory _raceGroupingFactory;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _decriptorSelectionFactory;

        public bool ExchangeRules { get; set; } = true;
        public bool ExchangeNotes { get; set; } = true;
        public bool IncludeAttributeGroups { get; set; } = true;
        public bool IncludeRaceGroupings { get; set; } = true;
        public DescriptorRulesMergeMode DescriptorMergeMode { get; set; } = DescriptorRulesMergeMode.Merge;

        public RelayCommand ActionCommand { get; }

        /// <summary>Exports the selected BodySlides plus their referenced attribute groups, race groupings, and descriptors to a chosen JSON file.</summary>
        /// <returns><c>true</c> if the export completed and the window should close.</returns>
        public bool Export()
        {
            BodySlideExchange exchange = new();
            HashSet<string> referencedAttributeGroups = new();
            HashSet<string> referencedRaceGroupings = new();
            HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors = new();

            ExportGendered(_oBodyUI.BodySlidesUI.BodySlidesMale, exchange.BodySlidesMale, referencedAttributeGroups, referencedRaceGroupings, referencedDescriptors);
            ExportGendered(_oBodyUI.BodySlidesUI.BodySlidesFemale, exchange.BodySlidesFemale, referencedAttributeGroups, referencedRaceGroupings, referencedDescriptors);

            CompileDescriptorAttributeAndRaceGroups(referencedDescriptors, referencedAttributeGroups, referencedRaceGroupings); // get referenced groups from Descriptors' Associated Rules

            exchange.TemplateDescriptors = _oBodyUI.DescriptorUI.DumpSelectedToViewModels(referencedDescriptors);

            if (IncludeAttributeGroups)
            {
                foreach (var group in _oBodyUI.AttributeGroupMenu.Groups.Where(x => referencedAttributeGroups.Contains(x.Label)).ToArray())
                {
                    exchange.AttributeGroups.Add(VM_AttributeGroup.DumpViewModelToModel(group));
                }
            }

            if (IncludeRaceGroupings)
            {
                foreach (var grouping in _generalUI.RaceGroupingEditor.RaceGroupings.Where(x => referencedRaceGroupings.Contains(x.Label)).ToArray())
                {
                    exchange.RaceGroupings.Add(grouping.DumpViewModelToModel());
                }
            }

            bool closeWindow = true;
            if (IO_Aux.SelectFileSave("", "Bodyslide files (*.json)|*.json", ".json", "Save Asset Config File", out string savePath, "ExportedBodySlides.json"))
            {
                JSONhandler<BodySlideExchange>.SaveJSONFile(exchange, savePath, out bool success, out string exception);
                if (!success)
                {
                    MessageWindow.DisplayNotificationOK("Export Failed", exception);
                    closeWindow = false;
                }
            }
            else
            {
                closeWindow = false;
            }

            return closeWindow;
        }

        /// <summary>Exports one gender's (non-hidden) BodySlides into the exchange, collecting the attribute groups, race groupings, and descriptors they reference (honoring the rules/notes toggles).</summary>
        /// <param name="bodySlides">The source BodySlide placeholders.</param>
        /// <param name="destinationList">The exchange list to append to.</param>
        /// <param name="referencedAttributeGroups">Accumulates referenced attribute-group labels.</param>
        /// <param name="referencedRaceGroupings">Accumulates referenced race-grouping labels.</param>
        /// <param name="referencedDescriptors">Accumulates referenced descriptor signatures.</param>
        public void ExportGendered(ObservableCollection<VM_BodySlidePlaceHolder> bodySlides, List<BodySlideSetting> destinationList, HashSet<string> referencedAttributeGroups, HashSet<string> referencedRaceGroupings, HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors)
        {
            foreach (var fullModel in bodySlides.Where(x => !x.IsHidden).Select(x => x.AssociatedModel).ToArray())
            {
                var model = new BodySlideSetting();
                model.BodyShapeDescriptorsByWeight = fullModel.BodyShapeDescriptorsByWeight;
                model.RemovedDefaultWeightSlots = fullModel.RemovedDefaultWeightSlots;

                if (ExchangeRules)
                {
                    model = fullModel;
                }
                else
                {
                    model.Label = fullModel.Label;
                    model.ReferencedBodySlide = fullModel.ReferencedBodySlide;
                }

                if (ExchangeNotes)
                {
                    model.Notes = fullModel.Notes;
                }
                else
                {
                    model.Notes = String.Empty;
                }

                destinationList.Add(model);

                foreach (var attribute in fullModel.AllowedAttributes.And(fullModel.DisallowedAttributes))
                {
                    foreach (var subAttribute in attribute.SubAttributes.Where(x => x.Type == NPCAttributeType.Group).ToArray())
                    {
                        var groupAttribute = (NPCAttributeGroup)subAttribute;
                        foreach (var selection in groupAttribute.SelectedLabels.Where(x => !referencedAttributeGroups.Contains(x)).ToArray())
                        {
                            referencedAttributeGroups.Add(selection);
                        }
                    }
                }

                foreach (var racegrouping in fullModel.AllowedRaceGroupings.And(fullModel.DisallowedRaceGroupings))
                {
                    if (!referencedRaceGroupings.Contains(racegrouping))
                    {
                        referencedRaceGroupings.Add(racegrouping);
                    }
                }

                foreach (var descriptor in fullModel.EnumerateAllDescriptors())
                {
                    if (!descriptor.CollectionContainsThisDescriptor(referencedDescriptors))
                    {
                        referencedDescriptors.Add(descriptor);
                    }
                }
            }
        }

        /// <summary>Walks the referenced descriptors' associated rules to collect any further attribute groups and race groupings they reference (so the export is self-contained).</summary>
        /// <param name="referencedDescriptors">The descriptors whose rules are scanned.</param>
        /// <param name="referencedAttributeGroups">Accumulates referenced attribute-group labels.</param>
        /// <param name="referencedRaceGroupings">Accumulates referenced race-grouping labels.</param>
        public void CompileDescriptorAttributeAndRaceGroups(HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors, HashSet<string> referencedAttributeGroups, HashSet<string> referencedRaceGroupings)
        {
            var referencedDescriptorStrings = referencedDescriptors.Select(x => x.ToString()).ToArray();
            foreach (var category in _oBodyUI.DescriptorUI.TemplateDescriptors)
            {
                foreach (var value in category.Descriptors)
                {
                    if (referencedDescriptorStrings.Contains(value.Signature))
                    {
                        // add missing attributes
                        foreach (var attribute in value.AssociatedRules.AllowedAttributes.And(value.AssociatedRules.DisallowedAttributes).Select(x => x.DumpViewModelToModel()))
                        {
                            foreach (var subAttribute in attribute.SubAttributes.Where(x => x.Type == NPCAttributeType.Group).ToArray())
                            {
                                var groupAttribute = (NPCAttributeGroup)subAttribute;
                                foreach (var selection in groupAttribute.SelectedLabels.Where(x => !referencedAttributeGroups.Contains(x)).ToArray())
                                {
                                    referencedAttributeGroups.Add(selection);
                                }
                            }
                        }

                        // add missing race groupings
                        var allowedGroupings = value.AssociatedRules.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToArray();
                        var disallowedGroupings = value.AssociatedRules.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToArray();
                        foreach (var racegrouping in allowedGroupings.And(disallowedGroupings))
                        {
                            if (!referencedRaceGroupings.Contains(racegrouping))
                            {
                                referencedRaceGroupings.Add(racegrouping);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Imports a BodySlide exchange JSON file into the OBody settings, optionally backing up current settings first and warning on annotation-count mismatches.</summary>
        /// <returns><c>true</c> if the import completed and the window should close.</returns>
        public bool Import()
        {
            if (!IO_Aux.SelectFile("", "Bodyslide files (*.json)|*.json", "Select Export File", out string loadPath))
            {
                return false;
            }

            var exchange = JSONhandler<BodySlideExchange>.LoadJSONFile(loadPath, out bool success, out string exception);
            if (!success)
            {
                MessageWindow.DisplayNotificationOK("Import Failed", exception);
                return false;
            }

            if (MessageWindow.DisplayNotificationYesNo("Settings Backup", "Back up your current BodySlide settings before importing?"))
            {
                var currentSettings = _oBodyUI.DumpViewModelToModel();
                if (currentSettings != null && IO_Aux.SelectFileSave("", "Bodyslide Settings files (*.json)|*.json", ".json", "Save BodySlide Settings", out string savePath, "OBodySettings.json"))
                {
                    JSONhandler<Settings_OBody>.SaveJSONFile(currentSettings, savePath, out bool succes, out string saveException);
                    if (!succes)
                    {
                        MessageWindow.DisplayNotificationOK("Failed to save settings", "Settings could not be saved. Error: " + Environment.NewLine + Environment.NewLine + saveException);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (IncludeAttributeGroups)
            {
                foreach (var group in exchange.AttributeGroups.Where(x => !_oBodyUI.AttributeGroupMenu.Groups.Select(x => x.Label).Contains(x.Label)).ToArray())
                {
                    var groupVM = _attributeGroupFactory(_oBodyUI.AttributeGroupMenu);
                    groupVM.CopyInViewModelFromModel(group);
                    _oBodyUI.AttributeGroupMenu.Groups.Add(groupVM);
                }
            }

            if (IncludeRaceGroupings)
            {
                var missingGroupings = exchange.RaceGroupings.Where(x => !_generalUI.RaceGroupingEditor.RaceGroupings.Select(x => x.Label).Contains(x.Label)).ToList();
                foreach (var group in missingGroupings)
                {
                    var groupVM = _raceGroupingFactory(group, _generalUI.RaceGroupingEditor);
                    _generalUI.RaceGroupingEditor.RaceGroupings.Add(groupVM);
                }
            }

            List<string> mergedDescriptors = new();
            _oBodyUI.DescriptorUI.MergeInMissingModels(exchange.TemplateDescriptors, DescriptorMergeMode, mergedDescriptors);
            if (mergedDescriptors.Any())
            {
                MessageWindow.DisplayNotificationOK("Descriptor Merge", "The following already existing Descriptors were merged from the imported file. Please check their associated distribution rules to make sure the merged product is consistent with your preferences." + Environment.NewLine + string.Join(Environment.NewLine, mergedDescriptors));
            }

            List<(string, int, int)> multiplexWarnings = new();
            ImportGendered(_oBodyUI.BodySlidesUI.BodySlidesMale, exchange.BodySlidesMale, multiplexWarnings);
            ImportGendered(_oBodyUI.BodySlidesUI.BodySlidesFemale, exchange.BodySlidesFemale, multiplexWarnings);

            if (multiplexWarnings.Any())
            {
                List<string> warnStrs = new();
                foreach (var warning in multiplexWarnings)
                {
                    warnStrs.Add("BodySlide: " + warning.Item1 + Environment.NewLine + "Existing BodySlide Entries: " + warning.Item2.ToString() + Environment.NewLine + "Imported BodySlide Entries: " + warning.Item3.ToString());
                }

                string dispStr = "Could not import annotations for the following BodySlides because the number of existing entries in your BodySlide Settings does not match the number of annotations in the exchange file. Either adjust the number of your entries to match by copying/deleting the existing one(s), or delete all but one, and then try importing again." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, warnStrs);
                MessageWindow.DisplayNotificationOK("Import Warnings", dispStr);
            }

            return true;
        }

        /// <summary>Matches one gender's imported annotations to existing BodySlides by referenced preset, cloning existing entries to match counts where unambiguous and warning on irreconcilable mismatches.</summary>
        /// <param name="currentBodySlides">The user's current BodySlide placeholders.</param>
        /// <param name="importedBodySlides">The imported annotations.</param>
        /// <param name="multiplexWarnings">Accumulates (preset, existingCount, importedCount) tuples for unmatched groups.</param>
        public void ImportGendered(ObservableCollection<VM_BodySlidePlaceHolder> currentBodySlides, List<BodySlideSetting> importedBodySlides, List<(string, int, int)> multiplexWarnings)
        {
            var groupedAnnotations = importedBodySlides.GroupBy(x => x.ReferencedBodySlide).ToArray(); // group annotations by the bodyslide that they're referencing (remember that BodySlide annotation can be cloned)

            foreach (var groupedAnnotation in groupedAnnotations) // match imported annotations to their counterparts in the user's settings if they exist
            {
                var existingBodySlides = currentBodySlides.Where(x => x.AssociatedModel.ReferencedBodySlide == groupedAnnotation.Key).ToList();
                var importedBodySlideAnnotations = groupedAnnotation.ToList();

                if (existingBodySlides.Count == 0)
                {
                    continue; // move on if the user doesn't have the given bodyslide installed
                }
                else if (existingBodySlides.Count == 1 && importedBodySlideAnnotations.Count > 1) // if an imported bodyslide annotation is cloned and the corresponding bodyslide is not cloned in the user's settings, clone it for them
                {
                    var template = existingBodySlides.First();
                    while (existingBodySlides.Count < importedBodySlideAnnotations.Count)
                    {
                        var clonedModel = template.AssociatedModel.DeepCopyByExpressionTree();
                        existingBodySlides.Add(_placeHolderFactory(clonedModel, template.ParentCollection));
                    }
                }
                else if (existingBodySlides.Count != importedBodySlideAnnotations.Count)
                {
                    multiplexWarnings.Add((groupedAnnotation.Key, existingBodySlides.Count, importedBodySlideAnnotations.Count));
                    continue;
                }

                for (int i = 0; i < existingBodySlides.Count; i++)
                {
                    var targetPlaceHolder = existingBodySlides[i];
                    var importedAnnotation = importedBodySlideAnnotations[i];
                    ImportBodySlide(currentBodySlides, importedAnnotation, targetPlaceHolder);
                }
            }
        }

        /// <summary>Applies one imported annotation to a target BodySlide placeholder, honoring the rules/notes exchange toggles and refreshing its display.</summary>
        /// <param name="bodySlides">The collection the placeholder belongs to.</param>
        /// <param name="importedBS">The imported BodySlide annotation.</param>
        /// <param name="targetPlaceHolder">The placeholder to update.</param>
        public void ImportBodySlide(ObservableCollection<VM_BodySlidePlaceHolder> bodySlides, BodySlideSetting importedBS, VM_BodySlidePlaceHolder targetPlaceHolder)
        {
            var notesBak = targetPlaceHolder.AssociatedModel.Notes;

            if (ExchangeRules)
            {
                targetPlaceHolder.AssociatedModel = importedBS;
            }
            else
            {
                targetPlaceHolder.AssociatedModel.BodyShapeDescriptorsByWeight = importedBS.BodyShapeDescriptorsByWeight;
                targetPlaceHolder.AssociatedModel.RemovedDefaultWeightSlots = importedBS.RemovedDefaultWeightSlots;
            }

            if (ExchangeNotes)
            {
                targetPlaceHolder.AssociatedModel.Notes = importedBS.Notes;
            }
            else
            {
                targetPlaceHolder.AssociatedModel.Notes = notesBak;
            }

            targetPlaceHolder.InitializeBorderColor(); // refresh border around the list member
            if (targetPlaceHolder.AssociatedViewModel != null) 
            {
                targetPlaceHolder.AssociatedViewModel.CopyInViewModelFromModel(targetPlaceHolder.AssociatedModel); // refresh displayed view model with new descriptors
            }
        }
    }

    /// <summary>Whether the exchange window is importing or exporting.</summary>
    public enum ExchangeMode
    {
        /// <summary>Import a BodySlide exchange file.</summary>
        Import,
        /// <summary>Export to a BodySlide exchange file.</summary>
        Export
    }
}

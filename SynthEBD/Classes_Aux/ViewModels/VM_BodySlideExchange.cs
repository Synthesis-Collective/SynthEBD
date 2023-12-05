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
    public class VM_BodySlideExchange
    {
        public delegate VM_BodySlideExchange Factory(ExchangeMode mode, Window_BodySlideExchange window);
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
            if (IO_Aux.SelectFileSave("", "Bodyslide files (.json|*.json", ".json", "Save Asset Config File", out string savePath, "ExportedBodySlides.json"))
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

        public void ExportGendered(ObservableCollection<VM_BodySlidePlaceHolder> bodySlides, List<BodySlideSetting> destinationList, HashSet<string> referencedAttributeGroups, HashSet<string> referencedRaceGroupings, HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors)
        {
            foreach (var fullModel in bodySlides.Where(x => !x.IsHidden).Select(x => x.AssociatedModel).ToArray())
            {
                var model = new BodySlideSetting();
                model.BodyShapeDescriptors = fullModel.BodyShapeDescriptors;

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

                foreach (var descriptor in fullModel.BodyShapeDescriptors)
                {
                    if (!descriptor.CollectionContainsThisDescriptor(referencedDescriptors))
                    {
                        referencedDescriptors.Add(descriptor);
                    }
                }
            }
        }

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
                if (currentSettings != null && IO_Aux.SelectFileSave("", "Bodyslide Settings files (.json|*.json", ".json", "Save BodySlide Settings", out string savePath, "OBodySettings.json"))
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

        public void ImportBodySlide(ObservableCollection<VM_BodySlidePlaceHolder> bodySlides, BodySlideSetting importedBS, VM_BodySlidePlaceHolder targetPlaceHolder)
        {
            var notesBak = targetPlaceHolder.AssociatedModel.Notes;

            if (ExchangeRules)
            {
                targetPlaceHolder.AssociatedModel = importedBS;
            }
            else
            {
                targetPlaceHolder.AssociatedModel.BodyShapeDescriptors = importedBS.BodyShapeDescriptors;
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

    public enum ExchangeMode
    {
        Import,
        Export
    }
}

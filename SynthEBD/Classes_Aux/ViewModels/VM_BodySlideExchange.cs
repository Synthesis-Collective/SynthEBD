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
        public VM_BodySlideExchange(ExchangeMode mode, Window_BodySlideExchange window, VM_SettingsOBody oBodyUI, VM_Settings_General generalUI, VM_BodySlideSetting.Factory bodySlideFactory, VM_AttributeGroup.Factory attributeGroupFactory, VM_RaceGrouping.Factory raceGroupingFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            Mode = mode;
            _oBodyUI = oBodyUI;
            _generalUI = generalUI;
            _bodySlideFactory = bodySlideFactory;
            _attributeGroupFactory = attributeGroupFactory;
            _raceGroupingFactory = raceGroupingFactory;
            _decriptorSelectionFactory = descriptorSelectionFactory;

            ActionCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { 
                    switch(Mode)
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

        public  ExchangeMode Mode { get; }
        private readonly VM_SettingsOBody _oBodyUI;
        private readonly VM_Settings_General _generalUI;
        private readonly VM_BodySlideSetting.Factory _bodySlideFactory;
        private readonly VM_AttributeGroup.Factory _attributeGroupFactory;
        private readonly VM_RaceGrouping.Factory _raceGroupingFactory;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _decriptorSelectionFactory;

        public bool ExchangeRules { get; set; } = true;
        public bool ExchangeNotes { get; set; } = true;
        public bool IncludeAttributeGroups { get; set; } = true; 
        public bool IncludeRaceGroupings { get; set; } = true;

        public RelayCommand ActionCommand { get; }

        public bool Export()
        {
            BodySlideExchange exchange = new();
            HashSet<string> referencedAttributeGroups = new();
            HashSet<string> referencedRaceGroupings = new();
            HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors = new();

            ExportGendered(_oBodyUI.BodySlidesUI.BodySlidesMale, exchange.BodySlidesMale, referencedAttributeGroups, referencedRaceGroupings, referencedDescriptors);
            ExportGendered(_oBodyUI.BodySlidesUI.BodySlidesFemale, exchange.BodySlidesFemale, referencedAttributeGroups, referencedRaceGroupings, referencedDescriptors);

            exchange.TemplateDescriptors = _oBodyUI.DescriptorUI.DumpSelectedToViewModels(referencedDescriptors);

            if (IncludeAttributeGroups)
            {
                foreach (var group in _oBodyUI.AttributeGroupMenu.Groups.Where(x => referencedAttributeGroups.Contains(x.Label)))
                {
                    exchange.AttributeGroups.Add(VM_AttributeGroup.DumpViewModelToModel(group));
                }
            }

            if (IncludeRaceGroupings)
            {
                foreach (var grouping in _generalUI.RaceGroupingEditor.RaceGroupings.Where(x => referencedRaceGroupings.Contains(x.Label)))
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
                    CustomMessageBox.DisplayNotificationOK("Export Failed", exception);
                    closeWindow = false;
                }
            }
            else
            {
                closeWindow = false;
            }

            return closeWindow;
        }

        public void ExportGendered(ObservableCollection<VM_BodySlideSetting> bodySlides, List<BodySlideSetting> destinationList, HashSet<string> referencedAttributeGroups, HashSet<string> referencedRaceGroupings, HashSet<BodyShapeDescriptor.LabelSignature> referencedDescriptors)
        {
            foreach (var bsVM in bodySlides.Where(x => !x.IsHidden))
            {
                var fullModel = VM_BodySlideSetting.DumpViewModelToModel(bsVM);
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
                    foreach (var subAttribute in attribute.SubAttributes.Where(x => x.Type == NPCAttributeType.Group))
                    {
                        var groupAttribute = (NPCAttributeGroup)subAttribute;
                        foreach (var selection in groupAttribute.SelectedLabels.Where(x => !referencedAttributeGroups.Contains(x)))
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

        public bool Import()
        {
            if(!IO_Aux.SelectFile("", "Bodyslide files (*.json)|*.json", "Select Export File", out string loadPath))
            {
                return false;
            }

            var exchange = JSONhandler<BodySlideExchange>.LoadJSONFile(loadPath, out bool success, out string exception);
            if (!success)
            {
                CustomMessageBox.DisplayNotificationOK("Import Failed", exception);
                return false;
            }

            if (CustomMessageBox.DisplayNotificationYesNo("Settings Backup", "Back up your current BodySlide settings before importing?"))
            {
                var currentSettings = _oBodyUI.DumpViewModelToModel();
                if (currentSettings != null && IO_Aux.SelectFileSave("", "Bodyslide Settings files (.json|*.json", ".json", "Save BodySlide Settings", out string savePath, "OBodySettings.json"))
                {
                    JSONhandler<Settings_OBody>.SaveJSONFile(currentSettings, savePath, out bool succes, out string saveException);
                    if (!succes)
                    {
                        CustomMessageBox.DisplayNotificationOK("Failed to save settings", "Settings could not be saved. Error: " + Environment.NewLine + Environment.NewLine + saveException);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            _oBodyUI.DescriptorUI.MergeInMissingModels(exchange.TemplateDescriptors);

            if (IncludeAttributeGroups)
            {
                foreach (var group in exchange.AttributeGroups.Where(x => !_oBodyUI.AttributeGroupMenu.Groups.Select(x => x.Label).Contains(x.Label)))
                {
                    var groupVM = _attributeGroupFactory(_oBodyUI.AttributeGroupMenu);
                    groupVM.CopyInViewModelFromModel(group);
                }
            }

            if (IncludeRaceGroupings)
            {
                var missingGroupings = exchange.RaceGroupings.Where(x => !_generalUI.RaceGroupingEditor.RaceGroupings.Select(x => x.Label).Contains(x.Label)).ToList();
                VM_RaceGrouping.GetViewModelsFromModels(missingGroupings, _generalUI.RaceGroupingEditor, _raceGroupingFactory);
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
                CustomMessageBox.DisplayNotificationOK("Import Warnings", dispStr);
            }

            return true;
        }

        public void ImportGendered(ObservableCollection<VM_BodySlideSetting> bodySlides, List<BodySlideSetting> sourceList, List<(string, int, int)> multiplexWarnings)
        {
            HashSet<string> alreadyAnnotatedMultiple = new();

            foreach (var newBS in sourceList)
            {
                var existingBodySlides = bodySlides.Where(x => x.ReferencedBodySlide == newBS.ReferencedBodySlide).ToList();
                if (!existingBodySlides.Any())
                {
                    continue;
                }
                else if (existingBodySlides.Count() == 1)
                {
                    var existingBS = existingBodySlides.First();
                    ImportBodySlide(bodySlides, newBS, existingBS);
                }
                else
                {
                    if (alreadyAnnotatedMultiple.Contains(newBS.ReferencedBodySlide)) { continue; }
                    alreadyAnnotatedMultiple.Add(newBS.ReferencedBodySlide);

                    var newAnnotations = sourceList.Where(x => x.ReferencedBodySlide == newBS.ReferencedBodySlide).ToList();
                    
                    if(existingBodySlides.Count == 1)
                    {
                        ImportBodySlide(bodySlides, newBS, existingBodySlides.First());
                    }
                    else if (newAnnotations.Count == existingBodySlides.Count)
                    {
                        for (int i = 0; i < newAnnotations.Count; i++)
                        {
                            ImportBodySlide(bodySlides, newAnnotations[i], existingBodySlides[i]);
                        }
                    }
                    else
                    {
                        multiplexWarnings.Add((newBS.ReferencedBodySlide, existingBodySlides.Count, newAnnotations.Count));
                    }
                }
            }
        }

        public void ImportBodySlide(ObservableCollection<VM_BodySlideSetting> bodySlides, BodySlideSetting importedBS, VM_BodySlideSetting existingBS)
        {
            var notesBak = existingBS.Notes;

            if (ExchangeRules)
            {
                var index = bodySlides.IndexOf(existingBS);
                var newVM = _bodySlideFactory(_oBodyUI.DescriptorUI, _generalUI.RaceGroupingEditor.RaceGroupings, bodySlides);
                newVM.CopyInViewModelFromModel(importedBS);
                bodySlides.Remove(existingBS);
                bodySlides.Insert(index, newVM);
                existingBS = newVM;
            }
            else
            {
                existingBS.DescriptorsSelectionMenu = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(importedBS.BodyShapeDescriptors, _oBodyUI.DescriptorUI, _generalUI.RaceGroupingEditor.RaceGroupings, _oBodyUI, _decriptorSelectionFactory);
            }

            if (ExchangeNotes)
            {
                existingBS.Notes = importedBS.Notes;
            }
            else
            {
                existingBS.Notes = notesBak;
            }
        }
    }

    public enum ExchangeMode
    {
        Import,
        Export
    }
}

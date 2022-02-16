﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_SettingsOBody : INotifyPropertyChanged, IHasAttributeGroupMenu
    {
        public VM_SettingsOBody(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_General generalSettingsVM)
        {
            DescriptorUI = new VM_BodyShapeDescriptorCreationMenu(raceGroupingVMs, this);
            BodySlidesUI = new VM_BodySlidesMenu(this, raceGroupingVMs);
            AttributeGroupMenu = new VM_AttributeGroupMenu();
            MiscUI = new VM_OBodyMiscSettings();

            DisplayedUI = BodySlidesUI;

            ImportAttributeGroups = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    var alreadyContainedGroups = AttributeGroupMenu.Groups.Select(x => x.Label).ToHashSet();
                    foreach (var attGroup in generalSettingsVM.AttributeGroupMenu.Groups)
                    {
                        if (!alreadyContainedGroups.Contains(attGroup.Label))
                        {
                            AttributeGroupMenu.Groups.Add(VM_AttributeGroup.Copy(attGroup, AttributeGroupMenu));
                        }
                    }
                }
                );

            ClickBodySlidesMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisplayedUI = BodySlidesUI
                );

            ClickDescriptorsMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => DisplayedUI = DescriptorUI
                );

            ClickAttributeGroupsMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => DisplayedUI = AttributeGroupMenu
                );

            ClickMiscMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => DisplayedUI = MiscUI
                );
        }

        public object DisplayedUI { get; set; }
        public VM_BodyShapeDescriptorCreationMenu DescriptorUI { get; set; }
        public VM_BodySlidesMenu BodySlidesUI { get; set; }
        public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
        public VM_OBodyMiscSettings MiscUI { get; set; }
        public RelayCommand ImportAttributeGroups { get; }
        public RelayCommand ClickBodySlidesMenu { get; }
        public RelayCommand ClickDescriptorsMenu { get; }
        public RelayCommand ClickAttributeGroupsMenu { get; }
        public RelayCommand ClickMiscMenu { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(Settings_OBody model, VM_SettingsOBody viewModel, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            viewModel.DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, raceGroupingVMs, viewModel, model);

            viewModel.DescriptorUI.TemplateDescriptorList.Clear();
            foreach (var descriptor in model.TemplateDescriptors)
            {
                viewModel.DescriptorUI.TemplateDescriptorList.Add(VM_BodyShapeDescriptor.GetViewModelFromModel(descriptor, raceGroupingVMs, viewModel, model));
            }

            viewModel.BodySlidesUI.CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides; // must load before presets

            viewModel.BodySlidesUI.BodySlidesMale.Clear();
            viewModel.BodySlidesUI.BodySlidesFemale.Clear();

            foreach (var preset in model.BodySlidesMale)
            {
                var presetVM = new VM_BodySlideSetting(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesMale, viewModel);
                VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel);
                viewModel.BodySlidesUI.BodySlidesMale.Add(presetVM);
            }

            foreach (var preset in model.BodySlidesFemale)
            {
                var presetVM = new VM_BodySlideSetting(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesFemale, viewModel);
                VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel);
                viewModel.BodySlidesUI.BodySlidesFemale.Add(presetVM);
            }

            VM_AttributeGroupMenu.GetViewModelFromModels(model.AttributeGroups, viewModel.AttributeGroupMenu);

            viewModel.MiscUI = VM_OBodyMiscSettings.GetViewModelFromModel(model);
        }

        public static void DumpViewModelToModel(Settings_OBody model, VM_SettingsOBody viewModel)
        {
            model.TemplateDescriptors = VM_BodyShapeDescriptorShell.DumpViewModelsToModels(viewModel.DescriptorUI.TemplateDescriptors, model.DescriptorRules);

            model.BodySlidesMale.Clear();
            model.BodySlidesFemale.Clear();

            foreach (var preset in viewModel.BodySlidesUI.BodySlidesMale)
            {
                model.BodySlidesMale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
            }
            foreach (var preset in viewModel.BodySlidesUI.BodySlidesFemale)
            {
                model.BodySlidesFemale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
            }
            VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);

            VM_OBodyMiscSettings.DumpViewModelToModel(model, viewModel.MiscUI);
        }
    }
}

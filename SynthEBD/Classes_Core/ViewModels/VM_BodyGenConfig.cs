using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SynthEBD
{
    class VM_BodyGenConfig : INotifyPropertyChanged
    {
        public VM_BodyGenConfig(ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            this.Label = "";
            this.Gender = Gender.female;

            this.GroupMappingUI = new VM_BodyGenGroupMappingMenu();
            this.GroupUI = new VM_BodyGenGroupsMenu();
            this.DescriptorUI = new VM_BodyGenMorphDescriptorMenu();
            this.TemplateMorphUI = new VM_BodyGenTemplateMenu(this, raceGroupingVMs);
            this.DisplayedUI = this.TemplateMorphUI;

            ClickTemplateMenu = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisplayedUI = this.TemplateMorphUI
                );

            ClickGroupMappingMenu = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisplayedUI = this.GroupMappingUI
                );
            ClickDescriptorMenu = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisplayedUI = this.DescriptorUI
                );
            ClickGroupsMenu = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisplayedUI = this.GroupUI
                );
        }

        public object DisplayedUI { get; set; }
        public VM_BodyGenGroupMappingMenu GroupMappingUI { get; set; }
        public VM_BodyGenGroupsMenu GroupUI { get; set; }
        public VM_BodyGenMorphDescriptorMenu DescriptorUI { get; set; }
        public VM_BodyGenTemplateMenu TemplateMorphUI { get; set; }

        public ICommand ClickTemplateMenu { get; }
        public ICommand ClickGroupMappingMenu { get; }
        public ICommand ClickDescriptorMenu { get; }
        public ICommand ClickGroupsMenu { get; }

        public string Label { get; set; }
        public Gender Gender { get; set; }



        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenConfig GetViewModelFromModel(BodyGenConfig model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            VM_BodyGenConfig viewModel = new VM_BodyGenConfig(raceGroupingVMs);
            viewModel.Label = model.Label;
            viewModel.Gender = model.Gender;
            foreach (var RTG in model.RacialTemplateGroupMap)
            {
                viewModel.GroupMappingUI.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG));
            }

            viewModel.GroupUI.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
            foreach (string group in model.TemplateGroups)
            {
                viewModel.GroupUI.TemplateGroups.Add(new VM_CollectionMemberString(group, viewModel.GroupUI.TemplateGroups));
            }

            viewModel.DescriptorUI.TemplateDescriptors = VM_BodyGenMorphDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors);

            foreach (var descriptor in model.TemplateDescriptors)
            {
                viewModel.DescriptorUI.TemplateDescriptorList.Add(VM_BodyGenMorphDescriptor.GetViewModelFromModel(descriptor));
            }

            foreach (var template in model.Templates)
            {
                var templateVM = new VM_BodyGenTemplate(viewModel.GroupUI.TemplateGroups, viewModel.DescriptorUI, raceGroupingVMs, viewModel.TemplateMorphUI.Templates);
                VM_BodyGenTemplate.GetViewModelFromModel(template, templateVM, viewModel.DescriptorUI, raceGroupingVMs);
                viewModel.TemplateMorphUI.Templates.Add(templateVM);
            }

            return viewModel;
        }
    }

    
    

    
}

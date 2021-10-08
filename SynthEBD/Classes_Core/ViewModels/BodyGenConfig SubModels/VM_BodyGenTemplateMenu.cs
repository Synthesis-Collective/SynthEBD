using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_BodyGenTemplateMenu : INotifyPropertyChanged
    {
        public VM_BodyGenTemplateMenu(VM_BodyGenConfig parentConfig)
        {
            this.Templates = new ObservableCollection<VM_BodyGenTemplate>();
            this.CurrentlyDisplayedTemplate = new VM_BodyGenTemplate(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI);
            AddTemplate = new SynthEBD.RelayCommand(
    canExecute: _ => true,
    execute: _ => this.Templates.Add(new VM_BodyGenTemplate(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI))
    );

            RemoveTemplate = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Templates.Remove(CurrentlyDisplayedTemplate)
                );
        }
        public ObservableCollection<VM_BodyGenTemplate> Templates { get; set; }

        public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }

        public RelayCommand AddTemplate { get; }
        public RelayCommand RemoveTemplate { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    
    public class VM_BodyGenTemplate : INotifyPropertyChanged
    {
        public VM_BodyGenTemplate(ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyGenMorphDescriptorMenu morphDescriptors)
        {
            this.Label = "";
            this.Notes = "";
            this.Specs = "";
            this.GroupSelectionCheckList = new VM_CollectionMemberStringCheckboxList(templateGroups);
            this.DescriptorsSelectionMenu = new VM_BodyGenMorphDescriptorSelectionMenu(morphDescriptors);
            this.AllowedRaces = new ObservableCollection<FormKey>();
            this.AllowedRaceGroupings = new ObservableCollection<string>();
            this.DisallowedRaces = new ObservableCollection<FormKey>();
            this.DisallowedRaceGroupings = new ObservableCollection<string>();
            this.AllowedAttributes = new ObservableCollection<NPCAttribute>();
            this.DisallowedAttributes = new ObservableCollection<NPCAttribute>();
            this.ForceIfAttributes = new ObservableCollection<NPCAttribute>();
            this.bAllowUnique = true;
            this.bAllowNonUnique = true;
            this.bAllowRandom = true;
            this.ProbabilityWeighting = 1;
            this.RequiredTemplates = new ObservableCollection<string>();
            this.WeightRange = new NPCWeightRange();

            this.Caption_MemberOfTemplateGroups = "";
            this.Caption_MorphDescriptors = "";
        }

        public string Label { get; set; }
        public string Notes { get; set; }
        public string Specs { get; set; } // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
        public VM_CollectionMemberStringCheckboxList GroupSelectionCheckList { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu DescriptorsSelectionMenu { get; set; }
        public ObservableCollection<FormKey> AllowedRaces { get; set; }
        public ObservableCollection<FormKey> DisallowedRaces { get; set; }
        public ObservableCollection<string> AllowedRaceGroupings { get; set; }
        public ObservableCollection<string> DisallowedRaceGroupings { get; set; }
        public ObservableCollection<NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
        public ObservableCollection<NPCAttribute> DisallowedAttributes { get; set; }
        public ObservableCollection<NPCAttribute> ForceIfAttributes { get; set; }
        public bool bAllowUnique { get; set; }
        public bool bAllowNonUnique { get; set; }
        public bool bAllowRandom { get; set; }
        public int ProbabilityWeighting { get; set; }
        public ObservableCollection<string> RequiredTemplates { get; set; }
        public NPCWeightRange WeightRange { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public string Caption_MemberOfTemplateGroups { get; set; }
        public string Caption_MorphDescriptors { get; set; }

        public static void GetViewModelFromModel(BodyGenConfig.BodyGenTemplate model, VM_BodyGenTemplate viewModel, VM_BodyGenMorphDescriptorMenu descriptorMenu)
        {
            viewModel.Label = model.Label;
            viewModel.Notes = model.Notes;
            viewModel.Specs = model.Specs;
            viewModel.GroupSelectionCheckList.InitializeFromHashSet(model.MemberOfTemplateGroups);
            viewModel.DescriptorsSelectionMenu = VM_BodyGenMorphDescriptorSelectionMenu.InitializeFromTemplate(model, descriptorMenu);
            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            viewModel.AllowedRaceGroupings = new ObservableCollection<string>(model.AllowedRaceGroupings);
            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            viewModel.DisallowedRaceGroupings = new ObservableCollection<string>(model.DisallowedRaceGroupings);
            viewModel.AllowedAttributes = new ObservableCollection<NPCAttribute>(model.AllowedAttributes);
            viewModel.DisallowedAttributes = new ObservableCollection<NPCAttribute>(model.DisallowedAttributes);
            viewModel.ForceIfAttributes = new ObservableCollection<NPCAttribute>(model.ForceIfAttributes);
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.bAllowRandom = model.bAllowRandom;
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.RequiredTemplates = new ObservableCollection<string>(model.RequiredTemplates);
            viewModel.WeightRange = model.WeightRange;
        }
    }
}

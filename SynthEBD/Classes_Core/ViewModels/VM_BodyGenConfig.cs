using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_BodyGenConfig : INotifyPropertyChanged
    {
        public VM_BodyGenConfig()
        {
            this.Label = "";
            this.Gender = Gender.female;
            this.RacialTemplateGroupMap = new ObservableCollection<VM_BodyGenRacialMapping>();
            this.Templates = new ObservableCollection<VM_BodyGenTemplate>();
            this.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
            this.TemplateGroupsCheckList = new VM_CollectionMemberStringCheckboxList(this.TemplateGroups);
            this.TemplateDescriptors = new ObservableCollection<VM_BodyGenMorphDescriptorShell>();
            this.TemplateDescriptorList = new ObservableCollection<VM_BodyGenMorphDescriptor>();

            this.CurrentlyDisplayedTemplate = new VM_BodyGenTemplate(this.TemplateGroups, this.TemplateDescriptors);
            this.CurrentlyDisplayedTemplateDescriptorShell = new VM_BodyGenMorphDescriptorShell(new ObservableCollection<VM_BodyGenMorphDescriptorShell>());

            AddTemplate = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Templates.Add(new VM_BodyGenTemplate(this.TemplateGroups, this.TemplateDescriptors))
                );

            RemoveTemplate = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Templates.Remove(CurrentlyDisplayedTemplate)
                );

            AddTemplateGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TemplateGroups.Add(new VM_CollectionMemberString("", this.TemplateGroups))
                );

            AddTemplateDescriptorShell = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TemplateDescriptors.Add(new VM_BodyGenMorphDescriptorShell(this.TemplateDescriptors))
                );

            RemoveTemplateDescriptorShell = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TemplateDescriptors.Remove(CurrentlyDisplayedTemplateDescriptorShell)
                ); 
        }

        public string Label { get; set; }
        public Gender Gender { get; set; }
        public ObservableCollection<VM_BodyGenRacialMapping> RacialTemplateGroupMap { get; set; }
        public ObservableCollection<VM_BodyGenTemplate> Templates { get; set; }
        public ObservableCollection<VM_CollectionMemberString> TemplateGroups { get; set; }
        public VM_CollectionMemberStringCheckboxList TemplateGroupsCheckList { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptorShell> TemplateDescriptors { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptor> TemplateDescriptorList { get; set; } // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

        public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }
        public VM_BodyGenMorphDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

        public RelayCommand AddTemplate { get; }
        public RelayCommand RemoveTemplate { get; }
        public RelayCommand AddTemplateGroup { get; }
        public RelayCommand RemoveTemplateGroup { get; }
        public RelayCommand AddTemplateDescriptorShell { get; }
        public RelayCommand RemoveTemplateDescriptorShell { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenConfig GetViewModelFromModel(BodyGenConfig model)
        {
            VM_BodyGenConfig viewModel = new VM_BodyGenConfig();
            viewModel.Label = model.Label;
            viewModel.Gender = model.Gender;
            foreach (var RTG in model.RacialTemplateGroupMap)
            {
                viewModel.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG));
            }

            viewModel.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
            foreach (string group in model.TemplateGroups)
            {
                viewModel.TemplateGroups.Add(new VM_CollectionMemberString(group, viewModel.TemplateGroups));
            }

            viewModel.TemplateDescriptors = VM_BodyGenMorphDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors);

            foreach (var descriptor in model.TemplateDescriptors)
            {
                viewModel.TemplateDescriptorList.Add(VM_BodyGenMorphDescriptor.GetViewModelFromModel(descriptor));
            }

            foreach (var template in model.Templates)
            {
                var templateVM = new VM_BodyGenTemplate(viewModel.TemplateGroups, viewModel.TemplateDescriptors);
                VM_BodyGenTemplate.GetViewModelFromModel(template, templateVM);
                viewModel.Templates.Add(templateVM);
            }

            return viewModel;
        }
    }

    public class VM_BodyGenMorphDescriptorSelector : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorSelector(ObservableCollection<VM_BodyGenMorphDescriptorShell> monitoredCollection)
        {
            this.MonitoredCollection = monitoredCollection;
        }

        public ObservableCollection<VM_BodyGenMorphDescriptorShell> MonitoredCollection { get; set; }
        public string Header { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        void BuildHeader()
        {
            string header = "";
            foreach (var shell in this.MonitoredCollection)
            {
                string subHeader = "";
                string catHeader = shell.Category + ": ";
                foreach (var descriptor in shell.Descriptors)
                {
                    if (descriptor.IsSelected)
                    {
                        subHeader += descriptor.Value + ", ";
                    }
                }
                if (subHeader.EndsWith(", "))
                {
                    subHeader = subHeader.Remove(subHeader.Length - 2);
                }
                if (subHeader != "")
                {
                    header = catHeader + subHeader + " | ";
                }
            }

            if (header.EndsWith(" | "))
            {
                header = header.Remove(header.Length - 3);
            }
            this.Header = header;
        }
    }

    public class VM_BodyGenMorphDescriptorShell : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorShell(ObservableCollection<VM_BodyGenMorphDescriptorShell> parentCollection)
        {
            this.Category = "";
            this.Descriptors = new ObservableCollection<VM_BodyGenMorphDescriptor>();
            this.ParentCollection = parentCollection;

            AddTemplateDescriptorValue = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Descriptors.Add(new VM_BodyGenMorphDescriptor(this))
                ) ;
        }

        public string Category { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptor> Descriptors {get; set;}
        public ObservableCollection<VM_BodyGenMorphDescriptorShell> ParentCollection { get; set; }
        public RelayCommand AddTemplateDescriptorValue { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_BodyGenMorphDescriptorShell> GetViewModelsFromModels(HashSet<BodyGenConfig.MorphDescriptor> models)
        {
            ObservableCollection<VM_BodyGenMorphDescriptorShell> viewModels = new ObservableCollection<VM_BodyGenMorphDescriptorShell>();
            VM_BodyGenMorphDescriptorShell shellViewModel = new VM_BodyGenMorphDescriptorShell(viewModels);
            VM_BodyGenMorphDescriptor viewModel = new VM_BodyGenMorphDescriptor(shellViewModel);
            List<string> usedCategories = new List<string>();

            foreach (var model in models)
            {
                viewModel = VM_BodyGenMorphDescriptor.GetViewModelFromModel(model);
                
                if (!usedCategories.Contains(model.Category))
                {
                    shellViewModel = new VM_BodyGenMorphDescriptorShell(viewModels);
                    shellViewModel.Category = model.Category;
                    viewModel.ParentShell = shellViewModel;
                    shellViewModel.Descriptors.Add(viewModel);
                    viewModels.Add(shellViewModel);
                    usedCategories.Add(model.Category);
                }
                else
                {
                    int index = usedCategories.IndexOf(model.Category);
                    viewModel.ParentShell = viewModels[index];
                    viewModels[index].Descriptors.Add(viewModel);
                }
            }

            return viewModels;
        }
    }

    public class VM_BodyGenMorphDescriptor : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptor(VM_BodyGenMorphDescriptorShell parentShell)
        {
            this.Category = "";
            this.Value = "";
            this.DispString = "";
            this.ParentShell = parentShell;
            this.IsSelected = false;

            RemoveDescriptorValue = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentShell.Descriptors.Remove(this)
                );
        }
        public string Category { get; set; }
        public string Value { get; set; }
        public string DispString { get; set; }
        public bool IsSelected { get; set; }

        public VM_BodyGenMorphDescriptorShell ParentShell { get; set; }

        public RelayCommand RemoveDescriptorValue { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenMorphDescriptor GetViewModelFromModel(BodyGenConfig.MorphDescriptor model)
        {
            VM_BodyGenMorphDescriptor viewModel = new VM_BodyGenMorphDescriptor(new VM_BodyGenMorphDescriptorShell(new ObservableCollection<VM_BodyGenMorphDescriptorShell>()));
            viewModel.Category = model.Category;
            viewModel.Value = model.Value;
            viewModel.DispString = model.DispString;
            return viewModel;
        }
    }
    public class VM_BodyGenRacialMapping : INotifyPropertyChanged
    {
        public VM_BodyGenRacialMapping()
        {
            this.Label = "";
            this.Races = new ObservableCollection<FormKey>();
            this.RaceGroupings = new ObservableCollection<string>();
            this.Combinations = new ObservableCollection<VM_BodyGenCombination>();
        }
        public string Label { get; set; }
        public ObservableCollection<FormKey> Races { get; set; }
        public ObservableCollection<string> RaceGroupings { get; set; }
        public ObservableCollection<VM_BodyGenCombination> Combinations { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model)
        {
            VM_BodyGenRacialMapping viewModel = new VM_BodyGenRacialMapping();

            viewModel.Label = model.Label;
            viewModel.Races = new ObservableCollection<FormKey>(model.Races);
            viewModel.RaceGroupings = new ObservableCollection<string>(model.RaceGroupings);
            foreach (var combination in model.Combinations)
            {
                viewModel.Combinations.Add(VM_BodyGenCombination.GetViewModelFromModel(combination));
            }
            return viewModel;
        }

        public class VM_BodyGenCombination : INotifyPropertyChanged
        {
            public VM_BodyGenCombination()
            {
                this.Members = new ObservableCollection<string>();
                this.ProbabilityWeighting = 1;
            }
            public ObservableCollection<string> Members { get; set; }
            public int ProbabilityWeighting { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model)
            {
                VM_BodyGenCombination viewModel = new VM_BodyGenCombination();
                viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
                viewModel.Members = new ObservableCollection<string>(model.Members);
                return viewModel;
            }
        }
    }

    public class VM_BodyGenTemplate : INotifyPropertyChanged
    {
        public VM_BodyGenTemplate(ObservableCollection<VM_CollectionMemberString> templateGroups, ObservableCollection<VM_BodyGenMorphDescriptorShell> morphDescriptors)
        {
            this.Label = "";
            this.Notes = "";
            this.Specs = "";
            this.MemberOfTemplateGroups = new VM_CollectionMemberStringCheckboxList(templateGroups);
            this.MorphDescriptors = new VM_BodyGenMorphDescriptorSelector(morphDescriptors);
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
        public VM_CollectionMemberStringCheckboxList MemberOfTemplateGroups { get; set; }
        public VM_BodyGenMorphDescriptorSelector MorphDescriptors { get; set; }
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

        public static void GetViewModelFromModel(BodyGenConfig.BodyGenTemplate model, VM_BodyGenTemplate viewModel)
        {
            viewModel.Label = model.Label;
            viewModel.Notes = model.Notes;
            viewModel.Specs = model.Specs;
            viewModel.MemberOfTemplateGroups.InitializeFromHashSet(model.MemberOfTemplateGroups);
            //viewModel.MorphDescriptors = VM_CollectionMemberString.InitializeFromHashSet(morphDescriptors, model.MorphDescriptors);
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

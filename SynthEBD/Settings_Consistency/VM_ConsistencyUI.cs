using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{
    public class VM_ConsistencyUI : INotifyPropertyChanged
    {
        public VM_ConsistencyUI()
        {
            this.Assignments = new ObservableCollection<VM_ConsistencyAssignment>();
            this.CurrentlyDisplayedAssignment = null;
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(INpcGetter).AsEnumerable();
            this.SelectedNPCFormKey = new FormKey();

            this.PropertyChanged += RefereshCurrentAssignment;

            DeleteCurrentNPC = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    this.CurrentlyDisplayedAssignment = null;
                    this.Assignments.Remove(CurrentlyDisplayedAssignment);
                }
                );

            DeleteAllNPCs = new SynthEBD.RelayCommand(
               canExecute: _ => true,
               execute: x =>
               {
                   if (MessageBox.Show("Are you sure you want to completely clear the consistency file?", "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                   {

                       this.CurrentlyDisplayedAssignment = null;
                       this.Assignments.Clear();
                   }
               }
               );
        }

        public ObservableCollection<VM_ConsistencyAssignment> Assignments { get; set; }
        public VM_ConsistencyAssignment CurrentlyDisplayedAssignment { get; set; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public FormKey SelectedNPCFormKey { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public RelayCommand DeleteCurrentNPC { get; set; }
        public RelayCommand DeleteAllNPCs { get; set; }

        public void RefereshCurrentAssignment(object sender, PropertyChangedEventArgs e)
        {
            this.CurrentlyDisplayedAssignment = this.Assignments.Where(x => x.NPCFormKey.ToString() == SelectedNPCFormKey.ToString()).FirstOrDefault();
        }

        public static void GetViewModelsFromModels(Dictionary<string, NPCAssignment> models, ObservableCollection<VM_ConsistencyAssignment> viewModels)
        {
            viewModels.Clear();
            foreach (var model in models)
            {
                if (model.Value == null) { continue; }
                viewModels.Add(VM_ConsistencyAssignment.GetViewModelFromModel(model.Value));
            }
        }

        public static void DumpViewModelsToModels(ObservableCollection<VM_ConsistencyAssignment> viewModels, Dictionary<string, NPCAssignment> models)
        {
            models.Clear();
            foreach (var viewModel in viewModels)
            {
                models.Add(viewModel.NPCFormKey.ToString(), VM_ConsistencyAssignment.DumpViewModelToModel(viewModel));
            }
        }
    }
}

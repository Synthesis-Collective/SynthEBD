using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_NPCAttribute
    {
        public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            this.Path = "";
            this.Value = "";
            this.GroupedAttributes = new ObservableCollection<object>();

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }

        public string Path { get; set; }
        public string Value { get; set; }
        public ObservableCollection<object> GroupedAttributes { get; set; } // everything within this collection is evaluated as AND (all must be true)

        public RelayCommand DeleteCommand { get; }


        public static VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            VM_NPCAttribute viewModel = new VM_NPCAttribute(parentCollection);
            viewModel.Path = model.Path;
            viewModel.Value = model.Value;

            return viewModel;
        }

        public static ObservableCollection<VM_NPCAttribute> GetViewModelsFromModels(HashSet<NPCAttribute> models)
        {
            ObservableCollection<VM_NPCAttribute> oc = new ObservableCollection<VM_NPCAttribute>();
            foreach (var m in models)
            {
                oc.Add(GetViewModelFromModel(m, oc));
            }
            return oc;
        }


        public class VM_NPCAttributeVoiceType
        {
            public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM)
            {
                this.VoiceTypeFormKey = new FormKey();
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public FormKey VoiceTypeFormKey { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }

        public class VM_NPCAttributeClass
        {
            public VM_NPCAttributeClass(VM_NPCAttribute parentVM)
            {
                this.ClassFormKey = new FormKey();
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public FormKey ClassFormKey { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }

        public class VM_NPCAttributeFactions
        {
            public VM_NPCAttributeFactions(VM_NPCAttribute parentVM)
            {
                this.FactionFormKeys = new HashSet<FormKey>();
                this.RankMin = -1;
                this.RankMax = 100;
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public HashSet<FormKey> FactionFormKeys { get; set; }
            public int RankMin { get; set; }
            public int RankMax { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }

        public class VM_NPCAttributeFaceTexture
        {
            public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM)
            {
                this.FaceTextureFormKey = new FormKey();
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public FormKey FaceTextureFormKey { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }

        public class VM_NPCAttributeRace
        {
            public VM_NPCAttributeRace(VM_NPCAttribute parentVM)
            {
                this.RaceFormKey = new FormKey();
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public FormKey RaceFormKey { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }

        public class VM_NPCAttributeNPC
        {
            public VM_NPCAttributeNPC(VM_NPCAttribute parentVM)
            {
                this.NPCFormKey = new FormKey();
                this.ParentVM = parentVM;
                DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedAttributes.Remove(this));
            }
            public FormKey NPCFormKey { get; set; }
            public VM_NPCAttribute ParentVM { get; set; }
            public RelayCommand DeleteCommand { get; }
        }
    }
    
}

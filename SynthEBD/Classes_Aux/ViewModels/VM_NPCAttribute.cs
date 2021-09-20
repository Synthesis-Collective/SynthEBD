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
            this.GroupedSubAttributes = new ObservableCollection<VM_NPCAttributeShell>();

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
            AddSubAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.GroupedSubAttributes.Add(new VM_NPCAttributeShell(this))
                );
        }

        public ObservableCollection<VM_NPCAttributeShell> GroupedSubAttributes { get; set; } // everything within this collection is evaluated as AND (all must be true)

        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddSubAttribute { get; }


        public static VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            VM_NPCAttribute viewModel = new VM_NPCAttribute(parentCollection);

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

        
    }

    public class VM_NPCAttributeShell
    {
        public VM_NPCAttributeShell(VM_NPCAttribute parentVM)
        {
            this.Type = NPCAttributeType.Class;
            this.Attribute = new VM_NPCAttributeClass(parentVM, this);
        }
        public object Attribute { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class VM_NPCAttributeVoiceType
    {
        public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.VoiceTypeFormKeys = new HashSet<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> VoiceTypeFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }

    public class VM_NPCAttributeClass
    {
        public VM_NPCAttributeClass(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.ClassFormKeys = new HashSet<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> ClassFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }

    public class VM_NPCAttributeFactions
    {
        public VM_NPCAttributeFactions(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FactionFormKeys = new HashSet<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> FactionFormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }

    public class VM_NPCAttributeFaceTexture
    {
        public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FaceTextureFormKeys = new HashSet<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> FaceTextureFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }

    public class VM_NPCAttributeRace
    {
        public VM_NPCAttributeRace(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.RaceFormKeys = new HashSet<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> RaceFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }

    public class VM_NPCAttributeNPC
    {
        public VM_NPCAttributeNPC(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.NPCFormKeys = new HashSet<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public HashSet<FormKey> NPCFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
    }
}

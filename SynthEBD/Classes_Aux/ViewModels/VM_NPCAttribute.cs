using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
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
    public class VM_NPCAttribute : INotifyPropertyChanged
    {
        public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            this.GroupedSubAttributes = new ObservableCollection<VM_NPCAttributeShell>();
            this.ParentCollection = parentCollection;
            this.GroupedSubAttributes.CollectionChanged += TrimEmptyAttributes;

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
            AddToParent = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Add(CreateNewFromUI(parentCollection)));
        }

        public ObservableCollection<VM_NPCAttributeShell> GroupedSubAttributes { get; set; } // everything within this collection is evaluated as AND (all must be true)

        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddToParent { get; }

        public ObservableCollection<VM_NPCAttribute> ParentCollection { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_NPCAttribute CreateNewFromUI(ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            VM_NPCAttribute newAtt = new VM_NPCAttribute(parentCollection);
            VM_NPCAttributeShell startingShell = new VM_NPCAttributeShell(newAtt);
            VM_NPCAttributeClass startingAttributeGroup = new VM_NPCAttributeClass(newAtt, startingShell);
            startingShell.Type = NPCAttributeType.Class;
            startingShell.Attribute = startingAttributeGroup;
            newAtt.GroupedSubAttributes.Add(startingShell);
            return newAtt;
        }

        public void TrimEmptyAttributes(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (this.GroupedSubAttributes.Count == 0)
            {
                this.ParentCollection.Remove(this);
            }
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

        public static VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            VM_NPCAttribute viewModel = new VM_NPCAttribute(parentCollection);
            foreach (var attributeShell in model.GroupedSubAttributes)
            {
                var shellVM = new VM_NPCAttributeShell(viewModel);
                shellVM.Type = attributeShell.Type;
                switch (attributeShell.Type)
                {
                    case NPCAttributeType.Class: shellVM.Attribute = VM_NPCAttributeClass.getViewModelFromModel((NPCAttributeClass)attributeShell, viewModel, shellVM); break;
                    case NPCAttributeType.Faction: shellVM.Attribute = VM_NPCAttributeFactions.getViewModelFromModel((NPCAttributeFactions)attributeShell, viewModel, shellVM); break;
                    case NPCAttributeType.FaceTexture: shellVM.Attribute = VM_NPCAttributeFaceTexture.getViewModelFromModel((NPCAttributeFaceTexture)attributeShell, viewModel, shellVM); break;
                    case NPCAttributeType.Race: shellVM.Attribute = VM_NPCAttributeRace.getViewModelFromModel((NPCAttributeRace)attributeShell, viewModel, shellVM); break;
                    case NPCAttributeType.NPC: shellVM.Attribute = VM_NPCAttributeNPC.getViewModelFromModel((NPCAttributeNPC)attributeShell, viewModel, shellVM); break;
                    case NPCAttributeType.VoiceType: shellVM.Attribute = VM_NPCAttributeVoiceType.getViewModelFromModel((NPCAttributeVoiceType)attributeShell, viewModel, shellVM); break;
                    default: //WARN USER
                        break;
                }
                viewModel.GroupedSubAttributes.Add(shellVM);
            }

            return viewModel;
        }
    }

    public class VM_NPCAttributeShell : INotifyPropertyChanged
    {
        public VM_NPCAttributeShell(VM_NPCAttribute parentVM)
        {
            this.Type = NPCAttributeType.Class;
            this.Attribute = new VM_NPCAttributeClass(parentVM, this);

            AddAdditionalSubAttributeToParent = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => parentVM.GroupedSubAttributes.Add(new VM_NPCAttributeShell(parentVM))
                );

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(this)); 

            ChangeType = new RelayCommand(canExecute: _ => true, execute: _ =>
            {
                switch (this.Type)
                {
                    case NPCAttributeType.Class: this.Attribute = new VM_NPCAttributeClass(parentVM, this); break;
                    case NPCAttributeType.FaceTexture: this.Attribute = new VM_NPCAttributeFaceTexture(parentVM, this); break;
                    case NPCAttributeType.Faction: this.Attribute = new VM_NPCAttributeFactions(parentVM, this); break;
                    case NPCAttributeType.NPC: this.Attribute = new VM_NPCAttributeNPC(parentVM, this); break;
                    case NPCAttributeType.Race: this.Attribute = new VM_NPCAttributeRace(parentVM, this); break;
                    case NPCAttributeType.VoiceType: this.Attribute = new VM_NPCAttributeVoiceType(parentVM, this); break;
                }
            }

            );
        }
        public object Attribute { get; set; }
        public NPCAttributeType Type { get; set; }

        public RelayCommand AddAdditionalSubAttributeToParent { get; }
        public RelayCommand DeleteCommand { get; }

        public RelayCommand ChangeType { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class VM_NPCAttributeVoiceType
    {
        public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.VoiceTypeFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(
                canExecute: _ => true, 
                execute: _ => 
                { 
                    parentVM.GroupedSubAttributes.Remove(parentShell);
                    if (parentVM.GroupedSubAttributes.Count == 0)
                    {
                        parentVM.ParentCollection.Remove(parentVM);
                    }
                }) ;
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IVoiceTypeGetter).AsEnumerable();

        }
        public ObservableCollection<FormKey> VoiceTypeFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeVoiceType getViewModelFromModel(NPCAttributeVoiceType model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeVoiceType(parentVM, parentShell);
            newAtt.VoiceTypeFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            return newAtt;
        }
    }

    public class VM_NPCAttributeClass
    {
        public VM_NPCAttributeClass(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.ClassFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;

            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IClassGetter).AsEnumerable();
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
        }
        public ObservableCollection<FormKey> ClassFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeClass getViewModelFromModel(NPCAttributeClass model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeClass(parentVM, parentShell);
            newAtt.ClassFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            return newAtt;
        }
    }

    public class VM_NPCAttributeFactions
    {
        public VM_NPCAttributeFactions(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FactionFormKeys = new ObservableCollection<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IFactionGetter).AsEnumerable();
        }
        public ObservableCollection<FormKey> FactionFormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeFactions getViewModelFromModel(NPCAttributeFactions model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeFactions(parentVM, parentShell);
            newAtt.FactionFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            newAtt.RankMin = model.RankMin;
            newAtt.RankMax = model.RankMax;
            return newAtt;
        }
    }

    public class VM_NPCAttributeFaceTexture
    {
        public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FaceTextureFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(ITextureSetGetter).AsEnumerable();
        }
        public ObservableCollection<FormKey> FaceTextureFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeFaceTexture getViewModelFromModel(NPCAttributeFaceTexture model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeFaceTexture(parentVM, parentShell);
            newAtt.FaceTextureFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            return newAtt;
        }
    }

    public class VM_NPCAttributeRace
    {
        public VM_NPCAttributeRace(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.RaceFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IRaceGetter).AsEnumerable();
        }
        public ObservableCollection<FormKey> RaceFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeRace getViewModelFromModel(NPCAttributeRace model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeRace(parentVM, parentShell);
            newAtt.RaceFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            return newAtt;
        }
    }

    public class VM_NPCAttributeNPC
    {
        public VM_NPCAttributeNPC(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.NPCFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.AllowedFormKeyTypes = typeof(INpcGetter).AsEnumerable();
        }
        public ObservableCollection<FormKey> NPCFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public static VM_NPCAttributeNPC getViewModelFromModel(NPCAttributeNPC model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeNPC(parentVM, parentShell);
            newAtt.NPCFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            return newAtt;
        }
    }
}

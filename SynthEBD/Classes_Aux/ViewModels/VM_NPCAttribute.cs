using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Media;
using static SynthEBD.RecordIntellisense;

namespace SynthEBD
{
    public class VM_NPCAttribute : INotifyPropertyChanged
    {
        public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            this.GroupedSubAttributes = new ObservableCollection<VM_NPCAttributeShell>();
            this.ParentCollection = parentCollection;
            this.GroupedSubAttributes.CollectionChanged += TrimEmptyAttributes;

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
            AddToParent = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Add(CreateNewFromUI(parentCollection, this.DisplayForceIfOption, this.DisplayForceIfWeight, attributeGroups)));
            this.DisplayForceIfOption = true;
        }

        public ObservableCollection<VM_NPCAttributeShell> GroupedSubAttributes { get; set; } // everything within this collection is evaluated as AND (all must be true)
        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddToParent { get; }
        public bool DisplayForceIfOption { get; set; }
        public bool? DisplayForceIfWeight { get; set; }
        public ObservableCollection<VM_NPCAttribute> ParentCollection { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_NPCAttribute CreateNewFromUI(ObservableCollection<VM_NPCAttribute> parentCollection, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            VM_NPCAttribute newAtt = new VM_NPCAttribute(parentCollection, attributeGroups);
            VM_NPCAttributeShell startingShell = new VM_NPCAttributeShell(newAtt, displayForceIfOption, displayForceIfWeight, attributeGroups);
            VM_NPCAttributeClass startingAttributeGroup = new VM_NPCAttributeClass(newAtt, startingShell);
            startingShell.Type = NPCAttributeType.Class;
            startingShell.Attribute = startingAttributeGroup;
            newAtt.GroupedSubAttributes.Add(startingShell);
            newAtt.DisplayForceIfOption = displayForceIfOption;
            return newAtt;
        }

        public void TrimEmptyAttributes(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (this.GroupedSubAttributes.Count == 0)
            {
                this.ParentCollection.Remove(this);
            }
        }

        public static ObservableCollection<VM_NPCAttribute> GetViewModelsFromModels(HashSet<NPCAttribute> models, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            ObservableCollection<VM_NPCAttribute> oc = new ObservableCollection<VM_NPCAttribute>();
            foreach (var m in models)
            {
                oc.Add(GetViewModelFromModel(m, oc, attributeGroups, displayForceIfOption, displayForceIfWeight));
            }
            return oc;
        } 

        public static VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            VM_NPCAttribute viewModel = new VM_NPCAttribute(parentCollection, attributeGroups);
            viewModel.DisplayForceIfOption = displayForceIfOption;
            foreach (var attributeShellModel in model.SubAttributes)
            {
                var shellVM = new VM_NPCAttributeShell(viewModel, displayForceIfOption, displayForceIfWeight, attributeGroups);
                shellVM.Type = attributeShellModel.Type;
                switch (attributeShellModel.Type)
                {
                    case NPCAttributeType.Class: shellVM.Attribute = VM_NPCAttributeClass.GetViewModelFromModel((NPCAttributeClass)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.Custom: shellVM.Attribute = VM_NPCAttributeCustom.GetViewModelFromModel((NPCAttributeCustom)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.Faction: shellVM.Attribute = VM_NPCAttributeFactions.GetViewModelFromModel((NPCAttributeFactions)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.FaceTexture: shellVM.Attribute = VM_NPCAttributeFaceTexture.GetViewModelFromModel((NPCAttributeFaceTexture)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.Race: shellVM.Attribute = VM_NPCAttributeRace.getViewModelFromModel((NPCAttributeRace)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.NPC: shellVM.Attribute = VM_NPCAttributeNPC.getViewModelFromModel((NPCAttributeNPC)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.VoiceType: shellVM.Attribute = VM_NPCAttributeVoiceType.GetViewModelFromModel((NPCAttributeVoiceType)attributeShellModel, viewModel, shellVM); break;
                    case NPCAttributeType.Group: shellVM.Attribute = VM_NPCAttributeGroup.GetViewModelFromModel((NPCAttributeGroup)attributeShellModel, viewModel, shellVM, attributeGroups); break; //new VM_NPCAttributeGroup(viewModel, shellVM, attributeGroups); break; // Setting the checkbox selections MUST be done in the calling function after all `attributeGroups` view models have been created from their corresponding model (otherwise the required checkbox entry may not yet exist). This is done in VM_AttributeGroupMenu.GetViewModelFromModels().
                    default: //WARN USER
                        break;
                }
                shellVM.ForceIf = attributeShellModel.ForceIf;
                shellVM.DisplayForceIfOption = displayForceIfOption;
                viewModel.GroupedSubAttributes.Add(shellVM);
            }

            return viewModel;
        }

        public static HashSet<NPCAttribute> DumpViewModelsToModels(ObservableCollection<VM_NPCAttribute> viewModels)
        {
            HashSet<NPCAttribute> hs = new HashSet<NPCAttribute>();
            foreach (var v in viewModels)
            {
                hs.Add(DumpViewModelToModel(v));
            }
            return hs;
        }

        public static NPCAttribute DumpViewModelToModel(VM_NPCAttribute viewModel)
        {
            var model = new NPCAttribute();
            foreach (var subAttVM in viewModel.GroupedSubAttributes)
            {
                switch(subAttVM.Type)
                {
                    case NPCAttributeType.Class: model.SubAttributes.Add(VM_NPCAttributeClass.DumpViewModelToModel((VM_NPCAttributeClass)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.Custom: model.SubAttributes.Add(VM_NPCAttributeCustom.DumpViewModelToModel((VM_NPCAttributeCustom)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.Faction: model.SubAttributes.Add(VM_NPCAttributeFactions.DumpViewModelToModel((VM_NPCAttributeFactions)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.FaceTexture: model.SubAttributes.Add(VM_NPCAttributeFaceTexture.DumpViewModelToModel((VM_NPCAttributeFaceTexture)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.Group: model.SubAttributes.Add(VM_NPCAttributeGroup.DumpViewModelToModel((VM_NPCAttributeGroup)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.Race: model.SubAttributes.Add(VM_NPCAttributeRace.DumpViewModelToModel((VM_NPCAttributeRace)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.NPC: model.SubAttributes.Add(VM_NPCAttributeNPC.DumpViewModelToModel((VM_NPCAttributeNPC)subAttVM.Attribute, subAttVM.ForceIf)); break;
                    case NPCAttributeType.VoiceType: model.SubAttributes.Add(VM_NPCAttributeVoiceType.DumpViewModelToModel((VM_NPCAttributeVoiceType)subAttVM.Attribute, subAttVM.ForceIf)); break;
                }
            }
            return model;
        }
    }

    public class VM_NPCAttributeShell : INotifyPropertyChanged
    {
        public VM_NPCAttributeShell(VM_NPCAttribute parentVM, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            this.Type = NPCAttributeType.Class;
            this.Attribute = new VM_NPCAttributeClass(parentVM, this);
            this.ForceIf = false;
            this.ForceIfWeight = 1;
            this.DisplayForceIfOption = displayForceIfOption;
            if (displayForceIfWeight is not null)
            {
                this.DisplayForceIfWeight = displayForceIfWeight.Value;
            }
            else
            {
                this.DisplayForceIfWeight = displayForceIfOption;
            }

            AddAdditionalSubAttributeToParent = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => parentVM.GroupedSubAttributes.Add(new VM_NPCAttributeShell(parentVM, this.DisplayForceIfOption, this.DisplayForceIfWeight, attributeGroups))
                );

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(this));

            ChangeType = new RelayCommand(canExecute: _ => true, execute: _ =>
            {
                switch (this.Type)
                {
                    case NPCAttributeType.Class: this.Attribute = new VM_NPCAttributeClass(parentVM, this); break;
                    case NPCAttributeType.Custom: this.Attribute = new VM_NPCAttributeCustom(parentVM, this); break;
                    case NPCAttributeType.FaceTexture: this.Attribute = new VM_NPCAttributeFaceTexture(parentVM, this); break;
                    case NPCAttributeType.Faction: this.Attribute = new VM_NPCAttributeFactions(parentVM, this); break;
                    case NPCAttributeType.Group: this.Attribute = new VM_NPCAttributeGroup(parentVM, this, attributeGroups); break;
                    case NPCAttributeType.NPC: this.Attribute = new VM_NPCAttributeNPC(parentVM, this); break;
                    case NPCAttributeType.Race: this.Attribute = new VM_NPCAttributeRace(parentVM, this); break;
                    case NPCAttributeType.VoiceType: this.Attribute = new VM_NPCAttributeVoiceType(parentVM, this); break;
                }
            }

            );
        }
        public ISubAttributeViewModel Attribute { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public int ForceIfWeight { get; set; }
        public bool DisplayForceIfOption { get; set; }
        public bool DisplayForceIfWeight { get; set; }

        public RelayCommand AddAdditionalSubAttributeToParent { get; }
        public RelayCommand DeleteCommand { get; }

        public RelayCommand ChangeType { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public interface ISubAttributeViewModel
    {
        VM_NPCAttribute ParentVM { get; set; }
        IObservable<System.Reactive.Unit> NeedsRefresh { get; }
    }

    public class VM_NPCAttributeVoiceType : ISubAttributeViewModel
    {
        public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.VoiceTypeFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
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
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IVoiceTypeGetter).AsEnumerable();

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> VoiceTypeFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }

        public IObservable<Unit> NeedsRefresh { get; }

        public static VM_NPCAttributeVoiceType GetViewModelFromModel(NPCAttributeVoiceType model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeVoiceType(parentVM, parentShell);
            newAtt.VoiceTypeFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }
        public static NPCAttributeVoiceType DumpViewModelToModel(VM_NPCAttributeVoiceType viewModel, bool forceIf)
        {
            return new NPCAttributeVoiceType() { Type = NPCAttributeType.VoiceType, FormKeys = viewModel.VoiceTypeFormKeys.ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight};
        }
    }

    public class VM_NPCAttributeClass : ISubAttributeViewModel
    {
        public VM_NPCAttributeClass(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.ClassFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IClassGetter).AsEnumerable();
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> ClassFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public IObservable<Unit> NeedsRefresh { get; }
        public static VM_NPCAttributeClass GetViewModelFromModel(NPCAttributeClass model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeClass(parentVM, parentShell);
            newAtt.ClassFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }

        public static NPCAttributeClass DumpViewModelToModel(VM_NPCAttributeClass viewModel, bool forceIf)
        {
            return new NPCAttributeClass() { Type = NPCAttributeType.Class, FormKeys = viewModel.ClassFormKeys.ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public class VM_NPCAttributeCustom : ISubAttributeViewModel, INotifyPropertyChanged, IImplementsRecordIntellisense
    {
        public VM_NPCAttributeCustom(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.CustomType = CustomAttributeType.Text;
            this.IntellisensedPath = "";
            this.ValueStr = "";
            this.ValueFKs = new ObservableCollection<FormKey>();

            this.ShowValueTextField = true;
            this.ShowValueFormKeyPicker = false;
            this.ShowValueBoolPicker = false;

            this.PathSuggestions = new ObservableCollection<PathSuggestion>();
            this.ChosenPathSuggestion = null;
            this.ReferenceNPCFormKey = new FormKey();
            this.ReferenceNPCType = typeof(INpcGetter).AsEnumerable();
            
            this.ValueGetterTypes = new SortedDictionary<string, Type>();

            foreach (var reg in Loqui.LoquiRegistration.StaticRegister.Registrations.Where(x => x.ProtocolKey.Namespace == "Skyrim").Where(x => x.GetterType.IsAssignableTo(typeof(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter))))
            {
                ValueGetterTypes.Add(reg.Name, reg.GetterType);
            }

            this.Comparators = new ObservableCollection<string>();

            this.LinkCache = PatcherEnvironmentProvider.Environment.LinkCache;

            ParentVM = parentVM;
            ParentShell = parentShell;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();

            this.StatusFontColor = new SolidColorBrush(Colors.White);

            InitializeSubscriptions(this);

            this.WhenAnyValue(x => x.CustomType).Subscribe(x => UpdateValueDisplay());

            this.WhenAnyValue(x => x.ValueFKtype).Subscribe(x => UpdateFormKeyPickerRecordType());

            this.WhenAnyValue(x => x.ValueStr).Subscribe(x => Evaluate());
            this.WhenAnyValue(x => x.ValueFKs).Subscribe(x => Evaluate());
            this.WhenAnyValue(x => x.ChosenComparator).Subscribe(x => Evaluate());
            this.ValueFKs.CollectionChanged += Evaluate;
            this.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => Evaluate());
            this.WhenAnyValue(x => x.ReferenceNPCFormKey).Subscribe(x => Evaluate());
        }

        public CustomAttributeType CustomType { get; set; }
        public string IntellisensedPath { get; set; }
        public string ValueStr { get; set; }
        public ObservableCollection<FormKey> ValueFKs { get; set; }
        public SortedDictionary<string, Type> ValueGetterTypes { get; set; }
        public Type ValueFKtype { get; set; }
        public IEnumerable<Type> ValueFKtypeCollection { get; set; }
        public ILinkCache LinkCache { get; set; }
        public ObservableCollection<PathSuggestion> PathSuggestions { get; set; }
        public PathSuggestion ChosenPathSuggestion { get; set; }
        public FormKey ReferenceNPCFormKey { get; set; }
        public IEnumerable<Type> ReferenceNPCType { get; set; }
        public ObservableCollection<string> Comparators { get; set; }
        public string ChosenComparator { get; set; }
        public string EvalResult { get; set; }

        public bool ShowValueTextField { get; set; }
        public bool ShowValueFormKeyPicker { get; set; }
        public bool ShowValueBoolPicker { get; set; }

        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public IObservable<Unit> NeedsRefresh { get; }
        public System.Windows.Media.SolidColorBrush StatusFontColor { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public static VM_NPCAttributeCustom GetViewModelFromModel(NPCAttributeCustom model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var viewModel = new VM_NPCAttributeCustom(parentVM, parentShell);
            viewModel.ChosenComparator = model.Comparator;
            viewModel.CustomType = model.CustomType;
            viewModel.IntellisensedPath = model.Path;
            viewModel.ValueStr = model.ValueStr;
            viewModel.ValueFKs = new ObservableCollection<FormKey>(model.ValueFKs);
            viewModel.ValueFKtype = model.SelectedFormKeyType;
            viewModel.ReferenceNPCFormKey = model.ReferenceNPCFK;
            viewModel.ChosenPathSuggestion = null;
            parentShell.ForceIfWeight = model.Weighting;
            return viewModel;
        }

        public static NPCAttributeCustom DumpViewModelToModel(VM_NPCAttributeCustom viewModel, bool forceIf)
        {
            var model = new NPCAttributeCustom();
            model.Type = NPCAttributeType.Custom;
            model.Comparator = viewModel.ChosenComparator;
            model.CustomType = viewModel.CustomType;
            model.Path = viewModel.IntellisensedPath;
            model.ValueStr = viewModel.ValueStr;
            model.ValueFKs = viewModel.ValueFKs.ToHashSet();
            model.ForceIf = forceIf;
            model.Weighting = viewModel.ParentShell.ForceIfWeight;
            model.ReferenceNPCFK = viewModel.ReferenceNPCFormKey;
            model.SelectedFormKeyType = viewModel.ValueFKtype;
            return model;
        }

        public void Evaluate(object sender, NotifyCollectionChangedEventArgs e)
        {
            Evaluate();
        }
        public void Evaluate()
        {
            if (this.ReferenceNPCFormKey.IsNull)
            {
                EvalResult = "Can't evaluate: Reference NPC not set";
                this.StatusFontColor = new SolidColorBrush(Colors.Yellow);
            }
            else if (this.CustomType != CustomAttributeType.Record && this.ValueStr == "")
            {
                EvalResult = "Can't evaluate: No value provided";
                this.StatusFontColor = new SolidColorBrush(Colors.Yellow);
            }
            else if (this.CustomType == CustomAttributeType.Record && !this.ValueFKs.Any())
            {
                EvalResult = "Can't evaluate: No FormKeys selected";
                this.StatusFontColor = new SolidColorBrush(Colors.Yellow);
            }
            else if (this.CustomType == CustomAttributeType.Integer && !Int32.TryParse(ValueStr, out _))
            {
                EvalResult = "Can't convert " + ValueStr + " to an Integer value";
                this.StatusFontColor = new SolidColorBrush(Colors.Red);
            }
            else if (this.CustomType == CustomAttributeType.Decimal && !float.TryParse(ValueStr, out _))
            {
                EvalResult = "Can't convert " + ValueStr + " to a Decimal value";
                this.StatusFontColor = new SolidColorBrush(Colors.Red);
            }
            else if (this.CustomType == CustomAttributeType.Boolean && !bool.TryParse(ValueStr, out _))
            {
                EvalResult = "Can't convert " + ValueStr + " to a Boolean value";
                this.StatusFontColor = new SolidColorBrush(Colors.Red);
            }
            else
            {
                if (!PatcherEnvironmentProvider.Environment.LinkCache.TryResolve<INpcGetter>(ReferenceNPCFormKey, out var refNPC))
                {
                    EvalResult = "Error: can't resolve reference NPC.";
                    this.StatusFontColor = new SolidColorBrush(Colors.Red);
                }
                bool matched = AttributeMatcher.EvaluateCustomAttribute(refNPC, DumpViewModelToModel(this, false), LinkCache, out string dispMessage);
                if (matched)
                {
                    EvalResult = "Matched!";
                    this.StatusFontColor = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    EvalResult = dispMessage;
                    this.StatusFontColor = new SolidColorBrush(Colors.Red);
                }
            }
        }

        public void UpdateValueDisplay()
        {
            if (this.CustomType == CustomAttributeType.Record)
            {
                this.ShowValueFormKeyPicker = true;
                this.ShowValueTextField = false;
                this.ShowValueBoolPicker = false;
            }
            else if (this.CustomType == CustomAttributeType.Boolean)
            {
                this.ShowValueFormKeyPicker = false;
                this.ShowValueTextField = false;
                this.ShowValueBoolPicker = true;
            }
            else
            {
                this.ShowValueFormKeyPicker = false;
                this.ShowValueTextField = true;
                this.ShowValueBoolPicker = false;
            }

            this.Comparators = new ObservableCollection<string>() { "=", "!=" };
            if (this.CustomType == CustomAttributeType.Integer || this.CustomType == CustomAttributeType.Decimal)
            {
                this.Comparators.Add("<");
                this.Comparators.Add("<=");
                this.Comparators.Add(">");
                this.Comparators.Add(">=");
            }

            Evaluate();
        }

        public void UpdateFormKeyPickerRecordType()
        {
            ValueFKtypeCollection = ValueFKtype.AsEnumerable();
            Evaluate();
        }
    }

    public class VM_NPCAttributeFactions : ISubAttributeViewModel
    {
        public VM_NPCAttributeFactions(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FactionFormKeys = new ObservableCollection<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IFactionGetter).AsEnumerable();

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> FactionFormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public IObservable<Unit> NeedsRefresh { get; }
        public static VM_NPCAttributeFactions GetViewModelFromModel(NPCAttributeFactions model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeFactions(parentVM, parentShell);
            newAtt.FactionFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            newAtt.RankMin = model.RankMin;
            newAtt.RankMax = model.RankMax;
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }
        public static NPCAttributeFactions DumpViewModelToModel(VM_NPCAttributeFactions viewModel, bool forceIf)
        {
            return new NPCAttributeFactions() { Type = NPCAttributeType.Faction, FormKeys = viewModel.FactionFormKeys.ToHashSet(), RankMin = viewModel.RankMin, RankMax = viewModel.RankMax, ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public class VM_NPCAttributeFaceTexture : ISubAttributeViewModel
    {
        public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.FaceTextureFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(ITextureSetGetter).AsEnumerable();

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> FaceTextureFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public IObservable<Unit> NeedsRefresh { get; }
        public static VM_NPCAttributeFaceTexture GetViewModelFromModel(NPCAttributeFaceTexture model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeFaceTexture(parentVM, parentShell);
            newAtt.FaceTextureFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }

        public static NPCAttributeFaceTexture DumpViewModelToModel(VM_NPCAttributeFaceTexture viewModel, bool forceIf)
        {
            return new NPCAttributeFaceTexture() { Type = NPCAttributeType.FaceTexture, FormKeys = viewModel.FaceTextureFormKeys.ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public class VM_NPCAttributeRace : ISubAttributeViewModel
    {
        public VM_NPCAttributeRace(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.RaceFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(IRaceGetter).AsEnumerable();

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> RaceFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public IObservable<Unit> NeedsRefresh { get; }
        public static VM_NPCAttributeRace getViewModelFromModel(NPCAttributeRace model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeRace(parentVM, parentShell);
            newAtt.RaceFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }

        public static NPCAttributeRace DumpViewModelToModel(VM_NPCAttributeRace viewModel, bool forceIf)
        {
            return new NPCAttributeRace() { Type = NPCAttributeType.Race, FormKeys = viewModel.RaceFormKeys.ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public class VM_NPCAttributeNPC : ISubAttributeViewModel
    {
        public VM_NPCAttributeNPC(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            this.NPCFormKeys = new ObservableCollection<FormKey>();
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
            this.AllowedFormKeyTypes = typeof(INpcGetter).AsEnumerable();

            this.NeedsRefresh = System.Reactive.Linq.Observable.Empty<Unit>();
        }
        public ObservableCollection<FormKey> NPCFormKeys { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
        public IObservable<Unit> NeedsRefresh { get; }
        public static VM_NPCAttributeNPC getViewModelFromModel(NPCAttributeNPC model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
        {
            var newAtt = new VM_NPCAttributeNPC(parentVM, parentShell);
            newAtt.NPCFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
            parentShell.ForceIfWeight = model.Weighting;
            return newAtt;
        }
        public static NPCAttributeNPC DumpViewModelToModel(VM_NPCAttributeNPC viewModel, bool forceIf)
        {
            return new NPCAttributeNPC() { Type = NPCAttributeType.NPC, FormKeys = viewModel.NPCFormKeys.ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public class VM_NPCAttributeGroup : INotifyPropertyChanged, ISubAttributeViewModel
    {
        public VM_NPCAttributeGroup(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            this.AttributeCheckList = new VM_AttributeGroupCheckList(attributeGroups);
            this.ParentVM = parentVM;
            this.ParentShell = parentShell;
            this.SubscribedAttributeGroupCollection = attributeGroups;

            SubscribedAttributeGroupCollection.CollectionChanged += RebuildCheckList; // this is needed because the Subscription set in this constructor will not follow other attribute groups added to the parent collection after the current one is loaded

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));

            NeedsRefresh = this.AttributeCheckList.AttributeSelections.Select(x => x.WhenAnyValue(x => x.IsSelected)).Merge().Unit();
        }
        public VM_AttributeGroupCheckList AttributeCheckList { get; set; }
        public VM_NPCAttribute ParentVM { get; set; }
        public VM_NPCAttributeShell ParentShell { get; set; }
        public RelayCommand DeleteCommand { get; }

        public ObservableCollection<VM_AttributeGroup> SubscribedAttributeGroupCollection { get; set; }

        public IObservable<Unit> NeedsRefresh { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public void RebuildCheckList(object sender, NotifyCollectionChangedEventArgs e)
        {
            AttributeCheckList = new VM_AttributeGroupCheckList(SubscribedAttributeGroupCollection);
            NeedsRefresh = this.AttributeCheckList.AttributeSelections.Select(x => x.WhenAnyValue(x => x.IsSelected)).Merge().Unit();
        }

        public static VM_NPCAttributeGroup GetViewModelFromModel(NPCAttributeGroup model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            var newAtt = new VM_NPCAttributeGroup(parentVM, parentShell, attributeGroups);
            
            foreach (var group in newAtt.AttributeCheckList.AttributeSelections.Where(x => model.SelectedLabels.Contains(x.Label)))
            {
                group.IsSelected = true;
            }

            parentShell.ForceIfWeight = model.Weighting;

            return newAtt;
        }
        public static NPCAttributeGroup DumpViewModelToModel(VM_NPCAttributeGroup viewModel, bool forceIf)
        {
            return new NPCAttributeGroup() { Type = NPCAttributeType.Group, SelectedLabels = viewModel.AttributeCheckList.AttributeSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet(), ForceIf = forceIf, Weighting = viewModel.ParentShell.ForceIfWeight };
        }
    }

    public enum BoolVals
    {
        True,
        False
    }
}

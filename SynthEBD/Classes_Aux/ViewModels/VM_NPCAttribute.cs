using DynamicData;
using DynamicData.Binding;
using Humanizer;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Noggog.WPF;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Printing;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Media;
using static SynthEBD.RecordIntellisense;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_NPCAttribute : VM
{
    public delegate VM_NPCAttribute Factory(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups);
    private VM_NPCAttributeCreator _creator;
    private ObservableCollection<VM_AttributeGroup> _subscribedAttributeGroups;
    public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, VM_NPCAttributeCreator creator, AttributeMatcher attributeMatcher, IEnvironmentStateProvider environmentProvider, VM_NPCAttribute.Factory selfFactory)
    {
        _creator = creator;
        _subscribedAttributeGroups = attributeGroups;

        ParentCollection = parentCollection;

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        AddToParent = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Add(_creator.CreateNewFromUI(ParentCollection, DisplayForceIfOption, DisplayForceIfWeight, _subscribedAttributeGroups)));
        Validate = new RelayCommand(canExecute: _ => true, execute: _ => {
            var validator = new VM_AttributeValidator(this, _subscribedAttributeGroups, environmentProvider, attributeMatcher);
            Window_AttributeValidator window = new Window_AttributeValidator();
            window.DataContext = validator;
            window.ShowDialog();
        });

        GroupedSubAttributes.ToObservableChangeSet().Subscribe(x => {
            NeedsRefresh = GroupedSubAttributes.Select(x => x.WhenAnyObservable(y => y.Attribute.NeedsRefresh)).Merge().Unit();
            TrimEmptyAttributes();
        }).DisposeWith(this);
    }

    public ObservableCollection<VM_NPCAttributeShell> GroupedSubAttributes { get; set; } = new(); // everything within this collection is evaluated as AND (all must be true)
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddToParent { get; }
    public RelayCommand Validate { get; }
    public bool DisplayForceIfOption { get; set; } = true;
    public bool? DisplayForceIfWeight { get; set; }
    public ObservableCollection<VM_NPCAttribute> ParentCollection { get; set; }
    public VM_NPCAttributeShell MostRecentlyEditedShell { get; set; }
    public IObservable<Unit> NeedsRefresh { get; set; }

    public VM_NPCAttribute Clone(ObservableCollection<VM_NPCAttribute> parentCollection)
    {
        var model = DumpViewModelToModel();
        var clone = _creator.GetViewModelFromModel(model, parentCollection, _subscribedAttributeGroups, DisplayForceIfOption, DisplayForceIfWeight);
        return clone;
    }

    public class VM_NPCAttributeCreator
    {
        private readonly VM_NPCAttribute.Factory _attributeFactory;
        private readonly VM_NPCAttributeShell.Factory _shellFactory;
        private readonly VM_NPCAttributeClass.Factory _classFactory;
        private readonly VM_NPCAttributeCustom.Factory _customFactory;
        private readonly VM_NPCAttributeFaceTexture.Factory _faceTextureFactory;
        private readonly VM_NPCAttributeFactions.Factory _factionsFactory;
        private readonly VM_NPCAttributeKeyword.Factory _keywordFactory;
        private readonly VM_NPCAttributeMisc.Factory _miscFactory;
        private readonly VM_NPCAttributeMod.Factory _modFactory;
        private readonly VM_NPCAttributeNPC.Factory _npcFactory;
        private readonly VM_NPCAttributeRace.Factory _raceFactory;
        private readonly VM_NPCAttributeVoiceType.Factory _voiceTypeFactory;

        private readonly Logger _logger;

        public VM_NPCAttributeCreator(VM_NPCAttribute.Factory factory,
            VM_NPCAttributeShell.Factory shellFactory,
            VM_NPCAttributeClass.Factory classFactory,
            VM_NPCAttributeCustom.Factory customFactory,
            VM_NPCAttributeFaceTexture.Factory faceTextureFactory,
            VM_NPCAttributeFactions.Factory factionsFactory,
            VM_NPCAttributeKeyword.Factory keywordFactory,
            VM_NPCAttributeMisc.Factory miscFactory,
            VM_NPCAttributeMod.Factory modFactory,
            VM_NPCAttributeNPC.Factory npcFactory,
            VM_NPCAttributeRace.Factory raceFactory,
            VM_NPCAttributeVoiceType.Factory voiceTypeFactory,
            Logger logger
            )
        {
            _attributeFactory = factory;
            _shellFactory = shellFactory;

            _classFactory = classFactory;
            _customFactory = customFactory;
            _faceTextureFactory = faceTextureFactory;
            _factionsFactory = factionsFactory;
            _keywordFactory = keywordFactory;
            _miscFactory = miscFactory;
            _modFactory = modFactory;
            _npcFactory = npcFactory;
            _raceFactory = raceFactory;
            _voiceTypeFactory = voiceTypeFactory;

            _logger = logger;
        }
        public VM_NPCAttribute CreateNew(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            return _attributeFactory(parentCollection, attributeGroups);
        }
        public VM_NPCAttribute CreateNewFromUI(ObservableCollection<VM_NPCAttribute> parentCollection, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            var newAtt = _attributeFactory(parentCollection, attributeGroups);
            VM_NPCAttributeShell startingShell = _shellFactory(newAtt, displayForceIfOption, displayForceIfWeight, attributeGroups);
            VM_NPCAttributeClass startingAttributeGroup = _classFactory(newAtt, startingShell);
            startingShell.Type = NPCAttributeType.Class;
            startingShell.Attribute = startingAttributeGroup;
            startingShell.InitializedVMcache[startingShell.Type] = startingShell.Attribute;
            newAtt.GroupedSubAttributes.Add(startingShell);
            newAtt.DisplayForceIfOption = displayForceIfOption;
            newAtt.DisplayForceIfWeight = displayForceIfWeight;
            return newAtt;
        }
        public VM_NPCAttributeShell CreateNewShell(VM_NPCAttribute parentVM, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            return _shellFactory(parentVM, displayForceIfOption, displayForceIfWeight, attributeGroups);
        }

        public void CopyInFromModels(HashSet<NPCAttribute> models, ObservableCollection<VM_NPCAttribute> viewModelCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            var alreadyLoadedModels = viewModelCollection.Select(x => x.DumpViewModelToModel()).ToHashSet();
            var toCopyIn = models.Where(x => !alreadyLoadedModels.Contains(x)).ToHashSet();

            foreach (var m in toCopyIn)
            {
                viewModelCollection.Add(GetViewModelFromModel(m, viewModelCollection, attributeGroups, displayForceIfOption, displayForceIfWeight));
            }
        }

        public VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            VM_NPCAttribute viewModel = CreateNew(parentCollection, attributeGroups);
            viewModel.DisplayForceIfOption = displayForceIfOption;
            viewModel.DisplayForceIfWeight = displayForceIfWeight;
            foreach (var attributeShellModel in model.SubAttributes)
            {
                var shellVM = CreateNewShell(viewModel, displayForceIfOption, displayForceIfWeight, attributeGroups);
                shellVM.Type = attributeShellModel.Type;
                switch (attributeShellModel.Type)
                {
                    case NPCAttributeType.Class: shellVM.Attribute = VM_NPCAttributeClass.GetViewModelFromModel((NPCAttributeClass)attributeShellModel, viewModel, shellVM, _classFactory); break;
                    case NPCAttributeType.Custom: shellVM.Attribute = VM_NPCAttributeCustom.GetViewModelFromModel((NPCAttributeCustom)attributeShellModel, viewModel, shellVM, _customFactory); break;
                    case NPCAttributeType.Faction: shellVM.Attribute = VM_NPCAttributeFactions.GetViewModelFromModel((NPCAttributeFactions)attributeShellModel, viewModel, shellVM, _factionsFactory); break;
                    case NPCAttributeType.FaceTexture: shellVM.Attribute = VM_NPCAttributeFaceTexture.GetViewModelFromModel((NPCAttributeFaceTexture)attributeShellModel, viewModel, shellVM, _faceTextureFactory); break;
                    case NPCAttributeType.Keyword: shellVM.Attribute = VM_NPCAttributeKeyword.GetViewModelFromModel((NPCAttributeKeyword)attributeShellModel, viewModel, shellVM, _keywordFactory); break;
                    case NPCAttributeType.Misc: shellVM.Attribute = VM_NPCAttributeMisc.GetViewModelFromModel((NPCAttributeMisc)attributeShellModel, viewModel, shellVM, _miscFactory); break;
                    case NPCAttributeType.Mod: shellVM.Attribute = VM_NPCAttributeMod.GetViewModelFromModel((NPCAttributeMod)attributeShellModel, viewModel, shellVM, _modFactory); break;
                    case NPCAttributeType.NPC: shellVM.Attribute = VM_NPCAttributeNPC.GetViewModelFromModel((NPCAttributeNPC)attributeShellModel, viewModel, shellVM, _npcFactory); break;
                    case NPCAttributeType.Race: shellVM.Attribute = VM_NPCAttributeRace.GetViewModelFromModel((NPCAttributeRace)attributeShellModel, viewModel, shellVM, _raceFactory); break;
                    case NPCAttributeType.VoiceType: shellVM.Attribute = VM_NPCAttributeVoiceType.GetViewModelFromModel((NPCAttributeVoiceType)attributeShellModel, viewModel, shellVM, _voiceTypeFactory); break;
                    case NPCAttributeType.Group: shellVM.Attribute = VM_NPCAttributeGroup.GetViewModelFromModel((NPCAttributeGroup)attributeShellModel, viewModel, shellVM, attributeGroups); break; // Setting the checkbox selections MUST be done in the calling function after all `attributeGroups` view models have been created from their corresponding model (otherwise the required checkbox entry may not yet exist). This is done in VM_AttributeGroupMenu.GetViewModelFromModels().
                    default:
                        _logger.LogError("Could not determine attribute type of NPC Attribute " + attributeShellModel.Type.ToString() + ". Ignoring this attribute.");
                        break;
                }
                shellVM.InitializedVMcache[shellVM.Type] = shellVM.Attribute;
                shellVM.ForceModeStr = VM_NPCAttributeShell.ForceModeEnumToStrDict[attributeShellModel.ForceMode];
                viewModel.GroupedSubAttributes.Add(shellVM);
            }

            return viewModel;
        }
    }

    public void TrimEmptyAttributes()
    {
        if (GroupedSubAttributes.Count == 0)
        {
            ParentCollection.Remove(this);
        }
    }

    public static HashSet<NPCAttribute> DumpViewModelsToModels(ObservableCollection<VM_NPCAttribute> viewModels)
    {
        HashSet<NPCAttribute> hs = new HashSet<NPCAttribute>();
        foreach (var v in viewModels)
        {
            hs.Add(v.DumpViewModelToModel());
        }
        return hs;
    }

    public NPCAttribute DumpViewModelToModel()
    {
        var model = new NPCAttribute();
        foreach (var subAttVM in GroupedSubAttributes)
        {
            switch(subAttVM.Type)
            {
                case NPCAttributeType.Class: model.SubAttributes.Add(VM_NPCAttributeClass.DumpViewModelToModel((VM_NPCAttributeClass)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Custom: model.SubAttributes.Add(VM_NPCAttributeCustom.DumpViewModelToModel((VM_NPCAttributeCustom)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Faction: model.SubAttributes.Add(VM_NPCAttributeFactions.DumpViewModelToModel((VM_NPCAttributeFactions)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.FaceTexture: model.SubAttributes.Add(VM_NPCAttributeFaceTexture.DumpViewModelToModel((VM_NPCAttributeFaceTexture)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Group: model.SubAttributes.Add(VM_NPCAttributeGroup.DumpViewModelToModel((VM_NPCAttributeGroup)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Keyword: model.SubAttributes.Add(VM_NPCAttributeKeyword.DumpViewModelToModel((VM_NPCAttributeKeyword)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Misc: model.SubAttributes.Add(VM_NPCAttributeMisc.DumpViewModelToModel((VM_NPCAttributeMisc)subAttVM.Attribute,subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Mod: model.SubAttributes.Add(VM_NPCAttributeMod.DumpViewModelToModel((VM_NPCAttributeMod)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.NPC: model.SubAttributes.Add(VM_NPCAttributeNPC.DumpViewModelToModel((VM_NPCAttributeNPC)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.Race: model.SubAttributes.Add(VM_NPCAttributeRace.DumpViewModelToModel((VM_NPCAttributeRace)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
                case NPCAttributeType.VoiceType: model.SubAttributes.Add(VM_NPCAttributeVoiceType.DumpViewModelToModel((VM_NPCAttributeVoiceType)subAttVM.Attribute, subAttVM.ForceModeStr)); break;
            }
        }
        return model;
    }
}

public class VM_NPCAttributeShell : VM
{
    public delegate VM_NPCAttributeShell Factory(VM_NPCAttribute parentVM, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups);
    private readonly Factory _selfFactory;
    private readonly VM_NPCAttributeClass.Factory _classFactory;
    private readonly VM_NPCAttributeCustom.Factory _customFactory;
    private readonly VM_NPCAttributeFaceTexture.Factory _faceTextureFactory;
    private readonly VM_NPCAttributeFactions.Factory _factionsFactory;
    private readonly VM_NPCAttributeKeyword.Factory _keywordFactory;
    private readonly VM_NPCAttributeMisc.Factory _miscFactory;
    private readonly VM_NPCAttributeMod.Factory _modFactory;
    private readonly VM_NPCAttributeNPC.Factory _npcFactory;
    private readonly VM_NPCAttributeRace.Factory _raceFactory;
    private readonly VM_NPCAttributeVoiceType.Factory _voiceTypeFactory;
    public VM_NPCAttributeShell(VM_NPCAttribute parentVM, 
        bool displayForceIfOption, 
        ObservableCollection<VM_AttributeGroup> attributeGroups, 
        Factory selfFactory,
        VM_NPCAttributeClass.Factory classFactory,
        VM_NPCAttributeCustom.Factory customFactory,
        VM_NPCAttributeFaceTexture.Factory faceTextureFactory,
        VM_NPCAttributeFactions.Factory factionsFactory,
        VM_NPCAttributeKeyword.Factory keywordFactory,
        VM_NPCAttributeMisc.Factory miscFactory,
        VM_NPCAttributeMod.Factory modFactory,
        VM_NPCAttributeNPC.Factory npcFactory,
        VM_NPCAttributeRace.Factory raceFactory,
        VM_NPCAttributeVoiceType.Factory voiceTypeFactory
        )
    {
        _selfFactory = selfFactory;

        _classFactory = classFactory;
        _customFactory = customFactory;
        _faceTextureFactory = faceTextureFactory;
        _factionsFactory = factionsFactory;
        _keywordFactory = keywordFactory;
        _miscFactory = miscFactory;
        _modFactory = modFactory;
        _npcFactory = npcFactory;
        _raceFactory = raceFactory;
        _voiceTypeFactory = voiceTypeFactory;

        Attribute = classFactory(parentVM, this);
        DisplayForceIfOption = displayForceIfOption;

        this.WhenAnyValue(x => x.ForceModeStr).Subscribe(x =>
        {
            if (DisplayForceIfOption == true && (ForceModeStr == AttributeForceIfStr || ForceModeStr == AttributeForceIfandRestrictStr))
            {
                DisplayForceIfWeight = true;
            }
            else
            {
                DisplayForceIfWeight = false;
            }
        }
        ).DisposeWith(this);
       

        AddAdditionalSubAttributeToParent = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentVM.GroupedSubAttributes.Add(_selfFactory(parentVM, DisplayForceIfOption, DisplayForceIfWeight, attributeGroups))
        );

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(this));

        ChangeType = new RelayCommand(canExecute: _ => true, execute: _ => GetOrCreateSubAttribute(Type, parentVM, attributeGroups)
        );
    }
    public ISubAttributeViewModel Attribute { get; set; }
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Class;
    public string ForceModeStr { get; set; } = ForceModeOptions.FirstOrDefault();
    public int ForceIfWeight { get; set; } = 1;
    public bool DisplayForceIfOption { get; set; }
    public bool DisplayForceIfWeight { get; set; }
    public bool Not { get; set; } = false;

    public RelayCommand AddAdditionalSubAttributeToParent { get; }
    public RelayCommand DeleteCommand { get; }

    public RelayCommand ChangeType { get; }

    public static string AttributeAllowStr { get; } = "Restrict";
    public static string AttributeForceIfStr { get; } = "Force If";
    public static string AttributeForceIfandRestrictStr { get; } = "Force If and Restrict";
    public static List<string> ForceModeOptions = new() { AttributeAllowStr, AttributeForceIfStr, AttributeForceIfandRestrictStr };

    public static Dictionary<string, AttributeForcing> ForceModeStrToEnumDict = new()
    {
        { AttributeAllowStr, AttributeForcing.Restrict },
        { AttributeForceIfStr, AttributeForcing.ForceIf },
        { AttributeForceIfandRestrictStr, AttributeForcing.ForceIfAndRestrict }
    };

    public static Dictionary<AttributeForcing, string> ForceModeEnumToStrDict = new()
    {
        { AttributeForcing.Restrict, AttributeAllowStr },
        { AttributeForcing.ForceIf, AttributeForceIfStr },
        { AttributeForcing.ForceIfAndRestrict, AttributeForceIfandRestrictStr }
    };

    // If adding a new attribute type, be sure to register it here
    public Dictionary<NPCAttributeType, ISubAttributeViewModel> InitializedVMcache { get; set; } = new()
    {
        { NPCAttributeType.Class, null },
        { NPCAttributeType.Custom, null },
        { NPCAttributeType.FaceTexture, null },
        { NPCAttributeType.Faction, null },
        { NPCAttributeType.Group, null },
        { NPCAttributeType.Keyword, null },
        { NPCAttributeType.Misc, null },
        { NPCAttributeType.Mod, null },
        { NPCAttributeType.NPC, null },
        { NPCAttributeType.Race, null },
        { NPCAttributeType.VoiceType, null }
    };

    public void GetOrCreateSubAttribute(NPCAttributeType type, VM_NPCAttribute parentVM, ObservableCollection<VM_AttributeGroup> attributeGroups)
    {
        if (InitializedVMcache[type] is not null)
        {
            Attribute = InitializedVMcache[type];
        }
        else
        {
            switch (type)
            {
                case NPCAttributeType.Class: Attribute = _classFactory(parentVM, this); break;
                case NPCAttributeType.Custom: Attribute = _customFactory(parentVM, this); break;
                case NPCAttributeType.FaceTexture: Attribute = _faceTextureFactory(parentVM, this); break;
                case NPCAttributeType.Faction: Attribute = _factionsFactory(parentVM, this); break;
                case NPCAttributeType.Group: Attribute = new VM_NPCAttributeGroup(parentVM, this, attributeGroups); break;
                case NPCAttributeType.Keyword: Attribute = _keywordFactory(parentVM, this); break;
                case NPCAttributeType.Misc: Attribute = _miscFactory(parentVM, this); break;
                case NPCAttributeType.Mod: Attribute = _modFactory(parentVM, this); break;
                case NPCAttributeType.NPC: Attribute = _npcFactory(parentVM, this); break;
                case NPCAttributeType.Race: Attribute = _raceFactory(parentVM, this); break;
                case NPCAttributeType.VoiceType: Attribute = _voiceTypeFactory(parentVM, this); break;
                default: throw new NotImplementedException();
            }
            InitializedVMcache[type] = Attribute;
        }
    }
}

public interface ISubAttributeViewModel
{
    VM_NPCAttribute ParentVM { get; set; }
    IObservable<System.Reactive.Unit> NeedsRefresh { get; }
}

public class VM_NPCAttributeVoiceType : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeVoiceType Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;

        ParentVM = parentVM;
        ParentShell = parentShell;
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
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
    }
    public ObservableCollection<FormKey> VoiceTypeFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IVoiceTypeGetter).AsEnumerable();

    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeVoiceType GetViewModelFromModel(NPCAttributeVoiceType model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeVoiceType.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.VoiceTypeFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    public static NPCAttributeVoiceType DumpViewModelToModel(VM_NPCAttributeVoiceType viewModel, string forceModeStr)
    {
        return new NPCAttributeVoiceType() { Type = NPCAttributeType.VoiceType, FormKeys = viewModel.VoiceTypeFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeClass : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeClass Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeClass(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> ClassFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IClassGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeClass GetViewModelFromModel(NPCAttributeClass model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeClass.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.ClassFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }

    public static NPCAttributeClass DumpViewModelToModel(VM_NPCAttributeClass viewModel, string forceModeStr)
    {
        return new NPCAttributeClass() { Type = NPCAttributeType.Class, FormKeys = viewModel.ClassFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeCustom : VM, ISubAttributeViewModel, IImplementsRecordIntellisense
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private AttributeMatcher _attributeMatcher;
    private RecordIntellisense _recordIntellisense;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeCustom Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeCustom(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, AttributeMatcher attributeMatcher, RecordIntellisense recordIntellisense, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _attributeMatcher = attributeMatcher;
        _recordIntellisense = recordIntellisense;
        _selfFactory = selfFactory;

        foreach (var reg in Loqui.LoquiRegistration.StaticRegister.Registrations.Where(x => x.ProtocolKey.Namespace == "Skyrim").Where(x => x.GetterType.IsAssignableTo(typeof(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter))).ToArray())
        {
            ValueGetterTypes.Add(reg.Name, reg.GetterType);
        }

        ParentVM = parentVM;
        ParentShell = parentShell;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));

        _recordIntellisense.InitializeSubscriptions(this);

        this.WhenAnyValue(x => x.CustomType).Subscribe(x => UpdateValueDisplay()).DisposeWith(this);

        this.WhenAnyValue(x => x.ValueFKtype).Subscribe(x => UpdateFormKeyPickerRecordType()).DisposeWith(this);

        this.WhenAnyValue(x => x.ValueStr).Subscribe(x => Evaluate()).DisposeWith(this);
        ValueFKs.ToObservableChangeSet().Subscribe(x => Evaluate()).DisposeWith(this);
        this.WhenAnyValue(x => x.ChosenComparator).Subscribe(x => Evaluate()).DisposeWith(this);

        this.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => Evaluate()).DisposeWith(this);
        this.WhenAnyValue(x => x.ReferenceNPCFormKey).Subscribe(x => Evaluate()).DisposeWith(this);
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);
    }

    public CustomAttributeType CustomType { get; set; } = CustomAttributeType.Text;
    public string IntellisensedPath { get; set; } = "";
    public string ValueStr { get; set; } = "";
    public ObservableCollection<FormKey> ValueFKs { get; set; } = new();
    public SortedDictionary<string, Type> ValueGetterTypes { get; set; } = new();
    public Type ValueFKtype { get; set; }
    public IEnumerable<Type> ValueFKtypeCollection { get; set; }
    public ILinkCache LinkCache { get; private set; }
    public ObservableCollection<PathSuggestion> PathSuggestions { get; set; } = new();
    public PathSuggestion ChosenPathSuggestion { get; set; } = null;
    public FormKey ReferenceNPCFormKey { get; set; } = new();
    public IEnumerable<Type> ReferenceNPCType { get; set; } = typeof(INpcGetter).AsEnumerable();
    public ObservableCollection<string> Comparators { get; set; } = new();
    public string ChosenComparator { get; set; }
    public string EvalResult { get; set; }

    public bool ShowValueTextField { get; set; } = true;
    public bool ShowValueFormKeyPicker { get; set; } = false;
    public bool ShowValueBoolPicker { get; set; } = false;

    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();
    public SolidColorBrush StatusFontColor { get; set; } = new(Colors.White);

    public static VM_NPCAttributeCustom GetViewModelFromModel(NPCAttributeCustom model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeCustom.Factory factory)
    {
        var viewModel = factory(parentVM, parentShell);
        viewModel.ChosenComparator = model.Comparator;
        viewModel.CustomType = model.CustomType;
        viewModel.IntellisensedPath = model.Path;
        viewModel.ValueStr = model.ValueStr;
        viewModel.ValueFKs = new ObservableCollection<FormKey>(model.ValueFKs);
        viewModel.ValueFKtype = model.SelectedFormKeyType;
        viewModel.ReferenceNPCFormKey = model.ReferenceNPCFK;
        viewModel.ChosenPathSuggestion = null;
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return viewModel;
    }

    public static NPCAttributeCustom DumpViewModelToModel(VM_NPCAttributeCustom viewModel, string forceModeStr)
    {
        var model = new NPCAttributeCustom();
        model.Type = NPCAttributeType.Custom;
        model.Comparator = viewModel.ChosenComparator;
        model.CustomType = viewModel.CustomType;
        model.Path = viewModel.IntellisensedPath;
        model.ValueStr = viewModel.ValueStr;
        model.ValueFKs = viewModel.ValueFKs.ToHashSet();
        model.ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr];
        model.Weighting = viewModel.ParentShell.ForceIfWeight;
        model.ReferenceNPCFK = viewModel.ReferenceNPCFormKey;
        model.SelectedFormKeyType = viewModel.ValueFKtype;
        model.Not = viewModel.ParentShell.Not;
        return model;
    }

    public void Evaluate()
    {
        if (ReferenceNPCFormKey.IsNull)
        {
            EvalResult = "Can't evaluate: Reference NPC not set";
            StatusFontColor = CommonColors.Yellow;
        }
        else if (CustomType != CustomAttributeType.Record && ValueStr == "")
        {
            EvalResult = "Can't evaluate: No value provided";
            StatusFontColor = CommonColors.Yellow;
        }
        else if (CustomType == CustomAttributeType.Record && !ValueFKs.Any())
        {
            EvalResult = "Can't evaluate: No FormKeys selected";
            StatusFontColor = CommonColors.Yellow;
        }
        else if (CustomType == CustomAttributeType.Integer && !Int32.TryParse(ValueStr, out _))
        {
            EvalResult = "Can't convert " + ValueStr + " to an Integer value";
            StatusFontColor = CommonColors.Red;
        }
        else if (CustomType == CustomAttributeType.Decimal && !float.TryParse(ValueStr, out _))
        {
            EvalResult = "Can't convert " + ValueStr + " to a Decimal value";
            StatusFontColor = CommonColors.Red;
        }
        else if (CustomType == CustomAttributeType.Boolean && !bool.TryParse(ValueStr, out _))
        {
            EvalResult = "Can't convert " + ValueStr + " to a Boolean value";
            StatusFontColor = CommonColors.Red;
        }
        else
        {
            if (!_environmentProvider.LinkCache.TryResolve<INpcGetter>(ReferenceNPCFormKey, out var refNPC))
            {
                EvalResult = "Error: can't resolve reference NPC.";
                StatusFontColor = CommonColors.Red;
            }
            bool matched = _attributeMatcher.EvaluateCustomAttribute(refNPC, DumpViewModelToModel(this, VM_NPCAttributeShell.AttributeAllowStr), LinkCache, out string dispMessage);
            if (matched)
            {
                EvalResult = "Matched!";
                StatusFontColor = CommonColors.Green;
            }
            else
            {
                EvalResult = dispMessage;
                StatusFontColor = CommonColors.Red;
            }
        }
    }

    public void UpdateValueDisplay()
    {
        if (CustomType == CustomAttributeType.Record)
        {
            ShowValueFormKeyPicker = true;
            ShowValueTextField = false;
            ShowValueBoolPicker = false;
        }
        else if (CustomType == CustomAttributeType.Boolean)
        {
            ShowValueFormKeyPicker = false;
            ShowValueTextField = false;
            ShowValueBoolPicker = true;
        }
        else
        {
            ShowValueFormKeyPicker = false;
            ShowValueTextField = true;
            ShowValueBoolPicker = false;
        }

        Comparators = new ObservableCollection<string>() { "=", "!=" };
        if (CustomType == CustomAttributeType.Integer || CustomType == CustomAttributeType.Decimal)
        {
            Comparators.Add("<");
            Comparators.Add("<=");
            Comparators.Add(">");
            Comparators.Add(">=");
        }
        else if (CustomType == CustomAttributeType.Text)
        {
            Comparators.Add("Contains");
            Comparators.Add("Starts With");
            Comparators.Add("Ends With");
        }

        Evaluate();
    }

    public void UpdateFormKeyPickerRecordType()
    {
        ValueFKtypeCollection = ValueFKtype.AsEnumerable();
        Evaluate();
    }
}

public class VM_NPCAttributeFactions : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeFactions Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeFactions(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> FactionFormKeys { get; set; } = new();
    public int RankMin { get; set; } = -1;
    public int RankMax { get; set; } = 100;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IFactionGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeFactions GetViewModelFromModel(NPCAttributeFactions model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeFactions.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FactionFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        newAtt.RankMin = model.RankMin;
        newAtt.RankMax = model.RankMax;
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    public static NPCAttributeFactions DumpViewModelToModel(VM_NPCAttributeFactions viewModel, string forceModeStr)
    {
        return new NPCAttributeFactions() { Type = NPCAttributeType.Faction, FormKeys = viewModel.FactionFormKeys.ToHashSet(), RankMin = viewModel.RankMin, RankMax = viewModel.RankMax, ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeFaceTexture : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeFaceTexture Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> FaceTextureFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(ITextureSetGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeFaceTexture GetViewModelFromModel(NPCAttributeFaceTexture model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeFaceTexture.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FaceTextureFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }

    public static NPCAttributeFaceTexture DumpViewModelToModel(VM_NPCAttributeFaceTexture viewModel, string forceModeStr)
    {
        return new NPCAttributeFaceTexture() { Type = NPCAttributeType.FaceTexture, FormKeys = viewModel.FaceTextureFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeKeyword : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeKeyword Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeKeyword(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> KeywordFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IKeywordGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeKeyword GetViewModelFromModel(NPCAttributeKeyword model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeKeyword.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.KeywordFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    public static NPCAttributeKeyword DumpViewModelToModel(VM_NPCAttributeKeyword viewModel, string forceModeStr)
    {
        return new NPCAttributeKeyword() { Type = NPCAttributeType.Keyword, FormKeys = viewModel.KeywordFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}
public class VM_NPCAttributeRace : VM, ISubAttributeViewModel
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeRace Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeRace(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> RaceFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeRace GetViewModelFromModel(NPCAttributeRace model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeRace.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.RaceFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }

    public static NPCAttributeRace DumpViewModelToModel(VM_NPCAttributeRace viewModel, string forceModeStr)
    {
        return new NPCAttributeRace() { Type = NPCAttributeType.Race, FormKeys = viewModel.RaceFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeMisc : VM, ISubAttributeViewModel
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeMisc Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeMisc(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ThreeWayState Unique { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Essential { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Protected { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Summonable { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Ghost { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Invulnerable { get; set; } = ThreeWayState.Ignore;
    public bool EvalMood { get; set; } = false;
    public Mood Mood { get; set; } = Mood.Neutral;
    public bool EvalAggression { get; set; } = false;
    public Aggression Aggression { get; set; } = Aggression.Unagressive;
    public bool EvalGender { get; set; } = false;
    public Gender NPCGender { get; set; } = Gender.Female;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeMisc GetViewModelFromModel(NPCAttributeMisc model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeMisc.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.Unique = model.Unique;
        newAtt.Essential = model.Essential;
        newAtt.Protected = model.Protected;
        newAtt.Summonable = model.Summonable;
        newAtt.Ghost = model.Ghost;
        newAtt.Invulnerable = model.Invulnerable;
        newAtt.EvalMood = model.EvalMood;
        newAtt.Mood = model.Mood;
        newAtt.EvalAggression = model.EvalAggression;
        newAtt.Aggression = model.Aggression;
        newAtt.EvalGender = model.EvalGender;
        newAtt.NPCGender = model.NPCGender;
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    public static NPCAttributeMisc DumpViewModelToModel(VM_NPCAttributeMisc viewModel, string forceModeStr)
    {
        var model = new NPCAttributeMisc();
        model.Unique = viewModel.Unique;
        model.Essential = viewModel.Essential;
        model.Protected = viewModel.Protected;
        model.Summonable = viewModel.Summonable;
        model.Ghost = viewModel.Ghost;
        model.Invulnerable = viewModel.Invulnerable;
        model.EvalMood = viewModel.EvalMood;
        model.Mood = viewModel.Mood;
        model.EvalAggression = viewModel.EvalAggression;
        model.Aggression = viewModel.Aggression;
        model.EvalGender = viewModel.EvalGender;
        model.NPCGender = viewModel.NPCGender;
        model.Weighting = viewModel.ParentShell.ForceIfWeight;
        model.ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr];
        model.Not = viewModel.ParentShell.Not;
        return model;
    }
}

public class VM_NPCAttributeMod : VM, ISubAttributeViewModel
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeMod Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeMod(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        ParentVM = parentVM;
        ParentShell = parentShell;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }

    public ObservableCollection<ModKey> ModKeys { get; set; } = new();
    public ModAttributeEnum ModActionType { get; set; } = ModAttributeEnum.PatchedBy;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public ILoadOrderGetter LoadOrder { get; private set; }

    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeMod GetViewModelFromModel(NPCAttributeMod model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeMod.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.ModKeys = new(model.ModKeys);
        newAtt.ModActionType = model.ModActionType;
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }

    public static NPCAttributeMod DumpViewModelToModel(VM_NPCAttributeMod viewModel, string forceModeStr)
    {
        var model = new NPCAttributeMod();
        model.ModKeys = viewModel.ModKeys.ToHashSet();
        model.ModActionType = viewModel.ModActionType;
        model.Weighting = viewModel.ParentShell.ForceIfWeight;
        model.ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr];
        model.Not = viewModel.ParentShell.Not;
        return model;
    }
}

public class VM_NPCAttributeNPC : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    public delegate VM_NPCAttributeNPC Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    public VM_NPCAttributeNPC(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;

        ParentVM = parentVM;
        ParentShell = parentShell;
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(parentShell));
    }
    public ObservableCollection<FormKey> NPCFormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    public static VM_NPCAttributeNPC GetViewModelFromModel(NPCAttributeNPC model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeNPC.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.NPCFormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    public static NPCAttributeNPC DumpViewModelToModel(VM_NPCAttributeNPC viewModel, string forceModeStr)
    {
        return new NPCAttributeNPC() { Type = NPCAttributeType.NPC, FormKeys = viewModel.NPCFormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public class VM_NPCAttributeGroup : VM, ISubAttributeViewModel
{
    public VM_NPCAttributeGroup(VM_NPCAttribute parentAttributeVM, VM_NPCAttributeShell parentShell, ObservableCollection<VM_AttributeGroup> sourceAttributeGroups)
    {
        ParentVM = parentAttributeVM;
        ParentShell = parentShell;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentAttributeVM.GroupedSubAttributes.Remove(parentShell));

        SubscribedAttributeGroups = sourceAttributeGroups;
        foreach (var attributeGroupVM in sourceAttributeGroups)
        {
            SelectableAttributeGroups.Add(new AttributeGroupSelection(attributeGroupVM, this));
        }

        SubscribedAttributeGroups.ToObservableChangeSet()
            .QueryWhenChanged(currentList => currentList)
            .Subscribe(x =>
            {
                RefreshCheckList();
                NeedsRefresh = SelectableAttributeGroups.Select(x => x.WhenAnyValue(x => x.IsSelected)).Merge().Unit();
            }
            ).DisposeWith(this);

        this.WhenAnyValue(x => x.MostRecentlyEditedSelection).Subscribe(_ => ParentVM.MostRecentlyEditedShell = ParentShell).DisposeWith(this);
    }
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public IObservable<Unit> NeedsRefresh { get; set; }
    public ObservableCollection<VM_AttributeGroup> SubscribedAttributeGroups { get; set; }
    public ObservableCollection<AttributeGroupSelection> SelectableAttributeGroups { get; set; } = new();
    public AttributeGroupSelection MostRecentlyEditedSelection { get; set; }

    void RefreshCheckList()
    {
        var currentSelections = SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label).ToList();

        SelectableAttributeGroups.Clear();
        foreach (var attributeGroupVM in SubscribedAttributeGroups)
        {
            var newSelection = new AttributeGroupSelection(attributeGroupVM, this);
            if (currentSelections.Contains(attributeGroupVM.Label))
            {
                newSelection.IsSelected = true;
            }
            SelectableAttributeGroups.Add(newSelection);
        }
    }
    public class AttributeGroupSelection : VM
    {
        public AttributeGroupSelection(VM_AttributeGroup attributeGroupVM, VM_NPCAttributeGroup parent)
        {
            SubscribedAttributeGroup = attributeGroupVM;
            Parent = parent;

            this.WhenAnyValue(x => x.IsSelected).Subscribe(_ => Parent.MostRecentlyEditedSelection = this).DisposeWith(this);
        }

        public bool IsSelected { get; set; } = false;
        public VM_AttributeGroup SubscribedAttributeGroup { get; set; }
        public VM_NPCAttributeGroup Parent { get; set; }
    }

    public static VM_NPCAttributeGroup GetViewModelFromModel(NPCAttributeGroup model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, ObservableCollection<VM_AttributeGroup> attributeGroups)
    {
        var newAtt = new VM_NPCAttributeGroup(parentVM, parentShell, attributeGroups);
            
        foreach (var group in newAtt.SelectableAttributeGroups.Where(x => model.SelectedLabels.Contains(x.SubscribedAttributeGroup.Label)).ToArray())
        {
            group.IsSelected = true;
        }

        parentShell.ForceIfWeight = model.Weighting;

        parentShell.Not = model.Not;

        return newAtt;
    }
    public static NPCAttributeGroup DumpViewModelToModel(VM_NPCAttributeGroup viewModel, string forceModeStr)
    {
        return new NPCAttributeGroup() { Type = NPCAttributeType.Group, SelectedLabels = viewModel.SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label).ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

public enum BoolVals
{
    True,
    False
}
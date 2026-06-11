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
using System.Diagnostics;
using System.Printing;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Media;
using static SynthEBD.RecordIntellisense;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// View-model mirror of the <see cref="NPCAttribute"/> model: a single NPC-matching condition whose
/// <see cref="GroupedSubAttributes"/> shells are combined with AND logic (an NPC matches only if it
/// matches every shell). Sibling conditions in <see cref="ParentCollection"/> combine with OR logic.
/// Owns the UI commands for adding an OR-sibling, deleting itself, and launching the attribute validator.
/// </summary>
[DebuggerDisplay("Attribute VM with {GroupedSubAttributes.Count} Sub-Attributes (AND logic)")]
public class VM_NPCAttribute : VM
{
    /// <summary>Autofac factory delegate for constructing a <see cref="VM_NPCAttribute"/> bound to its OR-sibling collection and the available attribute groups.</summary>
    public delegate VM_NPCAttribute Factory(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups);
    private VM_NPCAttributeCreator _creator;
    private ObservableCollection<VM_AttributeGroup> _subscribedAttributeGroups;
    /// <summary>
    /// Wires the delete / add-OR-sibling / validate commands and subscribes to the shell collection so
    /// <see cref="NeedsRefresh"/> is rebuilt and empty conditions are trimmed whenever the shells change.
    /// </summary>
    /// <param name="parentCollection">The OR-combined collection this condition belongs to.</param>
    /// <param name="attributeGroups">Attribute groups selectable by child <see cref="VM_NPCAttributeGroup"/> shells.</param>
    /// <param name="creator">Factory helper used to build new conditions/shells and to round-trip models.</param>
    /// <param name="attributeMatcher">Matcher used by the attribute-validator dialog.</param>
    /// <param name="environmentProvider">Supplies the link cache/load order consumed by the validator.</param>
    /// <param name="patcherState">Patcher state consumed by the validator.</param>
    public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, VM_NPCAttributeCreator creator, AttributeMatcher attributeMatcher, IEnvironmentStateProvider environmentProvider, PatcherState patcherState)
    {
        _creator = creator;
        _subscribedAttributeGroups = attributeGroups;

        ParentCollection = parentCollection;

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        AddToParent = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Add(_creator.CreateNewFromUI(ParentCollection, DisplayForceIfOption, DisplayForceIfWeight, _subscribedAttributeGroups)));
        Validate = new RelayCommand(canExecute: _ => true, execute: _ => {
            var validator = new VM_AttributeValidator(this, _subscribedAttributeGroups, patcherState, environmentProvider, attributeMatcher);
            Window_AttributeValidator window = new Window_AttributeValidator();
            window.DataContext = validator;
            window.ShowDialog();
        });

        GroupedSubAttributes.ToObservableChangeSet().Subscribe(x => {
            NeedsRefresh = GroupedSubAttributes.Select(x => x.WhenAnyObservable(y => y.Attribute.NeedsRefresh)).Merge().Unit();
            TrimEmptyAttributes();
        }).DisposeWith(this);
    }

    /// <summary>The sub-attribute shells combined with AND logic — the NPC must match every shell for this condition to hold.</summary>
    public ObservableCollection<VM_NPCAttributeShell> GroupedSubAttributes { get; set; } = new(); // everything within this collection is evaluated as AND (all must be true)
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddToParent { get; }
    public RelayCommand Validate { get; }
    /// <summary>Whether the per-shell forcing ("Force If") options are offered in the UI (false where only restriction makes sense).</summary>
    public bool DisplayForceIfOption { get; set; } = true;
    /// <summary>Whether the Force-If weighting field is shown (driven by the forcing mode chosen on the owning shell).</summary>
    public bool? DisplayForceIfWeight { get; set; }
    /// <summary>Whether the "OR" button is shown; hidden when this box is a single-condition host (e.g. a Probability Modifier row).</summary>
    public bool DisplayORButton { get; set; } = true;
    /// <summary>Whether the remove button is shown; hidden when an outer row owns removal (e.g. a Probability Modifier row).</summary>
    public bool DisplayRemoveButton { get; set; } = true;
    public ObservableCollection<VM_NPCAttribute> ParentCollection { get; set; }
    /// <summary>Tracks the most recently edited shell so the UI can focus it; set by child group-selection edits.</summary>
    public VM_NPCAttributeShell MostRecentlyEditedShell { get; set; }
    /// <summary>Fires when any child shell signals a refresh need; rebuilt whenever the shell collection changes.</summary>
    public IObservable<Unit> NeedsRefresh { get; set; }

    /// <summary>Deep-copies this condition into another collection by round-tripping through its model, reusing this VM's subscribed attribute groups.</summary>
    /// <param name="parentCollection">The collection the clone is created against.</param>
    /// <returns>The cloned condition.</returns>
    public VM_NPCAttribute CloneInto(ObservableCollection<VM_NPCAttribute> parentCollection)
    {
        var model = DumpViewModelToModel();
        var clone = _creator.GetViewModelFromModel(model, parentCollection, _subscribedAttributeGroups, DisplayForceIfOption, DisplayForceIfWeight);
        return clone;
    }

    /// <summary>Deep-copies this condition into another collection, binding the clone's group shells to a different set of attribute groups.</summary>
    /// <param name="parentCollection">The collection the clone is created against.</param>
    /// <param name="subscribedAttributeGroups">The attribute groups the clone's group shells should reference.</param>
    /// <returns>The cloned condition.</returns>
    public VM_NPCAttribute CloneInto(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> subscribedAttributeGroups)
    {
        var model = DumpViewModelToModel();
        var clone = _creator.GetViewModelFromModel(model, parentCollection, subscribedAttributeGroups, DisplayForceIfOption, DisplayForceIfWeight);
        return clone;
    }

    /// <summary>
    /// Factory/helper that constructs <see cref="VM_NPCAttribute"/> conditions and their typed sub-attribute
    /// shells, and round-trips them to/from <see cref="NPCAttribute"/> models. Holds the Autofac factory
    /// delegates for every sub-attribute VM type so the correct concrete VM can be created per attribute type.
    /// </summary>
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

        /// <summary>Captures the per-type sub-attribute VM factory delegates plus the logger used when an unknown attribute type is encountered during model reconstruction.</summary>
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
        /// <summary>Creates an empty condition VM (no shells) bound to the given collection and attribute groups.</summary>
        public VM_NPCAttribute CreateNew(ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            return _attributeFactory(parentCollection, attributeGroups);
        }
        /// <summary>Creates a condition pre-seeded with one <see cref="NPCAttributeType.Class"/> shell, as used when the user adds a new attribute box in the UI.</summary>
        /// <param name="parentCollection">The OR-collection to bind the new condition to.</param>
        /// <param name="displayForceIfOption">Whether forcing options should be offered on the seeded shell.</param>
        /// <param name="displayForceIfWeight">Initial Force-If weight visibility for the condition.</param>
        /// <param name="attributeGroups">Attribute groups available to group shells.</param>
        /// <returns>The new condition with its starting Class shell registered in the shell's per-type cache.</returns>
        public VM_NPCAttribute CreateNewFromUI(ObservableCollection<VM_NPCAttribute> parentCollection, bool displayForceIfOption, bool? displayForceIfWeight, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            var newAtt = _attributeFactory(parentCollection, attributeGroups);
            VM_NPCAttributeShell startingShell = _shellFactory(newAtt, displayForceIfOption, attributeGroups);
            VM_NPCAttributeClass startingAttributeGroup = _classFactory(newAtt, startingShell);
            startingShell.Type = NPCAttributeType.Class;
            startingShell.Attribute = startingAttributeGroup;
            startingShell.InitializedVMcache[startingShell.Type] = startingShell.Attribute;
            newAtt.GroupedSubAttributes.Add(startingShell);
            newAtt.DisplayForceIfOption = displayForceIfOption;
            newAtt.DisplayForceIfWeight = displayForceIfWeight;
            return newAtt;
        }
        /// <summary>Creates a single sub-attribute shell (defaulting to a Class attribute) parented to <paramref name="parentVM"/>.</summary>
        public VM_NPCAttributeShell CreateNewShell(VM_NPCAttribute parentVM, bool displayForceIfOption, ObservableCollection<VM_AttributeGroup> attributeGroups)
        {
            return _shellFactory(parentVM, displayForceIfOption, attributeGroups);
        }

        /// <summary>Adds VMs for every model not already represented in <paramref name="viewModelCollection"/> (deduplicated by round-tripping the existing VMs back to models).</summary>
        /// <param name="models">The source attribute models to import.</param>
        /// <param name="viewModelCollection">The target VM collection, appended in place.</param>
        /// <param name="attributeGroups">Attribute groups available to group shells.</param>
        /// <param name="displayForceIfOption">Forcing-option visibility applied to imported conditions.</param>
        /// <param name="displayForceIfWeight">Force-If weight visibility applied to imported conditions.</param>
        public void CopyInFromModels(HashSet<NPCAttribute> models, ObservableCollection<VM_NPCAttribute> viewModelCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            var alreadyLoadedModels = viewModelCollection.Select(x => x.DumpViewModelToModel()).ToHashSet();
            var toCopyIn = models.Where(x => !alreadyLoadedModels.Contains(x)).ToHashSet();

            foreach (var m in toCopyIn)
            {
                viewModelCollection.Add(GetViewModelFromModel(m, viewModelCollection, attributeGroups, displayForceIfOption, displayForceIfWeight));
            }
        }

        /// <summary>
        /// Builds a fully-populated condition VM from a model, creating and type-dispatching a shell VM for each
        /// <see cref="NPCAttribute.SubAttributes"/> entry and registering it in the shell's per-type cache.
        /// </summary>
        /// <param name="model">The source attribute model.</param>
        /// <param name="parentCollection">The OR-collection to bind the new condition to.</param>
        /// <param name="attributeGroups">Attribute groups available to group shells.</param>
        /// <param name="displayForceIfOption">Forcing-option visibility for the new condition.</param>
        /// <param name="displayForceIfWeight">Force-If weight visibility for the new condition.</param>
        /// <returns>The reconstructed condition VM.</returns>
        /// <remarks>For <see cref="NPCAttributeType.Group"/> shells the checkbox selections must be applied by the caller after every attribute-group VM exists (see the inline note and <c>VM_AttributeGroupMenu.GetViewModelFromModels</c>).</remarks>
        public VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, bool displayForceIfOption, bool? displayForceIfWeight)
        {
            VM_NPCAttribute viewModel = CreateNew(parentCollection, attributeGroups);
            viewModel.DisplayForceIfOption = displayForceIfOption;
            viewModel.DisplayForceIfWeight = displayForceIfWeight;
            foreach (var attributeShellModel in model.SubAttributes)
            {
                var shellVM = CreateNewShell(viewModel, displayForceIfOption, attributeGroups);
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

    /// <summary>Removes this condition from its parent OR-collection once it has no remaining sub-attribute shells.</summary>
    public void TrimEmptyAttributes()
    {
        if (GroupedSubAttributes.Count == 0)
        {
            ParentCollection.Remove(this);
        }
    }

    /// <summary>Serializes a collection of condition VMs to a set of <see cref="NPCAttribute"/> models.</summary>
    public static HashSet<NPCAttribute> DumpViewModelsToModels(ObservableCollection<VM_NPCAttribute> viewModels)
    {
        HashSet<NPCAttribute> hs = new HashSet<NPCAttribute>();
        foreach (var v in viewModels)
        {
            hs.Add(v.DumpViewModelToModel());
        }
        return hs;
    }

    /// <summary>Serializes this condition to an <see cref="NPCAttribute"/> model, dispatching each shell to its concrete VM's <c>DumpViewModelToModel</c> by attribute type.</summary>
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

/// <summary>
/// Wraps one typed sub-attribute (an <see cref="ISubAttributeViewModel"/>) together with the per-shell
/// forcing mode, Force-If weight, and negation flag. The attribute type can be switched at runtime via
/// <see cref="ChangeType"/>; previously-created VMs are kept per type in <see cref="InitializedVMcache"/>
/// so switching back and forth preserves their state. One shell is one AND-term of its parent condition.
/// </summary>
[DebuggerDisplay("{Attribute.DebuggerString}")]
public class VM_NPCAttributeShell : VM
{
    /// <summary>Autofac factory delegate for constructing a shell parented to a condition VM.</summary>
    public delegate VM_NPCAttributeShell Factory(VM_NPCAttribute parentVM, bool displayForceIfOption, ObservableCollection<VM_AttributeGroup> attributeGroups);
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
    /// <summary>
    /// Seeds a default <see cref="NPCAttributeType.Class"/> attribute, subscribes the forcing-mode string so
    /// the Force-If weight field is shown only for the forcing modes that use it, and wires the add-sibling,
    /// delete, and change-type commands.
    /// </summary>
    /// <param name="parentVM">The owning AND-condition.</param>
    /// <param name="displayForceIfOption">Whether forcing options are offered for this shell.</param>
    /// <param name="attributeGroups">Attribute groups available when the shell is switched to the Group type.</param>
    /// <param name="selfFactory">Factory used to add a sibling shell to the parent condition.</param>
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
            execute: _ => parentVM.GroupedSubAttributes.Add(_selfFactory(parentVM, DisplayForceIfOption, attributeGroups))
        );

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.GroupedSubAttributes.Remove(this));

        ChangeType = new RelayCommand(canExecute: _ => true, execute: _ => GetOrCreateSubAttribute(Type, parentVM, attributeGroups)
        );
    }
    /// <summary>The currently-selected typed sub-attribute VM (swapped when <see cref="Type"/> changes).</summary>
    public ISubAttributeViewModel Attribute { get; set; }
    /// <summary>The attribute kind this shell currently represents.</summary>
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Class;
    /// <summary>The forcing mode as its UI display string (see <see cref="ForceModeOptions"/>); mapped to/from <see cref="AttributeForcing"/> via the dictionaries below.</summary>
    public string ForceModeStr { get; set; } = ForceModeOptions.FirstOrDefault();
    /// <summary>Relative weight applied when this shell's forcing mode forces selection.</summary>
    public int ForceIfWeight { get; set; } = 1;
    public bool DisplayForceIfOption { get; set; }
    public bool DisplayForceIfWeight { get; set; }
    /// <summary>When true, the match condition for this shell is negated.</summary>
    public bool Not { get; set; } = false;

    public RelayCommand AddAdditionalSubAttributeToParent { get; }
    public RelayCommand DeleteCommand { get; }

    public RelayCommand ChangeType { get; }

    /// <summary>UI label for the <see cref="AttributeForcing.Restrict"/> forcing mode.</summary>
    public static string AttributeAllowStr { get; } = "Restrict";
    /// <summary>UI label for the <see cref="AttributeForcing.ForceIf"/> forcing mode.</summary>
    public static string AttributeForceIfStr { get; } = "Force If";
    /// <summary>UI label for the <see cref="AttributeForcing.ForceIfAndRestrict"/> forcing mode.</summary>
    public static string AttributeForceIfandRestrictStr { get; } = "Force If and Restrict";
    /// <summary>The forcing-mode labels offered in the UI dropdown, in order.</summary>
    public static List<string> ForceModeOptions = new() { AttributeAllowStr, AttributeForceIfStr, AttributeForceIfandRestrictStr };

    /// <summary>Maps a forcing-mode display string to its <see cref="AttributeForcing"/> enum value.</summary>
    public static Dictionary<string, AttributeForcing> ForceModeStrToEnumDict = new()
    {
        { AttributeAllowStr, AttributeForcing.Restrict },
        { AttributeForceIfStr, AttributeForcing.ForceIf },
        { AttributeForceIfandRestrictStr, AttributeForcing.ForceIfAndRestrict }
    };

    /// <summary>Maps an <see cref="AttributeForcing"/> enum value back to its forcing-mode display string.</summary>
    public static Dictionary<AttributeForcing, string> ForceModeEnumToStrDict = new()
    {
        { AttributeForcing.Restrict, AttributeAllowStr },
        { AttributeForcing.ForceIf, AttributeForceIfStr },
        { AttributeForcing.ForceIfAndRestrict, AttributeForceIfandRestrictStr }
    };

    /// <summary>Per-type cache of already-constructed sub-attribute VMs, so switching <see cref="Type"/> back to a previously-used type restores that VM's state instead of rebuilding it. Add an entry here when introducing a new attribute type.</summary>
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

    /// <summary>
    /// Switches <see cref="Attribute"/> to the requested type, reusing the cached VM for that type if one
    /// exists or constructing (and caching) a new one via the appropriate factory.
    /// </summary>
    /// <param name="type">The attribute type to switch to.</param>
    /// <param name="parentVM">The owning condition passed to the created VM.</param>
    /// <param name="attributeGroups">Attribute groups passed when constructing a Group VM.</param>
    /// <remarks>The Group case is constructed inline rather than via an injected factory (see review notes); an unmapped type throws <see cref="NotImplementedException"/>.</remarks>
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

/// <summary>
/// Common contract for the typed sub-attribute view models a <see cref="VM_NPCAttributeShell"/> can host
/// (Class, Custom, Faction, FaceTexture, Keyword, Misc, Mod, NPC, Race, VoiceType, Group). Each owns a
/// back-reference to its parent condition, a refresh signal, and a debugger string. The concrete VMs
/// additionally expose static <c>GetViewModelFromModel</c>/<c>DumpViewModelToModel</c> helpers that
/// round-trip the matching <see cref="ITypedNPCAttribute"/> model.
/// </summary>
public interface ISubAttributeViewModel
{
    /// <summary>The owning AND-condition VM.</summary>
    VM_NPCAttribute ParentVM { get; set; }
    /// <summary>Fires when the UI should re-evaluate this attribute (e.g. its selection changed).</summary>
    IObservable<System.Reactive.Unit> NeedsRefresh { get; }
    /// <summary>Human-readable summary shown in the debugger and as the shell's display string.</summary>
    public string DebuggerString { get; }
}

/// <summary>
/// Shared base for the FormKey-set sub-attribute VMs (Class, Race, Keyword, FaceTexture, VoiceType, NPC):
/// an editable <see cref="ObservableCollection{T}"/> of <see cref="FormKey"/> (bound by the views as
/// <c>FormKeys</c>) plus the common parent/shell back-references, link-cache tracking, delete command, and
/// debugger string. Subclasses supply the allowed record types, the display label, and the static model
/// round-trip helpers. <see cref="VM_NPCAttributeFactions"/> stays standalone (it adds a rank range).
/// </summary>
/// <typeparam name="TSelf">The concrete sub-attribute VM deriving from this base.</typeparam>
[DebuggerDisplay("{DebuggerString}")]
public abstract class VM_NPCAttributeFormKeyBase<TSelf> : VM, ISubAttributeViewModel
    where TSelf : VM_NPCAttributeFormKeyBase<TSelf>
{
    protected readonly IEnvironmentStateProvider _environmentProvider;

    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    protected VM_NPCAttributeFormKeyBase(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider)
    {
        _environmentProvider = environmentProvider;
        ParentVM = parentVM;
        ParentShell = parentShell;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => OnDelete(parentVM, parentShell));
    }

    /// <summary>Removes this sub-attribute's shell from its parent condition. Overridable so a subclass can add
    /// cleanup (e.g. removing the parent condition once it has no remaining sub-attributes).</summary>
    protected virtual void OnDelete(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
    {
        parentVM.GroupedSubAttributes.Remove(parentShell);
    }

    public ObservableCollection<FormKey> FormKeys { get; set; } = new();
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; }
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();

    /// <summary>The plural display label for this attribute kind (e.g. "Classes", "Races").</summary>
    protected abstract string PluralLabel { get; }

    public string DebuggerString => (ParentShell.Not ? "NOT " : "") + PluralLabel + ": " +
        (FormKeys.Any() ? String.Join(", ", FormKeys.Select(x => x.ToString())) : "None");
}

/// <summary>Sub-attribute VM matching NPCs whose voice type is among the selected FormKeys. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeVoiceType : VM_NPCAttributeFormKeyBase<VM_NPCAttributeVoiceType>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeVoiceType Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command (which also removes the parent condition once it is left empty).</summary>
    public VM_NPCAttributeVoiceType(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(IVoiceTypeGetter).AsEnumerable();
    }
    protected override string PluralLabel => "Voice Types";

    /// <summary>Also removes the parent condition once its last sub-attribute is deleted.</summary>
    protected override void OnDelete(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell)
    {
        parentVM.GroupedSubAttributes.Remove(parentShell);
        if (parentVM.GroupedSubAttributes.Count == 0)
        {
            parentVM.ParentCollection.Remove(parentVM);
        }
    }

    /// <summary>Builds a VoiceType sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeVoiceType GetViewModelFromModel(NPCAttributeVoiceType model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeVoiceType.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this VoiceType sub-attribute (with the shell's forcing mode, weight, and negation) back to an <see cref="NPCAttributeVoiceType"/> model.</summary>
    public static NPCAttributeVoiceType DumpViewModelToModel(VM_NPCAttributeVoiceType viewModel, string forceModeStr)
    {
        return new NPCAttributeVoiceType() { Type = NPCAttributeType.VoiceType, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>Sub-attribute VM matching NPCs whose Class record is among the selected FormKeys. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeClass : VM_NPCAttributeFormKeyBase<VM_NPCAttributeClass>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeClass Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    public VM_NPCAttributeClass(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(IClassGetter).AsEnumerable();
    }
    protected override string PluralLabel => "Classes";

    /// <summary>Builds a sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeClass GetViewModelFromModel(NPCAttributeClass model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeClass.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this sub-attribute (with the shell's forcing mode, weight, and negation) back to its <see cref="NPCAttributeClass"/> model.</summary>
    public static NPCAttributeClass DumpViewModelToModel(VM_NPCAttributeClass viewModel, string forceModeStr)
    {
        return new NPCAttributeClass() { Type = NPCAttributeType.Class, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>
/// Sub-attribute VM matching NPCs by a custom record-path comparison: the value at an intellisensed
/// record <see cref="IntellisensedPath"/> is compared — per <see cref="CustomType"/> and
/// <see cref="ChosenComparator"/> — against a text/numeric/boolean value or a set of FormKeys. Also
/// implements <see cref="IImplementsRecordIntellisense"/> for path autocompletion and supports live
/// evaluation against a chosen reference NPC. See <see cref="ISubAttributeViewModel"/> for the shared contract.
/// </summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeCustom : VM, ISubAttributeViewModel, IImplementsRecordIntellisense
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private AttributeMatcher _attributeMatcher;
    private RecordIntellisense _recordIntellisense;
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeCustom Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>
    /// Populates the record-type dropdown from the Skyrim major-record registrations, wires the intellisense
    /// subscriptions, and re-runs <see cref="Evaluate"/> whenever the type, value, comparator, path, or
    /// reference NPC changes. Tracks the link cache for record resolution.
    /// </summary>
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

    public string DebuggerString
    {
        get
        {
            return (ParentShell.Not ? "NOT " : "") + CustomType.ToString();
        }
    }

    /// <summary>Builds a Custom sub-attribute VM from its model, restoring the path, comparator, value(s), reference NPC, and the shell's weight/negation.</summary>
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

    /// <summary>Serializes this Custom sub-attribute (path, comparator, value(s), reference NPC, and the shell's forcing mode/weight/negation) back to an <see cref="NPCAttributeCustom"/> model.</summary>
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

    /// <summary>
    /// Validates the current inputs and, when complete, resolves the reference NPC and asks the
    /// <see cref="AttributeMatcher"/> whether it satisfies this condition, surfacing the outcome (and a
    /// status colour) via <see cref="EvalResult"/>/<see cref="StatusFontColor"/>.
    /// </summary>
    /// <remarks>If the reference NPC fails to resolve, the error is reported but evaluation still proceeds with a null reference (see review notes).</remarks>
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
                return;
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

    /// <summary>Shows the value editor appropriate to <see cref="CustomType"/> (text field / FormKey picker / boolean picker), rebuilds the comparators valid for that type, then re-evaluates.</summary>
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

    /// <summary>Narrows the FormKey picker to the currently selected record type, then re-evaluates.</summary>
    public void UpdateFormKeyPickerRecordType()
    {
        ValueFKtypeCollection = ValueFKtype.AsEnumerable();
        Evaluate();
    }
}

/// <summary>Sub-attribute VM matching NPCs that belong to any of the selected factions within the rank range <see cref="RankMin"/>–<see cref="RankMax"/>. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeFactions : VM, ISubAttributeViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeFactions Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command (removes this shell from the parent condition).</summary>
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
    /// <summary>Minimum faction rank to match (inclusive); the default -1 matches any rank at or below <see cref="RankMax"/>.</summary>
    public int RankMin { get; set; } = -1;
    /// <summary>Maximum faction rank to match (inclusive).</summary>
    public int RankMax { get; set; } = 100;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(IFactionGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();
    public string DebuggerString
    {
        get
        {
            if (FactionFormKeys.Any())
            {
                return (ParentShell.Not ? "NOT " : "") + "Factions: " + String.Join(", ", FactionFormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (ParentShell.Not ? "NOT " : "") + "Factions: None";
            }
        }
    }

    /// <summary>Builds a Factions sub-attribute VM from its model, copying the FormKeys, rank range, weight, and negation onto the VM/shell.</summary>
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
    /// <summary>Serializes this Factions sub-attribute (FormKeys, rank range, and the shell's forcing mode/weight/negation) back to an <see cref="NPCAttributeFactions"/> model.</summary>
    public static NPCAttributeFactions DumpViewModelToModel(VM_NPCAttributeFactions viewModel, string forceModeStr)
    {
        return new NPCAttributeFactions() { Type = NPCAttributeType.Faction, FormKeys = viewModel.FactionFormKeys.ToHashSet(), RankMin = viewModel.RankMin, RankMax = viewModel.RankMax, ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>Sub-attribute VM matching NPCs whose head FaceTexture (texture set) is among the selected FormKeys. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeFaceTexture : VM_NPCAttributeFormKeyBase<VM_NPCAttributeFaceTexture>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeFaceTexture Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    public VM_NPCAttributeFaceTexture(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(ITextureSetGetter).AsEnumerable();
    }
    protected override string PluralLabel => "Face Textures";

    /// <summary>Builds a sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeFaceTexture GetViewModelFromModel(NPCAttributeFaceTexture model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeFaceTexture.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this sub-attribute (with the shell's forcing mode, weight, and negation) back to its <see cref="NPCAttributeFaceTexture"/> model.</summary>
    public static NPCAttributeFaceTexture DumpViewModelToModel(VM_NPCAttributeFaceTexture viewModel, string forceModeStr)
    {
        return new NPCAttributeFaceTexture() { Type = NPCAttributeType.FaceTexture, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>Sub-attribute VM matching NPCs that carry any of the selected keywords. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeKeyword : VM_NPCAttributeFormKeyBase<VM_NPCAttributeKeyword>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeKeyword Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    public VM_NPCAttributeKeyword(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(IKeywordGetter).AsEnumerable();
    }
    protected override string PluralLabel => "Keywords";

    /// <summary>Builds a sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeKeyword GetViewModelFromModel(NPCAttributeKeyword model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeKeyword.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this sub-attribute (with the shell's forcing mode, weight, and negation) back to its <see cref="NPCAttributeKeyword"/> model.</summary>
    public static NPCAttributeKeyword DumpViewModelToModel(VM_NPCAttributeKeyword viewModel, string forceModeStr)
    {
        return new NPCAttributeKeyword() { Type = NPCAttributeType.Keyword, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>Sub-attribute VM matching NPCs of any of the selected races. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeRace : VM_NPCAttributeFormKeyBase<VM_NPCAttributeRace>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeRace Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    public VM_NPCAttributeRace(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(IRaceGetter).AsEnumerable();
    }
    protected override string PluralLabel => "Races";

    /// <summary>Builds a sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeRace GetViewModelFromModel(NPCAttributeRace model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeRace.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this sub-attribute (with the shell's forcing mode, weight, and negation) back to its <see cref="NPCAttributeRace"/> model.</summary>
    public static NPCAttributeRace DumpViewModelToModel(VM_NPCAttributeRace viewModel, string forceModeStr)
    {
        return new NPCAttributeRace() { Type = NPCAttributeType.Race, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>
/// Sub-attribute VM matching NPCs by miscellaneous tri-state flags (unique, essential, protected,
/// summonable, ghost, invulnerable) and, optionally, mood, aggression, and gender. See
/// <see cref="ISubAttributeViewModel"/> for the shared contract.
/// </summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeMisc : VM, ISubAttributeViewModel
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeMisc Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command (removes this shell from the parent condition).</summary>
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
    public Aggression Aggression { get; set; } = Aggression.Unaggressive;
    public bool EvalGender { get; set; } = false;
    public Gender NPCGender { get; set; } = Gender.Female;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();
    public string DebuggerString
    {
        get
        {
            return (ParentShell.Not ? "NOT " : "") + "Miscellaneous Attributes";
        }
    }

    /// <summary>Builds a Misc sub-attribute VM from its model, copying every flag/trait plus the shell's weight and negation.</summary>
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
    /// <summary>Serializes this Misc sub-attribute (every flag/trait plus the shell's forcing mode/weight/negation) back to an <see cref="NPCAttributeMisc"/> model.</summary>
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

/// <summary>Sub-attribute VM matching NPCs by mod provenance (created / patched / winning override / winning appearance) for the selected mod keys, per <see cref="ModActionType"/>. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeMod : VM, ISubAttributeViewModel
{
    private IEnvironmentStateProvider _environmentProvider;
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeMod Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache and load order, and wires the delete command (removes this shell from the parent condition).</summary>
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

    /// <summary>The mod keys to match against.</summary>
    public ObservableCollection<ModKey> ModKeys { get; set; } = new();
    /// <summary>Which provenance relationship between the NPC record and the mod keys must hold.</summary>
    public ModAttributeEnum ModActionType { get; set; } = ModAttributeEnum.PatchedBy;
    public VM_NPCAttribute ParentVM { get; set; }
    public VM_NPCAttributeShell ParentShell { get; set; }
    public RelayCommand DeleteCommand { get; }
    public ILinkCache lk { get; private set; }
    public ILoadOrderGetter LoadOrder { get; private set; }

    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public IObservable<Unit> NeedsRefresh { get; } = System.Reactive.Linq.Observable.Empty<Unit>();
    public string DebuggerString
    {
        get
        {
            if (ModKeys.Any())
            {
                return (ParentShell.Not ? "NOT " : "") + "ModKeys: " + String.Join(", ", ModKeys.Select(x => x.ToString()));
            }
            else
            {
                return (ParentShell.Not ? "NOT " : "") + "ModKeys: None";
            }
        }
    }

    /// <summary>Builds a Mod sub-attribute VM from its model, copying the mod keys, action type, weight, and negation onto the VM/shell.</summary>
    public static VM_NPCAttributeMod GetViewModelFromModel(NPCAttributeMod model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeMod.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.ModKeys = new(model.ModKeys);
        newAtt.ModActionType = model.ModActionType;
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }

    /// <summary>Serializes this Mod sub-attribute (mod keys, action type, and the shell's forcing mode/weight/negation) back to an <see cref="NPCAttributeMod"/> model.</summary>
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

/// <summary>Sub-attribute VM matching the specific NPCs selected by FormKey. See <see cref="ISubAttributeViewModel"/> for the shared contract.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeNPC : VM_NPCAttributeFormKeyBase<VM_NPCAttributeNPC>
{
    private readonly Factory _selfFactory;
    /// <summary>Autofac factory delegate for constructing this sub-attribute VM under a shell.</summary>
    public delegate VM_NPCAttributeNPC Factory(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell);
    /// <summary>Stores the parent condition/shell, tracks the link cache, and wires the delete command.</summary>
    public VM_NPCAttributeNPC(VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, IEnvironmentStateProvider environmentProvider, Factory selfFactory)
        : base(parentVM, parentShell, environmentProvider)
    {
        _selfFactory = selfFactory;
        AllowedFormKeyTypes = typeof(INpcGetter).AsEnumerable();
    }
    protected override string PluralLabel => "NPCs";

    /// <summary>Builds a sub-attribute VM from its model, copying the FormKeys, weight, and negation onto the shell.</summary>
    public static VM_NPCAttributeNPC GetViewModelFromModel(NPCAttributeNPC model, VM_NPCAttribute parentVM, VM_NPCAttributeShell parentShell, VM_NPCAttributeNPC.Factory factory)
    {
        var newAtt = factory(parentVM, parentShell);
        newAtt.FormKeys = new ObservableCollection<FormKey>(model.FormKeys);
        parentShell.ForceIfWeight = model.Weighting;
        parentShell.Not = model.Not;
        return newAtt;
    }
    /// <summary>Serializes this sub-attribute (with the shell's forcing mode, weight, and negation) back to its <see cref="NPCAttributeNPC"/> model.</summary>
    public static NPCAttributeNPC DumpViewModelToModel(VM_NPCAttributeNPC viewModel, string forceModeStr)
    {
        return new NPCAttributeNPC() { Type = NPCAttributeType.NPC, FormKeys = viewModel.FormKeys.ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>
/// Sub-attribute VM matching NPCs that satisfy any of the selected attribute groups (resolved by label).
/// Presents the available groups as a checkable list (<see cref="SelectableAttributeGroups"/>) that
/// re-syncs whenever the source group collection changes. See <see cref="ISubAttributeViewModel"/> for the
/// shared contract.
/// </summary>
[DebuggerDisplay("{DebuggerString}")]
public class VM_NPCAttributeGroup : VM, ISubAttributeViewModel
{
    /// <summary>
    /// Builds the selectable checklist from the source groups and keeps it in sync (preserving checked
    /// labels) as groups are added/removed, rebuilding <see cref="NeedsRefresh"/> on each change. Wires the
    /// delete command and forwards the most-recently-edited selection to the parent condition.
    /// </summary>
    /// <param name="parentAttributeVM">The owning AND-condition.</param>
    /// <param name="parentShell">The shell hosting this attribute.</param>
    /// <param name="sourceAttributeGroups">The live collection of available attribute groups to mirror as checkboxes.</param>
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
    /// <summary>The live source collection of available attribute groups this checklist mirrors.</summary>
    public ObservableCollection<VM_AttributeGroup> SubscribedAttributeGroups { get; set; }
    /// <summary>The per-group checkbox rows shown in the UI, kept in sync with <see cref="SubscribedAttributeGroups"/>.</summary>
    public ObservableCollection<AttributeGroupSelection> SelectableAttributeGroups { get; set; } = new();
    /// <summary>The most recently toggled checkbox row (used to surface the active shell to the parent condition).</summary>
    public AttributeGroupSelection MostRecentlyEditedSelection { get; set; }

    public string DebuggerString
    {
        get {
            var selected = SelectableAttributeGroups.Where(x => x.IsSelected).ToArray();
            if (selected.Any())
            {
                return (ParentShell.Not ? "NOT " : "") + "Selected Groups: " + String.Join(", ", selected.Select(x => x.SubscribedAttributeGroup.Label));
            }
            else
            {
                return (ParentShell.Not ? "NOT " : "") + "Selected Groups: None";
            }
        }
    }

    /// <summary>Rebuilds <see cref="SelectableAttributeGroups"/> from the current source groups, preserving which group labels were checked.</summary>
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
    /// <summary>One checkbox row pairing an available <see cref="VM_AttributeGroup"/> with its checked state; reports itself to the parent as the most-recently-edited selection when toggled.</summary>
    public class AttributeGroupSelection : VM
    {
        /// <summary>Captures the group and parent, and notifies the parent whenever this row's checked state changes.</summary>
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

    /// <summary>Builds a Group sub-attribute VM from its model, checking the boxes whose labels are listed in the model and restoring the shell's weight/negation.</summary>
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
    /// <summary>Serializes this Group sub-attribute (the checked group labels and the shell's forcing mode/weight/negation) back to an <see cref="NPCAttributeGroup"/> model.</summary>
    public static NPCAttributeGroup DumpViewModelToModel(VM_NPCAttributeGroup viewModel, string forceModeStr)
    {
        return new NPCAttributeGroup() { Type = NPCAttributeType.Group, SelectedLabels = viewModel.SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label).ToHashSet(), ForceMode = VM_NPCAttributeShell.ForceModeStrToEnumDict[forceModeStr], Weighting = viewModel.ParentShell.ForceIfWeight, Not = viewModel.ParentShell.Not };
    }
}

/// <summary>Boolean choices presented as a two-item enum for binding a true/false picker in the UI.</summary>
public enum BoolVals
{
    /// <summary>Boolean true.</summary>
    True,
    /// <summary>Boolean false.</summary>
    False
}
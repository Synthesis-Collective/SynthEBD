using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for an additional record-template assignment: maps a set of races to a template NPC plus
/// the record paths used to resolve those races, with commands to add paths and delete the entry.
/// </summary>
public class VM_AdditionalRecordTemplate : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    /// <summary>Autofac factory delegate for constructing an assignment within a parent collection.</summary>
    public delegate VM_AdditionalRecordTemplate Factory(ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache,
        ObservableCollection<VM_AdditionalRecordTemplate> parentCollection);
    /// <summary>Creates the VM, wiring link-cache tracking and the add-path / delete commands.</summary>
    /// <param name="environmentProvider">Supplies the main link cache for race pickers.</param>
    /// <param name="recordTemplateLinkCache">Link cache over the record-template plugins (for the template-NPC picker).</param>
    /// <param name="parentCollection">The collection this entry belongs to.</param>
    public VM_AdditionalRecordTemplate(IEnvironmentStateProvider environmentProvider,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache,
        ObservableCollection<VM_AdditionalRecordTemplate> parentCollection)
    {
        _environmentProvider = environmentProvider;
        RecordTemplateLinkCache = recordTemplateLinkCache;
        ParentCollection = parentCollection;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AddAdditionalRacesPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { AdditionalRacesPaths.Add(new VM_CollectionMemberString("", AdditionalRacesPaths)); }
        );

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { ParentCollection.Remove(this); }
        );
    }

    /// <summary>Projects the view model back into an <see cref="AdditionalRecordTemplate"/> model.</summary>
    /// <param name="viewModel">The view model to project.</param>
    /// <returns>The populated model.</returns>
    public static AdditionalRecordTemplate DumpViewModelToModel(VM_AdditionalRecordTemplate viewModel)
    {
        return new AdditionalRecordTemplate() { Races = viewModel.RaceFormKeys.ToHashSet(), TemplateNPC = viewModel.TemplateNPC, AdditionalRacesPaths = viewModel.AdditionalRacesPaths.Select(x => x.Content).ToHashSet() };
    }

    public ObservableCollection<FormKey> RaceFormKeys { get; set; } = new();
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerTypes { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public FormKey TemplateNPC { get; set; } = new();

    public ObservableCollection<VM_CollectionMemberString> AdditionalRacesPaths { get; set; } = new();

    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

    public ObservableCollection<VM_AdditionalRecordTemplate> ParentCollection { get; set; }
    public RelayCommand AddAdditionalRacesPath { get; }
    public RelayCommand DeleteCommand { get; set; }

    /// <summary>Default AdditionalRaces record paths (body/hands/feet armatures) seeded for non-beast races.</summary>
    public static List<string> AdditionalRacesPathsDefault = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };

    /// <summary>Additional AdditionalRaces record path (tail armature) seeded for beast races.</summary>
    public static List<string> AdditionalRacesPathsBeast = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };

    /// <summary>Additional AdditionalRaces record path (TNG genital armature, biped slot 52) seeded when TNG is in use.</summary>
    public static List<string> AdditionalRacesPathsTNG = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };
}
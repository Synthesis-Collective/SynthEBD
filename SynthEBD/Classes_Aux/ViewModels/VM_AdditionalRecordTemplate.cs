using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_AdditionalRecordTemplate : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    public delegate VM_AdditionalRecordTemplate Factory(ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache,
        ObservableCollection<VM_AdditionalRecordTemplate> parentCollection);
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

    public static List<string> AdditionalRacesPathsDefault = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };

    public static List<string> AdditionalRacesPathsBeast = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };

    public static List<string> AdditionalRacesPathsTNG = new()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].AdditionalRaces"
    };
}
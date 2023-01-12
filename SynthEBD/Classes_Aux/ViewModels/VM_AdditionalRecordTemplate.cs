using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_AdditionalRecordTemplate : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    public VM_AdditionalRecordTemplate(IEnvironmentStateProvider stateProvider,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, 
        ObservableCollection<VM_AdditionalRecordTemplate> parentCollection)
    {
        _stateProvider = stateProvider;
        RecordTemplateLinkCache = recordTemplateLinkCache;
        ParentCollection = parentCollection;
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AddAdditionalRacesPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.AdditionalRacesPaths.Add(new VM_CollectionMemberString("", this.AdditionalRacesPaths)); }
        );

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.ParentCollection.Remove(this); }
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
}
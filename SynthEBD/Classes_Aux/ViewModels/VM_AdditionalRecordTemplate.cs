using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_AdditionalRecordTemplate : ViewModel
{
    public VM_AdditionalRecordTemplate(ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, ObservableCollection<VM_AdditionalRecordTemplate> parentCollection)
    {
        this.RecordTemplateLinkCache = recordTemplateLinkCache;
        this.ParentCollection = parentCollection;
        
        _linkCache = PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .ToProperty(this, nameof(lk), default(ILinkCache))
            .DisposeWith(this);

        AddAdditionalRacesPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.AdditionalRacesPaths.Add(new VM_CollectionMemberString("", this.AdditionalRacesPaths)); }
        );

        DeleteCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.ParentCollection.Remove(this); }
        );
    }

    public static AdditionalRecordTemplate DumpViewModelToModel(VM_AdditionalRecordTemplate viewModel)
    {
        return new AdditionalRecordTemplate() { Races = viewModel.RaceFormKeys.ToHashSet(), TemplateNPC = viewModel.TemplateNPC, AdditionalRacesPaths = viewModel.AdditionalRacesPaths.Select(x => x.Content).ToHashSet() };
    }

    public ObservableCollection<FormKey> RaceFormKeys { get; set; } = new();

    private readonly ObservableAsPropertyHelper<ILinkCache> _linkCache;
    public ILinkCache lk => _linkCache.Value;
    public IEnumerable<Type> RacePickerTypes { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public FormKey TemplateNPC { get; set; } = new();

    public ObservableCollection<VM_CollectionMemberString> AdditionalRacesPaths { get; set; } = new();

    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

    public ObservableCollection<VM_AdditionalRecordTemplate> ParentCollection { get; set; }
    public RelayCommand AddAdditionalRacesPath { get; }
    public RelayCommand DeleteCommand { get; set; }
}
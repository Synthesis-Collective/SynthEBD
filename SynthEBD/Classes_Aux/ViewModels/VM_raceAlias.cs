using System.Collections.ObjectModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class VM_RaceAlias : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    public delegate VM_RaceAlias Factory(RaceAlias alias, VM_Settings_General parentVM);
    public VM_RaceAlias(RaceAlias alias, VM_Settings_General parentVM, IEnvironmentStateProvider environmentProvider)
    {
        _environmentProvider = environmentProvider;
        this.race = alias.Race;
        this.aliasRace = alias.AliasRace;
        this.bMale = alias.bMale;
        this.bFemale = alias.bFemale;
        this.bApplyToAssets = alias.bApplyToAssets;
        this.bApplyToBodyGen = alias.bApplyToBodyGen;
        this.bApplyToHeight = alias.bApplyToHeight;
        this.bApplyToHeadParts = alias.bApplyToHeadParts;
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.raceAliases.Remove(this));
    }

    public FormKey race { get; set; }
    public FormKey aliasRace { get; set; }
    public bool bMale { get; set; }
    public bool bFemale { get; set; }

    public bool bApplyToAssets { get; set; }
    public bool bApplyToBodyGen { get; set; }
    public bool bApplyToHeight { get; set; }
    public bool bApplyToHeadParts { get; set; }
    public IEnumerable<Type> FormKeyPickerTypes { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public ILinkCache lk { get; private set; }

    public VM_Settings_General ParentVM { get; set; }

    public RelayCommand DeleteCommand { get; }

    public static ObservableCollection<VM_RaceAlias> GetViewModelsFromModels(List<RaceAlias> models, VM_Settings_General parentVM, VM_RaceAlias.Factory factory)
    {
        var RAVM = new ObservableCollection<VM_RaceAlias>();

        foreach (var x in models)
        {
            var y = factory(x, parentVM);
            RAVM.Add(y);
        }

        return RAVM;
    }

    public static RaceAlias DumpViewModelToModel(VM_RaceAlias viewModel)
    {
        RaceAlias model = new RaceAlias();
        model.Race = viewModel.race;
        model.AliasRace = viewModel.aliasRace;
        model.bMale = viewModel.bMale;
        model.bFemale = viewModel.bFemale;
        model.bApplyToAssets = viewModel.bApplyToAssets;
        model.bApplyToBodyGen = viewModel.bApplyToBodyGen;
        model.bApplyToHeight = viewModel.bApplyToHeight;
        model.bApplyToHeadParts = viewModel.bApplyToHeadParts;
        return model;
    }
}
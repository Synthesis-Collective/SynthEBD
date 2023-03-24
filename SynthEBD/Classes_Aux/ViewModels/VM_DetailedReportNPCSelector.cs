using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD
{
    public class VM_DetailedReportNPCSelector : VM
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly VM_NPCAttributeCreator _attributeCreator;
        private readonly VM_AttributeGroupMenu _generalSettingsAttGroupMenu;
        private readonly VM_RaceGroupingEditor _generalSettingsRaceGroupingEditor;
        public delegate VM_DetailedReportNPCSelector Factory(VM_AttributeGroupMenu generalSettingsAttGroupMenu, VM_RaceGroupingEditor generalSettingsRaceGroupingEditor);
        public VM_DetailedReportNPCSelector(IEnvironmentStateProvider environmentProvider, Logger logger, VM_NPCAttributeCreator attributeCreator, VM_AttributeGroupMenu generalSettingsAttGroupMenu, VM_RaceGroupingEditor generalSettingsRaceGroupingEditor)
        {
            _environmentProvider = environmentProvider;
            _logger = logger;
            _attributeCreator = attributeCreator;
            _generalSettingsAttGroupMenu = generalSettingsAttGroupMenu;
            _generalSettingsRaceGroupingEditor = generalSettingsRaceGroupingEditor;

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
           .Subscribe(x => LinkCache = x)
           .DisposeWith(this);

            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(_generalSettingsRaceGroupingEditor.RaceGroupings);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(_generalSettingsRaceGroupingEditor.RaceGroupings);

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, false, null, _generalSettingsAttGroupMenu.Groups))
            );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, _generalSettingsAttGroupMenu.Groups))
            );
        }
        public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
        public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public NPCWeightRange WeightRange { get; set; } = new();

        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }

        public ILinkCache LinkCache { get; private set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();


        public void CopyInFromModel(DetailedReportNPCSelector model)
        {
            AllowedRaces.AddRange(model.AllowedRaces);
            DisallowedRaces.AddRange(model.DisallowedRaces);
            foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
            AllowUnique = model.AllowUnique;
            AllowNonUnique = model.AllowNonUnique;
            _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, _generalSettingsAttGroupMenu.Groups, false, null);
            _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, _generalSettingsAttGroupMenu.Groups, false, null);
            AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, _generalSettingsRaceGroupingEditor.RaceGroupings);
            DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, _generalSettingsRaceGroupingEditor.RaceGroupings);
            WeightRange = model.WeightRange;
        }

        public DetailedReportNPCSelector DumpToModel()
        {
            DetailedReportNPCSelector model = new();
            model.AllowedRaces = AllowedRaces.ToHashSet();
            model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
            model.DisallowedRaces = DisallowedRaces.ToHashSet();
            model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
            model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
            model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
            model.AllowUnique = AllowUnique;
            model.AllowNonUnique = AllowNonUnique;
            model.WeightRange = WeightRange;
            return model;
        }
    }
}

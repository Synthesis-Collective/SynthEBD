using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public interface IHasRaceGroupingEditor
    {
        public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    }
    public class VM_RaceGroupingEditor : VM, IHasRaceGroupingVMs
    {
        private readonly Func<VM_Settings_General> _generalSettingsVM;
        private readonly VM_RaceGrouping.Factory _raceGroupingFactory;
        public delegate VM_RaceGroupingEditor Factory(IHasRaceGroupingEditor parent, bool showImportFromGeneral);
        public VM_RaceGroupingEditor(IHasRaceGroupingEditor parent, bool showImportFromGeneral, VM_RaceGrouping.Factory raceGroupingFactory, Func<VM_Settings_General> generalSettingsVM)
        {
            Parent = parent;
            ShowImportFromGeneral = showImportFromGeneral;
            _raceGroupingFactory = raceGroupingFactory;
            _generalSettingsVM = generalSettingsVM;

            AddRaceGrouping = new RelayCommand(
                canExecute: _ => true,
                execute: _ => RaceGroupings.Add(_raceGroupingFactory(new RaceGrouping(), this))
                );

            ImportFromGeneral = new RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    RaceGroupings.AddRange(generalSettingsVM().RaceGroupingEditor.RaceGroupings.Where(x => !RaceGroupings.Select(x => x.Label).Contains(x.Label)).Select(x => x.Copy(this)));
                }
            );
        }

        public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; } = new();
        public IHasRaceGroupingEditor Parent { get; set; }
        public bool ShowImportFromGeneral { get; }
        public RelayCommand AddRaceGrouping { get; }
        public RelayCommand ImportFromGeneral { get; }

        public void CopyInFromModel(List<RaceGrouping> raceGroupings, ObservableCollection<VM_RaceGrouping> generalSettingsGroups)
        {
            RaceGroupings = VM_RaceGrouping.GetViewModelsFromModels(raceGroupings, this, _raceGroupingFactory);
            if (generalSettingsGroups != null)
            {
                var originals = RaceGroupings.Select(x => x.Label);
                foreach (var candidate in generalSettingsGroups) // add groups from General Settings that are not overwritten by local definitions
                {
                    if (!originals.Contains(candidate.Label))
                    {
                        RaceGroupings.Add(candidate.Copy(this));
                    }
                }
            }
        }

        public List<RaceGrouping> DumpToModel()
        {
            return RaceGroupings.Select(x => x.DumpViewModelToModel()).ToList();
        }
    }
}

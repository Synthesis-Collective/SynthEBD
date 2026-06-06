using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>Implemented by view models that host a <see cref="VM_RaceGroupingEditor"/>.</summary>
    public interface IHasRaceGroupingEditor
    {
        /// <summary>The hosted race-grouping editor.</summary>
        public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    }
    /// <summary>
    /// View model for the race-grouping editor: an editable list of <see cref="VM_RaceGrouping"/> with
    /// commands to add a grouping and import groupings from General Settings.
    /// </summary>
    public class VM_RaceGroupingEditor : VM, IHasRaceGroupingVMs
    {
        private readonly Func<VM_Settings_General> _generalSettingsVM;
        private readonly Logger _logger;
        private readonly VM_RaceGrouping.Factory _raceGroupingFactory;
        /// <summary>Autofac factory delegate for constructing the editor under a host.</summary>
        public delegate VM_RaceGroupingEditor Factory(IHasRaceGroupingEditor parent, bool showImportFromGeneral);
        /// <summary>Creates the editor and wires the add-grouping and import-from-general commands.</summary>
        /// <param name="parent">The hosting view model.</param>
        /// <param name="showImportFromGeneral">Whether to show the "import from General Settings" action.</param>
        /// <param name="raceGroupingFactory">Factory for grouping VMs.</param>
        /// <param name="generalSettingsVM">Accessor for the General Settings VM (source of importable groupings).</param>
        /// <param name="logger">Logger for startup timing.</param>
        public VM_RaceGroupingEditor(IHasRaceGroupingEditor parent, bool showImportFromGeneral, VM_RaceGrouping.Factory raceGroupingFactory, Func<VM_Settings_General> generalSettingsVM, Logger logger)
        {
            Parent = parent;
            ShowImportFromGeneral = showImportFromGeneral;
            _raceGroupingFactory = raceGroupingFactory;
            _generalSettingsVM = generalSettingsVM;
            _logger = logger;

            AddRaceGrouping = new RelayCommand(
                canExecute: _ => true,
                execute: _ => RaceGroupings.Add(_raceGroupingFactory(new RaceGrouping(), this))
                );

            ImportFromGeneral = new RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    ImportFromGeneralSettings();
                }
            );
        }

        public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; } = new();
        public IHasRaceGroupingEditor Parent { get; set; }
        public bool ShowImportFromGeneral { get; }
        public RelayCommand AddRaceGrouping { get; }
        public RelayCommand ImportFromGeneral { get; }

        /// <summary>Loads groupings from models, then appends any General-Settings groupings not overridden locally (by label).</summary>
        /// <param name="raceGroupings">The local grouping models to load.</param>
        /// <param name="generalSettingsGroups">General-Settings groupings to append where not overridden; may be null.</param>
        public void CopyInFromModel(List<RaceGrouping> raceGroupings, ObservableCollection<VM_RaceGrouping> generalSettingsGroups)
        {
            _logger.LogStartupEventStart("Generating Race Grouping Editor UI");
            RaceGroupings.AddRange(VM_RaceGrouping.GetViewModelsFromModels(raceGroupings.Where(x => !RaceGroupings.Select(y => y.Label).Contains(x.Label)).ToList(), this, _raceGroupingFactory));
            if (generalSettingsGroups != null)
            {
                var originals = RaceGroupings.Select(x => x.Label).ToArray();
                foreach (var candidate in generalSettingsGroups) // add groups from General Settings that are not overwritten by local definitions
                {
                    if (!originals.Contains(candidate.Label))
                    {
                        RaceGroupings.Add(candidate.Copy(this));
                    }
                }
            }
            _logger.LogStartupEventEnd("Generating Race Grouping Editor UI");
        }

        /// <summary>Projects the editor's groupings back into a list of models.</summary>
        /// <returns>The grouping models.</returns>
        public List<RaceGrouping> DumpToModel()
        {
            return RaceGroupings.Select(x => x.DumpViewModelToModel()).ToList();
        }

        /// <summary>Appends General-Settings race groupings that aren't already present (by label), copied into this editor.</summary>
        public void ImportFromGeneralSettings()
        {
            RaceGroupings.AddRange(_generalSettingsVM().RaceGroupingEditor.RaceGroupings.Where(x => !RaceGroupings.Select(x => x.Label).Contains(x.Label)).Select(x => x.Copy(this)));
        }
    }
}

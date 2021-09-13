using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD
{
    public class VM_Settings_General : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        public VM_Settings_General()
        {
            this.bShowToolTips = true;
            this.bChangeMeshesOrTextures = true;
            this.bEnableBodyGenIntegration = false;
            this.bChangeHeight = false;
            this.bEnableConsistency = true;
            this.bLinkNPCsWithSameName = true;
            this.patchFileName = "SynthEBD.esp";
            this.bVerboseModeAssetsNoncompliant = false;
            this.bVerboseModeAssetsAll = false;
            this.verboseModeNPClist = new ObservableCollection<FormKey>();
            this.bLoadSettingsFromDataFolder = false;
            this.patchableRaces = new ObservableCollection<FormKey>();
            this.raceAliases = new ObservableCollection<VM_raceAlias>();
            this.RaceGroupings = new ObservableCollection<VM_RaceGrouping>();

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
            this.NPCPickerFormKeys = typeof(INpcGetter).AsEnumerable();

            AddRaceAlias = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.raceAliases.Add(new VM_raceAlias(new RaceAlias(), new GameEnvironmentProvider().MyEnvironment, this))
                );

            AddRaceGrouping = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.RaceGroupings.Add(new VM_RaceGrouping(new RaceGrouping(), new GameEnvironmentProvider().MyEnvironment, this))
                );
        }

        public bool bShowToolTips { get;  set;}
        public bool bChangeMeshesOrTextures { get; set;  }

        public bool bEnableBodyGenIntegration { get; set;  }

        public bool bChangeHeight { get; set;  }
        public bool bEnableConsistency { get; set;  }
        public bool bLinkNPCsWithSameName { get; set;  }
        public string patchFileName { get; set;  }

        public bool bVerboseModeAssetsNoncompliant { get; set;  }
        public bool bVerboseModeAssetsAll { get; set;  }
        public ObservableCollection<FormKey> verboseModeNPClist { get; set; }
        public bool bLoadSettingsFromDataFolder { get; set;  }

        public ObservableCollection<FormKey> patchableRaces { get; set; }

        public ObservableCollection<VM_raceAlias> raceAliases { get; set;  }
        public RelayCommand AddRaceAlias { get; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }
        public IEnumerable<Type> NPCPickerFormKeys { get; set; }

        public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; }
        public RelayCommand AddRaceGrouping { get; }

        public static void GetViewModelFromModel(VM_Settings_General viewModel, SynthEBD.Settings_General model)
        {
            viewModel.bShowToolTips = model.bShowToolTips;
            viewModel.bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
            viewModel.bEnableBodyGenIntegration = model.bEnableBodyGenIntegration;
            viewModel.bChangeHeight = model.bChangeHeight;
            viewModel.bEnableConsistency = model.bEnableConsistency;
            viewModel.bLinkNPCsWithSameName = model.bLinkNPCsWithSameName;
            viewModel.patchFileName = model.patchFileName;
            viewModel.bVerboseModeAssetsNoncompliant = model.bVerboseModeAssetsNoncompliant;
            viewModel.bVerboseModeAssetsAll = model.bVerboseModeAssetsAll;
            viewModel.verboseModeNPClist = new ObservableCollection<FormKey>(model.verboseModeNPClist);
            viewModel.bLoadSettingsFromDataFolder = model.bLoadSettingsFromDataFolder;
            viewModel.patchableRaces = new ObservableCollection<FormKey>(model.patchableRaces);
            viewModel.raceAliases = VM_raceAlias.GetViewModelsFromModels(model.raceAliases, new GameEnvironmentProvider().MyEnvironment, viewModel);
            viewModel.RaceGroupings = VM_RaceGrouping.GetViewModelsFromModels(model.RaceGroupings, new GameEnvironmentProvider().MyEnvironment, viewModel);
        }
        public static void DumpViewModelToModel(VM_Settings_General viewModel, Settings_General model)
        {
            model.bShowToolTips = viewModel.bShowToolTips;
            model.bChangeMeshesOrTextures = viewModel.bChangeMeshesOrTextures;
            model.bEnableBodyGenIntegration = viewModel.bEnableBodyGenIntegration;
            model.bChangeHeight = viewModel.bChangeHeight;
            model.bEnableConsistency = viewModel.bEnableConsistency;
            model.bLinkNPCsWithSameName = viewModel.bLinkNPCsWithSameName;
            model.patchFileName = viewModel.patchFileName;
            model.bVerboseModeAssetsNoncompliant = viewModel.bVerboseModeAssetsNoncompliant;
            model.bVerboseModeAssetsAll = viewModel.bVerboseModeAssetsAll;
            model.verboseModeNPClist = viewModel.verboseModeNPClist.ToList();
            model.bLoadSettingsFromDataFolder = viewModel.bLoadSettingsFromDataFolder;
            model.patchableRaces = viewModel.patchableRaces.ToList();

            model.raceAliases.Clear();
            foreach (var x in viewModel.raceAliases)
            {
                model.raceAliases.Add(VM_raceAlias.DumpViewModelToModel(x));
            }

            model.RaceGroupings.Clear();
            foreach (var x in viewModel.RaceGroupings)
            {
                //model.RaceGroupings.Add(x.RaceGrouping);
                model.RaceGroupings.Add(VM_RaceGrouping.DumpViewModelToModel(x));
            }
        }
    }
}

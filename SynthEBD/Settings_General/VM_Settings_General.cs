using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD.Settings_General
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
            this.verboseModeNPClist = new List<FormKey>();
            this.bLoadSettingsFromDataFolder = false;
            this.patchableRaces = new List<FormKey>();
            this.raceAliases = new List<Internal_Data_Classes.ViewModels.VM_raceAlias>();

            AddRaceAlias = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.raceAliases.Add(new Internal_Data_Classes.ViewModels.VM_raceAlias(new Internal_Data_Classes.raceAlias(), new GUI_Aux.GameEnvironmentProvider().MyEnvironment))
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
        public List<FormKey> verboseModeNPClist { get; set;  }
        public bool bLoadSettingsFromDataFolder { get; set;  }

        public List<FormKey> patchableRaces { get; set;  } 

        public List<Internal_Data_Classes.ViewModels.VM_raceAlias> raceAliases { get; set;  }
        public RelayCommand AddRaceAlias { get; }

        public static void GetViewModelFromModel(VM_Settings_General viewModel, SynthEBD.Settings_General.Settings_General model)
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
            viewModel.verboseModeNPClist = model.verboseModeNPClist;
            viewModel.bLoadSettingsFromDataFolder = model.bLoadSettingsFromDataFolder;
            viewModel.patchableRaces = model.patchableRaces;
            viewModel.raceAliases = Internal_Data_Classes.ViewModels.VM_raceAlias.GetViewModelsFromModels(model.raceAliases, new GUI_Aux.GameEnvironmentProvider().MyEnvironment);
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
            model.verboseModeNPClist = viewModel.verboseModeNPClist;
            model.bLoadSettingsFromDataFolder = viewModel.bLoadSettingsFromDataFolder;
            model.patchableRaces = viewModel.patchableRaces;

            model.raceAliases.Clear();
            foreach (var x in viewModel.raceAliases)
            {
                model.raceAliases.Add(Internal_Data_Classes.ViewModels.VM_raceAlias.DumpViewModelToModel(x));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD.Settings_General
{
    public class VM_Settings_General : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        public VM_Settings_General(SynthEBD.Settings_General.Settings_General settings)
        {
            this.bShowToolTips = settings.bShowToolTips;
            this.bChangeMeshesOrTextures = settings.bChangeMeshesOrTextures;
            this.bEnableBodyGenIntegration = settings.bEnableBodyGenIntegration;
            this.bChangeHeight = settings.bChangeHeight;
            this.bEnableConsistency = settings.bEnableConsistency;
            this.bLinkNPCsWithSameName = settings.bLinkNPCsWithSameName;
            this.patchFileName = settings.patchFileName;
            this.bVerboseModeAssetsNoncompliant = settings.bVerboseModeAssetsNoncompliant;
            this.bVerboseModeAssetsAll = settings.bVerboseModeAssetsAll;
            this.verboseModeNPClist = settings.verboseModeNPClist;
            this.bLoadSettingsFromDataFolder = settings.bLoadSettingsFromDataFolder;
            this.patchableRaces = settings.patchableRaces;
            this.raceAliases = settings.raceAliases;

            AddRaceAlias = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.raceAliases.Add(new())
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

        public List<Internal_Data_Classes.raceAlias> raceAliases { get; set;  }
        public RelayCommand AddRaceAlias { get; }

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
            model.raceAliases = viewModel.raceAliases;
        }
    }
}

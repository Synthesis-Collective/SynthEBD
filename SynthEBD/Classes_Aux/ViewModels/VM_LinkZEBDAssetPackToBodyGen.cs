﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_LinkZEBDAssetPackToBodyGen : INotifyPropertyChanged
    {
        public VM_LinkZEBDAssetPackToBodyGen(BodyGenConfigs availableConfigs, Gender gender, string assetPackLabel, Window_LinkZEBDAssetPackToBodyGen associatedWindow)
        {
            this.AssociatedWindow = associatedWindow;
            this.AssociatedWindow.WindowStyle = System.Windows.WindowStyle.None; // hide title bar and close button
            this.DispString = "Attempting to upgrade " + assetPackLabel + " from zEBD Config to SynthEBD format. Which BodyGen Config should be associated with this config file?";
            switch (gender)
            {
                case Gender.female: AvailableConfigs = availableConfigs.Female; break;
                case Gender.male: AvailableConfigs = availableConfigs.Male; break;
            }

            this.SelectedConfig = null;
            if (AvailableConfigs.Count > 0) { this.SelectedConfig = this.AvailableConfigs.First(); }

            OKcommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AssociatedWindow.Close()
                );

            ClearCommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    this.SelectedConfig = null;
                    this.AssociatedWindow.Close();
                }
                );
        }

        public string DispString { get; set; }
        public HashSet<BodyGenConfig> AvailableConfigs { get; set; }
        public BodyGenConfig SelectedConfig { get; set; }
        public Window_LinkZEBDAssetPackToBodyGen AssociatedWindow { get; set; }
        public RelayCommand OKcommand { get; }
        public RelayCommand ClearCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
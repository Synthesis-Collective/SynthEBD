﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace SynthEBD
{
    public class VM_SettingsTexMesh : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public VM_SettingsTexMesh(VM_SettingsBodyGen bodygenSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, VM_Settings_General generalSettingsVM)
        {
            this.bChangeNPCTextures = true;
            this.bChangeNPCMeshes = true;
            this.bApplyToNPCsWithCustomSkins = true;
            this.bApplyToNPCsWithCustomFaces = true;
            this.bForwardArmatureFromExistingWNAMs = true;
            this.bDisplayPopupAlerts = true;
            this.bGenerateAssignmentLog = true;
            this.TrimPaths = new ObservableCollection<TrimPath>();
            this.AssetPacks = new ObservableCollection<VM_AssetPack>();

            AddTrimPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TrimPaths.Add(new TrimPath())
                );
            RemoveTrimPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.TrimPaths.Remove((TrimPath)x)
                );

            AddNewAssetPackConfigFile = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AssetPacks.Add(new VM_AssetPack(this.AssetPacks, bodygenSettingsVM, OBodyDescriptorMenu, generalSettingsVM))
                );
        }

        public bool bChangeNPCTextures { get; set; }
        public bool bChangeNPCMeshes { get; set; }
        public bool bApplyToNPCsWithCustomSkins { get; set; }
        public bool bApplyToNPCsWithCustomFaces { get; set; }
        public bool bForwardArmatureFromExistingWNAMs { get; set; }
        public bool bDisplayPopupAlerts { get; set; }
        public bool bGenerateAssignmentLog { get; set; }

        public ObservableCollection<TrimPath> TrimPaths { get; set; }

        public ObservableCollection<VM_AssetPack> AssetPacks { get; set; }

        public RelayCommand AddTrimPath { get; }
        public RelayCommand RemoveTrimPath { get; }
        public RelayCommand AddNewAssetPackConfigFile { get; }

        public static void GetViewModelFromModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            viewModel.bChangeNPCTextures = model.bChangeNPCTextures;
            viewModel.bChangeNPCMeshes = model.bChangeNPCMeshes;
            viewModel.bApplyToNPCsWithCustomSkins = model.bApplyToNPCsWithCustomSkins;
            viewModel.bApplyToNPCsWithCustomFaces = model.bApplyToNPCsWithCustomFaces;
            viewModel.bForwardArmatureFromExistingWNAMs = model.bForwardArmatureFromExistingWNAMs;
            viewModel.bDisplayPopupAlerts = model.bDisplayPopupAlerts;
            viewModel.bGenerateAssignmentLog = model.bGenerateAssignmentLog;
            viewModel.TrimPaths = new ObservableCollection<TrimPath>(model.TrimPaths);
        }

        public static void DumpViewModelToModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            model.bChangeNPCTextures = viewModel.bChangeNPCTextures;
            model.bChangeNPCMeshes = viewModel.bChangeNPCMeshes;
            model.bApplyToNPCsWithCustomSkins = viewModel.bApplyToNPCsWithCustomSkins;
            model.bApplyToNPCsWithCustomFaces = viewModel.bApplyToNPCsWithCustomFaces;
            model.bForwardArmatureFromExistingWNAMs = viewModel.bForwardArmatureFromExistingWNAMs;
            model.bDisplayPopupAlerts = viewModel.bDisplayPopupAlerts;
            model.bGenerateAssignmentLog = viewModel.bGenerateAssignmentLog;
            model.SelectedAssetPacks = viewModel.AssetPacks.Where(x => x.IsSelected).Select(x => x.groupName).ToHashSet();
        }

    }
}

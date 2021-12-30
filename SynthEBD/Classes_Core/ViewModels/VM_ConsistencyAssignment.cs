﻿using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_ConsistencyAssignment : INotifyPropertyChanged
    {
        public VM_ConsistencyAssignment()
        {
            this.SubgroupIDs = new ObservableCollection<VM_CollectionMemberString>();
            this.BodyGenMorphNames = new ObservableCollection<VM_CollectionMemberString>();

            this.AssetPackAssigned = false;
            this.HeightAssigned = false;

            this.WhenAnyValue(x => x.AssetPackName).Subscribe(x => AssetPackAssigned = AssetPackName != null && AssetPackName.Any());
            this.WhenAnyValue(x => x.Height).Subscribe(x => HeightAssigned = Height != null && Height.Any());

            DeleteAssetPackCommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => 
                {
                    this.AssetPackName = "";
                    this.SubgroupIDs.Clear();
                }
                );

            DeleteHeightCommand = new SynthEBD.RelayCommand(
               canExecute: _ => true,
               execute: x => this.Height = ""
               );;
        }

        public string AssetPackName { get; set; }
        public ObservableCollection<VM_CollectionMemberString> SubgroupIDs { get; set; }
        public ObservableCollection<VM_CollectionMemberString> BodyGenMorphNames { get; set; }
        public string Height { get; set; }
        public string DispName { get; set; }
        public FormKey NPCFormKey { get; set; }

        public RelayCommand DeleteAssetPackCommand { get; set; }
        public RelayCommand DeleteHeightCommand { get; set; }

        public bool AssetPackAssigned { get; set; }
        public bool HeightAssigned { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_ConsistencyAssignment GetViewModelFromModel(NPCAssignment model)
        {
            VM_ConsistencyAssignment viewModel = new VM_ConsistencyAssignment();
            viewModel.AssetPackName = model.AssetPackName;
            viewModel.SubgroupIDs = new ObservableCollection<VM_CollectionMemberString>();
            if (model.SubgroupIDs != null)
            {
                foreach (var id in model.SubgroupIDs)
                {
                    viewModel.SubgroupIDs.Add(new VM_CollectionMemberString(id, viewModel.SubgroupIDs));
                }
            }
            viewModel.BodyGenMorphNames = new ObservableCollection<VM_CollectionMemberString>();
            if (model.BodyGenMorphNames != null)
            {
                foreach (var morph in model.BodyGenMorphNames)
                {
                    viewModel.BodyGenMorphNames.Add(new VM_CollectionMemberString(morph, viewModel.BodyGenMorphNames));
                }
            }
            if (model.Height != null)
            {
                viewModel.Height = model.Height.ToString();
            }
            else
            {
                viewModel.Height = "";
            }
            
            viewModel.DispName = model.DispName;
            viewModel.NPCFormKey = model.NPCFormKey;
            return viewModel;
        }

        public static NPCAssignment DumpViewModelToModel(VM_ConsistencyAssignment viewModel)
        {
            NPCAssignment model = new NPCAssignment();
            model.AssetPackName = viewModel.AssetPackName;
            model.SubgroupIDs = viewModel.SubgroupIDs.Select(x => x.Content).ToList();
            if (model.SubgroupIDs.Count == 0) { model.SubgroupIDs = null; }
            model.BodyGenMorphNames = viewModel.BodyGenMorphNames.Select(x => x.Content).ToList();
            if (model.BodyGenMorphNames.Count == 0) { model.BodyGenMorphNames = null; }

            if (viewModel.Height == "")
            {
                model.Height = null;
            }
            else if (float.TryParse(viewModel.Height, out var height))
            {
                model.Height = height;
            }
            else
            {
                Logger.LogError("Error parsing consistency assignment " + viewModel.DispName + ". Cannot parse height: " + viewModel.Height);
            }
            
            model.DispName = viewModel.DispName;
            model.NPCFormKey = viewModel.NPCFormKey;
            return model;
        }
    }
}

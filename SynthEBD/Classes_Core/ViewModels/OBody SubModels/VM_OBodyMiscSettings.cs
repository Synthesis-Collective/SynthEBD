using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_OBodyMiscSettings : INotifyPropertyChanged
    {
        public VM_OBodyMiscSettings()
        {
            MaleBodySlideGroups = new ObservableCollection<VM_CollectionMemberString>();
            FemaleBodySlideGroups = new ObservableCollection<VM_CollectionMemberString>();

            AddMaleSliderGroup = new RelayCommand(
                canExecute: _ => true,
                execute: _ => MaleBodySlideGroups.Add(new VM_CollectionMemberString("", MaleBodySlideGroups))
                );

            AddFemaleSliderGroup = new RelayCommand(
                canExecute: _ => true,
                execute: _ => FemaleBodySlideGroups.Add(new VM_CollectionMemberString("", FemaleBodySlideGroups))
                );
        }

        public ObservableCollection<VM_CollectionMemberString> MaleBodySlideGroups { get; set; }
        public ObservableCollection<VM_CollectionMemberString> FemaleBodySlideGroups { get; set; }

        public RelayCommand AddMaleSliderGroup { get; set; }
        public RelayCommand AddFemaleSliderGroup { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_OBodyMiscSettings GetViewModelFromModel(Settings_OBody model)
        {
            VM_OBodyMiscSettings viewModel = new VM_OBodyMiscSettings();
            viewModel.MaleBodySlideGroups.Clear();
            foreach (var g in model.MaleSliderGroups)
            {
                viewModel.MaleBodySlideGroups.Add(new VM_CollectionMemberString(g, viewModel.MaleBodySlideGroups));
            }
            viewModel.FemaleBodySlideGroups.Clear();
            foreach (var g in model.FemaleSliderGroups)
            {
                viewModel.FemaleBodySlideGroups.Add(new VM_CollectionMemberString(g, viewModel.FemaleBodySlideGroups));
            }
            return viewModel;
        }

        public static void DumpViewModelToModel(Settings_OBody model, VM_OBodyMiscSettings viewModel)
        {
            model.MaleSliderGroups = viewModel.MaleBodySlideGroups.Select(x => x.Content).ToHashSet();
            model.FemaleSliderGroups = viewModel.FemaleBodySlideGroups.Select(x => x.Content).ToHashSet();
        }
    }
}

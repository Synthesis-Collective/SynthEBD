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
    public class VM_BodySlidesMenu : INotifyPropertyChanged
    {
        public VM_BodySlidesMenu(VM_SettingsOBody parentVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            BodySlidesMale = new ObservableCollection<VM_BodySlideSetting>();
            BodySlidesFemale = new ObservableCollection<VM_BodySlideSetting>();
            SelectedGender = Gender.female;
            CurrentlyDisplayedBodySlide = null;
            CurrentlyExistingBodySlides = new HashSet<string>();
            
            AddPreset = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => CurrentlyDisplayedBodySlides.Add(new VM_BodySlideSetting(parentVM.DescriptorUI, raceGroupingVMs, this.CurrentlyDisplayedBodySlides, parentVM))
                );

            RemovePreset = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.CurrentlyDisplayedBodySlides.Remove((VM_BodySlideSetting)x)
                );

            this.WhenAnyValue(x => x.SelectedGender).Subscribe(x =>
            {
                switch (SelectedGender)
                {
                    case Gender.female: CurrentlyDisplayedBodySlides = BodySlidesFemale; break;
                    case Gender.male: CurrentlyDisplayedBodySlides = BodySlidesMale; break;
                }
            });
        }
        public ObservableCollection<VM_BodySlideSetting> BodySlidesMale { get; set; }
        public ObservableCollection<VM_BodySlideSetting> BodySlidesFemale { get; set; }

        public ObservableCollection<VM_BodySlideSetting> CurrentlyDisplayedBodySlides { get; set; }
        public VM_BodySlideSetting CurrentlyDisplayedBodySlide { get; set; }
        public Gender SelectedGender { get; set; }

        public HashSet<string> CurrentlyExistingBodySlides { get; set; }

        public RelayCommand AddPreset { get; }
        public RelayCommand RemovePreset { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

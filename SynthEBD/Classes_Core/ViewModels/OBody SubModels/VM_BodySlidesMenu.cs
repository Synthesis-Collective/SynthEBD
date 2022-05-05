using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_BodySlidesMenu : INotifyPropertyChanged
{
    public VM_BodySlidesMenu(VM_SettingsOBody parentVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        BodySlidesMale = new ObservableCollection<VM_BodySlideSetting>();
        BodySlidesFemale = new ObservableCollection<VM_BodySlideSetting>();
        SelectedGender = Gender.Female;
        CurrentlyDisplayedBodySlide = null;
        CurrentlyExistingBodySlides = new HashSet<string>();
        ShowHidden = false;

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
                case Gender.Female: CurrentlyDisplayedBodySlides = BodySlidesFemale; break;
                case Gender.Male: CurrentlyDisplayedBodySlides = BodySlidesMale; break;
            }
        });

        this.WhenAnyValue(x => x.ShowHidden).Subscribe(x =>
        {
            TogglePresetVisibility(BodySlidesMale, ShowHidden);
            TogglePresetVisibility(BodySlidesFemale, ShowHidden);
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
    public bool ShowHidden { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    private static void TogglePresetVisibility(ObservableCollection<VM_BodySlideSetting> bodySlides, bool showHidden)
    {
        foreach (var b in bodySlides)
        {
            switch (showHidden)
            {
                case true: b.IsVisible = true; break;
                case false:
                    switch (b.IsHidden)
                    {
                        case true: b.IsVisible = false; break;
                        case false: b.IsVisible = true; break;
                    }
                    break;
            }
        }
    }
}
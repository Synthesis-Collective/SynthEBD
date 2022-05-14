using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_BodySlidesMenu : VM
{
    public VM_BodySlidesMenu(VM_SettingsOBody parentVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
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
    public ObservableCollection<VM_BodySlideSetting> BodySlidesMale { get; set; } = new();
    public ObservableCollection<VM_BodySlideSetting> BodySlidesFemale { get; set; } = new();

    public ObservableCollection<VM_BodySlideSetting> CurrentlyDisplayedBodySlides { get; set; }
    public VM_BodySlideSetting CurrentlyDisplayedBodySlide { get; set; } = null;
    public Gender SelectedGender { get; set; } = Gender.Female;

    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public RelayCommand AddPreset { get; }
    public RelayCommand RemovePreset { get; }
    public bool ShowHidden { get; set; } = false;

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
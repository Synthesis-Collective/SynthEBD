using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_BodySlidesMenu : VM
{
    public VM_BodySlidesMenu(VM_SettingsOBody parentVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodySlideSetting.Factory bodySlideFactory)
    {
        AddPreset = new RelayCommand(
            canExecute: _ => true,
            execute: _ => CurrentlyDisplayedBodySlides.Add(bodySlideFactory(parentVM.DescriptorUI, raceGroupingVMs, this.CurrentlyDisplayedBodySlides))
        );

        RemovePreset = new RelayCommand(
            canExecute: _ => true,
            execute: x => CurrentlyDisplayedBodySlides.Remove((VM_BodySlideSetting)x)
        );

        CurrentlyDisplayedBodySlides = BodySlidesFemale;
        Alphabetizer_Male = new(BodySlidesMale, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));
        Alphabetizer_Female = new(BodySlidesFemale, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));

        this.WhenAnyValue(x => x.SelectedGender).Subscribe(x =>
        {
            switch (SelectedGender)
            {
                case Gender.Female: 
                    CurrentlyDisplayedBodySlides = BodySlidesFemale;
                    Alphabetizer = Alphabetizer_Female;
                    break;
                case Gender.Male: 
                    CurrentlyDisplayedBodySlides = BodySlidesMale;
                    Alphabetizer = Alphabetizer_Male;
                    break;
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

    public VM_Alphabetizer<VM_BodySlideSetting, string> Alphabetizer_Male { get; set; }
    public VM_Alphabetizer<VM_BodySlideSetting, string> Alphabetizer_Female { get; set; }
    public VM_Alphabetizer<VM_BodySlideSetting, string> Alphabetizer { get; set; }

    public ObservableCollection<VM_BodySlideSetting> CurrentlyDisplayedBodySlides { get; set; } = new();
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
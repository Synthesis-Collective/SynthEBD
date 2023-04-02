using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_BodySlidesMenu : VM
{
    public delegate VM_BodySlidesMenu Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    public VM_BodySlidesMenu(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodySlidePlaceHolder.Factory placeHolderFactory, VM_BodySlideSetting.Factory bodySlideFactory, VM_BodySlideExchange.Factory exchangeFactory)
    {
        AddPreset = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newPlaceHolder = placeHolderFactory(new BodySlideSetting(), CurrentlyDisplayedBodySlides);
                CurrentlyDisplayedBodySlides.Add(newPlaceHolder);
                var newPreset = bodySlideFactory(newPlaceHolder, raceGroupingVMs);
                newPreset.UnlockReference();
                CurrentlyDisplayedBodySlide = newPreset;
            }
        );

        RemovePreset = new RelayCommand(
            canExecute: _ => true,
            execute: x => CurrentlyDisplayedBodySlides.Remove((VM_BodySlidePlaceHolder)x)
        );

        RemovePresetsMissing = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides that are not in your current game environment?"))
                {
                    for (int i = 0; i < CurrentlyDisplayedBodySlides.Count; i++)
                    {
                        if (CurrentlyDisplayedBodySlides[i].BorderColor == VM_BodySlideSetting.BorderColorMissing)
                        {
                            CurrentlyDisplayedBodySlides.RemoveAt(i);
                            i--;
                        }
                    }
                }
            });

        RemovePresetsUnannotated = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides that are not currently annotated with Body Shape Descriptors?"))
                {
                    for (int i = 0; i < CurrentlyDisplayedBodySlides.Count; i++)
                    {
                        if (!CurrentlyDisplayedBodySlides[i].AssociatedModel.BodyShapeDescriptors.Any())
                        {
                            CurrentlyDisplayedBodySlides.RemoveAt(i);
                            i--;
                        }
                    }
                }
            });

        RemovePresetsAll = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides?"))
                {
                    for (int i = 0; i < CurrentlyDisplayedBodySlides.Count; i++)
                    {
                        if (!CurrentlyDisplayedBodySlides[i].IsHidden)
                        {
                            CurrentlyDisplayedBodySlides.RemoveAt(i);
                            i--;
                        }
                    }
                }
            });

        ImportAnnotations = new RelayCommand(
            canExecute: _ => true,
            execute: x => {
                if (CurrentlyDisplayedBodySlide != null)
                {
                    CurrentlyDisplayedBodySlide.AssociatedPlaceHolder.AssociatedModel = CurrentlyDisplayedBodySlide.DumpToModel();
                }
                var exchangeWindow = new Window_BodySlideExchange();
                var exchange = exchangeFactory(ExchangeMode.Import, exchangeWindow);
                exchangeWindow.DataContext = exchange;
                exchangeWindow.Show();
            });

        ExportAnnotations = new RelayCommand(
            canExecute: _ => true,
            execute: x => {
                if (CurrentlyDisplayedBodySlide != null)
                {
                    CurrentlyDisplayedBodySlide.AssociatedPlaceHolder.AssociatedModel = CurrentlyDisplayedBodySlide.DumpToModel();
                }
                var exchangeWindow = new Window_BodySlideExchange();
                var exchange = exchangeFactory(ExchangeMode.Export, exchangeWindow);
                exchangeWindow.DataContext = exchange;
                exchangeWindow.Show();
            });

        CurrentlyDisplayedBodySlides = BodySlidesFemale;
        Alphabetizer_Male = new(BodySlidesMale, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));
        Alphabetizer_Female = new(BodySlidesFemale, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpToModel();
             }

             if (t.Current != null)
             {
                 CurrentlyDisplayedBodySlide = bodySlideFactory(t.Current, raceGroupingVMs);
                 CurrentlyDisplayedBodySlide.CopyInViewModelFromModel(t.Current.AssociatedModel);
             }
         }).DisposeWith(this);

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
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ShowHidden).Subscribe(x =>
        {
            TogglePresetVisibility(BodySlidesMale, ShowHidden);
            TogglePresetVisibility(BodySlidesFemale, ShowHidden);
        }).DisposeWith(this);
    }
    public ObservableCollection<VM_BodySlidePlaceHolder> BodySlidesMale { get; set; } = new();
    public ObservableCollection<VM_BodySlidePlaceHolder> BodySlidesFemale { get; set; } = new();

    public VM_Alphabetizer<VM_BodySlidePlaceHolder, string> Alphabetizer_Male { get; set; }
    public VM_Alphabetizer<VM_BodySlidePlaceHolder, string> Alphabetizer_Female { get; set; }
    public VM_Alphabetizer<VM_BodySlidePlaceHolder, string> Alphabetizer { get; set; }

    public ObservableCollection<VM_BodySlidePlaceHolder> CurrentlyDisplayedBodySlides { get; set; } = new();
    public VM_BodySlideSetting CurrentlyDisplayedBodySlide { get; set; } = null;
    public VM_BodySlidePlaceHolder SelectedPlaceHolder { get; set; }
    public Gender SelectedGender { get; set; } = Gender.Female;

    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public RelayCommand AddPreset { get; }
    public RelayCommand RemovePreset { get; }

    public RelayCommand RemovePresetsUnannotated { get; }
    public RelayCommand RemovePresetsMissing { get; }
    public RelayCommand RemovePresetsAll { get; }
    public RelayCommand ImportAnnotations { get; }
    public RelayCommand ExportAnnotations { get; }
    public bool ShowHidden { get; set; } = false;

    private static void TogglePresetVisibility(ObservableCollection<VM_BodySlidePlaceHolder> bodySlides, bool showHidden)
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
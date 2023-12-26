using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;
using System.Reactive.Linq;
using DynamicData.Binding;

namespace SynthEBD;

public class VM_BodySlidesMenu : VM
{
    public delegate VM_BodySlidesMenu Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _filterFactory;
    public VM_BodySlidesMenu(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodySlidePlaceHolder.Factory placeHolderFactory, VM_BodySlideSetting.Factory bodySlideFactory, VM_BodySlideExchange.Factory exchangeFactory, Func<VM_SettingsOBody> oBodyVM, VM_BodyShapeDescriptorSelectionMenu.Factory filterFactory)
    {
        _filterFactory = filterFactory;

        AddPreset = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
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
                if (MessageWindow.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides that are not in your current game environment?"))
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
                if (MessageWindow.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides that are not currently annotated with Body Shape Descriptors?"))
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
                if (MessageWindow.DisplayNotificationYesNo("", "Are you sure you want to remove all displayed BodySlides?"))
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
            execute: x =>
            {
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
            execute: x =>
            {
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
         .Subscribe(t =>
         {
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
            TogglePresetVisibility();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedSliderGroup).Subscribe(x =>
        {
            TogglePresetVisibility();
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
    public ObservableCollection<string> AvailableSliderGroups { get; set; } = new();
    public string SelectedSliderGroup { get; set; }
    public const string SliderGroupSelectionAll = "ALL";

    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public RelayCommand AddPreset { get; }
    public RelayCommand RemovePreset { get; }

    public RelayCommand RemovePresetsUnannotated { get; }
    public RelayCommand RemovePresetsMissing { get; }
    public RelayCommand RemovePresetsAll { get; }
    public RelayCommand ImportAnnotations { get; }
    public RelayCommand ExportAnnotations { get; }
    public bool ShowHidden { get; set; } = false;
    public VM_BodyShapeDescriptorSelectionMenu DescriptorFilter { get; set; }
    public string FilterCaption { get; set; } = _defaultFilterCaption;
    private static string _defaultFilterCaption = "Descriptor Filter";
    private Dictionary<string, HashSet<string>> _selectedDescriptors = new();

    private void TogglePresetVisibility()
    {
        var bodySlides = BodySlidesMale.And(BodySlidesFemale).ToList();
        foreach (var b in bodySlides)
        {
            if (SelectedSliderGroup != SliderGroupSelectionAll && b.AssociatedModel.SliderGroup != SelectedSliderGroup)
            {
                b.IsVisible = false;
                continue;
            }

            if (_selectedDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(_selectedDescriptors, b.AssociatedModel.BodyShapeDescriptors, DescriptorFilter.MatchMode, out _))
            {
                b.IsVisible = false;
                continue;
            }

            switch (ShowHidden)
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

    public void InitializeDescriptorFilter(VM_SettingsOBody oBodyVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        DescriptorFilter = _filterFactory(oBodyVM.DescriptorUI, raceGroupingVMs, oBodyVM, true, DescriptorMatchMode.All, false);

        this.WhenAnyValue(x => x.DescriptorFilter.Header).Subscribe(caption =>
        {
            if (caption.IsNullOrEmpty())
            {
                FilterCaption = _defaultFilterCaption;
                _selectedDescriptors.Clear();
            }
            else
            {
                FilterCaption = String.Empty;
                var selectedDescriptorSet = DescriptorFilter.DumpToHashSet();
                _selectedDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(selectedDescriptorSet);
            }

            TogglePresetVisibility();
        }).DisposeWith(this);
    }
}
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ReactiveUI;
using Noggog;
using System.Reactive.Linq;

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
                        if (!CurrentlyDisplayedBodySlides[i].AssociatedModel.HasAnyDescriptors())
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
             int? carryWeight = null;
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 // Remember the weight the user had selected so we can restore it on the
                 // new preset (if it exposes that weight slot) and avoid a spurious
                 // preview-NPC reload back to weight 0.
                 carryWeight = t.Previous.AssociatedViewModel.SelectedWeightSlot?.Weight;
                 t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpToModel();
                 // Release the previous preset's VM_CharacterViewer (GL context + caches)
                 // rather than orphaning it on the placeholder across selection churn.
                 t.Previous.AssociatedViewModel.Dispose();
                 t.Previous.AssociatedViewModel = null;
             }

             if (t.Current != null)
             {
                 CurrentlyDisplayedBodySlide = bodySlideFactory(t.Current, raceGroupingVMs);
                 CurrentlyDisplayedBodySlide.CopyInViewModelFromModel(t.Current.AssociatedModel, carryWeight);
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
            RefreshAvailableBodyTypesForGender();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ShowHidden).Subscribe(x =>
        {
            TogglePresetVisibility();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedBodyType).Subscribe(x =>
        {
            TogglePresetVisibility();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.PresetFilterText).Subscribe(x =>
        {
            TogglePresetVisibility();
        }).DisposeWith(this);

        // When the master list gets (re)populated (typically by VM_BodySlideAnnotator after
        // all presets finish loading), refresh the gender-scoped dropdown. Also watch the
        // per-gender collections so that adds/removes through the UI stay in sync.
        AvailableSliderGroups.CollectionChanged += (_, _) => RefreshAvailableBodyTypesForGender();
        BodySlidesFemale.CollectionChanged += OnPresetCollectionChanged;
        BodySlidesMale.CollectionChanged += OnPresetCollectionChanged;
    }

    private void OnPresetCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Only refresh if the change is for the currently displayed gender; otherwise it's
        // guaranteed not to affect what the user sees in the dropdown.
        bool femaleChanged = ReferenceEquals(sender, BodySlidesFemale);
        bool maleChanged = ReferenceEquals(sender, BodySlidesMale);
        if ((femaleChanged && SelectedGender == Gender.Female) ||
            (maleChanged && SelectedGender == Gender.Male))
        {
            RefreshAvailableBodyTypesForGender();
        }
    }

    /// <summary>
    /// Rebuilds <see cref="AvailableBodyTypesForGender"/> from the SliderGroup values
    /// actually present on the currently displayed preset list (for the selected gender).
    /// SliderGroup on a preset corresponds 1:1 with body-type Name in the registry.
    /// Always includes the "ALL" sentinel. If the user's current SelectedBodyType falls
    /// out of the new set (e.g. they switched to Male and "CBBE" no longer applies), it
    /// snaps back to "ALL" to avoid showing an empty list.
    /// </summary>
    private void RefreshAvailableBodyTypesForGender()
    {
        var source = SelectedGender == Gender.Male ? BodySlidesMale : BodySlidesFemale;
        var groups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ph in source)
        {
            var g = ph?.AssociatedModel?.SliderGroup;
            if (!string.IsNullOrWhiteSpace(g)) groups.Add(g);
        }

        AvailableBodyTypesForGender.Clear();
        AvailableBodyTypesForGender.Add(BodyTypeSelectionAll);
        foreach (var g in groups) AvailableBodyTypesForGender.Add(g);

        if (!string.Equals(SelectedBodyType, BodyTypeSelectionAll, StringComparison.OrdinalIgnoreCase) &&
            !AvailableBodyTypesForGender.Contains(SelectedBodyType))
        {
            SelectedBodyType = BodyTypeSelectionAll;
        }
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
    // Gender-scoped view of AvailableSliderGroups, bound by the XAML Body Type ComboBox.
    // Rebuilt whenever SelectedGender changes or the per-gender preset lists mutate.
    public ObservableCollection<string> AvailableBodyTypesForGender { get; set; } = new() { BodyTypeSelectionAll };
    public string SelectedBodyType { get; set; } = BodyTypeSelectionAll;
    public const string BodyTypeSelectionAll = "ALL";

    /// <summary>Substring filter applied to preset Label in TogglePresetVisibility.
    /// Case-insensitive; empty string matches all.</summary>
    public string PresetFilterText { get; set; } = "";

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
    private VM_BodySlidePlaceHolder _stashedPlaceHolder;

    private void TogglePresetVisibility()
    {
        var bodySlides = BodySlidesMale.And(BodySlidesFemale).ToList();
        string filter = PresetFilterText ?? "";
        foreach (var b in bodySlides)
        {
            if (SelectedBodyType != BodyTypeSelectionAll && b.AssociatedModel.SliderGroup != SelectedBodyType)
            {
                b.IsVisible = false;
                continue;
            }

            if (filter.Length > 0 && (b.Label == null ||
                b.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
            {
                b.IsVisible = false;
                continue;
            }

            if (_selectedDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(_selectedDescriptors, b.AssociatedModel.GetDescriptorUnion(), DescriptorFilter.MatchMode, out _))
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

    public void StashAndNullDisplayedBodySlide()
    {
        if (SelectedPlaceHolder != null)
        {
            _stashedPlaceHolder = SelectedPlaceHolder;
            SelectedPlaceHolder = null;
        }
    }

    public void RestoreStashedBodySlide()
    {
        if (_stashedPlaceHolder != null)
        {
            SelectedPlaceHolder = _stashedPlaceHolder;
        }
    }
}
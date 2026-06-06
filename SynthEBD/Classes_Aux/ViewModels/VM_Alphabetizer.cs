using DynamicData.Binding;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Noggog;

namespace SynthEBD
{
    /// <summary>
    /// Reusable view model for an alphabetize/undo toggle button over an observable collection: sorts by a
    /// key selector (ascending ↔ descending), labels the button "AZ"/"ZA" from the current sort state, and
    /// can revert to the pre-sort order.
    /// </summary>
    /// <typeparam name="TSource">Element type of the collection.</typeparam>
    /// <typeparam name="TKey">Sort key type.</typeparam>
    public class VM_Alphabetizer<TSource, TKey> : VM
    {
        public ObservableCollection<TSource> SubscribedCollection { get; set; } = new();
        public Func<TSource, TKey> KeySelector { get; set; }
        public string DisplayText { get; set; } = "AZ";
        public RelayCommand AlphabetizeCommand { get; set; }
        public RelayCommand UndoCommand { get; set; }
        private SortState State { get; set; } = SortState.Unsorted;
        public SolidColorBrush ButtonColor { get; set; } = new(Colors.White);
        private ObservableCollection<TSource> _originalOrder { get; set; } = new();
        public bool WasSorted { get; set; } = false;

        /// <summary>Creates the alphabetizer over a collection, wiring sort/undo commands and tracking the collection's sort state (throttled).</summary>
        /// <param name="subscribedCollection">The collection to sort.</param>
        /// <param name="keySelector">Selects the sort key from an element.</param>
        /// <param name="buttonColor">Button color.</param>
        public VM_Alphabetizer(ObservableCollection<TSource> subscribedCollection, Func<TSource, TKey> keySelector, SolidColorBrush buttonColor)
        {
            SubscribedCollection = subscribedCollection;
            KeySelector = keySelector;
            ButtonColor = buttonColor;

            AlphabetizeCommand = new RelayCommand(
                    canExecute: _ => true,
                    execute: _ => Sort()
                    );

            UndoCommand = new RelayCommand(
                    canExecute: _ => true,
                    execute: _ => Revert()
                    );

            SubscribedCollection.ToObservableChangeSet().Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler).Subscribe(x => GetSortState()).DisposeWith(this);

            this.WhenAnyValue(x => x.State).Subscribe(x => {
                if (State == SortState.Unsorted || State == SortState.Reversed) { DisplayText = "AZ"; }
                else { DisplayText = "ZA"; }
            }).DisposeWith(this); ;
        }

        /// <summary>Whether the collection is currently sorted forward, reversed, or in neither order.</summary>
        private enum SortState
        {
            /// <summary>Sorted ascending by the key.</summary>
            Forward,
            /// <summary>Sorted descending by the key.</summary>
            Reversed,
            /// <summary>Not sorted in either direction.</summary>
            Unsorted
        }

        /// <summary>Sorts the collection, toggling ascending/descending from the current state and snapshotting the original order on first sort.</summary>
        private void Sort()
        {
            if (SubscribedCollection is null) { return; }
            if (!WasSorted)
            {
                _originalOrder = new ObservableCollection<TSource>(SubscribedCollection);
            }

            if (State == SortState.Unsorted || State == SortState.Reversed)
            {
                SubscribedCollection.Sort(KeySelector, false);
            }
            else
            {
                SubscribedCollection.Sort(KeySelector, true);
            }
            WasSorted = true;
        }

        /// <summary>Restores the collection to its pre-sort order.</summary>
        private void Revert()
        {
            if (SubscribedCollection is null) { return; }
            if (WasSorted)
            {
                SubscribedCollection.Clear();
                foreach (var item in _originalOrder)
                {
                    SubscribedCollection.Add(item);
                }
                WasSorted = false;
            }
        }

        /// <summary>Recomputes <see cref="State"/> by testing whether the collection is currently sorted forward, reversed, or neither.</summary>
        private void GetSortState()
        {
            if (SubscribedCollection is null || SubscribedCollection.Where(x => x == null).Any()) { return; }
            if (SubscribedCollection.IsSorted(KeySelector, false))
            {
                State = SortState.Forward;
            }
            else if (SubscribedCollection.IsSorted(KeySelector, true))
            {
                State = SortState.Reversed;
            }
            else
            {
                State = SortState.Unsorted;
            }
        }
    }
}

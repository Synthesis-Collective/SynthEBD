using System;
using System.Collections;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace SynthEBD
{
    /// <summary>
    /// Code-behind for the Label by Sliders preset browser (sortable preset list + selected
    /// preset's slider readout, side by side in the lower middle pane).
    /// </summary>
    public partial class UC_SliderAnnotatorPresetBrowser : UserControl
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public UC_SliderAnnotatorPresetBrowser()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Custom sort for the preset grid so presets that lack the picked slider (blank Low/High/Interp
        /// cells) always sort to the <b>bottom</b>, regardless of ascending/descending. WPF's default
        /// nullable sort clumps those blanks at the <i>top</i> on an ascending click, burying the actual
        /// sorted values — the same annoyance the measurement table's Sorting handler fixes. We take over
        /// sorting for every column (a ListCollectionView.CustomSort suppresses SortDescriptions once set,
        /// so all columns must route through here) and break value ties by preset name.
        /// </summary>
        private void OnPresetDataGridSorting(object sender, DataGridSortingEventArgs e)
        {
            if (sender is not DataGrid grid || e.Column == null) return;
            if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view) return;

            string path = e.Column.SortMemberPath ?? "";
            bool valueColumn = path is "Low" or "High" or "Interpolated";

            // Value columns start Descending on a fresh click (highest first, the useful default when
            // eyeballing where a slider peaks); Label starts Ascending (A-Z). Re-clicking flips either.
            ListSortDirection direction;
            if (valueColumn)
            {
                direction = e.Column.SortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
            }
            else
            {
                direction = e.Column.SortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            e.Handled = true;

            foreach (var c in grid.Columns) c.SortDirection = null;
            e.Column.SortDirection = direction;

            view.CustomSort = new PresetRowComparer(path, direction);
        }

        /// <summary>Nulls-last comparer for <see cref="VM_AnnotatorPresetRow"/>: missing slider values
        /// always rank after present ones in both directions; equal/absent values tie-break by label.</summary>
        private sealed class PresetRowComparer : IComparer
        {
            private readonly string _path;
            private readonly ListSortDirection _dir;

            public PresetRowComparer(string path, ListSortDirection dir)
            {
                _path = path;
                _dir = dir;
            }

            public int Compare(object? x, object? y)
            {
                if (x is not VM_AnnotatorPresetRow a || y is not VM_AnnotatorPresetRow b) return 0;

                int cmp;
                switch (_path)
                {
                    case "Low": cmp = CompareNullable(a.Low, b.Low); break;
                    case "High": cmp = CompareNullable(a.High, b.High); break;
                    case "Interpolated": cmp = CompareNullable(a.Interpolated, b.Interpolated); break;
                    default:
                        // Label (or any other path): plain directional name sort, no null handling.
                        return ApplyDirection(string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
                }

                // Value columns: tie (including two blanks) breaks by label ascending for a stable,
                // readable order within the null group and within equal-value runs.
                if (cmp == 0) return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                return cmp;
            }

            /// <summary>Compares two nullable values keeping nulls last in BOTH directions: the null
            /// verdict is decided before the direction flip, so a blank never rises to the top.</summary>
            private int CompareNullable<T>(T? a, T? b) where T : struct, IComparable<T>
            {
                if (!a.HasValue && !b.HasValue) return 0;
                if (!a.HasValue) return 1;  // a blank -> after b
                if (!b.HasValue) return -1; // b blank -> a before
                return ApplyDirection(a.Value.CompareTo(b.Value));
            }

            private int ApplyDirection(int cmp) => _dir == ListSortDirection.Ascending ? cmp : -cmp;
        }
    }
}

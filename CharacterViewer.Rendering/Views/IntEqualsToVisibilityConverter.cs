using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CharacterViewer.Rendering.Views;

/// <summary>
/// <see cref="Visibility.Visible"/> when the bound int equals the converter
/// parameter (parsed as int), otherwise <see cref="Visibility.Collapsed"/>.
/// Used by <see cref="UC_CharacterViewerLightingPanel"/>'s per-light editor
/// to show exactly one of Key/Fill/Rim editors at a time based on
/// <see cref="VM_CharacterViewer.SelectedLightIndex"/>.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int v = value is int i ? i : System.Convert.ToInt32(value);
        int p = parameter is int pi ? pi : int.Parse((string)parameter, culture);
        return v == p ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null!;
}

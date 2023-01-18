using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace SynthEBD;

public class BodySlideVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is BodyShapeSelectionMode)
        {
            visibility = (BodyShapeSelectionMode)value == BodyShapeSelectionMode.BodySlide;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}
public class BodyGenVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is BodyShapeSelectionMode)
        {
            visibility = (BodyShapeSelectionMode)value == BodyShapeSelectionMode.BodyGen;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}

//https://stackoverflow.com/questions/2502178/hide-grid-row-in-wpf
// visibility hidden if binding is FALSE
[ValueConversion(typeof(bool), typeof(GridLength))]
public class BoolToGridRowHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return ((bool)value == true) ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {    // Don't need any convert back
        return null;
    }
}
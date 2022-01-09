using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
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
}

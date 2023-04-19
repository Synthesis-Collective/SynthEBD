using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SynthEBD
{
    public class MaxHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double fracHeight = 1;

            string aParam = parameter as string;
            if (aParam != null && double.TryParse(aParam, out double d))
            {
                fracHeight = d;
            }
            else if (aParam != null && int.TryParse(aParam, out int i))
            {
                fracHeight = System.Convert.ToDouble(i);
            }
            else if (aParam != null)
            {
                //throw new Exception("MaxHeightConverter expects an argument of type double"); Fail silently
            }

            //if ((fracHeight <= 0.0) || (fracHeight > 1.0))
            //    throw new Exception("MaxHeightConverter expects parameter in the range (0,1)");

            return ((double)value * fracHeight);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

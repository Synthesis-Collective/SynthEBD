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
            double pctHeight = -1;

            string aParam = parameter as string;
            if (aParam != null && double.TryParse(aParam, out double d))
            {
                pctHeight = d;
            }
            else if (aParam != null)
            {
                throw new Exception("MaxHeightConverter expects an argument of type double");
            }
            else if (aParam == null)
            {
                pctHeight = (double)parameter;
            }

            if ((pctHeight <= 0.0) || (pctHeight > 100.0))
                throw new Exception("MaxHeightConverter expects parameter in the range (0,100]");

            return ((double)value * pctHeight);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

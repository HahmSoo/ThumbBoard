using System;
using System.Globalization;
using System.Windows.Data;

namespace ArchiveThumbViewer.Utils
{
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double p = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
                return v * p;
            }
            catch { return 0.0; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}

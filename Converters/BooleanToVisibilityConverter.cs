using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = false;
            if (value is bool b)
            {
                isVisible = b;
            }

            if (Invert) isVisible = !isVisible;

            if (parameter is string paramStr && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool isVisible = (v == Visibility.Visible);
                if (Invert) isVisible = !isVisible;
                return isVisible;
            }
            return false;
        }
    }
}

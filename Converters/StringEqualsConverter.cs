using System;
using System.Globalization;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Compares a string value to a parameter string.
    /// Returns true if they match (case-insensitive), false otherwise.
    /// </summary>
    public class StringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

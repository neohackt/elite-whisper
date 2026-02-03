using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Compares a string value to a parameter string.
    /// Returns Visible if they match (case-insensitive), Collapsed otherwise.
    /// </summary>
    public class StringEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            bool matches = string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

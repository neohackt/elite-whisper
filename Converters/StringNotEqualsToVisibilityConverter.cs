using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Compares a string value to a parameter string.
    /// Returns Collapsed if they match (case-insensitive), Visible otherwise.
    /// Inverse of StringEqualsToVisibilityConverter.
    /// </summary>
    public class StringNotEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Visible;

            bool matches = string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            return matches ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

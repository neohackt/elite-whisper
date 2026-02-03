using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Multi-value converter that compares two string values and returns Visibility.
    /// Values[0] = ActiveModelId from ViewModel
    /// Values[1] = Model Id from card
    /// Returns Visible if they match (case-insensitive), Collapsed otherwise.
    /// </summary>
    public class StringEqualsToVisibilityMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return Visibility.Collapsed;

            if (values[0] == null || values[1] == null)
                return Visibility.Collapsed;

            bool matches = string.Equals(values[0].ToString(), values[1].ToString(), StringComparison.OrdinalIgnoreCase);
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

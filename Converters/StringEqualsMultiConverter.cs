using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Multi-value converter that compares two string values.
    /// Values[0] = ActiveModelId from ViewModel
    /// Values[1] = Model Id from card
    /// Returns true if they match (case-insensitive), false otherwise.
    /// </summary>
    public class StringEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            if (values[0] == null || values[1] == null)
                return false;

            return string.Equals(values[0].ToString(), values[1].ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

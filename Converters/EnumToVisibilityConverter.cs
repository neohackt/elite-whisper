using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            string? checkValue = value.ToString();
            string? targetValue = parameter.ToString();
            
            if (checkValue == null || targetValue == null) return Visibility.Collapsed;
            
            // Should match exact string name of enum or property value
            return checkValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

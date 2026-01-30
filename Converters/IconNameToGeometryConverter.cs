using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EliteWhisper.Converters
{
    public class IconNameToGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string resourceKey && !string.IsNullOrEmpty(resourceKey))
            {
                try 
                {
                    var resource = Application.Current.FindResource(resourceKey);
                    return resource as Geometry ?? DependencyProperty.UnsetValue;
                }
                catch
                {
                    return DependencyProperty.UnsetValue;
                }
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

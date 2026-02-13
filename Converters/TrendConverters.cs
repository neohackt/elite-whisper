using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EliteWhisper.Converters
{
    public class TrendToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                if (d > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Green
                if (d < 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TrendToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                if (d > 0) return $"▲ {d:F0}%";
                if (d < 0) return $"▼ {Math.Abs(d):F0}%";
            }
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

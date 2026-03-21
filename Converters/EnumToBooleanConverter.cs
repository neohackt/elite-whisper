using System;
using System.Globalization;
using System.Windows.Data;

namespace EliteWhisper.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string? checkValue = value.ToString();
            string? targetValue = parameter.ToString();
            
            if (checkValue == null || targetValue == null) return false;

            return checkValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Binding.DoNothing;

            if ((bool)value)
            {
                try
                {
                    string? paramStr = parameter.ToString();
                    if (paramStr == null) return Binding.DoNothing;
                    return Enum.Parse(targetType, paramStr);
                }
                catch
                {
                    return Binding.DoNothing;
                }
            }

            return Binding.DoNothing;
        }
    }
}

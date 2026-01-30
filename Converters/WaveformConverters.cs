using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace EliteWhisper.Converters
{
    /// <summary>
    /// Converts an amplitude value (0-1) to a bar height for waveform visualization.
    /// The bar grows symmetrically from center, so height = amplitude * maxHeight.
    /// </summary>
    public class AmplitudeToHeightConverter : IValueConverter
    {
        public double MaxHeight { get; set; } = 40.0;
        public double MinHeight { get; set; } = 4.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double amplitude)
            {
                // Clamp amplitude to 0-1
                amplitude = Math.Clamp(amplitude, 0.0, 1.0);
                
                // Calculate height: min + (max - min) * amplitude
                return MinHeight + (MaxHeight - MinHeight) * amplitude;
            }
            return MinHeight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// MultiValueConverter that extracts amplitude at a specific index from an array.
    /// Used as: {MultiBinding Converter={StaticResource ArrayIndexConverter}}
    ///   - First binding: double[] ExpandedAmplitudes
    ///   - Second binding: Index (from Tag or ConverterParameter)
    /// </summary>
    public class ArrayIndexToAmplitudeConverter : IMultiValueConverter
    {
        public double MaxHeight { get; set; } = 40.0;
        public double MinHeight { get; set; } = 4.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && 
                values[0] is double[] amplitudes && 
                values[1] is int index &&
                index >= 0 && index < amplitudes.Length)
            {
                double amplitude = Math.Clamp(amplitudes[index], 0.0, 1.0);
                return MinHeight + (MaxHeight - MinHeight) * amplitude;
            }
            return MinHeight;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

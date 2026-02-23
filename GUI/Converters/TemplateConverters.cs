using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RauskuClaw.GUI.Converters
{
    /// <summary>
    /// Converts bool to border color (selected vs unselected).
    /// </summary>
    public class BoolToBorderColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return new SolidColorBrush(Color.FromRgb(77, 163, 255)); // #4DA3FF
            return new SolidColorBrush(Color.FromRgb(42, 50, 64)); // #2A3240
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts bool to fill brush for selection indicator.
    /// </summary>
    public class BoolToFillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return new SolidColorBrush(Color.FromRgb(77, 163, 255)); // #4DA3FF
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}

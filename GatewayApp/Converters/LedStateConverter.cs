using GatewayApp.Models;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GatewayApp.Converters;

public sealed class LedStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is LedState state
            ? state switch
            {
                LedState.On => new SolidColorBrush(Color.FromRgb(0x3e, 0xdc, 0x3e)),
                LedState.ForceOn => new SolidColorBrush(Color.FromRgb(0xff, 0x8c, 0x00)),
                LedState.ForceOff => new SolidColorBrush(Color.FromRgb(0x5a, 0x3a, 0x00)),
                LedState.Error => new SolidColorBrush(Color.FromRgb(0xdd, 0x44, 0x44)),
                _ => new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
            }
            : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class LedStrokeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is LedState.ForceOn or LedState.ForceOff
            ? new SolidColorBrush(Color.FromRgb(0xff, 0x8c, 0x00))
            : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class LedStrokeThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is LedState.ForceOn or LedState.ForceOff ? 1.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

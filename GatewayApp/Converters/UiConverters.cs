using GatewayApp.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GatewayApp.Converters;

public sealed class EntryValueBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isForceApplied)
        {
            return isForceApplied
                ? new SolidColorBrush(Color.FromRgb(0xff, 0x8c, 0x00))
                : new SolidColorBrush(Color.FromRgb(0x4e, 0xe0, 0x72));
        }

        if (value is not MappingEntry entry)
        {
            return new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        }

        if (entry.IsForceApplied)
        {
            return new SolidColorBrush(Color.FromRgb(0xff, 0x8c, 0x00));
        }

        if (entry.IsRegister)
        {
            return new SolidColorBrush(Color.FromRgb(0x4e, 0xe0, 0x72));
        }

        return entry.EffectiveRawValue != 0
            ? new SolidColorBrush(Color.FromRgb(0x4e, 0xe0, 0x72))
            : new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class StringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class DisplayTypeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            DisplayType.Bool => "Bool",
            DisplayType.Int16 => "Int",
            DisplayType.ScaledReal => "Float",
            _ => string.Empty,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

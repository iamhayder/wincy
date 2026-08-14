using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Wincy.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>When true, false maps to Collapsed and true to Visible — inverted otherwise.</summary>
    public bool Invert { get; set; }

    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is not null && value is not string { Length: 0 };
        if (Invert)
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Paints a row: accent fill when selected, transparent otherwise.</summary>
public sealed class SelectionBrushConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Length > 0 && values[0] is bool b && b;
        var accent = values.Length > 1 ? values[1] as Brush : null;

        if (!selected)
        {
            return Brushes.Transparent;
        }

        return accent ?? Brushes.SteelBlue;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Row text colour: the accent's contrast colour when selected, normal otherwise.</summary>
public sealed class SelectionForegroundConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Length > 0 && values[0] is bool b && b;
        var onAccent = values.Length > 1 ? values[1] as Brush : null;
        var normal = values.Length > 2 ? values[2] as Brush : null;

        return (selected ? onAccent : normal) ?? Brushes.Black;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Binds a radio button or similar to one value of an enum.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Formats a byte count for the storage pane.</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes <= 0)
        {
            return "—";
        }

        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {Units[unit]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

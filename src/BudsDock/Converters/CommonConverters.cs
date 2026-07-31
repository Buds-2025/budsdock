using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BudsDock.Models;
using BudsDock.Services;

namespace BudsDock.Converters;

public sealed class DockOrientationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DockOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Orientation.Horizontal ? DockOrientation.Horizontal : DockOrientation.Vertical;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (Invert)
        {
            visible = !visible;
        }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible ^ Invert;
}

public sealed class IconImageConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.FirstOrDefault() is DockItem item
            ? ((App)Application.Current).IconService.GetImage(item)
            : DependencyProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IconGlowColorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.FirstOrDefault() is DockItem item
            ? ((App)Application.Current).IconService.GetGlowColor(item)
            : Color.FromRgb(112, 142, 255);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DisplayNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.FirstOrDefault() is DockItem item
            ? ((App)Application.Current).LocalizationService.DisplayName(item)
            : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class HalfValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double number ? number / 2 : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double number ? number * 2 : 0d;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (Invert)
        {
            visible = !visible;
        }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class SpacingToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double number ? new Thickness(number / 2) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Looks up a format string from the merged localization dictionary via the
/// ConverterParameter (the resource key), then string-formats the bound value
/// into it.  Lets XAML use {0:P0}-style placeholders without baking the
/// surrounding English text into markup.
/// </summary>
public sealed class FormatStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string key || string.IsNullOrEmpty(key))
        {
            return value?.ToString() ?? string.Empty;
        }

        var fmt = ((App)Application.Current).LocalizationService.Translate(key);
        if (fmt == key)
        {
            return value?.ToString() ?? string.Empty;
        }

        return string.Format(culture, fmt, value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the bound DockPlacement value equals the ConverterParameter
/// enum value.  Used by the placement tile buttons so the active selection
/// stays lit as the user drags the Dock to a custom position.
/// </summary>
public sealed class PlacementEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DockPlacement placement && parameter is DockPlacement target && placement == target;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GlowOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 3 && values[0] is true && values[1] is double intensity && values[2] is true
            ? Math.Clamp(intensity * 0.62, 0, 0.55)
            : 0d;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class AmbientGlowOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 3 && values[0] is true && values[1] is double intensity && values[2] is true
            ? Math.Clamp(intensity * 1.15, 0, 0.78)
            : 0d;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IconSizeToGlowBlurConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double size ? Math.Clamp(size * 0.50, 16.0, 52.0) : 27.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IconSizeToGlowSafeMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var size = values.Length > 0 && values[0] is double iconSize ? iconSize : 54d;
        var hoverScale = values.Length > 1 && values[1] is double hover ? hover : 1.5d;
        var dockScale = values.Length > 2 && values[2] is double dock ? dock : 1d;
        var blurRadius = Math.Clamp(size * 0.50, 16d, 52d);
        var ambientExtent = size * 0.38;
        var effectExtent = Math.Max(blurRadius, ambientExtent);
        var margin = Math.Ceiling((effectExtent * Math.Max(1d, hoverScale) * Math.Max(1d, dockScale)) + 6d);
        return new Thickness(margin);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IconSizeToReflectionHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double size ? Math.Clamp(size * 0.26, 7.0, 28.0) : 14.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ReflectionRowHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var show = values.Length > 0 && values[0] is true;
        var horizontal = values.Length > 1 && values[1] is DockOrientation.Horizontal;
        var size = values.Length > 2 && values[2] is double number ? number : 54d;
        return show && horizontal
            ? new GridLength(Math.Clamp(size * 0.26, 7.0, 28.0))
            : new GridLength(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ReflectionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 2 && values[0] is true && values[1] is DockOrientation.Horizontal
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LocalizedEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ThemeMode.System => "Theme.Dark",
            ThemeMode.Dark => "Theme.Dark",
            ThemeMode.Light => "Theme.Light",
            AppLanguage.System => "Language.System",
            AppLanguage.ChineseSimplified => "Language.Chinese",
            AppLanguage.English => "Language.English",
            IconVisualMode.Original => "IconStyle.Original",
            IconVisualMode.Tile => "IconStyle.Tile",
            IconVisualMode.Monochrome => "IconStyle.Monochrome",
            _ => value?.ToString() ?? string.Empty
        };
        return ((App)Application.Current).LocalizationService.Translate(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var equal = value is int i && parameter is string s && int.TryParse(s, System.Globalization.NumberStyles.Integer, culture, out var p) && i == p;
        if (targetType == typeof(Visibility))
        {
            return equal ? Visibility.Visible : Visibility.Collapsed;
        }
        return equal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => parameter is string s && int.TryParse(s, System.Globalization.NumberStyles.Integer, culture, out var p) ? p : DependencyProperty.UnsetValue;
}

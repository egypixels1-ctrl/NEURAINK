using System.Globalization;
using System.Windows.Data;

namespace Neuraink.App.Services;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : false;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Radio buttons bound to a string: checked when value == parameter; checking sets value = parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
}

/// <summary>Visible when the text is not empty (or the value is not null).</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? (s.Trim().Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed)
        : value != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when every bound value is true.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Brush preset preview: the nib ellipse height (20 px wide) from its roundness 0–1.</summary>
public sealed class NibHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double r ? Math.Max(2.5, 20 * r) : 20.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>-x (brush angles turn counter-clockwise on screen).</summary>
public sealed class NegateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double d ? -d : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

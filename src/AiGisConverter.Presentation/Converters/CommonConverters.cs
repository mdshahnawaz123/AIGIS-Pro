using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Presentation.Converters;

/// <summary>Shows an element when a boolean is true.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Shows an element when a value is present.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Visibility does not map back to a value.");
}

/// <summary>
/// Colours a finding by how serious it is.
/// </summary>
/// <remarks>
/// Colour is the secondary cue: the severity is always shown as text beside it, because a report
/// that can only be read in colour cannot be read by everyone, or printed.
/// </remarks>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            IssueSeverity.Critical => new SolidColorBrush(Color.FromRgb(0x8C, 0x1D, 0x18)),
            IssueSeverity.Error => new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
            IssueSeverity.Warning => new SolidColorBrush(Color.FromRgb(0x8A, 0x61, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("A brush does not map back to a severity.");
}

/// <summary>Renders a fraction as a percentage.</summary>
public sealed class FractionToPercentConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double fraction ? fraction * 100d : 0d;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent ? percent / 100d : 0d;
}

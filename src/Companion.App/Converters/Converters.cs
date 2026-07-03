using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Companion.App.Converters;

/// <summary>null → Collapsed, non-null → Visible (Invert flips it).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is not null;
        if (Invert)
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Visible, false → Collapsed (Invert flips it).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is true;
        if (Invert)
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>count > 0 → Visible (Invert: count == 0 → Visible).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is int count && count > 0;
        if (Invert)
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s && s.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>value.Equals(parameter) → Visible; used to switch wizard step panels.</summary>
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ItemsControl.AlternationIndex → finishing-position label ("P1", "P2", ...).</summary>
public sealed class AlternationPositionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? $"P{index + 1}" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>One-letter DNF reason → display text (m/a/o per the result-entry grammar).</summary>
public sealed class DnfReasonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "m" => "mechanical",
            "a" => "accident",
            _ => "retired",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Movement glyph (▲2 / ▼1 / –) → up-green / down-red / muted brush.</summary>
public sealed class GlyphBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Up = new(Color.FromRgb(0x58, 0xB3, 0x68));
    private static readonly SolidColorBrush Down = new(Color.FromRgb(0xE0, 0x5A, 0x5A));
    private static readonly SolidColorBrush Flat = new(Color.FromRgb(0x9A, 0x9A, 0xA5));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string glyph && glyph.Length > 0
            ? glyph[0] switch { '▲' => Up, '▼' => Down, _ => Flat }
            : Flat;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Candidates dropdown visibility: candidates exist AND (the input has text, or the
/// DNF phase is on — where the remaining drivers ARE the candidates for bare-Enter bulking).
/// Values: [0] Candidates.Count (int), [1] Input (string), [2] IsDnfPhase (bool).</summary>
public sealed class CandidatesVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasCandidates = values.Length > 0 && values[0] is int count && count > 0;
        bool hasText = values.Length > 1 && values[1] is string s && s.Trim().Length > 0;
        bool dnfPhase = values.Length > 2 && values[2] is true;
        return hasCandidates && (hasText || dnfPhase) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

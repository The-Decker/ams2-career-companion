using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Companion.App.Behaviors;

/// <summary>
/// Visible when two bound strings are equal and non-empty — drives the inline DNF reason
/// picker, which only shows on the row whose driver id matches
/// ResultEntryViewModel.ReasonPickerDriverId (values[0] picker id, values[1] row driver id).
/// Lives beside the drag-drop behavior because they ship the same feature.
/// </summary>
public sealed class StringsEqualToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 &&
        values[0] is string a && a.Length > 0 &&
        values[1] is string b &&
        string.Equals(a, b, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

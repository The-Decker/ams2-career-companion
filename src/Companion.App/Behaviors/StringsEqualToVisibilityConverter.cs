using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Companion.App.Behaviors;

/// <summary>
/// Visible when two bound strings are equal and non-empty, drives the inline reason editor,
/// which only shows on the ONE row whose driver id matches
/// ResultEntryViewModel.EditingReasonDriverId (values[0] editing id, values[1] row driver id).
/// With <see cref="Invert"/> set it flips to Visible-when-NOT-editing, which drives the row's
/// compact DISPLAY state (name + team + reason label). Lives beside the drag-drop behavior
/// because they ship the same feature.
/// </summary>
public sealed class StringsEqualToVisibilityConverter : IMultiValueConverter
{
    /// <summary>Flip the result: Visible when the two strings are NOT equal (i.e. this row is not
    /// the one being edited), used for the compact DISPLAY half of a two-state row.</summary>
    public bool Invert { get; set; }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool equal =
            values.Length >= 2 &&
            values[0] is string a && a.Length > 0 &&
            values[1] is string b &&
            string.Equals(a, b, StringComparison.Ordinal);
        bool visible = Invert ? !equal : equal;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

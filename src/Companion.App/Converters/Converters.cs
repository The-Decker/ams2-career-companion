using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

/// <summary>DNF reason → display text. Accepts either a one-letter code (m/a/o) or a whole
/// DnfEntry, in which case a custom "Other" detail is shown verbatim (e.g. "Engine fire").</summary>
public sealed class DnfReasonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Whole entry: prefer the custom cause text when the reason is a customised "other".
        if (value is Companion.ViewModels.ResultEntry.DnfEntry entry)
        {
            if (entry.Reason == "o" && !string.IsNullOrWhiteSpace(entry.Detail))
                return entry.DriverAttributed ? $"{entry.Detail!.Trim()} (driver)" : entry.Detail!.Trim();
            return Word(entry.Reason);
        }
        return Word(value as string);
    }

    private static string Word(string? reason) => reason switch
    {
        "m" => "mechanical",
        "a" => "accident",
        _ => "retired",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>value.ToString() == parameter → Visible (Ordinal); drives the "custom Other" box,
/// shown only when a DNF row's reason is "o".</summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A career name → its era accent brush, parsed from a 4-digit year in the name
/// (career gallery). Neutral slate when the name carries no year.</summary>
public sealed class EraAccentBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Neutral = new(Color.FromRgb(0x6A, 0x6A, 0x74));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Companion.Core.Career.EraThemes.FromText(value as string) is not { } theme)
            return Neutral;
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.AccentHex));
        }
        catch (FormatException)
        {
            return Neutral;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A career name → its era medium label ("TELEGRAM"/"FAX"/"EMAIL"), or "" when the
/// name carries no 4-digit year.</summary>
public sealed class EraLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Companion.Core.Career.EraThemes.FromText(value as string)?.Label ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A career name (or a year) → the drop-in era-art image for its gallery card, or null
/// when none is present (the card then shows its coloured era placeholder). Real historical photos
/// live in <c>{BaseDirectory}\data\ams2\era-art\</c>; the resolver picks the most specific one
/// (a year file like <c>1967.jpg</c> over the era-medium file like <c>telegram.jpg</c>) — see
/// career-hub-design.md §11. The bitmap is loaded with <see cref="BitmapCacheOption.OnLoad"/> and
/// frozen so the file is read once and never left locked (images can be swapped while the app runs).
/// </summary>
public sealed class EraImageConverter : IValueConverter
{
    /// <summary>The era-art folder beside the exe (populated by the App csproj asset glob).</summary>
    private static readonly string EraArtDirectory =
        Path.Combine(AppContext.BaseDirectory, "data", "ams2", "era-art");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int? year = value switch
        {
            int y => y,
            string name => Companion.ViewModels.Services.EraArtResolver.YearFromText(name),
            _ => null,
        };
        if (year is not int resolvedYear)
            return null;

        string? path = Companion.ViewModels.Services.EraArtResolver.Resolve(EraArtDirectory, resolvedYear);
        if (path is null)
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read fully now → the file is never locked
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze(); // cross-thread safe + immutable
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UriFormatException)
        {
            // A corrupt or unreadable image must never crash the gallery — fall back to the placeholder.
            return null;
        }
    }

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

/// <summary>DSQ reason → display label for a disqualified row's compact DISPLAY state. The custom
/// reason verbatim when one is set (e.g. "Underweight"), else the plain word "disqualified".
/// Values: [0] the ResultEntryViewModel, [1] the row's driver id (string). Bound through the VM
/// (rather than the seat) because the DSQ reason lives in the viewmodel keyed by driver id; the
/// binding rides on Disqualified changing so it refreshes when a reason is committed.</summary>
public sealed class DsqReasonLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is Companion.ViewModels.ResultEntry.ResultEntryViewModel vm &&
            values[1] is string driverId)
        {
            string reason = vm.DsqReasonOf(driverId);
            if (!string.IsNullOrWhiteSpace(reason))
                return reason.Trim();
        }
        return "disqualified";
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
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

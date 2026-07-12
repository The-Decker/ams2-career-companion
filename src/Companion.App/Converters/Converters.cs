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

/// <summary>Negates a bool both ways (two-way) — e.g. a "Dark" radio bound to the inverse of IsLightTheme.</summary>
public sealed class NegateBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
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

/// <summary>Shared era-year resolution for the gallery converters. A <see cref="RecentCareer"/>
/// resolves to its STORED season year (falling back to a year parsed from the name for legacy
/// entries — <see cref="Companion.ViewModels.Services.EraArtResolver.YearForEntry"/>); a bare int is
/// itself; a bare string is parsed for a year. Null when nothing yields a plausible year, so the
/// card shows its neutral placeholder.</summary>
internal static class EraCardYear
{
    public static int? From(object? value) => value switch
    {
        Companion.ViewModels.Services.RecentCareer entry =>
            Companion.ViewModels.Services.EraArtResolver.YearForEntry(entry),
        Companion.ViewModels.Services.DiscoveredPack pack => pack.SeasonYear,
        int y => y,
        string name => Companion.ViewModels.Services.EraArtResolver.YearFromText(name),
        _ => null,
    };

    /// <summary>The identity art key ("smgp") when this value is an SMGP career/pack — it must
    /// resolve its own art rather than collide on its (shared) 1990 year. Null otherwise.</summary>
    public static string? IdentityKey(object? value)
    {
        string style = Companion.ViewModels.Services.EraArtResolver.SmgpArtKey;
        return value switch
        {
            Companion.ViewModels.Services.DiscoveredPack pack when
                string.Equals(pack.Manifest?.CareerStyle, style, System.StringComparison.Ordinal) => style,
            Companion.ViewModels.Services.RecentCareer entry when
                string.Equals(entry.CareerStyle, style, System.StringComparison.Ordinal) => style,
            _ => null,
        };
    }
}

/// <summary>A career (a <see cref="RecentCareer"/>, a year, or a name) → its era accent brush,
/// keyed off the stored season year for MRU entries (career gallery). Neutral slate when no era
/// resolves.</summary>
public sealed class EraAccentBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Neutral = new(Color.FromRgb(0x6A, 0x6A, 0x74));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (EraCardYear.From(value) is not int year)
            return Neutral;
        try
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(Companion.Core.Career.EraThemes.ForYear(year).AccentHex));
        }
        catch (FormatException)
        {
            return Neutral;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A "#RRGGBB" hex string → a SolidColorBrush (e.g. an offer document's era accent, which
/// the view-model already carries as hex). Transparent when the value is not a parseable hex.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (FormatException)
            {
                // fall through to transparent
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A career (a <see cref="RecentCareer"/>, a year, or a name) → its era medium label
/// ("TELEGRAM"/"FAX"/"EMAIL"), keyed off the stored season year for MRU entries; "" when no era
/// resolves.</summary>
public sealed class EraLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        EraCardYear.From(value) is int year ? Companion.Core.Career.EraThemes.ForYear(year).Label : "";

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
        // 1) A user-chosen card image (picked with "Set card image…") wins, when the file still
        //    exists — point-to-file, so a moved/deleted image just falls back to the era art below.
        if (value is Companion.ViewModels.Services.RecentCareer { CustomImagePath: { Length: > 0 } custom }
            && File.Exists(custom)
            && LoadFrozen(custom) is { } chosen)
        {
            return chosen;
        }

        // 2) IDENTITY-keyed art (the SMGP fictional world's SEGA Grand Prix picture) beats the
        //    year — SMGP shares 1990 with the f1-1990 pack, so it must not collide on 1990.jpg.
        if (EraCardYear.IdentityKey(value) is { } key &&
            Companion.ViewModels.Services.EraArtResolver.ResolveKey(EraArtDirectory, key) is { } keyedPath)
        {
            return LoadFrozen(keyedPath);
        }

        // 3) Otherwise the drop-in era art resolved by the career's STORED season year (name-parse
        //    fallback for legacy entries); a bare int/name keep the old contract so non-gallery
        //    callers are unaffected.
        if (EraCardYear.From(value) is not int resolvedYear)
            return null;

        string? path = Companion.ViewModels.Services.EraArtResolver.Resolve(EraArtDirectory, resolvedYear);
        return path is null ? null : LoadFrozen(path);
    }

    private static BitmapImage? LoadFrozen(string path) => FrozenImage.Load(path);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shared user-image loader for the gallery / track / story art converters. Loads a file
/// fully now (<see cref="BitmapCacheOption.OnLoad"/>) and freezes it, so the file is never left
/// locked (art can be swapped while the app runs) and the bitmap is cross-thread safe. A
/// corrupt/unreadable file returns null — a view never crashes on bad art, it shows its placeholder.</summary>
internal static class FrozenImage
{
    public static BitmapImage? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>A track id (string) → the drop-in track-layout thumbnail for that track, or null when
/// none is present (the view then hides the image). User-managed art lives in
/// <c>{BaseDirectory}\data\ams2\track-art\&lt;trackId&gt;.{jpg,jpeg,png}</c> — the shared
/// "folder + key + resolver with fallback" convention (<see cref="Companion.ViewModels.Services.UserImageResolver"/>),
/// keyed by the track id from data/ams2/tracks.json. Untracked, like era art.</summary>
public sealed class TrackImageConverter : IValueConverter
{
    private static readonly string TrackArtDirectory =
        Path.Combine(AppContext.BaseDirectory, "data", "ams2", "track-art");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string trackId || string.IsNullOrWhiteSpace(trackId))
            return null;
        string? path = Companion.ViewModels.Services.UserImageResolver.ResolveByKey(TrackArtDirectory, trackId);
        return path is null ? null : FrozenImage.Load(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool IsExpanded → a Segoe MDL2 chevron glyph: ChevronDown (open) / ChevronRight
/// (closed). For collapsible section headers.</summary>
public sealed class ExpandGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "" : ""; // ChevronDown : ChevronRight

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A key (string or int) → the drop-in user image for it, from the folder named by the
/// <c>ConverterParameter</c> under <c>data/ams2/</c> — e.g. <c>ConverterParameter=history-art</c>
/// resolves <c>data/ams2/history-art/&lt;key&gt;.{jpg,jpeg,png}</c>. The parameter may list several
/// folders separated by <c>|</c> in preference order — <c>ConverterParameter=portraits|cars</c>
/// tries <c>portraits/&lt;key&gt;</c> then falls back to <c>cars/&lt;key&gt;</c> — so a driver card
/// shows the hand-supplied portrait when present, else the extracted car preview. The shared,
/// reusable half of the user-asset convention (<see cref="Companion.ViewModels.Services.UserImageResolver"/>):
/// one converter, any keyed image folder. Null when absent (the view hides the image); user art is
/// untracked, like era art.</summary>
public sealed class KeyedAssetImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string kinds || string.IsNullOrWhiteSpace(kinds))
            return null;
        string key = value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        foreach (string kind in kinds.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "data", "ams2", kind);
            if (Companion.ViewModels.Services.UserImageResolver.ResolveByKey(dir, key) is { } path)
                return FrozenImage.Load(path);
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A circuit-layout id (e.g. "monaco-5") → a frozen <see cref="Geometry"/> for the circuit
/// map, from the shipped <c>data/ams2/circuits/&lt;layoutId&gt;.json</c> (f1db-derived path data,
/// already normalized to WPF's path mini-language by the build tool). Rendered by a <c>Path</c> with
/// <c>Stretch="Uniform"</c>. Parsed once per layout and cached (frozen → cross-thread safe); null when
/// the file is missing or the path fails to parse (the view then shows no map).</summary>
public sealed class CircuitGeometryConverter : IValueConverter
{
    private static readonly string CircuitsDirectory =
        Path.Combine(AppContext.BaseDirectory, "data", "ams2", "circuits");
    private static readonly Dictionary<string, Geometry?> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string layoutId || string.IsNullOrWhiteSpace(layoutId))
            return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(layoutId, out var cached))
                return cached;
            var geometry = LoadFrom(CircuitsDirectory, layoutId);
            Cache[layoutId] = geometry;
            return geometry;
        }
    }

    /// <summary>Reads <c>&lt;directory&gt;/&lt;layoutId&gt;.json</c> and parses its normalized path data
    /// into a frozen <see cref="Geometry"/> (null on missing/unreadable/unparseable). Public + directory
    /// -parameterized so it can be tested against the real shipped circuit files.</summary>
    public static Geometry? LoadFrom(string directory, string layoutId)
    {
        string file = Path.Combine(directory, layoutId + ".json");
        if (!File.Exists(file))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("paths", out var paths))
                return null;
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var p in paths.EnumerateArray())
            {
                if (p.GetString() is { Length: > 0 } d)
                    group.Children.Add(Geometry.Parse(d));
            }
            if (group.Children.Count == 0)
                return null;
            Geometry leveled = LevelToHorizontal(group);
            leveled.Freeze();
            return leveled;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or FormatException or InvalidOperationException)
        {
            // A bad/unreadable circuit file must never crash a screen — just show no map.
            return null;
        }
    }

    /// <summary>Auto-levels a circuit map: f1db paths are drawn in geographic orientation, so a circuit
    /// that runs diagonally looks tilted (Watkins Glen / Montreal render near-vertical). Flatten the
    /// outline, find its principal axis (PCA of the point cloud), and rotate the geometry so that
    /// longest axis is horizontal — a consistent, level look across every map. Two guards: a circuit
    /// that is already near-level keeps its authored orientation, and a near-round shape (no dominant
    /// axis — principal spread ratio under ~1.25) is left alone because its PCA angle is unstable.
    /// Pure geometry at load time, cached by the caller, no data regen. Public like
    /// <see cref="LoadFrom"/> so the render harness can prove the leveling on real shipped data.</summary>
    public static Geometry LevelToHorizontal(Geometry geometry)
    {
        var points = new List<Point>();
        foreach (var figure in geometry.GetFlattenedPathGeometry().Figures)
        {
            points.Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly)
                    points.AddRange(poly.Points);
                else if (segment is LineSegment line)
                    points.Add(line.Point);
            }
        }
        if (points.Count < 3)
            return geometry;

        double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (var p in points)
        {
            double dx = p.X - mx, dy = p.Y - my;
            sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
        }

        // Eigenvalues of the 2×2 covariance = the spread along/across the principal axis. A shape
        // without a dominant axis (ratio < ~1.25) has no stable "level", so keep it as authored.
        double trace = sxx + syy;
        double root = Math.Sqrt((sxx - syy) * (sxx - syy) + 4 * sxy * sxy);
        double major = (trace + root) / 2, minor = (trace - root) / 2;
        if (minor <= 0 || major / minor < 1.25)
            return geometry;

        // Principal-axis angle; rotating by its negative brings that axis to horizontal.
        double angleDeg = 0.5 * Math.Atan2(2 * sxy, sxx - syy) * 180.0 / Math.PI;
        if (Math.Abs(angleDeg) < 3.0)
            return geometry; // already level enough — keep the authored orientation

        var rotated = geometry.Clone();
        var bounds = geometry.Bounds; // includes any existing transform
        var level = new RotateTransform(
            -angleDeg, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        // COMPOSE with an existing transform (assigning would silently replace it — the shipped
        // circuit paths carry none, but the utility must be correct for any geometry).
        if (rotated.Transform is { } existing && !existing.Value.IsIdentity)
        {
            var group = new TransformGroup();
            group.Children.Add(existing);
            group.Children.Add(level);
            rotated.Transform = group;
        }
        else
        {
            rotated.Transform = level;
        }
        return rotated;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An element's ActualWidth → a height at a fixed aspect ratio (ConverterParameter =
/// height ÷ width, default 0.5625 = 16:9). Lets an image hero keep its aspect as its card stretches
/// to fill a responsive grid (e.g. the season-pick cards, which flex to 4 columns of any width).
/// Returns UnsetValue for a zero/unmeasured width so the element keeps its own MinHeight until the
/// first real layout pass sets ActualWidth.</summary>
public sealed class AspectRatioHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = 0.5625; // 16:9
        if (parameter is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
            parsed > 0)
        {
            ratio = parsed;
        }
        return value is double width && width > 0 && !double.IsInfinity(width)
            ? width * ratio
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An ancestor's ActualWidth → a fraction of it (ConverterParameter = the fraction,
/// default 0.7). Turns a hard-coded page cap (<c>MaxWidth="860"</c>) into a proportional one, so a
/// column keeps a readable measure at 2560 wide yet fills a small or 130%-scaled window. Parameter
/// accepts an optional floor and ceiling after "|"s ("0.7|520" = 70% but never under 520;
/// "0.35|140|280" also never over 280). Returns UnsetValue until the ancestor has a real measured
/// width.</summary>
public sealed class WidthFractionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double fraction = 0.7, floor = 0, ceiling = double.PositiveInfinity;
        if (parameter is string s && s.Length > 0)
        {
            string[] parts = s.Split('|');
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double f) && f > 0)
                fraction = f;
            if (parts.Length > 1 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFloor))
                floor = parsedFloor;
            if (parts.Length > 2 &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedCeiling) &&
                parsedCeiling > 0)
                ceiling = parsedCeiling;
        }
        return value is double width && width > 0 && !double.IsInfinity(width)
            ? Math.Min(Math.Max(width * fraction, floor), ceiling)
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An element's ActualWidth → how many UniformGrid columns fit (ConverterParameter = the
/// target card width, default 360), clamped 2–6. Makes a card grid adaptive: ~2 columns at 920,
/// 4 around 1440, 6 on an ultrawide — instead of a fixed count that cramps or balloons.</summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double cardWidth = 360;
        if (parameter is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
            parsed > 0)
        {
            cardWidth = parsed;
        }
        return value is double width && width > 0 && !double.IsInfinity(width)
            ? Math.Clamp((int)(width / cardWidth), 2, 6)
            : 4;
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

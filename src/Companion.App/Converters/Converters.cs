using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
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

/// <summary>Negates a bool both ways (two-way), e.g. a "Dark" radio bound to the inverse of IsLightTheme.</summary>
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
/// entries, <see cref="Companion.ViewModels.Services.EraArtResolver.YearForEntry"/>); a bare int is
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

    /// <summary>The identity art key ("smgp") when this value is an SMGP career/pack, it must
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

/// <summary>
/// Two view-model supplied team colours -> one frozen presentation brush. Replica teams with a
/// two-colour identity receive an even diagonal split; single-colour and legacy teams remain solid.
/// The converter deliberately knows nothing about team ids: the palette stays in the VM lane and
/// this App-layer type only turns its two published hex values into WPF paint.
/// </summary>
public sealed class TeamAccentBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryColor(values.ElementAtOrDefault(0), out Color primary))
            return Brushes.Transparent;

        if (!TryColor(values.ElementAtOrDefault(1), out Color secondary) || primary == secondary)
            return Frozen(new SolidColorBrush(primary));

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        // Duplicate the midpoint stops so each identity reads as two crisp racing stripes rather
        // than an invented blended colour between the authored primary and secondary.
        brush.GradientStops.Add(new GradientStop(primary, 0));
        brush.GradientStops.Add(new GradientStop(primary, 0.5));
        brush.GradientStops.Add(new GradientStop(secondary, 0.5));
        brush.GradientStops.Add(new GradientStop(secondary, 1));
        return Frozen(brush);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryColor(object? value, out Color color)
    {
        color = default;
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static T Frozen<T>(T brush) where T : Brush
    {
        brush.Freeze();
        return brush;
    }
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
/// (a year file like <c>1967.jpg</c> over the era-medium file like <c>telegram.jpg</c>), see
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
        //    exists, point-to-file, so a moved/deleted image just falls back to the era art below.
        if (value is Companion.ViewModels.Services.RecentCareer { CustomImagePath: { Length: > 0 } custom }
            && File.Exists(custom)
            && LoadFrozen(custom) is { } chosen)
        {
            return chosen;
        }

        // 2) IDENTITY-keyed art (the SMGP fictional world's SEGA Grand Prix picture) beats the
        //    year, SMGP shares 1990 with the f1-1990 pack, so it must not collide on 1990.jpg.
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
/// corrupt/unreadable file returns null, a view never crashes on bad art, it shows its placeholder.</summary>
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
/// <c>{BaseDirectory}\data\ams2\track-art\&lt;trackId&gt;.{jpg,jpeg,png}</c>, the shared
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

/// <summary>
/// Resolves an AMS2 track id to its shipped panoramic Calendar banner. The manifest and JPEG
/// masters are WPF pack resources, so the art remains available in the self-contained executable.
/// Unknown ids or malformed/corrupt resources return null and preserve the circuit-map fallback.
/// </summary>
public sealed class TrackBannerImageConverter : IValueConverter
{
    private const string ResourceRoot = "Assets/TrackBanners/";
    private const string ManifestPath = ResourceRoot + "manifest.json";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Manifest =
        new(LoadManifest, LazyThreadSafetyMode.ExecutionAndPublication);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string trackId || string.IsNullOrWhiteSpace(trackId) ||
            !Manifest.Value.TryGetValue(trackId.Trim(), out string? relativePath))
        {
            return null;
        }

        try
        {
            var resource = Application.GetResourceStream(ResourceUri(ResourceRoot + relativePath));
            if (resource is null)
                return null;

            using (resource.Stream)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = resource.Stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   NotSupportedException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IReadOnlyDictionary<string, string> LoadManifest()
    {
        try
        {
            var resource = Application.GetResourceStream(ResourceUri(ManifestPath));
            if (resource is null)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (resource.Stream)
            using (var document = JsonDocument.Parse(resource.Stream))
            {
                if (!document.RootElement.TryGetProperty("tracks", out JsonElement tracks) ||
                    tracks.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty entry in tracks.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String)
                        continue;

                    string? path = entry.Value.GetString()?.Replace('\\', '/').TrimStart('/');
                    if (string.IsNullOrWhiteSpace(entry.Name) || !IsSafeJpegPath(path))
                        continue;
                    result[entry.Name] = path!;
                }
                return result;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   JsonException or ArgumentException or UriFormatException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsSafeJpegPath(string? path) =>
        path is { Length: > 0 } &&
        !path.Contains("..", StringComparison.Ordinal) &&
        !path.Contains(':', StringComparison.Ordinal) &&
        (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    private static Uri ResourceUri(string path) => new(
        $"/AMS2CareerCompanion;component/{path}", UriKind.Relative);
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
/// <c>ConverterParameter</c> under <c>data/ams2/</c>, e.g. <c>ConverterParameter=history-art</c>
/// resolves <c>data/ams2/history-art/&lt;key&gt;.{jpg,jpeg,png}</c>. The parameter may list several
/// folders separated by <c>|</c> in preference order, <c>ConverterParameter=portraits|cars</c>
/// tries <c>portraits/&lt;key&gt;</c> then falls back to <c>cars/&lt;key&gt;</c>, so a driver card
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

/// <summary>An SMGP team id (for example <c>team.iris</c>) → its drop-in team photograph.
/// Team reference ids deliberately carry the <c>team.</c> namespace while the user-art filenames
/// are the short id (<c>iris.jpg</c>); this presentation-only adapter keeps that naming seam out of
/// the view and delegates the actual image loading/caching contract to
/// <see cref="KeyedAssetImageConverter"/>.</summary>
public sealed class TeamAssetImageConverter : IValueConverter
{
    private static readonly KeyedAssetImageConverter Inner = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return null;

        const string prefix = "team.";
        if (key.StartsWith(prefix, StringComparison.Ordinal))
            key = key[prefix.Length..];

        return Inner.Convert(key, targetType, parameter, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>One App-layer cache for the expensive SMGP read-side projections. Several controls bind
/// the same projection (header + News, Paddock selections, collapsed Team HQ), so compute each at
/// most once per career-session/current-round token and share it without extending the VM lane.</summary>
internal static class SmgpBindingProjectionCache
{
    private sealed class State
    {
        public string DispatchToken { get; set; } = "";
        public bool DispatchLoaded { get; set; }
        public IReadOnlyList<Companion.Core.Smgp.SmgpDispatch> Dispatches { get; set; } = [];
        public string PaddockToken { get; set; } = "";
        public bool PaddockLoaded { get; set; }
        public Companion.ViewModels.Services.SmgpPaddockModel? Paddock { get; set; }
        public string DashboardToken { get; set; } = "";
        public bool DashboardLoaded { get; set; }
        public Companion.ViewModels.Services.SmgpTeamDashboard? Dashboard { get; set; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Companion.ViewModels.Services.ICareerSession, State> States = new();

    public static IReadOnlyList<Companion.Core.Smgp.SmgpDispatch> Dispatches(
        Companion.ViewModels.Services.ICareerSession session, string token)
    {
        var state = States.GetValue(session, static _ => new State());
        lock (state)
        {
            if (!state.DispatchLoaded || !string.Equals(state.DispatchToken, token, StringComparison.Ordinal))
            {
                state.Dispatches = session.SmgpDispatches();
                state.DispatchToken = token;
                state.DispatchLoaded = true;
            }
            return state.Dispatches;
        }
    }

    public static Companion.ViewModels.Services.SmgpPaddockModel? Paddock(
        Companion.ViewModels.Services.ICareerSession session, string token)
    {
        var state = States.GetValue(session, static _ => new State());
        lock (state)
        {
            if (!state.PaddockLoaded || !string.Equals(state.PaddockToken, token, StringComparison.Ordinal))
            {
                state.Paddock = session.SmgpPaddock();
                state.PaddockToken = token;
                state.PaddockLoaded = true;
            }
            return state.Paddock;
        }
    }

    public static Companion.ViewModels.Services.SmgpTeamDashboard? Dashboard(
        Companion.ViewModels.Services.ICareerSession session, string token)
    {
        var state = States.GetValue(session, static _ => new State());
        lock (state)
        {
            if (!state.DashboardLoaded || !string.Equals(state.DashboardToken, token, StringComparison.Ordinal))
            {
                state.Dashboard = session.SmgpTeamDashboard();
                state.DashboardToken = token;
                state.DashboardLoaded = true;
            }
            return state.Dashboard;
        }
    }

    public static string Token(object[] values) => values.OfType<string>().FirstOrDefault() ?? "";
}

/// <summary>The public SMGP living-world projection on an <c>ICareerSession</c> → its newest-first
/// dispatch list. The second multi-binding value is deliberately only a refresh token (the hub's
/// current-round label), so a completed fold re-runs this display-only read without adding a wrapper
/// property to the shared ViewModel lane.</summary>
public sealed class SmgpDispatchesConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var session = values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault();
        return session is not null
            ? SmgpBindingProjectionCache.Dispatches(session, SmgpBindingProjectionCache.Token(values))
            : Array.Empty<Companion.Core.Smgp.SmgpDispatch>();
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Chooses the latest SMGP world dispatch for the persistent header wire, falling back to
/// the existing journal-news item supplied as the third value outside SMGP or before its first beat.</summary>
public sealed class LatestHubDispatchConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault() is { } session &&
            SmgpBindingProjectionCache.Dispatches(session, SmgpBindingProjectionCache.Token(values)) is { Count: > 0 } dispatches)
            return dispatches[0];

        return values.Length > 2 && values[2] is not null &&
               !ReferenceEquals(values[2], DependencyProperty.UnsetValue)
            ? values[2]
            : null;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Resolves the real Task-4 paddock rumor straight from the public session projection, then
/// falls through to a future ViewModel wrapper and the selected driver/team garage voices. This keeps
/// the tear-off useful even though it has no Hub ancestor.</summary>
public sealed class SmgpPaddockRumorConverter : IMultiValueConverter
{
    private const string Quiet = "The paddock is quiet, for now.";

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault() is { } session &&
            SmgpBindingProjectionCache.Paddock(session, SmgpBindingProjectionCache.Token(values))?.PaddockRumor is { Length: > 0 } projected)
            return projected;

        // Values 0..3 are the main/tear-off session + refresh tokens. Only the typed wrapper
        // and garage-voice values after them are legitimate text fallbacks.
        for (int i = 4; i < values.Length; i++)
        {
            if (values[i] is string { Length: > 0 } fallback)
                return fallback;
        }
        return Quiet;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Resolves the Task-5 dashboard's player-team entry from the public session projection,
/// falling through to today's selected Paddock team off-SMGP. The second value is a current-round
/// refresh token.</summary>
public sealed class SmgpPlayerTeamConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault() is { } session &&
            SmgpBindingProjectionCache.Dashboard(session, SmgpBindingProjectionCache.Token(values))?.PlayerTeam is { } playerTeam)
            return playerTeam;

        for (int i = 2; i < values.Length; i++)
        {
            if (values[i] is not null && !ReferenceEquals(values[i], DependencyProperty.UnsetValue))
                return values[i];
        }
        return null;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The public campaign timeline read projection on an <c>ICareerSession</c>. History
/// supplies the live Hub session (or the tear-off window's tagged Hub) plus RoundText as a refresh
/// token; the converter intentionally ignores the token and simply re-reads the deterministic,
/// display-only projection whenever WPF invalidates the MultiBinding.</summary>
public sealed class CampaignTimelineConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var session = values
            .OfType<Companion.ViewModels.Services.ICareerSession>()
            .FirstOrDefault();
        return session?.CampaignTimeline()
            ?? Array.Empty<Companion.ViewModels.Services.CampaignTimelineEntry>();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Joins the Calendar card already projected by the shared ViewModel to Task 3's richer
/// <see cref="Companion.ViewModels.Services.SeasonScheduleEntry"/> from the public session read side.
/// The round label is stable, and the refresh-token value makes the join re-run after every fold.
/// The whole schedule is cached once per session/token so a 16-card render performs one projection.</summary>
public sealed class CalendarTask3DetailConverter : IMultiValueConverter
{
    private sealed class ScheduleCache
    {
        public string Token { get; set; } = "";
        public IReadOnlyDictionary<string, Companion.ViewModels.Services.SeasonScheduleEntry> ByRound { get; set; } =
            new Dictionary<string, Companion.ViewModels.Services.SeasonScheduleEntry>(StringComparer.Ordinal);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Companion.ViewModels.Services.ICareerSession, ScheduleCache> Cache = new();

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not Companion.ViewModels.Hub.CalendarRoundViewModel round)
            return null;

        var session = values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault();
        if (session is null)
            return null;

        string token = values.OfType<string>().FirstOrDefault() ?? "";
        var cache = Cache.GetOrCreateValue(session);
        lock (cache)
        {
            if (!string.Equals(cache.Token, token, StringComparison.Ordinal) || cache.ByRound.Count == 0)
            {
                cache.ByRound = session.SeasonSchedule()
                    .GroupBy(entry => Key(entry.Name, entry.Date), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                cache.Token = token;
            }
            return cache.ByRound.TryGetValue(Key(round.Name, round.DateText), out var entry) ? entry : null;
        }
    }

    private static string Key(string name, string date) => $"{name}\u001f{date}";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Resolves the selected SMGP calendar venue to its shipped round illustration. Round art is
/// keyed to the original sixteen-venue campaign, while later SMGP seasons may shuffle those venues,
/// so the presentation bridge maps the venue identity instead of the selected season's round number.
/// Historical careers deliberately return null and retain the circuit-map hero.</summary>
public sealed class SmgpCalendarRoundArtConverter : IMultiValueConverter
{
    private static readonly KeyedAssetImageConverter Inner = new();

    private static readonly IReadOnlyDictionary<string, int> ArtByVenue =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["San Marino"] = 1,
            ["Autodromo Internazionale Enzo e Dino Ferrari"] = 1,
            ["Brazil"] = 2,
            ["Autodromo Internacional do Rio de Janeiro (Jacarepagua)"] = 2,
            ["France"] = 3,
            ["Circuit Paul Ricard"] = 3,
            ["Hungary"] = 4,
            ["Hungaroring"] = 4,
            ["West Germany"] = 5,
            ["Hockenheimring"] = 5,
            ["U.S.A."] = 6,
            ["USA"] = 6,
            ["Phoenix Street Circuit"] = 6,
            ["Canada"] = 7,
            ["Circuit Gilles Villeneuve"] = 7,
            ["Great Britain"] = 8,
            ["Silverstone Circuit"] = 8,
            ["Italy"] = 9,
            ["Autodromo Nazionale Monza"] = 9,
            ["Portugal"] = 10,
            ["Autodromo do Estoril"] = 10,
            ["Spain"] = 11,
            ["Circuito de Jerez"] = 11,
            ["Mexico"] = 12,
            ["Autodromo Hermanos Rodriguez"] = 12,
            ["Japan"] = 13,
            ["Suzuka Circuit"] = 13,
            ["Belgium"] = 14,
            ["Circuit de Spa-Francorchamps"] = 14,
            ["Australia"] = 15,
            ["Adelaide Street Circuit"] = 15,
            ["Monaco"] = 16,
            ["Circuit de Monaco"] = 16,
        };

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not Companion.ViewModels.Hub.CalendarRoundViewModel round)
            return null;

        var session = values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault();
        if (session is null || !string.Equals(
                session.Pack.Manifest.CareerStyle,
                Companion.Core.Smgp.SmgpRules.CareerStyle,
                StringComparison.Ordinal))
            return null;

        if (!TryArtKey(round.Name, out int key) && !TryArtKey(round.RealVenue, out key))
            return null;

        return Inner.Convert(key, targetType, "smgp/rounds", culture);
    }

    private static bool TryArtKey(string value, out int key) =>
        ArtByVenue.TryGetValue(RemoveDiacritics(value), out key);

    private static string RemoveDiacritics(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Controls the Calendar's spoiler-safe DNQ reveal. The pack knows every DNQ up front, but
/// the names appear only for the upcoming race while the player is on its Starting Grid. Past rounds
/// belong in History and later rounds stay sealed. The optional <c>sealed</c> parameter returns the
/// inverse state for the qualifying-envelope callout without leaking any driver identity.</summary>
public sealed class CalendarDnqVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not Companion.ViewModels.Services.SeasonScheduleEntry entry ||
            entry.Dnq.Count == 0)
            return Visibility.Collapsed;

        bool revealed = entry.Status == Companion.ViewModels.Services.SeasonRoundStatus.Next &&
            (IsTrue(values, 1) || IsTrue(values, 2));
        bool sealedState = string.Equals(parameter as string, "sealed", StringComparison.OrdinalIgnoreCase);
        bool visible = sealedState
            ? entry.Status != Companion.ViewModels.Services.SeasonRoundStatus.Done && !revealed
            : revealed;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsTrue(object[] values, int index) => index < values.Length && values[index] is true;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Projects Task 3's per-round player almanac for a History season card. The shared card
/// ViewModel intentionally remains unchanged; this App-layer join reads the public timeline by year.
/// The full timeline is cached once per session/token, avoiding one career replay per season card.</summary>
public sealed class HistoryRoundLinesConverter : IMultiValueConverter
{
    private sealed class TimelineCache
    {
        public string Token { get; set; } = "";
        public IReadOnlyDictionary<int, IReadOnlyList<Companion.ViewModels.Services.CareerSeasonRoundLine>> ByYear { get; set; } =
            new Dictionary<int, IReadOnlyList<Companion.ViewModels.Services.CareerSeasonRoundLine>>();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Companion.ViewModels.Services.ICareerSession, TimelineCache> Cache = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not Companion.ViewModels.Hub.SeasonCardViewModel card)
            return Array.Empty<Companion.ViewModels.Services.CareerSeasonRoundLine>();

        var session = values.OfType<Companion.ViewModels.Services.ICareerSession>().FirstOrDefault();
        if (session is null)
            return Array.Empty<Companion.ViewModels.Services.CareerSeasonRoundLine>();

        string token = values.OfType<string>().FirstOrDefault() ?? "";
        var cache = Cache.GetOrCreateValue(session);
        lock (cache)
        {
            if (!string.Equals(cache.Token, token, StringComparison.Ordinal) || cache.ByYear.Count == 0)
            {
                cache.ByYear = session.CareerTimeline().Seasons
                    .GroupBy(season => season.SeasonYear)
                    .ToDictionary(group => group.Key, group => group.First().RoundLines);
                cache.Token = token;
            }
            return cache.ByYear.TryGetValue(card.SeasonYear, out var lines)
                ? lines
                : Array.Empty<Companion.ViewModels.Services.CareerSeasonRoundLine>();
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
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
            group.Freeze();
            return group;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or FormatException or InvalidOperationException)
        {
            // A bad/unreadable circuit file must never crash a screen, just show no map.
            return null;
        }
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
/// 4 around 1440, 6 on an ultrawide, instead of a fixed count that cramps or balloons.</summary>
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

/// <summary>An element width to a small responsive column count. The parameter is
/// <c>targetWidth|min|max</c> (defaults <c>360|1|3</c>). Unlike the wizard's intentionally dense
/// <see cref='WidthToColumnsConverter'/>, reading surfaces must be able to collapse to one column
/// at 920x620 and under the app's 130% root scale.</summary>
public sealed class AdaptiveColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double targetWidth = 360;
        int min = 1;
        int max = 3;
        if (parameter is string text && text.Length > 0)
        {
            string[] parts = text.Split('|');
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedWidth) &&
                parsedWidth > 0)
            {
                targetWidth = parsedWidth;
            }
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMin))
                min = Math.Max(1, parsedMin);
            if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
                max = Math.Max(min, parsedMax);
        }

        return value is double width && width > 0 && !double.IsInfinity(width)
            ? Math.Clamp((int)(width / targetWidth), min, max)
            : min;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Starting-grid viewport width to the authored venue crop or its matching three-band
/// layout. Wide screens retain the grandstand/track/pit composition; at the narrow shell viewport
/// the image crops farther into the straight and the live two-file grid receives enough asphalt to
/// keep both cars and portraits legible. The crop and column ratios deliberately change together so
/// dynamic overlays never drift onto scenery.</summary>
public sealed class StartingGridViewportConverter : IValueConverter
{
    private const double CompactBreakpoint = 640;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool compact = value is double width && width > 0 && width < CompactBreakpoint;
        return (parameter as string) switch
        {
            "plate" => compact ? new Rect(0.25, 0, 0.50, 1) : new Rect(0.12, 0, 0.76, 1),
            "left" or "right" => new GridLength(compact ? 10 : 24, GridUnitType.Star),
            "center" => new GridLength(compact ? 80 : 52, GridUnitType.Star),
            _ => DependencyProperty.UnsetValue,
        };
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

/// <summary>
/// Maps authored semantic skill icon keys to the app's compact Segoe MDL2 symbol vocabulary.
/// The catalog remains presentation-agnostic; unknown keys intentionally receive a stable
/// telemetry glyph instead of exposing their raw identifier in the UI.
/// </summary>
public sealed class SkillIconKeyToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value as string ?? "";
        return key.ToLowerInvariant() switch
        {
            var text when text.Contains("weather", StringComparison.Ordinal) ||
                              text.Contains("wet", StringComparison.Ordinal) => "\uE706",
            var text when text.Contains("physical", StringComparison.Ordinal) ||
                              text.Contains("fitness", StringComparison.Ordinal) ||
                              text.Contains("stamina", StringComparison.Ordinal) => "\uE95B",
            var text when text.Contains("mental", StringComparison.Ordinal) ||
                              text.Contains("focus", StringComparison.Ordinal) => "\uE77B",
            var text when text.Contains("business", StringComparison.Ordinal) ||
                              text.Contains("contract", StringComparison.Ordinal) ||
                              text.Contains("sponsor", StringComparison.Ordinal) => "\uE821",
            var text when text.Contains("team", StringComparison.Ordinal) ||
                              text.Contains("engineer", StringComparison.Ordinal) => "\uE716",
            var text when text.Contains("media", StringComparison.Ordinal) ||
                              text.Contains("fame", StringComparison.Ordinal) => "\uE8A5",
            var text when text.Contains("era", StringComparison.Ordinal) ||
                              text.Contains("legacy", StringComparison.Ordinal) => "\uE81C",
            var text when text.Contains("pace", StringComparison.Ordinal) ||
                              text.Contains("speed", StringComparison.Ordinal) => "\uE768",
            var text when text.Contains("attribute", StringComparison.Ordinal) ||
                              text.Contains("rail", StringComparison.Ordinal) => "\uE8D2",
            _ => "\uE7C1",
        };
    }

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
/// DNF phase is on, where the remaining drivers ARE the candidates for bare-Enter bulking).
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

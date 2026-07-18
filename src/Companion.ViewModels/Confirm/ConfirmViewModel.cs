using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Confirm;

/// <summary>One driver's points contribution for the round being confirmed.</summary>
public sealed record ConfirmPointsRow(string DriverId, string DisplayName, string PointsText);

/// <summary>One driver's standings movement, with the direction glyph (▲2 / ▼1 / –) and a
/// plain-words tooltip ("P5 → P2: gained 3 places").</summary>
public sealed record ConfirmMovementRow(
    string DriverId,
    string DisplayName,
    string FromText,
    string ToText,
    string Glyph,
    string Tooltip);

/// <summary>
/// The confirm interstitial between result entry and Apply: the round's computed points
/// (StandingsEngine output via <see cref="ICareerSession.Preview"/>), standings movement with
/// direction glyphs and tooltips, and the headline. Apply/Back behavior is injected by the
/// shell. Minimal-narrative mode (settings) suppresses the headline unless it reads as
/// championship-critical.
/// </summary>
public sealed partial class ConfirmViewModel : ObservableObject
{
    private readonly Action _onApply;
    private readonly Action _onBack;

    public ConfirmViewModel(
        ConfirmModel model,
        Action onApply,
        Action onBack,
        Func<string, string>? displayName = null,
        bool minimalNarrative = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        _onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));
        _onBack = onBack ?? throw new ArgumentNullException(nameof(onBack));

        var name = displayName ?? (id => id);

        RoundPoints = model.RoundPoints
            .Select(p => new ConfirmPointsRow(p.DriverId, name(p.DriverId), p.Points.ToString()))
            .ToArray();

        Movements = model.Movements
            .Select(m => new ConfirmMovementRow(
                m.DriverId,
                name(m.DriverId),
                m.From?.ToString() ?? "–",
                m.To?.ToString() ?? "–",
                Glyph(m.From, m.To),
                MovementTooltip(name(m.DriverId), m.From, m.To)))
            .ToArray();

        Headline = model.Headline;
        ShowHeadline = !minimalNarrative || IsChampionshipCritical(model.Headline);
    }

    public IReadOnlyList<ConfirmPointsRow> RoundPoints { get; }

    public IReadOnlyList<ConfirmMovementRow> Movements { get; }

    public string Headline { get; }

    /// <summary>False only in minimal-narrative mode for a non-championship-critical
    /// headline, the view shows the plain review line instead.</summary>
    public bool ShowHeadline { get; }

    /// <summary>Minimal narrative keeps championship-critical headlines: the authored
    /// bank marks those moments with champion/title wording.</summary>
    public static bool IsChampionshipCritical(string headline) =>
        headline.Contains("champion", StringComparison.OrdinalIgnoreCase)
        || headline.Contains("title", StringComparison.OrdinalIgnoreCase);

    /// <summary>▲n = gained n positions (numerically lower), ▼n = lost n, – = unchanged or
    /// no prior position (round 1).</summary>
    public static string Glyph(int? from, int? to) => (from, to) switch
    {
        (null, _) or (_, null) => "–",
        ({ } f, { } t) when t < f => $"▲{f - t}",
        ({ } f, { } t) when t > f => $"▼{t - f}",
        _ => "–",
    };

    /// <summary>The movement row's hover text, e.g. "Jim Clark, P5 → P2: gained 3 places".</summary>
    public static string MovementTooltip(string displayName, int? from, int? to) => (from, to) switch
    {
        (null, { } t) => $"{displayName}, first classification: P{t}",
        ({ } f, null) => $"{displayName}, no longer classified (was P{f})",
        (null, null) => $"{displayName}, not classified",
        ({ } f, { } t) when t < f => $"{displayName}, P{f} → P{t}: gained {f - t} " + Places(f - t),
        ({ } f, { } t) when t > f => $"{displayName}, P{f} → P{t}: lost {t - f} " + Places(t - f),
        ({ } f, _) => $"{displayName}, holds P{f}",
    };

    private static string Places(int count) => count == 1 ? "place" : "places";

    [RelayCommand]
    private void Apply() => _onApply();

    [RelayCommand]
    private void Back() => _onBack();
}

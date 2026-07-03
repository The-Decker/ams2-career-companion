using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Confirm;

/// <summary>One driver's points contribution for the round being confirmed.</summary>
public sealed record ConfirmPointsRow(string DriverId, string DisplayName, string PointsText);

/// <summary>One driver's standings movement, with the direction glyph (▲2 / ▼1 / –).</summary>
public sealed record ConfirmMovementRow(
    string DriverId,
    string DisplayName,
    string FromText,
    string ToText,
    string Glyph);

/// <summary>
/// The confirm interstitial between result entry and Apply: the round's computed points
/// (StandingsEngine output via <see cref="ICareerSession.Preview"/>), standings movement with
/// direction glyphs, and the headline. Apply/Back behavior is injected by the shell.
/// </summary>
public sealed partial class ConfirmViewModel : ObservableObject
{
    private readonly Action _onApply;
    private readonly Action _onBack;

    public ConfirmViewModel(
        ConfirmModel model,
        Action onApply,
        Action onBack,
        Func<string, string>? displayName = null)
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
                Glyph(m.From, m.To)))
            .ToArray();

        Headline = model.Headline;
    }

    public IReadOnlyList<ConfirmPointsRow> RoundPoints { get; }

    public IReadOnlyList<ConfirmMovementRow> Movements { get; }

    public string Headline { get; }

    /// <summary>▲n = gained n positions (numerically lower), ▼n = lost n, – = unchanged or
    /// no prior position (round 1).</summary>
    public static string Glyph(int? from, int? to) => (from, to) switch
    {
        (null, _) or (_, null) => "–",
        ({ } f, { } t) when t < f => $"▲{f - t}",
        ({ } f, { } t) when t > f => $"▼{t - f}",
        _ => "–",
    };

    [RelayCommand]
    private void Apply() => _onApply();

    [RelayCommand]
    private void Back() => _onBack();
}

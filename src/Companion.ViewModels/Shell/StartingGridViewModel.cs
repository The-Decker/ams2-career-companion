using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Grid;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The "here's your starting grid" screen — shown AFTER qualifying and BEFORE the race, so the player
/// sees the qualifying result laid out pole-first as team-coloured driver + car cards before they go
/// racing, with the race conditions (lap distance, weather, track state, fuel) framing it. DISPLAY-ONLY:
/// it just projects the already-captured grid order + round conditions, never a fold input.
/// </summary>
public sealed class StartingGridViewModel : ObservableObject
{
    public StartingGridViewModel(
        IReadOnlyList<GridSeat> orderedGrid, string playerDriverId, string? sessionTitle,
        GridConditions? conditions = null, string? playerCarArtDriverId = null)
    {
        Title = string.IsNullOrEmpty(sessionTitle) ? "Starting grid" : $"Starting grid  ·  {sessionTitle}";
        Conditions = conditions ?? GridConditions.Unknown;
        Slots = orderedGrid.Select((seat, i) => new StartingGridSlot(
            Position: i + 1,
            DriverId: seat.DriverId,
            DriverName: seat.DriverName,
            TeamId: seat.TeamId,
            TeamName: seat.TeamName,
            Number: seat.Number,
            IsPlayer: seat.IsPlayer,
            // The player's own card shows the team-coloured player image (like the Season's Grid);
            // every other card shows the seat driver's portrait.
            PortraitKey: seat.IsPlayer ? GridSeatChoice.PlayerImageKey(seat.TeamId) : seat.DriverId,
            // The car preview keys off the seat's driver — EXCEPT the player, whose distinct-driver id
            // (driver.player-entrant, the SMGP clean-swap synthetic) has no car art, so their card
            // rendered a blank car. The player physically drives the car they took over, so key their
            // preview to that car's authored driver (passed in) — the exact team car they chose. Falls
            // back to the seat id (a custom own-entrant livery with no authored car still shows nothing).
            CarKey: seat.IsPlayer ? (playerCarArtDriverId ?? seat.DriverId) : seat.DriverId)).ToList();
    }

    /// <summary>Heading — "Starting grid" plus the session label on a multi-race weekend.</summary>
    public string Title { get; }

    /// <summary>The grid pole-first (index 0 = P1).</summary>
    public IReadOnlyList<StartingGridSlot> Slots { get; }

    /// <summary>The odd grid slots (P1, P3, P5 …) — the FRONT row of each pair, laid across the top,
    /// exactly like a real starting grid (the even slots sit a half-car back on the bottom row).</summary>
    public IReadOnlyList<StartingGridSlot> TopRow => Slots.Where(s => s.Position % 2 == 1).ToList();

    /// <summary>The even grid slots (P2, P4, P6 …) — the BACK row of each pair, offset a half-car.</summary>
    public IReadOnlyList<StartingGridSlot> BottomRow => Slots.Where(s => s.Position % 2 == 0).ToList();

    /// <summary>The race conditions shown in the top/bottom bars (lap distance, weather, fuel …).</summary>
    public GridConditions Conditions { get; }
}

/// <summary>One starting-grid card: the grid position, driver/team, the team accent colour, and the
/// drop-in portrait &amp; car preview keys (framed placeholders show until the art is dropped).</summary>
public sealed record StartingGridSlot(
    int Position, string DriverId, string DriverName, string TeamId, string TeamName, string? Number,
    bool IsPlayer, string PortraitKey, string? CarKey)
{
    public string DriverNameUpper => DriverName.ToUpperInvariant();
    public string TeamNameUpper => TeamName.ToUpperInvariant();

    /// <summary>Grid slot label, "P1", "P2", … for the card corner.</summary>
    public string PositionLabel => "P" + Position;

    /// <summary>Car number with a leading # when the entry carries one; empty otherwise.</summary>
    public string NumberLabel => string.IsNullOrEmpty(Number) ? "" : "#" + Number;

    /// <summary>The team's accent colour ("#RRGGBB") — the position box, name accent and card edge.</summary>
    public string TeamColor => TeamPalette.For(TeamId);
}

/// <summary>The race-day conditions the starting-grid bars display (all display-only). Lap distance
/// and weather come from the round; the atmospheric readouts (track/air temp, wind, humidity) are
/// synthesised deterministically from the weather for flavour; fuel is the start-of-race default.</summary>
public sealed record GridConditions
{
    /// <summary>The lap distance in km (the circuit length), or null when unknown.</summary>
    public double? LapDistanceKm { get; init; }

    /// <summary>The race weather label ("Clear", "Light Rain", …).</summary>
    public string Weather { get; init; } = "Clear";

    /// <summary>True when the race weather is wet (drives the TRACK DRY/WET readout + icon).</summary>
    public bool IsWet { get; init; }

    public int TrackTempC { get; init; } = 26;
    public int AirTempC { get; init; } = 20;
    public double WindMs { get; init; } = 2.0;
    public int HumidityPct { get; init; }
    public int FuelPct { get; init; } = 100;

    public string LapDistanceLabel => LapDistanceKm is { } km ? $"{km:0.000} km" : "—";
    public string TrackTempLabel => $"{TrackTempC}°C";
    public string AirTempLabel => $"{AirTempC}°C";
    public string WindLabel => $"{WindMs:0.0} m/s";
    public string HumidityLabel => $"{HumidityPct}%";
    public string FuelLabel => $"Fuel {FuelPct}%";
    public string TrackStateLabel => IsWet ? "Track wet" : "Track dry";

    /// <summary>A Segoe MDL2 glyph for the weather — sun (clear) or raindrops (wet).</summary>
    public string WeatherGlyph => IsWet ? "" : "";

    public static GridConditions Unknown { get; } = new();
}

using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Grid;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The "here's your starting grid" screen, shown AFTER qualifying and BEFORE the race, so the player
/// sees the qualifying result laid out pole-first as team-coloured driver + car cards before they go
/// racing, with the race conditions (lap distance, weather, track state, fuel) framing it. DISPLAY-ONLY:
/// it just projects the already-captured grid order + round conditions, never a fold input.
/// </summary>
public sealed class StartingGridViewModel : ObservableObject
{
    public StartingGridViewModel(
        IReadOnlyList<GridSeat> orderedGrid, string playerDriverId, string? sessionTitle,
        GridConditions? conditions = null, string? playerCarArtDriverId = null,
        IReadOnlyList<StartingGridDnq>? dnq = null,
        string? playerCountryFlagKey = null,
        IReadOnlyDictionary<string, string>? carArtKeyByLivery = null)
    {
        Title = string.IsNullOrEmpty(sessionTitle) ? "Starting grid" : $"Starting grid  ·  {sessionTitle}";
        Conditions = conditions ?? GridConditions.Unknown;
        Dnq = dnq ?? [];
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
            // SMGP car art follows the fixed livery/seat, not the active driver: later-season
            // reshuffles move drivers between physical cars. The existing player override remains
            // the fallback for legacy sessions, then the active driver id for ordinary grids.
            CarKey: ResolveCarKey(seat, playerCarArtDriverId, carArtKeyByLivery))
        {
            // AI flags retain their authored driver-keyed art. The player uses the immutable
            // country selection; a legacy profile has no key and therefore shows no false donor flag.
            CountryFlagKey = seat.IsPlayer ? playerCountryFlagKey : seat.DriverId,
        }).ToList();
    }

    private static string ResolveCarKey(
        GridSeat seat,
        string? playerCarArtDriverId,
        IReadOnlyDictionary<string, string>? carArtKeyByLivery)
    {
        if (carArtKeyByLivery is not null &&
            carArtKeyByLivery.TryGetValue(seat.Ams2LiveryName, out string? fixedCarKey))
        {
            return fixedCarKey;
        }
        return seat.IsPlayer ? (playerCarArtDriverId ?? seat.DriverId) : seat.DriverId;
    }

    /// <summary>Heading, "Starting grid" plus the session label on a multi-race weekend.</summary>
    public string Title { get; }

    /// <summary>The grid pole-first (index 0 = P1).</summary>
    public IReadOnlyList<StartingGridSlot> Slots { get; }

    /// <summary>The odd grid slots (P1, P3, P5 …), the FRONT row of each pair, laid across the top,
    /// exactly like a real starting grid (the even slots sit a half-car back on the bottom row).</summary>
    public IReadOnlyList<StartingGridSlot> TopRow => Slots.Where(s => s.Position % 2 == 1).ToList();

    /// <summary>The even grid slots (P2, P4, P6 …), the BACK row of each pair, offset a half-car.</summary>
    public IReadOnlyList<StartingGridSlot> BottomRow => Slots.Where(s => s.Position % 2 == 0).ToList();

    /// <summary>The race conditions shown in the top/bottom bars (lap distance, weather, fuel …).</summary>
    public GridConditions Conditions { get; }

    /// <summary>The cars that DID NOT QUALIFY this round, the SMGP dynamic DNQ field's rotating tail
    /// (the pack fields 34 painted cars but the grid caps at ~26, so the slowest 8-9 sit out, and which
    /// ones rotates race to race). Empty for a full-field pack (no DNQ), which hides the strip.</summary>
    public IReadOnlyList<StartingGridDnq> Dnq { get; }

    /// <summary>True when any car missed the cut this round, gates the "DID NOT QUALIFY" strip.</summary>
    public bool HasDnq => Dnq.Count > 0;

    /// <summary>The DNQ strip header, e.g. "DID NOT QUALIFY · 8".</summary>
    public string DnqHeader => $"DID NOT QUALIFY · {Dnq.Count}";
}

/// <summary>One car that failed to qualify this round, shown grayed on the grid's DNQ strip so the
/// player can see the rotating field (who narrowly missed, who is a perennial backmarker).</summary>
public sealed record StartingGridDnq(string Name, string TeamName, string? Number)
{
    public string NameUpper => Name.ToUpperInvariant();
    public string NumberLabel => string.IsNullOrEmpty(Number) ? "" : "#" + Number;
}

/// <summary>One starting-grid card: the grid position, driver/team, the team accent colour, and the
/// drop-in portrait &amp; car preview keys (framed placeholders show until the art is dropped).</summary>
public sealed record StartingGridSlot(
    int Position, string DriverId, string DriverName, string TeamId, string TeamName, string? Number,
    bool IsPlayer, string PortraitKey, string? CarKey)
{
    /// <summary>Key under <c>data/ams2/smgp/flags</c>. AI remains driver-keyed; the player receives
    /// the country-keyed asset for their selected nationality. Null hides the flag for legacy players.</summary>
    public string? CountryFlagKey { get; init; }

    public string DriverNameUpper => DriverName.ToUpperInvariant();
    public string TeamNameUpper => TeamName.ToUpperInvariant();

    /// <summary>Grid slot label, "P1", "P2", … for the card corner.</summary>
    public string PositionLabel => "P" + Position;

    /// <summary>Car number with a leading # when the entry carries one; empty otherwise.</summary>
    public string NumberLabel => string.IsNullOrEmpty(Number) ? "" : "#" + Number;

    /// <summary>The team's accent colour ("#RRGGBB"), the position box, name accent and card edge.</summary>
    public string TeamColor => TeamPalette.For(TeamId);

    /// <summary>The team's second livery colour. It equals <see cref="TeamColor"/> for a
    /// single-colour/unmapped team, keeping the starting-grid binding contract branch-free.</summary>
    public string TeamSecondaryColor => TeamPalette.SecondaryFor(TeamId);
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

    public string LapDistanceLabel => LapDistanceKm is { } km ? $"{km:0.000} km" : "-";
    public string TrackTempLabel => $"{TrackTempC}°C";
    public string AirTempLabel => $"{AirTempC}°C";
    public string WindLabel => $"{WindMs:0.0} m/s";
    public string HumidityLabel => $"{HumidityPct}%";
    public string FuelLabel => $"Fuel {FuelPct}%";
    public string TrackStateLabel => IsWet ? "Track wet" : "Track dry";

    /// <summary>A Segoe MDL2 glyph for the weather, sun (clear) or raindrops (wet).</summary>
    public string WeatherGlyph => IsWet ? "" : "";

    public static GridConditions Unknown { get; } = new();
}

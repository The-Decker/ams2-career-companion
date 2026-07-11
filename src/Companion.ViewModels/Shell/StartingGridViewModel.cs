using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Grid;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The "here's your starting grid" screen — shown AFTER qualifying and BEFORE the race, so the player
/// sees the qualifying result laid out pole-first as driver + car cards (two wide, scrollable) before
/// they go racing. DISPLAY-ONLY: it just projects the already-captured grid order, never a fold input.
/// </summary>
public sealed class StartingGridViewModel : ObservableObject
{
    public StartingGridViewModel(
        IReadOnlyList<GridSeat> orderedGrid, string playerDriverId, string? sessionTitle)
    {
        Title = string.IsNullOrEmpty(sessionTitle) ? "Starting grid" : $"Starting grid  ·  {sessionTitle}";
        Slots = orderedGrid.Select((seat, i) => new StartingGridSlot(
            Position: i + 1,
            DriverId: seat.DriverId,
            DriverName: seat.DriverName,
            TeamName: seat.TeamName,
            Number: seat.Number,
            IsPlayer: seat.IsPlayer,
            // The player's own card shows the team-coloured player image (like the Season's Grid);
            // every other card shows the seat driver's portrait. The car preview is keyed by the
            // seat's driver either way.
            PortraitKey: seat.IsPlayer ? GridSeatChoice.PlayerImageKey(seat.TeamId) : seat.DriverId,
            CarKey: seat.DriverId)).ToList();
    }

    /// <summary>Heading — "Starting grid" plus the session label on a multi-race weekend.</summary>
    public string Title { get; }

    /// <summary>The grid pole-first (index 0 = P1).</summary>
    public IReadOnlyList<StartingGridSlot> Slots { get; }
}

/// <summary>One starting-grid card: the grid position, driver/team, and the drop-in portrait &amp;
/// car preview keys (framed placeholders show until the art is dropped).</summary>
public sealed record StartingGridSlot(
    int Position, string DriverId, string DriverName, string TeamName, string? Number,
    bool IsPlayer, string PortraitKey, string? CarKey)
{
    public string DriverNameUpper => DriverName.ToUpperInvariant();
    public string TeamNameUpper => TeamName.ToUpperInvariant();

    /// <summary>Grid slot label, "P1", "P2", … for the card corner.</summary>
    public string PositionLabel => "P" + Position;

    /// <summary>Car number with a leading # when the entry carries one; empty otherwise.</summary>
    public string NumberLabel => string.IsNullOrEmpty(Number) ? "" : "#" + Number;
}

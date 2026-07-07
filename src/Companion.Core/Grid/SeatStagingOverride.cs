namespace Companion.Core.Grid;

/// <summary>
/// A per-seat COSMETIC staging override (the Skins grid editor): a custom driver NAME and/or a
/// rebound LIVERY (skin) for the seat identified by its original <c>ams2LiveryName</c>. Applied
/// ONLY to the staged custom-AI file — never the resolved grid the sim scores — so it is sim-inert
/// and re-simulation stays byte-identical. Persisted per season, outside the append-only journal.
/// </summary>
public sealed record SeatStagingOverride
{
    /// <summary>The driver name to write into the AI file for this seat (null = keep the grid's
    /// name). Lets the player call an AI driver whatever they want in-game.</summary>
    public string? DriverName { get; init; }

    /// <summary>The livery to rebind this seat to — the exact NAME of a different installed livery,
    /// written as the AI entry's <c>livery_name</c> so the car shows that skin instead (null = keep
    /// the seat's original livery). Must be an ACTIVE livery for the class or it will not bind.</summary>
    public string? LiveryName { get; init; }

    /// <summary>True when the override carries nothing — a no-op that leaves the seat as authored.</summary>
    public bool IsEmpty => DriverName is null && LiveryName is null;
}

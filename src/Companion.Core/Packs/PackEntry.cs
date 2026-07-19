namespace Companion.Core.Packs;

/// <summary>entries.json root.</summary>
public sealed record PackEntriesFile
{
    public required IReadOnlyList<PackEntry> Entries { get; init; }
}

/// <summary>Who drives what, when. The livery binding is the load-bearing string.</summary>
public sealed record PackEntry
{
    public required string TeamId { get; init; }

    public required string DriverId { get; init; }

    /// <summary>Race number as displayed (string: "1", "2T", ...).</summary>
    public required string Number { get; init; }

    /// <summary>Calendar rounds this entry participates in, as a <see cref="RoundsRange"/>
    /// expression: "1-11", "4", or "1,3,5-8" (ranges/lists for mid-season swaps).</summary>
    public required string Rounds { get; init; }

    /// <summary>EXACT livery display name (case-sensitive), the load-bearing binding.</summary>
    public required string Ams2LiveryName { get; init; }
}

namespace Companion.Core.Packs;

/// <summary>
/// A fully parsed season pack: the five pack files as one aggregate. Produced by
/// <see cref="PackLoader.Parse"/>; structurally checked by <see cref="PackStructuralValidator"/>.
/// </summary>
public sealed record SeasonPack
{
    public required PackManifest Manifest { get; init; }

    public required SeasonDefinition Season { get; init; }

    public required IReadOnlyList<PackTeam> Teams { get; init; }

    public required IReadOnlyList<PackDriver> Drivers { get; init; }

    public required IReadOnlyList<PackEntry> Entries { get; init; }
}

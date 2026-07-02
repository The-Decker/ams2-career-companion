namespace Companion.Core.Packs;

/// <summary>teams.json root.</summary>
public sealed record PackTeamsFile
{
    public required IReadOnlyList<PackTeam> Teams { get; init; }
}

public sealed record PackTeam
{
    /// <summary>Lineage id, stable across era packs (e.g. "team.brabham").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>data/ams2/vehicles.json ids this team runs.</summary>
    public required IReadOnlyList<string> CarVehicleIds { get; init; }

    public PackTeamPerformance Performance { get; init; } = new();

    /// <summary>Maps to the custom-AI vehicle_reliability parameter.</summary>
    public double Reliability { get; init; } = 1.0;

    public int Prestige { get; init; }

    public int BudgetTier { get; init; }
}

/// <summary>Per-team performance scalars applied to the base vehicle; 1.0 is neutral.</summary>
public sealed record PackTeamPerformance
{
    public double WeightScalar { get; init; } = 1.0;

    public double PowerScalar { get; init; } = 1.0;

    public double DragScalar { get; init; } = 1.0;
}

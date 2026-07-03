namespace Companion.Core.Career;

/// <summary>Why a player DNF'd, for OPI blame assignment (docs/dev/career-sim.md, Player model).</summary>
public enum DnfCause
{
    /// <summary>The car broke — no blame: scores as the expected finish.</summary>
    Mechanical,

    /// <summary>The driver binned it — full blame: scores as the grid size.</summary>
    DriverError,
}

/// <summary>Per-AI-driver career state, folded from the journal. Ratings are stored as
/// drift DELTAS against the pinned pack baseline so the raw pack stays immutable.</summary>
public sealed record DriverCareerState
{
    /// <summary>Lineage id, stable across era packs ("driver.j_clark").</summary>
    public required string DriverId { get; init; }

    /// <summary>Age in the season this state describes.</summary>
    public required int Age { get; init; }

    /// <summary>Cumulative raceSkill drift vs the pack baseline (aging + form shocks).</summary>
    public double RaceSkillDelta { get; init; }

    /// <summary>Cumulative qualifyingSkill drift vs the pack baseline.</summary>
    public double QualifyingSkillDelta { get; init; }

    /// <summary>Short-term form, reserved for the per-round `form` stream (v1: carried, not
    /// consumed by the season-end pipeline).</summary>
    public double Form { get; init; }

    public bool Retired { get; init; }
}

/// <summary>Per-team career state.</summary>
public sealed record TeamCareerState
{
    /// <summary>Team id within the current pack (equals the lineage id in v1 packs).</summary>
    public required string TeamId { get; init; }

    /// <summary>Lineage id, stable across era packs ("team.lotus") — the M6 era-transition key.</summary>
    public required string LineageId { get; init; }

    /// <summary>Budget tier 1–5; 5 is the richest (tier drives scalar bands, salary bands,
    /// and expectations).</summary>
    public required int Tier { get; init; }
}

/// <summary>The player's career state.</summary>
public sealed record PlayerCareerState
{
    /// <summary>Reputation 0–100.</summary>
    public double Reputation { get; init; }

    /// <summary>Overperformance index: EWMA of (expectedFinish − actualFinish).</summary>
    public double Opi { get; init; }

    /// <summary>Pace anchor: EWMA (α=0.3) of the player's implied pace in Opponent Skill
    /// slider percent. 0 means "not yet calibrated" — the first round seeds it directly.</summary>
    public double PaceAnchor { get; init; }

    public int SeasonsCompleted { get; init; }

    public string? CurrentTeamId { get; init; }

    /// <summary>EXACT ams2LiveryName of the player's seat — identifies which pack entry the
    /// player occupies (that entry is excluded from the AI seat market).</summary>
    public string? LiveryName { get; init; }
}

/// <summary>A driver available to the AI seat market (free agents / journeymen the caller
/// authors or carries between seasons). Pay budget is in Budget Units per season —
/// era-correct pay-driver seats are first-class.</summary>
public sealed record SeatCandidate
{
    public required string DriverId { get; init; }

    public required double RaceSkill { get; init; }

    public required int Age { get; init; }

    /// <summary>Sponsorship money the driver brings, in BU/season (0 = pure merit hire).</summary>
    public double PayBudgetBu { get; init; }
}

/// <summary>A season-end offer letter to the player.</summary>
public sealed record PlayerOffer
{
    public required string TeamId { get; init; }

    public required int Tier { get; init; }

    /// <summary>Offered salary in Budget Units per season.</summary>
    public required double SalaryBu { get; init; }

    /// <summary>The archetype-weighted score that ranked this offer (kept for the "why?" inspector).</summary>
    public required double Score { get; init; }
}

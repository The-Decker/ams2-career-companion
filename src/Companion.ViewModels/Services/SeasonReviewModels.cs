namespace Companion.ViewModels.Services;

/// <summary>
/// The season review + offers screen data (docs/dev/m5-fix-integration.md, "App wiring"):
/// the player's final folded numbers, the journal's headline digest, and the offer letters
/// scored from the final round's FOLDED reputation/OPI.
/// </summary>
public sealed record SeasonReviewModel
{
    public required int SeasonYear { get; init; }

    /// <summary>Final championship position; null when the player never classified.</summary>
    public int? PlayerPosition { get; init; }

    /// <summary>Reputation after the season-end pipeline (season delta applied).</summary>
    public required double FinalReputation { get; init; }

    public required double FinalOpi { get; init; }

    /// <summary>Every journaled headline of the season, in journal order — the digest.</summary>
    public required IReadOnlyList<string> Headlines { get; init; }

    /// <summary>Offer letters in the sim's ranking order.</summary>
    public required IReadOnlyList<SeasonOfferModel> Offers { get; init; }

    /// <summary>The accepted offer's team id; null while the choice is open.</summary>
    public string? AcceptedTeamId { get; init; }
}

/// <summary>One offer letter, resolved against the pack for display.</summary>
public sealed record SeasonOfferModel
{
    public required string TeamId { get; init; }

    public required string TeamName { get; init; }

    /// <summary>Budget tier 1–5 of the offering team.</summary>
    public required int Tier { get; init; }

    /// <summary>Offered salary in Budget Units per season.</summary>
    public required double SalaryBu { get; init; }

    /// <summary>The archetype-weighted score that ranked this offer.</summary>
    public required double Score { get; init; }

    public required bool Accepted { get; init; }
}

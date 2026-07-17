using Companion.Core.Dynasty;

namespace Companion.ViewModels.Services;

/// <summary>
/// The Dynasty owner-economy dashboard — the complete bindable projection of the folded ledger
/// (docs/dev/dynasty-tycoon-economy.md §9). A pure DISPLAY read over the carried
/// <see cref="DynastyEconomyState"/>, the pinned rules tables, this season's economy journal
/// rows, and the pending (declared, not-yet-folded) decisions. All money is pre-formatted for
/// display at this boundary — exact Rational never leaves the fold. Null for every non-economy
/// career. Decisions go through <see cref="ICareerSession.DeclareEconomyDecision"/>; this model
/// only reports.
/// </summary>
public sealed record DynastyEconomyDashboard
{
    /// <summary>The folded balance, display-formatted ("88,000" / "-2,500").</summary>
    public required string Balance { get; init; }

    public required bool InDeficit { get; init; }

    /// <summary>Consecutive rounds ended in deficit so far.</summary>
    public required int DeficitRounds { get; init; }

    /// <summary>The data-defined grace: one deficit round beyond this folds the team.</summary>
    public required int GraceRounds { get; init; }

    /// <summary>The era-scaled overdraft limit (immediate bankruptcy at or below it).</summary>
    public required string HardFloor { get; init; }

    public required bool Bankrupt { get; init; }

    /// <summary>Folded car development level (before pending buys).</summary>
    public required int DevelopmentLevel { get; init; }

    public required int DevelopmentMaxLevel { get; init; }

    /// <summary>Cost of the NEXT increment counting pending buys ("" at the cap).</summary>
    public required string NextDevelopmentCost { get; init; }

    /// <summary>The programme is at its cap counting pending buys.</summary>
    public required bool DevelopmentAtCap { get; init; }

    /// <summary>Current engineering staff tier (0 = none).</summary>
    public required int StaffTier { get; init; }

    public required IReadOnlyList<DynastyStaffOptionModel> StaffOptions { get; init; }

    /// <summary>The second seat's contract economics (the occupant is always the pack teammate).</summary>
    public required SecondSeatDeal SecondSeat { get; init; }

    /// <summary>The retained deal's season salary at the current team's tier, display-formatted.</summary>
    public required string SecondSeatSalaryPerSeason { get; init; }

    /// <summary>The pay-driver deal's season backing, display-formatted.</summary>
    public required string PayDriverBackingPerSeason { get; init; }

    public required IReadOnlyList<DynastySponsorContractModel> ActiveSponsors { get; init; }

    /// <summary>Every board deal whose signing window covers this season, with eligibility.</summary>
    public required IReadOnlyList<DynastySponsorOfferModel> SponsorBoard { get; init; }

    /// <summary>This season's settlement lines (rounds + the season row), newest first.</summary>
    public required IReadOnlyList<DynastyLedgerLineModel> Statement { get; init; }

    /// <summary>Decisions declared for the next round, in the order the fold will apply them.</summary>
    public required IReadOnlyList<DynastyPendingDecisionModel> PendingDecisions { get; init; }

    /// <summary>The round pending decisions target (the next unfolded round).</summary>
    public required int NextRound { get; init; }

    public bool HasPendingDecisions => PendingDecisions.Count > 0;
}

public sealed record DynastyStaffOptionModel
{
    /// <summary>0 = no staff.</summary>
    public required int Tier { get; init; }

    public required string UpkeepPerSeason { get; init; }

    public required bool IsCurrent { get; init; }
}

public sealed record DynastySponsorContractModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>"title" / "major" / "minor".</summary>
    public required string TierSlot { get; init; }

    public required int SeasonsRemaining { get; init; }
    public required string PerRace { get; init; }
    public required string PerSeason { get; init; }
}

public sealed record DynastySponsorOfferModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string TierSlot { get; init; }
    public required string SigningBonus { get; init; }
    public required string PerRace { get; init; }
    public required string PerSeason { get; init; }
    public required int ContractSeasons { get; init; }

    /// <summary>Signable right now (window + slot + floors all pass, counting pending signs).</summary>
    public required bool Eligible { get; init; }

    /// <summary>Why not, when <see cref="Eligible"/> is false ("" otherwise).</summary>
    public required string IneligibleReason { get; init; }
}

public sealed record DynastyLedgerLineModel
{
    /// <summary>"Round 3" / "Season settlement" / "Decisions, round 2".</summary>
    public required string Label { get; init; }

    public int? Round { get; init; }

    /// <summary>Signed display amount for the line's net movement.</summary>
    public required string Net { get; init; }

    public required string BalanceAfter { get; init; }

    public required bool IsDeficit { get; init; }
}

public sealed record DynastyPendingDecisionModel
{
    /// <summary>Human line: "Sign Apex Lubricants" / "Buy development (stage 2)".</summary>
    public required string Description { get; init; }

    /// <summary>The decision's immediate money effect, signed display ("" for free levers).</summary>
    public required string Amount { get; init; }

    /// <summary>The journal seq of the INPUT row (a stable identity for the list).</summary>
    public required long Seq { get; init; }
}

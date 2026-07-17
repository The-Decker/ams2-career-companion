using System.Text.Json.Serialization;

namespace Companion.Core.Dynasty;

/// <summary>
/// One player economic decision — the payload of an <c>economy.decision</c> journal INPUT row
/// (provenance-excluded; the fold reads it back and applies it deterministically, so its DERIVED
/// effects are byte-compared). Declared for the next UNFOLDED round (the
/// <c>PlayerRoundConditionsStore.Declare</c> shape: refused once that round has a raw result) and
/// applied by that round's fold in journal seq order. The acceptance layer validates
/// affordability/availability BEFORE journaling; the fold applies journaled decisions
/// unconditionally and throws only on impossible (tampered) inputs.
/// </summary>
public sealed record DynastyEconomyDecision
{
    public required DynastyEconomyDecisionKind Kind { get; init; }

    /// <summary>The sponsor board id for <see cref="DynastyEconomyDecisionKind.SignSponsor"/> /
    /// <see cref="DynastyEconomyDecisionKind.DropSponsor"/>; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SponsorId { get; init; }

    /// <summary>The target engineering tier for <see cref="DynastyEconomyDecisionKind.SetStaff"/>
    /// (0 = no staff); null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StaffTier { get; init; }

    /// <summary>The target deal for <see cref="DynastyEconomyDecisionKind.SetSecondSeat"/>;
    /// null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecondSeatDeal? SecondSeat { get; init; }
}

/// <summary>The Dynasty economy decision surface (docs/dev/dynasty-tycoon-economy.md §5).
/// Serialized camelCase into the journal — part of the save format, never rename.</summary>
public enum DynastyEconomyDecisionKind
{
    /// <summary>Sign a sponsor from the board: the contract starts and the signing bonus lands.</summary>
    SignSponsor,

    /// <summary>Walk away from an active sponsor contract (no refund, no penalty).</summary>
    DropSponsor,

    /// <summary>Buy the next car development increment (escalating cost, staff-discounted).</summary>
    BuyDevelopment,

    /// <summary>Set the engineering staff tier; upkeep accrues from the next settlement on.</summary>
    SetStaff,

    /// <summary>Switch the second seat's contract economics (retained ↔ pay-driver).</summary>
    SetSecondSeat,
}

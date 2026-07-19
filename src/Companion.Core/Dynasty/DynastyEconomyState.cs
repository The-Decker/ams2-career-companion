using System.Text.Json.Serialization;
using Companion.Core.Numerics;

namespace Companion.Core.Dynasty;

/// <summary>
/// The Grand Prix Dynasty owner-economy's folded per-career state, rides
/// <see cref="Career.PlayerCareerState.Economy"/> and is null for every non-Dynasty career (and
/// every Dynasty career created before the economy shipped). The fold owns it exactly like
/// <see cref="Companion.Core.Smgp.SmgpState"/>: seeded once at career creation (mode
/// grandPrixDynasty + the explicit creation opt-in), carried forward each round via record
/// <c>with</c>, and mutated only by the deterministic economy fold over journaled results and
/// journaled <c>economy.decision</c> INPUT rows, so re-simulation re-derives it byte-identically.
/// The sponsor dictionary is kept in ORDINAL KEY ORDER via the With* helpers because the
/// serialized state cell must be canonical (replay byte-compares these blobs).
/// All money is exact <see cref="Rational"/> (serialized as "3"/"1/7" strings by CoreJson).
/// </summary>
public sealed record DynastyEconomyState
{
    /// <summary>The economy rules schema version this career was created under
    /// (<see cref="DynastyEconomyRules.CurrentSchemaVersion"/>), pinned at creation. The fold
    /// refuses to run against a rules file of a different schema version, so an old career can
    /// never silently drift onto later balance tables.</summary>
    public required int Version { get; init; }

    /// <summary>The team's current funds. Seeded from the starting-funds table (by the starting
    /// team's tier, era-scaled) at creation; every subsequent movement is a journaled derived row.</summary>
    public required Rational Balance { get; init; }

    /// <summary>Car development level bought so far this season (0 = the pack-authored baseline
    /// car). Feeds the seat-strength channel per level; partially carries across the season
    /// boundary via the data-defined carryover fraction. Omitted when 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DevelopmentLevel { get; init; }

    /// <summary>Engineering staff tier (0 = none). Upkeep accrues per round; each tier discounts
    /// development increments. Omitted when 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StaffTier { get; init; }

    /// <summary>The second seat's contract economics (the occupant is always the pack's authored
    /// teammate, the faithful grid is never touched). Retained (default): the team pays the
    /// salary and collects the second car's race prize. PayDriver: no salary, backing income
    /// accrues instead, and the second car's prize is forfeit. Omitted when Retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SecondSeatDeal SecondSeat { get; init; }

    /// <summary>Active sponsor contracts keyed by sponsor id (ordinal key order, canonical).
    /// Terms resolve from the rules board by id + <see cref="Version"/>.</summary>
    public IReadOnlyDictionary<string, DynastySponsorContract> Sponsors { get; init; } = EmptySponsors;

    /// <summary>Consecutive rounds the season has ENDED in deficit (balance &lt; 0 after the round
    /// settlement). Resets to 0 the moment a settlement ends at or above zero. Past the
    /// data-defined grace the career goes bankrupt. Omitted when 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DeficitRounds { get; init; }

    /// <summary>The team went BANKRUPT, TERMINAL. The career stops accepting rounds (mirrors
    /// <see cref="Career.PlayerCareerState.Deceased"/> and the SMGP CareerOver floor). Carried
    /// forward verbatim, never reset. Omitted when false.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bankrupt { get; init; }

    /// <summary>This sponsor's active contract, or null when not signed.</summary>
    public DynastySponsorContract? SponsorContract(string sponsorId) =>
        Sponsors.TryGetValue(sponsorId, out var contract) ? contract : null;

    /// <summary>The state with this sponsor's contract set/replaced, keys re-canonicalized.</summary>
    public DynastyEconomyState WithSponsor(string sponsorId, DynastySponsorContract contract) =>
        this with { Sponsors = Canonical(Sponsors, sponsorId, contract) };

    /// <summary>The state with this sponsor's contract removed (a no-op when absent), keys
    /// re-canonicalized.</summary>
    public DynastyEconomyState WithoutSponsor(string sponsorId)
    {
        if (!Sponsors.ContainsKey(sponsorId))
            return this;
        var remaining = new Dictionary<string, DynastySponsorContract>(StringComparer.Ordinal);
        foreach (var pair in Sponsors.Where(p => !string.Equals(p.Key, sponsorId, StringComparison.Ordinal))
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
            remaining[pair.Key] = pair.Value;
        return this with { Sponsors = remaining };
    }

    // STRUCTURAL equality, the record default compares the sponsor dictionary by REFERENCE,
    // which would make a re-derived rollover start state unequal to the deserialized stored one
    // and fail the byte-identical replay gate at every season boundary (the exact bug SmgpState
    // documents). The dictionary is canonically ordered, so sequence comparison is exact.
    public bool Equals(DynastyEconomyState? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return Version == other.Version
            && Balance == other.Balance
            && DevelopmentLevel == other.DevelopmentLevel
            && StaffTier == other.StaffTier
            && SecondSeat == other.SecondSeat
            && DeficitRounds == other.DeficitRounds
            && Bankrupt == other.Bankrupt
            && Sponsors.SequenceEqual(other.Sponsors);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Balance);
        hash.Add(DevelopmentLevel);
        hash.Add(StaffTier);
        hash.Add(SecondSeat);
        hash.Add(DeficitRounds);
        hash.Add(Bankrupt);
        foreach (var pair in Sponsors)
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value);
        }
        return hash.ToHashCode();
    }

    private static readonly IReadOnlyDictionary<string, DynastySponsorContract> EmptySponsors =
        new Dictionary<string, DynastySponsorContract>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, DynastySponsorContract> Canonical(
        IReadOnlyDictionary<string, DynastySponsorContract> source,
        string key,
        DynastySponsorContract value)
    {
        var merged = new Dictionary<string, DynastySponsorContract>(source, StringComparer.Ordinal)
        {
            [key] = value,
        };
        var canonical = new Dictionary<string, DynastySponsorContract>(merged.Count, StringComparer.Ordinal);
        foreach (var pair in merged.OrderBy(p => p.Key, StringComparer.Ordinal))
            canonical[pair.Key] = pair.Value;
        return canonical;
    }
}

/// <summary>The second seat's contract economics (see <see cref="DynastyEconomyState.SecondSeat"/>).
/// Retained = 0 is the enum default so it is omitted from serialization.</summary>
public enum SecondSeatDeal
{
    /// <summary>The team pays the second driver's salary and collects that car's race prize.</summary>
    Retained = 0,

    /// <summary>A pay-driver deal: no salary, backing income accrues per round, but the second
    /// car's race prize belongs to the driver's backers, not the team.</summary>
    PayDriver = 1,
}

/// <summary>One active sponsor contract. A plain value record, default structural equality is
/// exact, so <see cref="DynastyEconomyState"/>'s replay gate compares it correctly. The deal's
/// terms (payments, bonuses) live in the rules board, resolved by sponsor id.</summary>
public sealed record DynastySponsorContract
{
    /// <summary>Whole seasons of the contract still to run INCLUDING the current one. Decremented
    /// at each season end; the contract expires (is removed) when it reaches 0.</summary>
    public required int SeasonsRemaining { get; init; }
}

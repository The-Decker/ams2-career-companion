using System.Text.Json.Serialization;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP replica mode's folded per-career state (M3) — rides
/// <see cref="Career.PlayerCareerState.Smgp"/> and is null for every non-smgp career. The fold
/// owns it exactly like the rest of the player state: seeded at career creation (pack
/// careerStyle "smgp" + the explicit creation opt-in), carried forward each round via record
/// <c>with</c>, and mutated only from journaled inputs (the envelope's rival calls) so
/// re-simulation re-derives it byte-identically. Dictionaries are kept in ORDINAL KEY ORDER via
/// the With* helpers — the serialized state cell must be canonical because replay byte-compares
/// these blobs.
/// </summary>
public sealed record SmgpState
{
    /// <summary>EXACT ams2LiveryName of the car the player drives THIS season. Starts as the
    /// wizard seat pick and changes on an accepted seat swap (the mode's ladder); the round grid
    /// seats the player here rather than the career-level livery (M3 slice 3).</summary>
    public required string CurrentSeatLivery { get; init; }

    /// <summary>Per-rival battle streaks keyed by pack driver id (ordinal key order).</summary>
    public IReadOnlyDictionary<string, SmgpBattleTally> Tallies { get; init; } = EmptyTallies;

    /// <summary>Championships won in the mode (two = the replica is beaten).</summary>
    public int Titles { get; init; }

    /// <summary>The game-over state: the player lost too many battles at the LEVEL D floor
    /// (<see cref="SmgpRules.FloorLossLimit"/>) — kicked out of F1 SMGP. The one hard-fail state;
    /// the career stops accepting rounds.</summary>
    public bool CareerOver { get; init; }

    /// <summary>Rival battles LOST while in a LEVEL D team, cumulative across rivals. At
    /// <see cref="SmgpRules.FloorLossLimit"/> the career ends. Resets to 0 when the player climbs
    /// out of D (a promotion) and between seasons. Omitted when 0 (WhenWritingDefault).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int FloorLosses { get; init; }

    /// <summary>Seat-swap displacements applied to the AI field (pack driver id → the
    /// ams2LiveryName that driver NOW occupies), ordinal key order. Applied by the grid resolver
    /// after the cap so swapped rivals really drive their new cars (M3 slice 3). A driver with
    /// no season ENTRY here (the reserved title-defense challenger) is INTRODUCED into that car,
    /// replacing its authored occupant.</summary>
    public IReadOnlyDictionary<string, string> AiSeatOverrides { get; init; } = EmptyOverrides;

    /// <summary>The Madonna title defense is LIVE this season (M3 slice 4): set when the player
    /// wins the championship (next season starts in Madonna; the reserved challenger forces
    /// battles at rounds 1 + 2), cleared when the defense resolves — or at season end, unresolved
    /// = survived. Omitted when false so pre-slice-4 state cells parse/serialize unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TitleDefense { get; init; }

    /// <summary>Round 1's defense battle outcome, carried so <see cref="SmgpRules.TitleDefense"/>
    /// can resolve both rounds together at round 2. Void (the default, omitted) = not fought /
    /// no better than a void — exactly how the resolve treats it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SmgpBattleOutcome DefenseRound1 { get; init; }

    /// <summary>The TWO-PHASE promotion seam (3c-2): a career created after this shipped defers a
    /// two-wins seat-swap offer to the post-race PROMOTION SCREEN instead of applying it inline in
    /// the battle fold. Seeded true at creation for new smgp careers; OMITTED when false so every
    /// pre-3c-2 state cell parses as false and keeps the legacy inline-apply path — the byte-identical
    /// gate. Carried forward each round (and across the season reset / champion rollover) like every
    /// other state field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TwoPhasePromotion { get; init; }

    /// <summary>The PER-SEASON DNQ RE-ROLL gate (17-season campaign): a career created after this shipped
    /// re-rolls its backmarker DNQ field for every season 2+ (each season a fresh seeded field), instead
    /// of every season sharing season 1's pinned roll. Seeded true at creation for new smgp careers with a
    /// DNQ field; OMITTED when false so every pre-change state cell parses as false and keeps the single
    /// pinned field across all seasons — the byte-identical gate. Carried forward each round and across the
    /// season reset / champion rollover (it is a career-level decision, never reset). Read from the season
    /// START state to decide whether <see cref="SmgpDnqField.ForSeason"/> transforms the pack on both the
    /// live-fold and replay paths.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PerSeasonDnq { get; init; }

    /// <summary>New-career gate for the standings-driven between-season entry reshuffle. Omitted
    /// when false so legacy SMGP careers keep their authored entries byte-identically.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StandingsReshuffle { get; init; }

    /// <summary>A two-wins seat-swap offer AWAITING the player's post-race accept/decline on the
    /// promotion screen (3c-2, two-phase careers only): the battle fold records it here INSTEAD of
    /// moving the seat, and the resolution fold (driven by the journaled <c>smgp.swap</c> input,
    /// default = the standing up-front answer) applies-or-clears it. Null (omitted) = no offer
    /// pending — every non-smgp career, every legacy career, and every resolved round.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SmgpPendingOffer? PendingSwap { get; init; }

    /// <summary>This rival's running tally (a rival never battled starts empty).</summary>
    public SmgpBattleTally TallyFor(string rivalDriverId) =>
        Tallies.TryGetValue(rivalDriverId, out var tally) ? tally : SmgpBattleTally.Empty;

    /// <summary>The state with this rival's tally replaced, keys re-canonicalized.</summary>
    public SmgpState WithTally(string rivalDriverId, SmgpBattleTally tally) =>
        this with { Tallies = Canonical(Tallies, rivalDriverId, tally) };

    /// <summary>The state with one AI seat override set/replaced, keys re-canonicalized.</summary>
    public SmgpState WithAiSeatOverride(string driverId, string ams2LiveryName) =>
        this with { AiSeatOverrides = Canonical(AiSeatOverrides, driverId, ams2LiveryName) };

    /// <summary>The between-seasons reset: rival streaks and the defense scratchpad clear (each
    /// season's ladder starts fresh); seats, titles and the career-over flag carry.</summary>
    public SmgpState WithSeasonReset() => this with
    {
        Tallies = EmptyTallies,
        TitleDefense = false,
        DefenseRound1 = SmgpBattleOutcome.Void,
        FloorLosses = 0,
        // An offer never answered by season's end lapses — the new season's ladder starts clean.
        // (TwoPhasePromotion is the gate, so it is NOT reset; it carries via `this with`.)
        PendingSwap = null,
    };

    // STRUCTURAL equality — the record default compares the dictionaries by REFERENCE, which
    // would make the rollover verifier's re-derived start state unequal to the deserialized
    // stored one and fail the byte-identical replay gate at every smgp season boundary (the
    // exact bug GridSelection/CharacterProfile hit). Both dictionaries are canonically ordered,
    // so order-sensitive sequence comparison is exact.
    public bool Equals(SmgpState? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return string.Equals(CurrentSeatLivery, other.CurrentSeatLivery, StringComparison.Ordinal)
            && Titles == other.Titles
            && CareerOver == other.CareerOver
            && TitleDefense == other.TitleDefense
            && DefenseRound1 == other.DefenseRound1
            && FloorLosses == other.FloorLosses
            && TwoPhasePromotion == other.TwoPhasePromotion
            && PerSeasonDnq == other.PerSeasonDnq
            && StandingsReshuffle == other.StandingsReshuffle
            && Equals(PendingSwap, other.PendingSwap)
            && Tallies.SequenceEqual(other.Tallies)
            && AiSeatOverrides.SequenceEqual(other.AiSeatOverrides);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CurrentSeatLivery, StringComparer.Ordinal);
        hash.Add(Titles);
        hash.Add(CareerOver);
        hash.Add(TitleDefense);
        hash.Add(DefenseRound1);
        hash.Add(FloorLosses);
        hash.Add(TwoPhasePromotion);
        hash.Add(PerSeasonDnq);
        hash.Add(StandingsReshuffle);
        hash.Add(PendingSwap);
        foreach (var pair in Tallies)
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value);
        }
        foreach (var pair in AiSeatOverrides)
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    private static readonly IReadOnlyDictionary<string, SmgpBattleTally> EmptyTallies =
        new Dictionary<string, SmgpBattleTally>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>A copy with <paramref name="key"/> set, rebuilt in ordinal key order so the
    /// serialized JSON object is canonical no matter what order battles happened in.</summary>
    private static IReadOnlyDictionary<string, TValue> Canonical<TValue>(
        IReadOnlyDictionary<string, TValue> source, string key, TValue value)
    {
        var merged = new Dictionary<string, TValue>(source, StringComparer.Ordinal)
        {
            [key] = value,
        };
        var canonical = new Dictionary<string, TValue>(merged.Count, StringComparer.Ordinal);
        foreach (var pair in merged.OrderBy(p => p.Key, StringComparer.Ordinal))
            canonical[pair.Key] = pair.Value;
        return canonical;
    }
}

/// <summary>A two-wins seat-swap offer the battle fold deferred to the promotion screen (3c-2):
/// the rival the player beat twice and the exact car (ams2LiveryName) they are offered — the seat
/// the resolution moves the player into on ACCEPT. A plain value record: default structural equality
/// is exact (both fields are ordinal strings), so <see cref="SmgpState"/>'s byte-identical replay
/// gate compares it correctly.</summary>
public sealed record SmgpPendingOffer
{
    /// <summary>The rival (pack driver id) the player beat twice to earn the offer.</summary>
    public required string RivalDriverId { get; init; }

    /// <summary>The car the player is offered — the rival's current seat (ams2LiveryName); the
    /// resolution sets <see cref="SmgpState.CurrentSeatLivery"/> to this on accept.</summary>
    public required string OfferedSeat { get; init; }
}

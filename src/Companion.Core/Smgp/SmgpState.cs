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

    /// <summary>The Zeroforce game-over state: a rival took the player's seat with nothing below
    /// LEVEL D. The one hard-fail state — the career stops accepting rounds.</summary>
    public bool CareerOver { get; init; }

    /// <summary>Seat-swap displacements applied to the AI field (pack driver id → the
    /// ams2LiveryName that driver NOW occupies), ordinal key order. Applied by the grid resolver
    /// after the cap so swapped rivals really drive their new cars (M3 slice 3).</summary>
    public IReadOnlyDictionary<string, string> AiSeatOverrides { get; init; } = EmptyOverrides;

    /// <summary>This rival's running tally (a rival never battled starts empty).</summary>
    public SmgpBattleTally TallyFor(string rivalDriverId) =>
        Tallies.TryGetValue(rivalDriverId, out var tally) ? tally : SmgpBattleTally.Empty;

    /// <summary>The state with this rival's tally replaced, keys re-canonicalized.</summary>
    public SmgpState WithTally(string rivalDriverId, SmgpBattleTally tally) =>
        this with { Tallies = Canonical(Tallies, rivalDriverId, tally) };

    /// <summary>The state with one AI seat override set/replaced, keys re-canonicalized.</summary>
    public SmgpState WithAiSeatOverride(string driverId, string ams2LiveryName) =>
        this with { AiSeatOverrides = Canonical(AiSeatOverrides, driverId, ams2LiveryName) };

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

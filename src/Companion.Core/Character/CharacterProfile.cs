using System.Text.Json.Serialization;

namespace Companion.Core.Character;

/// <summary>
/// A player's authored character: the seven stats (five talent + two meta, id → 0..1), the chosen
/// perk ids, and the CP not yet spent. This is the INPUT written once at creation (journaled as
/// <c>player.character</c>) and folded into <c>PlayerCareerState</c> — the sim derives the
/// player-seat rating writes and the <see cref="PlayerPerkModifiers"/> from it deterministically, so
/// the same profile reproduces the career byte-for-byte (docs/dev/character-system.md §5-6).
/// </summary>
public sealed record CharacterProfile
{
    /// <summary>All seven stats by id (pace/oneLap/craft/racecraft/adaptability + marketability/
    /// durability), each 0..1.</summary>
    public required IReadOnlyDictionary<string, double> Stats { get; init; }

    public required IReadOnlyList<string> PerkIds { get; init; }

    /// <summary>The player's chosen driver name — the identity the whole app uses (news, standings,
    /// dossier), rather than the historical driver whose seat they took. Empty for a legacy
    /// character created before naming existed (then the app falls back to the seat's driver).</summary>
    public string Name { get; init; } = "";

    /// <summary>Character Points left over at CREATION (immutable) — the starting bank. The pool
    /// available to spend later is this plus level grants minus <see cref="CpSpent"/>
    /// (<see cref="CharacterProgress.AvailableCp"/>).</summary>
    public int CpUnspent { get; init; }

    /// <summary>Total character points SPENT between seasons so far (raising stats, adding perks).
    /// Accumulates as the driver develops; 0 for a character that has never spent. Omitted from the
    /// state blob when 0, so a never-spent character serialises exactly as before.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CpSpent { get; init; }

    public double Stat(string id) => Stats.GetValueOrDefault(id);

    // Structural equality over the collections. The compiler-generated record equality would
    // compare Stats (a dictionary) and PerkIds (a list) by REFERENCE, so two structurally-identical
    // profiles deserialized separately (e.g. a re-derived season start state vs the stored one)
    // would compare unequal — a FALSE replay divergence at every season boundary. Comparing by value
    // makes PlayerCareerState's record equality (which the replay start-state gate uses) match the
    // JSON the DB actually stores. (Increment 4a — determinism fix.)
    public bool Equals(CharacterProfile? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return CpUnspent == other.CpUnspent
            && CpSpent == other.CpSpent
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && PerkIds.SequenceEqual(other.PerkIds)
            && StatsEqual(Stats, other.Stats);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CpUnspent);
        hash.Add(CpSpent);
        hash.Add(Name);
        foreach (string id in PerkIds)
            hash.Add(id);
        foreach (var (key, value) in Stats.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(key);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    private static bool StatsEqual(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out double other) || other != value)
                return false;
        return true;
    }

    /// <summary>Builds the complete, in-budget character an archetype preset describes — the
    /// one-click default at creation. Talent stats and meta stats merge into the one stat map.</summary>
    public static CharacterProfile FromArchetype(Archetype archetype)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, value) in archetype.StartStats)
            stats[id] = value;
        foreach (var (id, value) in archetype.StartMeta)
            stats[id] = value;

        return new CharacterProfile
        {
            Stats = stats,
            PerkIds = archetype.PerkIds.ToList(),
        };
    }
}

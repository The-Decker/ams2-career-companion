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

    /// <summary>Character Points not yet spent (leftover at creation + level grants), spendable
    /// between seasons.</summary>
    public int CpUnspent { get; init; }

    public double Stat(string id) => Stats.GetValueOrDefault(id);

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

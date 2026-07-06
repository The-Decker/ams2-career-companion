namespace Companion.Core.Character;

/// <summary>
/// One between-season character-development spend: raise a stat by one step, or add a perk. It is a
/// journaled INPUT (<c>player.statSpend</c>), re-applied on replay so the evolving driver reproduces
/// byte-for-byte (character depth 4). Player choices carry provenance-excluded, exactly like an
/// accepted offer.
/// </summary>
public sealed record CharacterSpend
{
    /// <summary>"stat" (raise the named stat one step) or "perk" (add the named perk).</summary>
    public required string Kind { get; init; }

    /// <summary>The stat id (for "stat") or perk id (for "perk").</summary>
    public required string Target { get; init; }

    /// <summary>The character points this spend costs (a stat step's cost, or the perk's cost).</summary>
    public required int Cost { get; init; }

    public static CharacterSpend Stat(string statId, int cost) => new() { Kind = "stat", Target = statId, Cost = cost };

    public static CharacterSpend Perk(string perkId, int cost) => new() { Kind = "perk", Target = perkId, Cost = cost };
}

/// <summary>Pure character-progression helpers: how much CP a driver has to spend, and how a spend
/// evolves the character.</summary>
public static class CharacterProgress
{
    /// <summary>Character points available to spend right now: the creation leftover, plus the level
    /// grants earned so far, minus everything already spent.</summary>
    public static int AvailableCp(CharacterProfile character, int level, CharacterRules rules) =>
        character.CpUnspent
        + rules.Levels.LevelGrants.CharacterPointsPerLevel * Math.Max(0, level - 1)
        - character.CpSpent;

    /// <summary>Applies a spend to the character — raise a stat by one step (capped at
    /// <c>statCapPerRating</c>, which is higher than the creation cap, so a driver develops beyond
    /// where they started) or add a perk — and charges the cost to <see cref="CharacterProfile.CpSpent"/>.
    /// Pure; the caller validates affordability first.</summary>
    public static CharacterProfile Apply(CharacterProfile character, CharacterSpend spend, CharacterRules rules)
    {
        var stats = new Dictionary<string, double>(character.Stats, StringComparer.Ordinal);
        var perks = character.PerkIds.ToList();

        if (spend.Kind == "stat")
        {
            double step = rules.Levels.LevelGrants.StatStepValue;
            double cap = rules.Levels.LevelGrants.StatCapPerRating;
            stats[spend.Target] = Math.Clamp(stats.GetValueOrDefault(spend.Target) + step, 0.0, cap);
        }
        else if (spend.Kind == "perk" && !perks.Contains(spend.Target, StringComparer.Ordinal))
        {
            perks.Add(spend.Target);
        }

        return character with
        {
            Stats = stats,
            PerkIds = perks,
            CpSpent = character.CpSpent + spend.Cost,
        };
    }

    /// <summary>Applies a sequence of spends in order (a whole between-season development batch).</summary>
    public static CharacterProfile ApplyAll(
        CharacterProfile character, IReadOnlyList<CharacterSpend> spends, CharacterRules rules)
    {
        foreach (var spend in spends)
            character = Apply(character, spend, rules);
        return character;
    }
}

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

/// <summary>A milestone-token respec input: unlearn one post-creation perk and refund its authored
/// cost into the shared Skill Point pool. The source perk is journaled as player.respec.</summary>
public sealed record CharacterRespec
{
    public required string NodeId { get; init; }
    public required int Refund { get; init; }
}

public static class CharacterRespecMath
{
    public static int AvailableTokens(int level, int used, CharacterRules rules)
    {
        int every = rules.Levels.LevelGrants.MilestoneEveryLevels;
        if (every <= 0 || !string.Equals(
                rules.Levels.LevelGrants.MilestoneGrant, "respecToken", StringComparison.Ordinal))
            return 0;
        int granted = (Math.Max(0, level) / every)
            * Math.Max(0, rules.Respec.RespecTokenGrantsPerMilestone);
        return Math.Max(0, granted - Math.Max(0, used));
    }
}

/// <summary>Pure character-progression helpers: how much CP a driver has to spend, and how a spend
/// evolves the character.</summary>
public static class CharacterProgress
{
    /// <summary>Character points available to spend right now: the creation leftover, plus the level
    /// grants earned so far, minus everything already spent.</summary>
    public static int AvailableCp(CharacterProfile character, int level, CharacterRules rules)
    {
        int perLevel = rules.Levels.LevelGrants.CharacterPointsPerLevel
            + PerkResolver.Resolve(character, rules).StatPointsPerLevelBonus;
        return character.CpUnspent
            + Math.Max(0, perLevel) * Math.Max(0, level - 1)
            - character.CpSpent;
    }

    /// <summary>Applies a spend to the character — raise a stat by one step (capped at
    /// <c>statCapPerRating</c>, which is higher than the creation cap, so a driver develops beyond
    /// where they started) or add a perk — and charges the cost to <see cref="CharacterProfile.CpSpent"/>.
    /// Pure; the caller validates affordability first.</summary>
    public static CharacterProfile Apply(CharacterProfile character, CharacterSpend spend, CharacterRules rules)
    {
        var stats = new Dictionary<string, double>(character.Stats, StringComparer.Ordinal);
        var perks = character.PerkIds.ToList();
        var skillNodes = character.SkillNodeIds.ToList();

        if (spend.Kind == "stat")
        {
            var node = rules.SkillTree.TryGetStatNode(spend.Target);
            string statId = node?.Stat ?? spend.Target; // raw stat ids are the legacy journal shape
            double step = rules.Levels.LevelGrants.StatStepValue;
            double cap = rules.Levels.LevelGrants.StatCapPerRating;
            stats[statId] = Math.Clamp(stats.GetValueOrDefault(statId) + step, 0.0, cap);
            if (node is not null && !skillNodes.Contains(node.Id, StringComparer.Ordinal))
                skillNodes.Add(node.Id);
        }
        else if (spend.Kind == "perk" && !perks.Contains(spend.Target, StringComparer.Ordinal))
        {
            perks.Add(spend.Target);
        }

        return character with
        {
            Stats = stats,
            PerkIds = perks,
            UnlockedSkillNodeIds = skillNodes.Count == 0 ? null : skillNodes,
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

    /// <summary>Applies respec inputs before the replacement spends at a season boundary.</summary>
    public static CharacterProfile ApplyRespecs(
        CharacterProfile character,
        IReadOnlyList<CharacterRespec> respecs)
    {
        foreach (var respec in respecs)
        {
            if (!character.PerkIds.Contains(respec.NodeId, StringComparer.Ordinal))
                continue;
            character = character with
            {
                PerkIds = character.PerkIds
                    .Where(id => !string.Equals(id, respec.NodeId, StringComparison.Ordinal))
                    .ToList(),
                CpSpent = Math.Max(0, character.CpSpent - Math.Max(0, respec.Refund)),
            };
        }
        return character;
    }
}

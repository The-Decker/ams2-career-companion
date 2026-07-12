namespace Companion.Core.Character;

/// <summary>The display state of one node in the driver's skill tree.</summary>
public enum SkillNodeState
{
    Owned = 0,
    Unlockable = 1,
    Locked = 2,
}

/// <summary>
/// One pure, display-ready skill-tree node. A node is either an existing perk or a repeatable
/// stat raise; this projection never creates a new perk and never mutates the character.
/// </summary>
public sealed record SkillNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int Cost { get; init; }
    public required int Tier { get; init; }
    public required int UnlockLevel { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
    public required SkillNodeState State { get; init; }
    public required string LockReason { get; init; }
}

/// <summary>One named lane of the skill tree.</summary>
public sealed record SkillBranch
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsMeta { get; init; }
    public required IReadOnlyList<SkillNode> Nodes { get; init; }
}

/// <summary>A pure snapshot of every skill-tree branch and node.</summary>
public sealed record SkillTreeSnapshot
{
    public static SkillTreeSnapshot Empty { get; } = new() { Branches = [] };

    public required IReadOnlyList<SkillBranch> Branches { get; init; }
}

/// <summary>
/// Builds the read-only skill-tree projection from the character and additive rules. It is a pure
/// read: unlock commands are journaled elsewhere and the projection consumes no randomness.
/// </summary>
public static class SkillTree
{
    public static SkillTreeSnapshot Build(
        CharacterProfile character,
        int level,
        int availableSp,
        CharacterRules rules)
    {
        if (rules.SkillTree.BranchOrder.Count == 0)
            return SkillTreeSnapshot.Empty;

        var ownedPerks = character.PerkIds.ToHashSet(StringComparer.Ordinal);
        var ownedNodes = character.SkillNodeIds.ToHashSet(StringComparer.Ordinal);
        var branches = new List<SkillBranch>();

        foreach (string branchId in rules.SkillTree.BranchOrder)
        {
            var nodes = new List<SkillNode>();
            foreach (var perk in rules.Perks.Where(p =>
                         string.Equals(p.EffectiveBranch, branchId, StringComparison.Ordinal)))
            {
                nodes.Add(BuildPerk(perk, ownedPerks, level, availableSp, rules));
            }
            foreach (var node in rules.SkillTree.StatNodes.Where(n =>
                         string.Equals(n.Branch, branchId, StringComparison.Ordinal)))
            {
                nodes.Add(BuildStat(node, ownedNodes, level, availableSp, rules));
            }

            branches.Add(new SkillBranch
            {
                Id = branchId,
                Name = CharacterLabels.Category(branchId),
                IsMeta = rules.SkillTree.MetaBranches.Contains(branchId, StringComparer.Ordinal),
                Nodes = nodes.OrderBy(n => n.Tier)
                    .ThenBy(n => n.UnlockLevel)
                    .ThenBy(n => n.Name, StringComparer.Ordinal)
                    .ToList(),
            });
        }

        return new SkillTreeSnapshot { Branches = branches };
    }

    private static SkillNode BuildPerk(
        Perk perk,
        IReadOnlySet<string> owned,
        int level,
        int availableSp,
        CharacterRules rules)
    {
        bool isOwned = owned.Contains(perk.Id);
        var missing = perk.Requires.Where(id => !owned.Contains(id)).ToList();
        var (state, reason) = State(
            isOwned, level, perk.UnlockLevel, perk.Cost, availableSp, missing,
            creationOnly: perk.Cost <= 0, rules);
        return new SkillNode
        {
            Id = perk.Id,
            Name = perk.Name,
            Kind = "perk",
            Cost = perk.Cost,
            Tier = perk.Tier,
            UnlockLevel = perk.UnlockLevel,
            Requires = perk.Requires,
            Benefits = PerkDescriber.Benefits(perk),
            Drawbacks = PerkDescriber.Drawbacks(perk),
            State = state,
            LockReason = reason,
        };
    }

    private static SkillNode BuildStat(
        StatNodeRule node,
        IReadOnlySet<string> owned,
        int level,
        int availableSp,
        CharacterRules rules)
    {
        bool isOwned = owned.Contains(node.Id);
        var missing = node.Requires.Where(id => !owned.Contains(id)).ToList();
        var (state, reason) = State(
            isOwned, level, node.UnlockLevel, node.Cost, availableSp, missing,
            creationOnly: false, rules);
        string statName = CharacterLabels.Stat(node.Stat);
        return new SkillNode
        {
            Id = node.Id,
            Name = node.Name is { Length: > 0 } name ? name : $"Raise {statName}",
            Kind = "stat",
            Cost = node.Cost,
            Tier = node.Tier,
            UnlockLevel = node.UnlockLevel,
            Requires = node.Requires,
            Benefits = [$"+{rules.Levels.LevelGrants.StatStepValue:0.##} {statName}"],
            Drawbacks = [],
            State = state,
            LockReason = reason,
        };
    }

    private static (SkillNodeState State, string Reason) State(
        bool owned,
        int level,
        int unlockLevel,
        int cost,
        int availableSp,
        IReadOnlyList<string> missing,
        bool creationOnly,
        CharacterRules rules)
    {
        if (owned)
            return (SkillNodeState.Owned, "");
        if (creationOnly)
            return (SkillNodeState.Locked, "Creation-only perk");
        if (level < unlockLevel)
            return (SkillNodeState.Locked, $"Reach level {unlockLevel}");
        if (missing.Count > 0)
        {
            string names = string.Join(", ", missing.Select(id => NodeName(id, rules)));
            return (SkillNodeState.Locked, $"Requires: {names}");
        }
        if (availableSp < cost)
            return (SkillNodeState.Locked, $"Costs {cost} SP");
        return (SkillNodeState.Unlockable, "");
    }

    private static string NodeName(string id, CharacterRules rules)
    {
        if (rules.TryGetPerk(id, out var perk))
            return perk.Name;
        var stat = rules.SkillTree.TryGetStatNode(id);
        return stat?.Name is { Length: > 0 } name
            ? name
            : stat is not null ? $"Raise {CharacterLabels.Stat(stat.Stat)}" : id;
    }
}

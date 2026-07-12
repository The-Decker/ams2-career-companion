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
/// Builds the read-only skill-tree projection. Slice 0 deliberately publishes an empty snapshot so
/// the GUI can bind the final contract before the additive rules schema and unlock logic land.
/// </summary>
public static class SkillTree
{
    public static SkillTreeSnapshot Build(
        CharacterProfile character,
        int level,
        int availableSp,
        CharacterRules rules) => SkillTreeSnapshot.Empty;
}

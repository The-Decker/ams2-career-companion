using Companion.Core.Character;

namespace Companion.ViewModels.Hub;

/// <summary>Bindable projection of one skill-tree branch.</summary>
public sealed record SkillBranchViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsMeta { get; init; }
    public required IReadOnlyList<SkillNodeViewModel> Nodes { get; init; }
    /// <summary>The family's ten authored v2 mastery nodes, excluding attribute rails.</summary>
    public IReadOnlyList<SkillNodeViewModel> MasteryNodes { get; init; } = [];
    public int OwnedMasteryCount => MasteryNodes.Count(node => node.IsOwned);
    public int TotalMasteryCount => MasteryNodes.Count;
}

/// <summary>Bindable projection of one progression-v2 attribute rail.</summary>
public sealed record SkillAttributeRailViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string StatId { get; init; }
    public required IReadOnlyList<SkillNodeViewModel> Nodes { get; init; }
    public int OwnedCount { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>Bindable projection of one skill-tree node.</summary>
public sealed record SkillNodeViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required string Kind { get; init; }
    public required int Cost { get; init; }
    public required int Tier { get; init; }
    public required int UnlockLevel { get; init; }
    public required IReadOnlyList<string> RequiresLabels { get; init; }
    public IReadOnlyList<string> RequiresIds { get; init; } = [];
    public int Order { get; init; }
    public string IconKey { get; init; } = "";
    public string? ExclusiveGroup { get; init; }
    public string? RailId { get; init; }
    public string RailName { get; init; } = "";
    public string AttributeStatId { get; init; } = "";
    public double? AttributeValueAfter { get; init; }
    public bool IsMasteryOverride { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }

    /// <summary>
    /// Display-ready effect lines grouped by the expectation, career, or car layer. Additive and
    /// empty by default so existing bindings can keep using <see cref="Benefits"/> and
    /// <see cref="Drawbacks"/> unchanged.
    /// </summary>
    public IReadOnlyList<CharacterEffectLine> Effects { get; init; } = [];

    public required SkillNodeState State { get; init; }
    public bool IsOwned => State == SkillNodeState.Owned;
    public bool CanUnlock => State is SkillNodeState.Unlockable or SkillNodeState.Mastery;
    public required string LockReason { get; init; }
}

using Companion.Core.Character;

namespace Companion.ViewModels.Hub;

/// <summary>Bindable projection of one skill-tree branch.</summary>
public sealed record SkillBranchViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsMeta { get; init; }
    public required IReadOnlyList<SkillNodeViewModel> Nodes { get; init; }
}

/// <summary>Bindable projection of one skill-tree node.</summary>
public sealed record SkillNodeViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int Cost { get; init; }
    public required int Tier { get; init; }
    public required int UnlockLevel { get; init; }
    public required IReadOnlyList<string> RequiresLabels { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
    public required SkillNodeState State { get; init; }
    public bool IsOwned => State == SkillNodeState.Owned;
    public bool CanUnlock => State == SkillNodeState.Unlockable;
    public required string LockReason { get; init; }
}

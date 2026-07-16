namespace Companion.Core.Character;

/// <summary>
/// One in-memory progression-v2 development action reconstructed from the append-only journal.
/// The wrapper itself is never serialized: each row retains its stable <c>player.skillPlan</c> or
/// <c>player.skillReset</c> payload, while this union preserves their cross-phase journal order.
/// </summary>
public abstract record CharacterSkillDevelopmentAction;

public sealed record CharacterSkillPlanAction(CharacterSkillPlanInput Input)
    : CharacterSkillDevelopmentAction;

public sealed record CharacterSkillResetAction(CharacterSkillResetInput Input)
    : CharacterSkillDevelopmentAction;

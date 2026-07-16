using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>
/// The single ordered reducer for progression-v2 development INPUTs. Plans and full resets are
/// deliberately interleaved in journal sequence: plan -&gt; reset -&gt; replacement plan must clear
/// the first build and apply only the replacement, identically on live rollover and replay.
/// </summary>
public static class CharacterSkillDevelopmentTransition
{
    public static PlayerCareerState Apply(
        PlayerCareerState player,
        IReadOnlyList<CharacterSkillDevelopmentAction>? actions,
        CharacterRules? characterRules,
        MasterySkillCatalog? masterySkills)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (actions is not { Count: > 0 })
            return player;

        foreach (var action in actions)
        {
            player = action switch
            {
                CharacterSkillPlanAction plan => CharacterSkillPlanTransition.Apply(
                    player, [plan.Input], masterySkills),
                CharacterSkillResetAction reset => CharacterSkillReset.Apply(
                    player,
                    reset.Input,
                    characterRules ?? throw new InvalidOperationException(
                        "A stored player.skillReset requires the pinned character rules."),
                    masterySkills ?? throw new InvalidOperationException(
                        "A stored player.skillReset requires the pinned mastery-skill catalog.")),
                null => throw new InvalidOperationException(
                    "A progression-v2 development sequence contains a null action."),
                _ => throw new NotSupportedException(
                    $"Character development action '{action.GetType().Name}' is not supported."),
            };
        }

        return player;
    }
}

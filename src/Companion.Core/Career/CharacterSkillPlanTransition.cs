using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>
/// The single season-boundary reducer for confirmed progression-v2 skill plans. Both the
/// same-pack rollover and era transition call this exact helper, and replay calls those same
/// boundary functions, so acquisition order and campaign gates cannot drift by path.
/// </summary>
public static class CharacterSkillPlanTransition
{
    public static PlayerCareerState Apply(
        PlayerCareerState player,
        IReadOnlyList<CharacterSkillPlanInput>? plans,
        MasterySkillCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (plans is not { Count: > 0 })
            return player;
        if (catalog is null)
        {
            throw new InvalidOperationException(
                "A stored player.skillPlan requires the pinned mastery-skill catalog.");
        }

        var character = player.Character
            ?? throw new InvalidOperationException(
                "A stored player.skillPlan requires a character in the season-end state.");
        var campaign = player.CampaignProgressionPlan
            ?? throw new InvalidOperationException(
                "A progression-v2 skill plan requires its pinned campaign progression plan.");
        campaign.Validate();
        if (!CareerExperienceModes.IsBoundedCampaign(player.ExperienceMode) ||
            !string.Equals(player.ExperienceMode, campaign.Mode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The committed skill-plan experience mode and campaign plan do not match.");
        }

        int available = CharacterProgressionV2Math.SkillPoints(
            player.Level,
            player.SeasonsCompleted,
            campaign.MasterySeason,
            character.SkillPointsSpent).Available;
        var facts = new MasteryProgressionFacts(
            player.Level,
            available,
            player.SeasonsCompleted >= campaign.MasterySeason);

        return player with
        {
            Character = MasterySkillPlan.ApplyAll(character, plans, facts, catalog),
        };
    }
}

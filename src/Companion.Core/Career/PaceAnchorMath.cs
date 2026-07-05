using Companion.Core.Grid;

namespace Companion.Core.Career;

/// <summary>
/// The player pace anchor: a rolling calibration (EWMA, α=0.3) of "player skill expressed in
/// Opponent Skill slider percent", estimated after each round from the player's finish among
/// AI drivers whose generated ratings are known exactly.
/// </summary>
public static class PaceAnchorMath
{
    public const double Alpha = 0.3;

    /// <summary>EWMA update. An anchor of 0 means "not yet calibrated": the first sample
    /// seeds the anchor directly instead of averaging against nothing.</summary>
    public static double Update(double anchor, double impliedPacePercent) =>
        anchor <= 0.0
            ? impliedPacePercent
            : (1.0 - Alpha) * anchor + Alpha * impliedPacePercent;

    /// <summary>The player's implied pace this round: the AI driver ranked at the player's
    /// finishing position by merged raceSkill is the player's nearest known yardstick; their
    /// pace at the slider used is the sample. Positions beyond the AI count clamp to the
    /// slowest AI.</summary>
    public static double ImpliedPlayerPace(GridPlan grid, int playerFinish, double sliderPercent)
    {
        if (playerFinish < 1)
            throw new ArgumentOutOfRangeException(nameof(playerFinish), "Positions are 1-based.");

        var aiSkills = grid.Seats
            .Where(s => !s.IsPlayer)
            .Select(s => s.Ratings.RaceSkill)
            .OrderByDescending(x => x)
            .ToList();
        if (aiSkills.Count == 0)
            throw new InvalidOperationException("The grid has no AI seats to calibrate against.");

        int index = Math.Clamp(playerFinish - 1, 0, aiSkills.Count - 1);
        return DifficultyModel.AiPacePercent(aiSkills[index], sliderPercent);
    }

    /// <summary>Median merged raceSkill of the AI grid — the reference rating the difficulty
    /// recommendation aims the player at (mid-grid at the recommended slider).</summary>
    public static double MedianAiRaceSkill(GridPlan grid) => MedianAiSkill(grid, race: true);

    /// <summary>The player's implied ONE-LAP pace this round: the AI ranked at the player's
    /// QUALIFYING position by <c>qualifyingSkill</c> is the nearest known yardstick; their pace
    /// at the slider used is the sample. The qualifying-axis mirror of <see cref="ImpliedPlayerPace"/>
    /// (Increment 2), feeding a separate one-lap anchor. Positions beyond the AI count clamp to
    /// the slowest AI qualifier.</summary>
    public static double ImpliedPlayerQualiPace(GridPlan grid, int playerQualiPosition, double sliderPercent)
    {
        if (playerQualiPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(playerQualiPosition), "Positions are 1-based.");

        var aiSkills = grid.Seats
            .Where(s => !s.IsPlayer)
            .Select(s => s.Ratings.QualifyingSkill)
            .OrderByDescending(x => x)
            .ToList();
        if (aiSkills.Count == 0)
            throw new InvalidOperationException("The grid has no AI seats to calibrate against.");

        int index = Math.Clamp(playerQualiPosition - 1, 0, aiSkills.Count - 1);
        return DifficultyModel.AiPacePercent(aiSkills[index], sliderPercent);
    }

    /// <summary>Median merged qualifyingSkill of the AI grid — the reference the qualifying anchor
    /// aims the player at (mid-grid on one-lap pace).</summary>
    public static double MedianAiQualifyingSkill(GridPlan grid) => MedianAiSkill(grid, race: false);

    private static double MedianAiSkill(GridPlan grid, bool race)
    {
        var skills = grid.Seats
            .Where(s => !s.IsPlayer)
            .Select(s => race ? s.Ratings.RaceSkill : s.Ratings.QualifyingSkill)
            .OrderBy(x => x)
            .ToList();
        if (skills.Count == 0)
            throw new InvalidOperationException("The grid has no AI seats.");

        int mid = skills.Count / 2;
        return skills.Count % 2 == 1 ? skills[mid] : (skills[mid - 1] + skills[mid]) / 2.0;
    }
}

/// <summary>
/// The research compression note made linear (RESEARCH.md §1/§6): AMS2 race_skill compresses
/// around the Opponent Skill slider — at 90% slider a 1.0-rated AI runs at ~95% pace and a
/// 0.0-rated AI at ~85%. Both axes interpolate linearly from those endpoints.
/// </summary>
public static class DifficultyModel
{
    public const int MinSlider = 70;
    public const int MaxSlider = 120;

    /// <summary>Effective pace (in slider percent) of an AI with the given rating at the
    /// given slider setting: <c>slider − 5 + 10·rating</c> (endpoints: 90% ⇒ 1.0→95, 0.0→85).</summary>
    public static double AiPacePercent(double rating, double sliderPercent) =>
        sliderPercent - 5.0 + 10.0 * rating;

    /// <summary>The Opponent Skill slider at which an AI of the target rating matches the
    /// player's calibrated pace — i.e. the slider that puts the player mid-grid against the
    /// target. Clamped to the in-game 70–120 range, rounded to a whole percent. Shown in the
    /// briefing, never auto-applied.</summary>
    public static int RecommendSlider(double paceAnchorPercent, double targetRating)
    {
        double slider = paceAnchorPercent + 5.0 - 10.0 * targetRating;
        return (int)Math.Round(
            Math.Clamp(slider, MinSlider, MaxSlider),
            MidpointRounding.AwayFromZero);
    }
}

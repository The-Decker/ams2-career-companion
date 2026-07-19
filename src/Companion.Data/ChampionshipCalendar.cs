using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Data;

/// <summary>
/// THE championship-round mapping (one place, used by both the unified fold and the app's
/// session service): a pack calendar may mix championship rounds with non-championship
/// events (Championship = false), but the scoring engine's round domain is CHAMPIONSHIP
/// rounds only, best-N segments, engine round numbers, and standings all use the
/// championship ordinal, never the calendar position, and non-championship results are
/// recorded but never scored.
/// </summary>
public static class ChampionshipCalendar
{
    /// <summary>How many rounds of the calendar are championship rounds, the round count
    /// the season's scoring definition resolves against.</summary>
    public static int RoundCount(SeasonPack pack) =>
        pack.Season.Rounds.Count(r => r.Championship);

    /// <summary>1-based position of a calendar round among championship rounds only, the
    /// round number the scoring engine and best-N segments operate on.</summary>
    public static int Ordinal(SeasonPack pack, int calendarRound) =>
        pack.Season.Rounds.Count(r => r.Championship && r.Round <= calendarRound);

    /// <summary>True when the calendar round exists and is a championship round (its result
    /// feeds the standings engine; non-championship results are journaled but unscored).</summary>
    public static bool IsChampionshipRound(SeasonPack pack, int calendarRound) =>
        pack.Season.Rounds.Any(r => r.Round == calendarRound && r.Championship);

    /// <summary>The season's scoring definition resolved over the CHAMPIONSHIP round count —
    /// the same resolution the structural validator checks and the app session scores with.</summary>
    public static SeasonScoringDefinition ResolveScoring(SeasonPack pack) =>
        pack.Season.PointsSystem.ResolveScoringDefinition(RoundCount(pack));
}

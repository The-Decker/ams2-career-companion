using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>
/// Character XP is a PURE function of the results the player enters — no dice, no stream — so a
/// replay from the same seed + same results reproduces every level byte-for-byte
/// (docs/dev/character-system.md §3.1). It lives beside <c>OpiMath</c> and consumes the same
/// already-computed round facts (<c>expectedFinish</c>, the OPI <c>effectiveFinish</c>, the DNF
/// cause); the fold emits a <c>player.xp</c> row from it. Nothing here is wired into scoring, so
/// the f1db oracle is untouched until a character career is actually simulated.
/// </summary>
public static class XpMath
{
    /// <summary>The already-classified round facts XP reads. All are values the fold computes
    /// anyway; XP adds no new inputs. <paramref name="EffectiveFinish"/> is the OPI effective
    /// finish (expected for a mechanical DNF, grid size for a driver-error DNF), so XP and OPI
    /// agree on "how the round went".</summary>
    public readonly record struct RoundInputs(
        int ExpectedFinish,
        double EffectiveFinish,
        int? FinishPosition,
        bool ScoredPoints,
        bool BeatTeammate,
        DnfCause? Dnf);

    /// <summary>Per-round XP: the overperformance term (clamped) plus one result bonus, plus the
    /// beat-teammate bonus on a classified finish. Overperforming in a weak car is the richest XP
    /// — it mirrors the reputation underdog logic, so a backmarker career still levels.
    /// <code>xpRound = clamp((expectedFinish − effectiveFinish) · perPlace, floor, cap) + resultBonus</code>
    /// The result bonus is mutually exclusive by outcome (a win is not also paid the podium+points
    /// bonuses); beat-teammate stacks only on a genuine finish.</summary>
    public static int PerRound(PerRoundXp cfg, RoundInputs r)
    {
        double finishTerm = Math.Clamp(
            (r.ExpectedFinish - r.EffectiveFinish) * cfg.FinishVsExpectedPerPlace,
            cfg.FinishVsExpectedFloor,
            cfg.FinishVsExpectedCap);

        double bonus;
        if (r.Dnf == DnfCause.DriverError)
            bonus = cfg.DnfDriverError;
        else if (r.Dnf == DnfCause.Mechanical)
            bonus = cfg.DnfMechanical;
        else if (r.FinishPosition is int position)
            bonus = position == 1 ? cfg.Win
                : position <= 3 ? cfg.Podium
                : r.ScoredPoints ? cfg.Points
                : 0.0;
        else
            bonus = 0.0;

        // Beating your teammate is only meaningful when you actually finished the race.
        if (r.BeatTeammate && r.Dnf is null)
            bonus += cfg.BeatTeammate;

        return (int)Math.Round(finishTerm + bonus, MidpointRounding.AwayFromZero);
    }

    /// <summary>Per-season XP: the best applicable championship-placement bonus plus the flat
    /// season-completed grant. Placement bonuses are mutually exclusive (a title is not also paid
    /// the top-3 and top-10 bonuses); the completion grant always stacks.</summary>
    public static int PerSeason(PerSeasonXp cfg, int? championshipPosition, bool seasonCompleted)
    {
        double xp = 0.0;
        if (championshipPosition is int position)
            xp += position == 1 ? cfg.Championship1
                : position <= 3 ? cfg.ChampionshipTop3
                : position <= 10 ? cfg.ChampionshipTop10
                : 0.0;
        if (seasonCompleted)
            xp += cfg.SeasonCompleted;

        return (int)Math.Round(xp, MidpointRounding.AwayFromZero);
    }
}

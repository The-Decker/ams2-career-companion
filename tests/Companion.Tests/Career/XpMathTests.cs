using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The pure character-XP function. XP is derived from entered results only (no dice, no
/// stream), so these lock the arithmetic that a replay must reproduce byte-for-byte.</summary>
public sealed class XpMathTests
{
    // The shipped per-round config (finishVsExpectedPerPlace 6, floor -30, cap 60, win 40,
    // podium 20, points 10, beatTeammate 8, dnfDriverError -15, dnfMechanical 0).
    private static PerRoundXp Round() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json")).Levels.XpSources.PerRound;

    private static PerSeasonXp Season() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json")).Levels.XpSources.PerSeason;

    [Fact]
    public void PerRound_OverperformanceTermPlusResultBonus()
    {
        // Expected P10, finished (and OPI-effective) P4, scored points: (10-4)*6 = 36, +points 10.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 10, EffectiveFinish: 4, FinishPosition: 4,
            ScoredPoints: true, BeatTeammate: false, Dnf: null));

        Assert.Equal(46, xp);
    }

    [Fact]
    public void PerRound_WinTakesTheWinBonusOnly_AndBeatingTheTeammateStacks()
    {
        // Expected P3, won: (3-1)*6 = 12, +win 40 (NOT also podium/points), +beatTeammate 8.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 3, EffectiveFinish: 1, FinishPosition: 1,
            ScoredPoints: true, BeatTeammate: true, Dnf: null));

        Assert.Equal(12 + 40 + 8, xp);
    }

    [Fact]
    public void PerRound_OverperformanceTermIsClampedToTheCap()
    {
        // Expected last (P20), won (P1): (20-1)*6 = 114, clamped to the +60 cap, +win 40.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 20, EffectiveFinish: 1, FinishPosition: 1,
            ScoredPoints: true, BeatTeammate: false, Dnf: null));

        Assert.Equal(60 + 40, xp);
    }

    [Fact]
    public void PerRound_UnderperformanceTermIsClampedToTheFloor()
    {
        // Expected P1, effective last (a driver-error DNF scores as the grid size, say 20):
        // (1-20)*6 = -114, clamped to the -30 floor, + the driver-error penalty -15.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 1, EffectiveFinish: 20, FinishPosition: null,
            ScoredPoints: false, BeatTeammate: false, Dnf: DnfCause.DriverError));

        Assert.Equal(-30 - 15, xp);
    }

    [Fact]
    public void PerRound_MechanicalDnfHasNoResultBonus_AndBeatTeammateDoesNotStackOnADnf()
    {
        // A mechanical DNF scores OPI-effective as the expected finish (no blame): term = 0,
        // dnfMechanical bonus = 0; beatTeammate must NOT apply to a DNF.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 8, EffectiveFinish: 8, FinishPosition: null,
            ScoredPoints: false, BeatTeammate: true, Dnf: DnfCause.Mechanical));

        Assert.Equal(0, xp);
    }

    [Fact]
    public void PerRound_MidfieldFinishOutOfThePointsGetsNoResultBonus()
    {
        // Expected P12, finished P11 but out of the points: (12-11)*6 = 6, no position bonus.
        int xp = XpMath.PerRound(Round(), new XpMath.RoundInputs(
            ExpectedFinish: 12, EffectiveFinish: 11, FinishPosition: 11,
            ScoredPoints: false, BeatTeammate: false, Dnf: null));

        Assert.Equal(6, xp);
    }

    [Fact]
    public void PerSeason_ChampionshipBonusIsMutuallyExclusiveAndCompletionStacks()
    {
        var cfg = Season();

        Assert.Equal(300 + 40, XpMath.PerSeason(cfg, championshipPosition: 1, seasonCompleted: true));
        Assert.Equal(150 + 40, XpMath.PerSeason(cfg, championshipPosition: 3, seasonCompleted: true));
        Assert.Equal(60 + 40, XpMath.PerSeason(cfg, championshipPosition: 8, seasonCompleted: true));
        Assert.Equal(40, XpMath.PerSeason(cfg, championshipPosition: 14, seasonCompleted: true));
        // An abandoned season (not completed) earns nothing, even with a placement.
        Assert.Equal(300, XpMath.PerSeason(cfg, championshipPosition: 1, seasonCompleted: false));
        Assert.Equal(0, XpMath.PerSeason(cfg, championshipPosition: null, seasonCompleted: false));
    }
}

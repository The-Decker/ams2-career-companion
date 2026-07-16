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
    public void PerRound_XpRateMultipliers_ScaleTheMatchingCause()
    {
        var cfg = Round();
        var win = new XpMath.RoundInputs(
            ExpectedFinish: 5, EffectiveFinish: 1, FinishPosition: 1,
            ScoredPoints: true, BeatTeammate: false, Dnf: null);

        // Base: (5-1)*6 = 24 overperformance + win 40 = 64.
        Assert.Equal(64, XpMath.PerRound(cfg, win));
        // opportunist: win ×0.90 → 24 + 36 = 60 (only the win bonus scales).
        Assert.Equal(60, XpMath.PerRound(cfg, win, Mods(("win", 0.90))));
        // qualifying_specialist: finishVsExpected ×0.90 → 21.6 + 40 = 61.6 → 62.
        Assert.Equal(62, XpMath.PerRound(cfg, win, Mods(("finishVsExpected", 0.90))));
    }

    [Fact]
    public void PerRound_Midfield_ScalesOnlyOffPodiumOverperformance()
    {
        var cfg = Round();
        // Expected P12, finished P8, scored points: (12-8)*6 = 24 + points 10 = 34.
        var midfield = new XpMath.RoundInputs(
            ExpectedFinish: 12, EffectiveFinish: 8, FinishPosition: 8,
            ScoredPoints: true, BeatTeammate: false, Dnf: null);
        Assert.Equal(34, XpMath.PerRound(cfg, midfield));
        // student_of_the_craft: midfield ×1.5 scales the OFF-podium overperformance: 36 + 10 = 46.
        Assert.Equal(46, XpMath.PerRound(cfg, midfield, Mods(("midfield", 1.5))));

        // A podium finish is NOT boosted by midfield (position ≤ 3): (6-2)*6 = 24 + podium 20 = 44.
        var podium = midfield with { ExpectedFinish = 6, EffectiveFinish = 2, FinishPosition = 2 };
        Assert.Equal(44, XpMath.PerRound(cfg, podium));
        Assert.Equal(44, XpMath.PerRound(cfg, podium, Mods(("midfield", 1.5))));
    }

    private static PlayerPerkModifiers Mods(params (string Cause, double Mult)[] xpMults) => new()
    {
        XpMults = xpMults.ToDictionary(x => x.Cause, x => x.Mult, StringComparer.Ordinal),
    };

    private static PlayerPerkModifiers MasteryMods(params (string Cause, double Mult)[] xpMults) =>
        Mods(xpMults) with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
        };

    [Fact]
    public void PerRound_BlanketMultipliers_ScaleTheWholeRound()
    {
        var cfg = Round();
        // Expected P10, finished P4, scored points: (10-4)*6 = 36 + points 10 = 46 base.
        var round = new XpMath.RoundInputs(
            ExpectedFinish: 10, EffectiveFinish: 4, FinishPosition: 4,
            ScoredPoints: true, BeatTeammate: false, Dnf: null);
        Assert.Equal(46, XpMath.PerRound(cfg, round));

        // adaptable: "all" ×0.85 scales the ENTIRE round (both the term and the bonus): 46*0.85 = 39.1 → 39.
        Assert.Equal(39, XpMath.PerRound(cfg, round, Mods(("all", 0.85))));
        // wonderkid young: "ageWindow" ×1.40 → 46*1.40 = 64.4 → 64.
        Assert.Equal(64, XpMath.PerRound(cfg, round, Mods(("ageWindow", 1.40))));
        // wonderkid at/past peak: "ageWindow" ×0.75 → 46*0.75 = 34.5 → 35 (away-from-zero).
        Assert.Equal(35, XpMath.PerRound(cfg, round, Mods(("ageWindow", 0.75))));
        // The two blanket multipliers compound: 46 * 0.85 * 1.40 = 54.74 → 55.
        Assert.Equal(55, XpMath.PerRound(cfg, round, Mods(("all", 0.85), ("ageWindow", 1.40))));
    }

    [Fact]
    public void PerRound_ActiveMasteryRates_ComposeCauseBlanketAndMidfieldPerComponent()
    {
        var round = new XpMath.RoundInputs(
            ExpectedFinish: 12, EffectiveFinish: 8, FinishPosition: 8,
            ScoredPoints: true, BeatTeammate: false, Dnf: null);
        var mods = MasteryMods(
            ("finishVsExpected", 0.50),
            ("midfield", 1.20),
            ("points", 0.80),
            ("all", 1.25),
            ("ageWindow", 1.10));

        // Finish: 24 * (.50 * 1.20 * 1.25 * 1.10) = 19.8.
        // Points: 10 * (.80 * 1.25 * 1.10) = 11.0. Total 30.8 rounds to 31.
        Assert.Equal(31, XpMath.PerRound(Round(), round, mods));
    }

    [Fact]
    public void PerRound_ActiveMasteryRatesClampButLegacyV0RetainsItsUnclampedArithmetic()
    {
        var round = new XpMath.RoundInputs(
            ExpectedFinish: 12, EffectiveFinish: 8, FinishPosition: 8,
            ScoredPoints: true, BeatTeammate: false, Dnf: null);
        var v0 = Mods(
            ("finishVsExpected", 2.0), ("midfield", 2.0), ("points", 2.0),
            ("all", 2.0), ("ageWindow", 2.0));
        var active = v0 with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
        };

        // The legacy path is intentionally unchanged: ((24 * 2 * 2) + (10 * 2)) * 2 * 2.
        Assert.Equal(464, XpMath.PerRound(Round(), round, v0));
        // Active mastery clamps each final component rate to 1.40: (24 + 10) * 1.40 = 47.6.
        Assert.Equal(48, XpMath.PerRound(Round(), round, active));
    }

    [Fact]
    public void PerRound_ActiveMasteryClampAppliesToNegativeFinishTerm_AndXpFloorEffectStaysDormant()
    {
        var round = new XpMath.RoundInputs(
            ExpectedFinish: 1, EffectiveFinish: 20, FinishPosition: 20,
            ScoredPoints: false, BeatTeammate: false, Dnf: null);
        var upper = MasteryMods(
            ("finishVsExpected", 2.0), ("midfield", 2.0),
            ("all", 2.0), ("ageWindow", 2.0)) with
        {
            RoundXpFloorMultiplier = 0.0,
        };

        // The configured -30 floor remains live, then the combined rate clamps to 1.40. If the
        // disputed floor effect were consumed this would incorrectly become zero.
        Assert.Equal(-42, XpMath.PerRound(Round(), round, upper));

        var lower = MasteryMods(("finishVsExpected", -1.0));
        Assert.Equal(0, XpMath.PerRound(Round(), round, lower));
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

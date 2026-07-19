using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The best-of-7 series math (owner-approved 2026-07-19, docs/dev/smgp-series-ladder.md):
/// wins accumulate independently per side (a loss never resets the other side), the first side
/// to four race wins takes the series, and a completed series resets to 0-0. The tally fields
/// keep their legacy streak meaning for ungated careers, so both rule sets share one blob.
/// </summary>
public sealed class SmgpSeriesLadderTests
{
    [Fact]
    public void SeriesWins_AccumulateWithoutResettingTheOtherSide()
    {
        var tally = SmgpBattleTally.Empty;
        tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.PlayerBeatRival).Tally;
        tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.PlayerBeatRival).Tally;
        tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.RivalBeatPlayer).Tally;

        Assert.Equal(2, tally.PlayerStreak); // the player's wins survive their loss
        Assert.Equal(1, tally.RivalStreak);  // the rival's win is banked, not a streak reset
    }

    [Fact]
    public void VoidBattles_DoNotCount()
    {
        var tally = SmgpBattleTally.Empty;
        var update = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.Void);
        Assert.Equal(SmgpTrigger.None, update.Trigger);
        Assert.Equal(0, update.Tally.PlayerStreak);
        Assert.Equal(0, update.Tally.RivalStreak);
    }

    [Fact]
    public void TheFourthPlayerWin_TriggersTheOffer_AndResetsTheSeries()
    {
        var tally = SmgpBattleTally.Empty;
        SmgpBattleUpdate update = new() { Tally = tally, Trigger = SmgpTrigger.None };
        for (int i = 0; i < 3; i++)
        {
            update = SmgpRules.ApplySeriesBattle(update.Tally, SmgpBattleOutcome.PlayerBeatRival);
            Assert.Equal(SmgpTrigger.None, update.Trigger);
        }

        update = SmgpRules.ApplySeriesBattle(update.Tally, SmgpBattleOutcome.PlayerBeatRival);
        Assert.Equal(SmgpTrigger.SeatSwapOfferToPlayer, update.Trigger);
        Assert.Equal(0, update.Tally.PlayerStreak);
        Assert.Equal(0, update.Tally.RivalStreak);
    }

    [Fact]
    public void TheFourthRivalWin_TriggersTheForfeit_AndResetsTheSeries()
    {
        var tally = SmgpBattleTally.Empty;
        // 3-3: a real fight. Then the rival takes it.
        for (int i = 0; i < 3; i++)
        {
            tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.PlayerBeatRival).Tally;
            tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.RivalBeatPlayer).Tally;
        }
        Assert.Equal(3, tally.PlayerStreak);
        Assert.Equal(3, tally.RivalStreak);

        var update = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.RivalBeatPlayer);
        Assert.Equal(SmgpTrigger.PlayerSeatForfeit, update.Trigger);
        Assert.Equal(0, update.Tally.PlayerStreak);
        Assert.Equal(0, update.Tally.RivalStreak);
    }

    [Fact]
    public void ACompletedSeries_StartsTheNextOneFromNil()
    {
        var tally = SmgpBattleTally.Empty;
        for (int i = 0; i < 4; i++)
            tally = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.PlayerBeatRival).Tally;
        // The series reset on the fourth win; the next fight opens 0-0 and needs four more.
        var update = SmgpRules.ApplySeriesBattle(tally, SmgpBattleOutcome.PlayerBeatRival);
        Assert.Equal(SmgpTrigger.None, update.Trigger);
        Assert.Equal(1, update.Tally.PlayerStreak);
    }

    [Fact]
    public void LegacyTwoWinsMath_IsUntouched()
    {
        // The legacy path keeps its streak semantics exactly (byte-identical for old careers).
        var tally = SmgpBattleTally.Empty;
        tally = SmgpRules.ApplyBattle(tally, SmgpBattleOutcome.PlayerBeatRival).Tally;
        tally = SmgpRules.ApplyBattle(tally, SmgpBattleOutcome.RivalBeatPlayer).Tally;
        Assert.Equal(0, tally.PlayerStreak); // a loss reset the player's streak
        Assert.Equal(1, tally.RivalStreak);
        var update = SmgpRules.ApplyBattle(tally, SmgpBattleOutcome.RivalBeatPlayer);
        Assert.Equal(SmgpTrigger.PlayerSeatForfeit, update.Trigger);
    }
}

using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>
/// The SMGP replica mode's pure rules, pinned to the manual-verified design
/// (docs/dev/smgp-design.md): rival battles decide by finishing ahead; two wins WITHOUT a loss
/// trigger the seat-swap offer; the displacement chain drops the rival one tier below the
/// player's OLD tier; losing at Zeroforce is career over; the Madonna defense needs one win of
/// two; two titles complete the game.
/// </summary>
public sealed class SmgpRulesTests
{
    // ---------- tiers ----------

    [Theory]
    [InlineData(5, 'A')]
    [InlineData(4, 'B')]
    [InlineData(3, 'C')]
    [InlineData(2, 'D')]
    public void Tier_MapsThePackPrestigeLadder(int prestige, char tier) =>
        Assert.Equal(tier, SmgpRules.Tier(prestige));

    [Fact]
    public void TierBelow_WalksTheLadder_AndStopsAtTheFloor()
    {
        Assert.Equal('B', SmgpRules.TierBelow('A'));
        Assert.Equal('C', SmgpRules.TierBelow('B'));
        Assert.Equal('D', SmgpRules.TierBelow('C'));
        Assert.Null(SmgpRules.TierBelow('D'));
    }

    // ---------- battle outcomes ----------

    [Theory]
    [InlineData(3, 7, SmgpBattleOutcome.PlayerBeatRival)]
    [InlineData(7, 3, SmgpBattleOutcome.RivalBeatPlayer)]
    [InlineData(5, null, SmgpBattleOutcome.PlayerBeatRival)]  // a finish beats a DNF
    [InlineData(null, 5, SmgpBattleOutcome.RivalBeatPlayer)]
    [InlineData(null, null, SmgpBattleOutcome.Void)]          // both out — nobody beat anyone
    public void BattleOutcome_DecidesByFinishingAhead(int? player, int? rival, SmgpBattleOutcome expected) =>
        Assert.Equal(expected, SmgpRules.BattleOutcome(player, rival));

    // ---------- the two-wins rule ----------

    [Fact]
    public void TwoWinsWithoutALoss_TriggersTheSeatSwapOffer_AndRestartsTheStreak()
    {
        var afterOne = SmgpRules.ApplyBattle(SmgpBattleTally.Empty, SmgpBattleOutcome.PlayerBeatRival);
        Assert.Equal(SmgpTrigger.None, afterOne.Trigger);
        Assert.Equal(1, afterOne.Tally.PlayerStreak);

        var afterTwo = SmgpRules.ApplyBattle(afterOne.Tally, SmgpBattleOutcome.PlayerBeatRival);
        Assert.Equal(SmgpTrigger.SeatSwapOfferToPlayer, afterTwo.Trigger);
        Assert.Equal(0, afterTwo.Tally.PlayerStreak); // the ladder restarts after the offer
    }

    [Fact]
    public void ALossResetsThePlayersStreak_SoWinLossWinNeverTriggers()
    {
        var t = SmgpRules.ApplyBattle(SmgpBattleTally.Empty, SmgpBattleOutcome.PlayerBeatRival).Tally;
        t = SmgpRules.ApplyBattle(t, SmgpBattleOutcome.RivalBeatPlayer).Tally;
        Assert.Equal(0, t.PlayerStreak);
        Assert.Equal(1, t.RivalStreak);

        var winAgain = SmgpRules.ApplyBattle(t, SmgpBattleOutcome.PlayerBeatRival);
        Assert.Equal(SmgpTrigger.None, winAgain.Trigger); // only ONE consecutive win
        Assert.Equal(0, winAgain.Tally.RivalStreak);      // and the rival's streak reset too
    }

    [Fact]
    public void RivalBeatingThePlayerTwice_TriggersTheForfeit()
    {
        var t = SmgpRules.ApplyBattle(SmgpBattleTally.Empty, SmgpBattleOutcome.RivalBeatPlayer).Tally;
        var second = SmgpRules.ApplyBattle(t, SmgpBattleOutcome.RivalBeatPlayer);
        Assert.Equal(SmgpTrigger.PlayerSeatForfeit, second.Trigger);
    }

    [Fact]
    public void VoidBattles_MoveNothing()
    {
        var t = SmgpRules.ApplyBattle(SmgpBattleTally.Empty, SmgpBattleOutcome.PlayerBeatRival).Tally;
        var afterVoid = SmgpRules.ApplyBattle(t, SmgpBattleOutcome.Void);
        Assert.Equal(SmgpTrigger.None, afterVoid.Trigger);
        Assert.Equal(t, afterVoid.Tally);
    }

    // ---------- the displacement chain ----------

    [Fact]
    public void PlayerSeatSwap_DropsTheRivalOneTierBelow_AndTheDisplacedDriverTakesTheOldSeat()
    {
        // Player at Minarae (C) beats a LEVEL B rival twice: player → rival's seat; rival → the
        // designated D-tier seat; that seat's driver → Minarae.
        var swap = SmgpRules.PlayerSeatSwap(
            playerSeat: "Minarae #20 B. Miller", playerTier: 'C',
            rivalSeat: "Tyrant #11 M. Hamano",
            displacementSeatByTier: new Dictionary<char, string> { ['D'] = "Rigel #26 R. Cotman" });

        Assert.Equal("Tyrant #11 M. Hamano", swap.PlayerNewSeat);
        Assert.Equal("Rigel #26 R. Cotman", swap.RivalNewSeat);
        Assert.Equal("Minarae #20 B. Miller", swap.DisplacedDriverNewSeat);
        Assert.Equal("Rigel #26 R. Cotman", swap.DisplacedSeat);
    }

    [Fact]
    public void PlayerSeatSwap_AtTheFloor_IsAStraightExchange()
    {
        var swap = SmgpRules.PlayerSeatSwap(
            playerSeat: "Zeroforce #32 P. Kilnger", playerTier: 'D',
            rivalSeat: "Comet #29 E. Tornio",
            displacementSeatByTier: new Dictionary<char, string>());

        Assert.Equal("Comet #29 E. Tornio", swap.PlayerNewSeat);
        Assert.Equal("Zeroforce #32 P. Kilnger", swap.RivalNewSeat);
        Assert.Null(swap.DisplacedDriverNewSeat);
    }

    // ---------- title defense / completion ----------
    // (The old "forfeit at the Zeroforce floor = career over" rule is superseded by the LEVEL-D
    //  4-loss counter — see SmgpRules.FloorLossLimit + SmgpChallengeRulesTests /
    //  SmgpBattleFoldDeterminismTests.FourLosses_AtTheFloor_EndTheCareer_ButNotBefore.)

    [Theory]
    [InlineData(SmgpBattleOutcome.PlayerBeatRival, SmgpBattleOutcome.RivalBeatPlayer, SmgpTitleDefense.MadonnaKept)]
    [InlineData(SmgpBattleOutcome.RivalBeatPlayer, SmgpBattleOutcome.PlayerBeatRival, SmgpTitleDefense.MadonnaKept)]
    [InlineData(SmgpBattleOutcome.RivalBeatPlayer, SmgpBattleOutcome.RivalBeatPlayer, SmgpTitleDefense.FiredToDardan)]
    [InlineData(SmgpBattleOutcome.Void, SmgpBattleOutcome.RivalBeatPlayer, SmgpTitleDefense.FiredToDardan)]
    public void TitleDefense_NeedsAtLeastOneWinOfTheTwoCearaChallenges(
        SmgpBattleOutcome r1, SmgpBattleOutcome r2, SmgpTitleDefense expected) =>
        Assert.Equal(expected, SmgpRules.TitleDefense(r1, r2));

    [Fact]
    public void TwoTitles_CompleteTheGame()
    {
        Assert.False(SmgpRules.IsComplete(0));
        Assert.False(SmgpRules.IsComplete(1));
        Assert.True(SmgpRules.IsComplete(2));
    }

    // ---------- the 17-season grand campaign ----------

    [Fact]
    public void CampaignSeasons_IsSeventeen() => Assert.Equal(17, SmgpRules.CampaignSeasons);

    [Theory]
    // Short of 17 → not complete, whatever the CareerOver state.
    [InlineData(1, false, false)]
    [InlineData(16, false, false)]
    // Reached the summit, survived → completed (special.jpg unlocks).
    [InlineData(17, false, true)]
    [InlineData(18, false, true)] // carrying on past the summit stays "completed"
    // Reached 17 but the career ended on the D-floor → NOT completed (the game-over screen shows instead).
    [InlineData(17, true, false)]
    public void CampaignComplete_NeedsSeventeenSeasonsAndNotCareerOver(int seasonOrdinal, bool careerOver, bool expected) =>
        Assert.Equal(expected, SmgpRules.CampaignComplete(seasonOrdinal, careerOver));

    [Theory]
    // Completed all 17 but only 16 titles → survivor, NOT flawless.
    [InlineData(17, 16, false, false)]
    // Champion in all 17 → the flawless emperor (ultimate.jpg unlocks).
    [InlineData(17, 17, false, true)]
    // 17 titles but the career ended on the floor → not even completed, so not flawless.
    [InlineData(17, 17, true, false)]
    // Short of the summit → never flawless, even with a perfect record so far.
    [InlineData(10, 10, false, false)]
    public void CampaignFlawless_NeedsCompletionPlusEveryTitle(int seasonOrdinal, int titles, bool careerOver, bool expected) =>
        Assert.Equal(expected, SmgpRules.CampaignFlawless(seasonOrdinal, titles, careerOver));
}

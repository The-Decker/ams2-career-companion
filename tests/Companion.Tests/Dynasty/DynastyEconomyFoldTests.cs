using Companion.Core.Career;
using Companion.Core.Dynasty;
using Companion.Core.Numerics;
using Companion.Tests.Career;

namespace Companion.Tests.Dynasty;

/// <summary>Pure-fold economy math against the SHIPPED tables: every settlement line is exact
/// rational, decisions apply in order with exact escalation, the deficit grace and the hard floor
/// trip bankruptcy exactly at their data-defined lines, and tampered decisions fail loudly.</summary>
public sealed class DynastyEconomyFoldTests
{
    private static readonly DynastyEconomyRules Rules =
        DynastyEconomyRules.Load(CareerTestData.RulesDirectory)!;

    private static DynastyEconomyState Fresh(string balance = "100000") => new()
    {
        Version = Rules.SchemaVersion,
        Balance = Rational.Parse(balance),
    };

    private static DynastyRoundSettleContext Settle(
        DynastyEconomyState state,
        int?[] playerFinishes,
        int?[] teammateFinishes,
        bool playerStarted = true,
        bool hasSecondCar = true,
        DnfCause? dnf = null,
        AccidentSeverity? severity = null) => new()
    {
        State = state,
        Rules = Rules,
        Year = 1967,
        Round = 1,
        RoundsInSeason = 2,
        PlayerTeamTier = 5,
        PlayerStarted = playerStarted,
        PlayerSessionFinishes = playerFinishes,
        TeammateSessionFinishes = teammateFinishes,
        HasSecondCar = hasSecondCar,
        PlayerDnf = dnf,
        PlayerAccidentSeverity = severity,
    };

    [Fact]
    public void SettleRound_RetainedDeal_ExactStatement()
    {
        // P3 + teammate P5, tier 5, 2-round season, no sponsors/staff:
        // income  = 5000 (P3) + 2000 (P5) + 1100 (appearance) = 8100
        // costs   = 500 + 1500 + 2500 + 38000/2 (second salary) = 23500
        var result = DynastyEconomyFold.SettleRound(Settle(Fresh(), [3], [5]));

        Assert.Equal(Rational.Parse("84600"), result.State.Balance); // 100000 − 15400
        Assert.Equal(0, result.State.DeficitRounds);
        Assert.False(result.WentBankrupt);
        var row = Assert.Single(result.Events);
        Assert.Equal(JournalPhases.EconomyRound, row.Phase);
        Assert.Equal("surplus", row.Cause);
        Assert.Contains("\"net\":\"-15400\"", row.DeltaJson, StringComparison.Ordinal);
        Assert.Contains("\"racePrize\":\"5000\"", row.DeltaJson, StringComparison.Ordinal);
        Assert.Contains("\"secondCarPrize\":\"2000\"", row.DeltaJson, StringComparison.Ordinal);
        Assert.Contains("\"secondSalary\":\"19000\"", row.DeltaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SettleRound_SponsorPaysPerRaceAndPodium()
    {
        var state = Fresh().WithSponsor(
            "sponsor.apex-lubricants", new DynastySponsorContract { SeasonsRemaining = 2 });
        var result = DynastyEconomyFold.SettleRound(Settle(state, [3], [5]));

        // The base statement above (−15400) plus perRace 150 + podium 150.
        Assert.Equal(Rational.Parse("84900"), result.State.Balance);
    }

    [Fact]
    public void SettleRound_WinAddsWinAndPodiumBonuses()
    {
        var state = Fresh().WithSponsor(
            "sponsor.apex-lubricants", new DynastySponsorContract { SeasonsRemaining = 2 });
        var result = DynastyEconomyFold.SettleRound(Settle(state, [1], [5]));

        // income = 10000 (P1) + 2000 + 1100 + 150 (perRace) + 150 (podium) + 300 (win) = 13700
        // costs  = 23500 → net = −9800
        Assert.Equal(Rational.Parse("90200"), result.State.Balance);
    }

    [Fact]
    public void SettleRound_PayDriverDeal_SwapsPrizeForBacking()
    {
        var state = Fresh() with { SecondSeat = SecondSeatDeal.PayDriver };
        var result = DynastyEconomyFold.SettleRound(Settle(state, [3], [5]));

        // income = 5000 + 1100 + 10000/2 (backing) = 11100; costs = 4500 (no salary, prize forfeit)
        Assert.Equal(Rational.Parse("106600"), result.State.Balance);
    }

    [Fact]
    public void SettleRound_AccidentDnfBillsBySeverityAndSecondCarDnfBillsFlat()
    {
        var result = DynastyEconomyFold.SettleRound(Settle(
            Fresh(), [null], [null], dnf: DnfCause.DriverError, severity: AccidentSeverity.Heavy));

        // income = 1100 (appearance only); costs = 4500 + 19000 + 9000 (heavy) + 2000 (2nd car DNF)
        Assert.Equal(Rational.Parse("100000") + Rational.Parse("1100") - Rational.Parse("34500"),
            result.State.Balance);
        Assert.Equal("surplus", Assert.Single(result.Events).Cause);
    }

    [Fact]
    public void SettleRound_SatOutRound_StillPaysTheBills()
    {
        var result = DynastyEconomyFold.SettleRound(Settle(
            Fresh(), [], [4], playerStarted: false));

        // income = 3000 (second car P4); costs = 4500 + 19000 = 23500 → net −20500.
        Assert.Equal(Rational.Parse("79500"), result.State.Balance);
    }

    [Fact]
    public void SettleRound_DeficitCountsAndGraceTripsBankruptcy()
    {
        // Deep in the grace window already: one more deficit round is one past graceRounds=4.
        var state = Fresh("-100") with { DeficitRounds = 4 };
        var result = DynastyEconomyFold.SettleRound(Settle(
            state, [], [], playerStarted: false, hasSecondCar: false));

        Assert.Equal(Rational.Parse("-4600"), result.State.Balance); // −100 − 4500 in fees
        Assert.Equal(5, result.State.DeficitRounds);
        Assert.True(result.State.Bankrupt);
        Assert.True(result.WentBankrupt);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(JournalPhases.EconomyBankruptcy, result.Events[1].Phase);
        Assert.Equal("bankruptcy", result.Events[1].Cause);
    }

    [Fact]
    public void SettleRound_RecoveryResetsTheDeficitCounter()
    {
        var state = Fresh("100000") with { DeficitRounds = 3 };
        var result = DynastyEconomyFold.SettleRound(Settle(state, [1], [2]));

        Assert.Equal(0, result.State.DeficitRounds);
        Assert.False(result.State.Bankrupt);
    }

    [Fact]
    public void SettleRound_HardFloorIsImmediateBankruptcy()
    {
        // −22000 balance, first-ever deficit round, but the settlement lands below −25000.
        var result = DynastyEconomyFold.SettleRound(Settle(
            Fresh("-22000"), [], [], playerStarted: false, hasSecondCar: false));

        Assert.Equal(Rational.Parse("-26500"), result.State.Balance); // −22000 − 4500
        Assert.Equal(1, result.State.DeficitRounds);
        Assert.True(result.State.Bankrupt);
    }

    [Fact]
    public void ApplyDecisions_SignBuysAndEscalatesInSeqOrder()
    {
        var result = DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
        {
            State = Fresh(),
            Rules = Rules,
            Year = 1967,
            Round = 1,
            Decisions =
            [
                new() { Kind = DynastyEconomyDecisionKind.SignSponsor, SponsorId = "sponsor.apex-lubricants" },
                new() { Kind = DynastyEconomyDecisionKind.BuyDevelopment },
                new() { Kind = DynastyEconomyDecisionKind.BuyDevelopment },
                new() { Kind = DynastyEconomyDecisionKind.SetStaff, StaffTier = 2 },
                new() { Kind = DynastyEconomyDecisionKind.SetSecondSeat, SecondSeat = SecondSeatDeal.PayDriver },
            ],
        });

        // +500 signing, −8000 (level 0→1), −10800 (level 1→2, ×27/20), staff/second free.
        Assert.Equal(Rational.Parse("81700"), result.State.Balance);
        Assert.Equal(2, result.State.DevelopmentLevel);
        Assert.Equal(2, result.State.StaffTier);
        Assert.Equal(SecondSeatDeal.PayDriver, result.State.SecondSeat);
        Assert.Equal(2, result.State.SponsorContract("sponsor.apex-lubricants")!.SeasonsRemaining);
        Assert.Equal(5, result.Events.Count);
        Assert.All(result.Events, e => Assert.Equal(JournalPhases.EconomyApplied, e.Phase));
        Assert.Equal(
            ["sign-sponsor", "buy-development", "buy-development", "set-staff", "set-second-seat"],
            result.Events.Select(e => e.Cause));
    }

    [Fact]
    public void ApplyDecisions_StaffDiscountsTheNextIncrement()
    {
        var state = Fresh() with { StaffTier = 2 };
        var result = DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
        {
            State = state,
            Rules = Rules,
            Year = 1967,
            Round = 1,
            Decisions = [new() { Kind = DynastyEconomyDecisionKind.BuyDevelopment }],
        });

        // 8000 × (1 − 2/12) = 20000/3, exact, no rounding anywhere in the ledger.
        Assert.Equal(Rational.Parse("100000") - new Rational(20000, 3), result.State.Balance);
    }

    [Theory]
    [InlineData(DynastyEconomyDecisionKind.SignSponsor, "sponsor.no-such", null, "unknown sponsor")]
    [InlineData(DynastyEconomyDecisionKind.DropSponsor, "sponsor.apex-lubricants", null, "absent sponsor")]
    [InlineData(DynastyEconomyDecisionKind.SetStaff, null, 9, "invalid tier")]
    public void ApplyDecisions_TamperedInputsFailLoudly(
        DynastyEconomyDecisionKind kind, string? sponsorId, int? staffTier, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
            {
                State = Fresh(),
                Rules = Rules,
                Year = 1967,
                Round = 1,
                Decisions = [new() { Kind = kind, SponsorId = sponsorId, StaffTier = staffTier }],
            }));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyDecisions_DevelopmentPastTheCapFailsLoudly()
    {
        var state = Fresh() with { DevelopmentLevel = Rules.Development.MaxLevel };
        Assert.Throws<InvalidOperationException>(() =>
            DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
            {
                State = state,
                Rules = Rules,
                Year = 1967,
                Round = 1,
                Decisions = [new() { Kind = DynastyEconomyDecisionKind.BuyDevelopment }],
            }));
    }

    [Fact]
    public void SettleSeason_PrizeSponsorMoneyDecrementAndCarryover()
    {
        var state = (Fresh() with { DevelopmentLevel = 3 })
            .WithSponsor("sponsor.apex-lubricants", new DynastySponsorContract { SeasonsRemaining = 2 })
            .WithSponsor("sponsor.corsa-tyres", new DynastySponsorContract { SeasonsRemaining = 1 });
        var result = DynastyEconomyFold.SettleSeason(new DynastySeasonSettleContext
        {
            State = state,
            Rules = Rules,
            Year = 1967,
            ConstructorsPosition = 2,
            DriversPosition = 2,
        });

        // 26000 (C2) + 1000 + 1100 (perSeason) = 28100; no title bonus at P2.
        Assert.Equal(Rational.Parse("128100"), result.State.Balance);
        Assert.Equal(1, result.State.SponsorContract("sponsor.apex-lubricants")!.SeasonsRemaining);
        Assert.Null(result.State.SponsorContract("sponsor.corsa-tyres")); // 1 → 0 → expired
        Assert.Equal(1, result.State.DevelopmentLevel); // floor(3 × 1/2)
        var row = Assert.Single(result.Events);
        Assert.Equal(JournalPhases.EconomySeason, row.Phase);
        Assert.Contains("sponsor.corsa-tyres", row.DeltaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SettleSeason_ChampionCollectsTitleBonuses()
    {
        var state = Fresh().WithSponsor(
            "sponsor.apex-lubricants", new DynastySponsorContract { SeasonsRemaining = 2 });
        var result = DynastyEconomyFold.SettleSeason(new DynastySeasonSettleContext
        {
            State = state,
            Rules = Rules,
            Year = 1967,
            ConstructorsPosition = 1,
            DriversPosition = 1,
        });

        // 40000 (C1) + 1000 (perSeason) + 1000 (title) = 42000.
        Assert.Equal(Rational.Parse("142000"), result.State.Balance);
    }

    [Fact]
    public void SettleSeason_NoConstructorsTable_FallsBackToDriversPosition()
    {
        var result = DynastyEconomyFold.SettleSeason(new DynastySeasonSettleContext
        {
            State = Fresh(),
            Rules = Rules,
            Year = 1967,
            ConstructorsPosition = null,
            DriversPosition = 3,
        });

        Assert.Equal(Rational.Parse("118000"), result.State.Balance); // + 18000 (position 3)
    }

    [Fact]
    public void SchemaMismatch_RefusesToFold()
    {
        var state = Fresh() with { Version = 99 };
        Assert.Throws<InvalidOperationException>(() =>
            DynastyEconomyFold.SettleRound(Settle(state, [3], [5])));
    }
}

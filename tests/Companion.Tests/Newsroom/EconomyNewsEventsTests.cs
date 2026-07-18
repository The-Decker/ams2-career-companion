using Companion.Core.Newsroom;

namespace Companion.Tests.Newsroom;

/// <summary>The Dynasty economy news detectors (economy §8), to the ProgressionNewsEventsTests
/// bar: one event per trigger, deterministic re-detection, unique dedupe keys, terminal
/// bankruptcy stops the career's coverage, and a non-economy career emits nothing new.</summary>
public sealed class EconomyNewsEventsTests
{
    private static NewsroomSeason Season(
        IReadOnlyList<NewsroomRound> rounds,
        bool complete = false,
        string seasonAmount = "",
        bool windfall = false) => new()
    {
        Ordinal = 1,
        Year = 1967,
        ChampionshipRoundCount = rounds.Count,
        Complete = complete,
        PlayerTeamId = "team.mid",
        PlayerTeamName = "Mid Team",
        Rounds = rounds,
        EconomySeasonAmount = seasonAmount,
        EconomyWindfall = windfall,
    };

    private static NewsroomRound Round(int round) => new()
    {
        Round = round,
        Venue = $"Venue {round}",
        PlayerFinish = 8,
    };

    [Fact]
    public void EconomyTriggers_EmitOnceEachWithUniqueKeys()
    {
        var season = Season(
        [
            Round(1) with
            {
                EconomySponsorsSigned = ["Apex Lubricants", "Corsa Tyre Company"],
                EconomyBalance = "88,000",
            },
            Round(2) with
            {
                EconomyRepairAmount = "9,000",
                EconomyMajorRepair = true,
                EconomyBalance = "70,000",
            },
            Round(3) with
            {
                EconomyDevelopmentLevel = 8,
                EconomyDevelopmentMaxed = true,
                EconomyBalance = "40,000",
            },
        ]);

        var events = CareerNewsEvents.Detect([season]);

        var signed = events.Where(e => e.Kind == NewsEventKind.SponsorSigned).ToList();
        Assert.Equal(2, signed.Count);
        Assert.Equal(["Apex Lubricants", "Corsa Tyre Company"], signed.Select(e => e.Facts.SponsorName));
        Assert.Equal(2, signed.Select(e => e.DedupeKey).Distinct(StringComparer.Ordinal).Count());

        var repair = Assert.Single(events, e => e.Kind == NewsEventKind.MajorRepairBill);
        Assert.Equal("9,000", repair.Facts.MoneyAmount);
        Assert.Equal(2, repair.Round);

        var development = Assert.Single(events, e => e.Kind == NewsEventKind.DevelopmentMilestone);
        Assert.Equal(8, development.Facts.MilestoneValue);
        Assert.Equal("development", development.Facts.MilestoneCounter);

        // Deterministic re-detection: the same input yields the identical event set.
        Assert.Equal(events.Select(e => e.DedupeKey), CareerNewsEvents.Detect([season]).Select(e => e.DedupeKey));
        Assert.Equal(events.Count, events.Select(e => e.DedupeKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Brink_IsEdgeTriggered_AndRearmsAfterRecovery()
    {
        var season = Season(
        [
            Round(1) with { EconomyOnTheBrink = true, EconomyBalance = "-2,000" },
            Round(2) with { EconomyOnTheBrink = true, EconomyBalance = "-1,500" }, // same streak, no second story
            Round(3) with { EconomyBalance = "5,000" },                            // recovered, re-arms
            Round(4) with { EconomyOnTheBrink = true, EconomyBalance = "-800" },   // a new brush, a new story
        ]);

        var brinks = CareerNewsEvents.Detect([season])
            .Where(e => e.Kind == NewsEventKind.NearBankruptcy)
            .ToList();
        Assert.Equal([1, 4], brinks.Select(e => e.Round));
    }

    [Fact]
    public void Bankruptcy_IsTerminal_NoCoverageAfterTheCollapse()
    {
        var season = Season(
        [
            Round(1),
            Round(2) with { EconomyBankrupt = true, EconomyBalance = "-26,400" },
            Round(3) with { EconomySponsorsSigned = ["Ghost Sponsor"] }, // must never be reached
        ], complete: true);

        var events = CareerNewsEvents.Detect([season]);

        var bankruptcy = Assert.Single(events, e => e.Kind == NewsEventKind.BankruptcyDeclared);
        Assert.Equal(2, bankruptcy.Round);
        Assert.Equal("-26,400", bankruptcy.Facts.MoneyAmount);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.SponsorSigned);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.SeasonCompleted);
    }

    [Fact]
    public void Windfall_EmitsAtTheSeasonEnd()
    {
        var season = Season([Round(1)], complete: true, seasonAmount: "42,000", windfall: true);

        var windfall = Assert.Single(
            CareerNewsEvents.Detect([season]),
            e => e.Kind == NewsEventKind.FinancialWindfall);
        Assert.Equal(CareerNewsEvents.SeasonEndRound, windfall.Round);
        Assert.Equal("42,000", windfall.Facts.MoneyAmount);
    }

    [Fact]
    public void NonEconomyCareer_EmitsNoEconomyEvents()
    {
        var season = Season([Round(1), Round(2)], complete: true);
        Assert.DoesNotContain(
            CareerNewsEvents.Detect([season]),
            e => e.Kind is NewsEventKind.SponsorSigned or NewsEventKind.MajorRepairBill
                or NewsEventKind.NearBankruptcy or NewsEventKind.FinancialWindfall
                or NewsEventKind.BankruptcyDeclared or NewsEventKind.DevelopmentMilestone);
    }
}

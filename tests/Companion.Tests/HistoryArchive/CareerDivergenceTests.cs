using Companion.Core.Newsroom;
using Companion.ViewModels.Services;
using Xunit;

namespace Companion.Tests.HistoryArchive;

public class CareerDivergenceTests
{
    [Fact]
    public void ChangedAndUnchangedWinnersAreClassifiedSideBySide()
    {
        var report = CareerDivergence.Compare(
            Career(
                Round(1, winner: "Niki Lauda", winnerTeam: "Ferrari"),
                Round(2, winner: "You", winnerTeam: "Privateer")),
            Historical(1976,
                HistRound(1, "Niki Lauda", "Ferrari"),
                HistRound(2, "James Hunt", "McLaren"),
                HistRound(3, "James Hunt", "McLaren")));

        Assert.Equal(1976, report.SeasonYear);
        var r1 = Assert.Single(report.Rounds, r => r.Round == 1);
        Assert.Equal(DivergenceKind.UnchangedEvent, r1.Kind);
        Assert.False(r1.NonHistoricalWinner);

        var r2 = Assert.Single(report.Rounds, r => r.Round == 2);
        Assert.Equal(DivergenceKind.AlternateOutcome, r2.Kind);
        Assert.Equal("James Hunt", r2.HistoricalWinner);
        Assert.Equal("You", r2.CareerWinner);
        Assert.True(r2.NonHistoricalWinner, "the player's entrant exists in no historical record");

        var r3 = Assert.Single(report.Rounds, r => r.Round == 3);
        Assert.Equal(DivergenceKind.NotYetRaced, r3.Kind);
        Assert.Equal("James Hunt", r3.HistoricalWinner);
        Assert.Equal("", r3.CareerWinner);

        Assert.Equal(1, report.AlternateOutcomes);
        Assert.Equal(1, report.UnchangedEvents);
    }

    [Fact]
    public void TheChampionVerdictWaitsForACompleteSeason()
    {
        var running = CareerDivergence.Compare(
            Career(Round(1, winner: "You", winnerTeam: "Privateer")),
            Historical(1976, HistRound(1, "Niki Lauda", "Ferrari")));
        Assert.Null(running.ChampionChanged);
        Assert.Equal("", running.CareerChampion);
        Assert.Equal("James Hunt", running.HistoricalChampion); // the record itself is never hidden

        var finished = CareerDivergence.Compare(
            Career(Round(1, winner: "You", winnerTeam: "Privateer")) with
            {
                Complete = true,
                ChampionName = "Niki Lauda",
            },
            Historical(1976, HistRound(1, "Niki Lauda", "Ferrari")));
        Assert.True(finished.ChampionChanged);
    }

    [Fact]
    public void DivergenceEventsCoverChangesAndTheSeasonVerdict()
    {
        var report = CareerDivergence.Compare(
            Career(
                Round(1, winner: "James Hunt", winnerTeam: "McLaren"),
                Round(2, winner: "You", winnerTeam: "Privateer")) with
            {
                Complete = true,
                ChampionName = "James Hunt",
            },
            Historical(1976,
                HistRound(1, "Niki Lauda", "Ferrari"),
                HistRound(2, "James Hunt", "McLaren")));

        var events = CareerDivergence.ToNewsEvents(report);

        // Round 1 changed (Hunt for Lauda), round 2 changed (You for Hunt), champion held.
        Assert.Equal(2, events.Count(e => e.Kind == NewsEventKind.HistoryDiverged));
        var held = Assert.Single(events, e => e.Kind == NewsEventKind.HistoryHeld);
        Assert.Equal(CareerNewsEvents.SeasonEndRound, held.Round);
        Assert.Equal("James Hunt", held.Facts.WinnerName);
        Assert.False(held.Facts.ClinchedTitle);

        var playerWin = Assert.Single(events, e => e.Round == 2 && e.Kind == NewsEventKind.HistoryDiverged);
        Assert.Equal("player", playerWin.SubjectId);
        Assert.Equal("James Hunt", playerWin.Facts.RivalName); // the displaced historical winner

        // Distinct keys, the champion verdict never collides with a round divergence.
        Assert.Equal(events.Count, events.Select(e => e.DedupeKey).Distinct().Count());
    }

    [Fact]
    public void UndocumentedRoundsSaySoInsteadOfInventing()
    {
        var report = CareerDivergence.Compare(
            Career(Round(4, winner: "You", winnerTeam: "Privateer")),
            Historical(1976, HistRound(1, "Niki Lauda", "Ferrari")));

        var r4 = Assert.Single(report.Rounds, r => r.Round == 4);
        Assert.Equal(DivergenceKind.NotDocumented, r4.Kind);
        Assert.Equal("", r4.HistoricalWinner);
        Assert.Empty(CareerDivergence.ToNewsEvents(report)
            .Where(e => e.Round == 4)); // no divergence claim without a documented fact
    }

    [Fact]
    public void TheComparisonIsDeterministic()
    {
        var career = Career(
            Round(1, winner: "Niki Lauda", winnerTeam: "Ferrari"),
            Round(2, winner: "You", winnerTeam: "Privateer"));
        var historical = Historical(1976,
            HistRound(1, "Niki Lauda", "Ferrari"),
            HistRound(2, "James Hunt", "McLaren"));

        var first = CareerDivergence.Compare(career, historical);
        var second = CareerDivergence.Compare(career, historical);

        Assert.Equal(first with { Rounds = [] }, second with { Rounds = [] });
        Assert.Equal(first.Rounds, (IEnumerable<RoundDivergence>)second.Rounds);
    }

    private static NewsroomSeason Career(params NewsroomRound[] rounds) => new()
    {
        Ordinal = 1,
        Year = 1976,
        ChampionshipRoundCount = Math.Max(rounds.Length, 3),
        Rounds = rounds,
    };

    private static NewsroomRound Round(int round, string winner, string winnerTeam) => new()
    {
        Round = round,
        Venue = $"Venue {round}",
        WinnerId = winner == "You" ? "player" : $"driver.{winner.Replace(' ', '_').ToLowerInvariant()}",
        WinnerName = winner,
        WinnerTeamName = winnerTeam,
    };

    private static HistoricalSeason Historical(int year, params HistoricalRound[] rounds) => new()
    {
        Year = year,
        Source = "test fixture",
        DriversChampion = new HistoricalChampion { Driver = "James Hunt", Team = "McLaren" },
        Rounds = rounds,
    };

    private static HistoricalRound HistRound(int round, string winner, string team) => new()
    {
        Round = round,
        Name = $"Grand Prix {round}",
        Winner = winner,
        WinnerTeam = team,
        Results =
        [
            new HistoricalResult { Pos = "1", Driver = winner, Team = team },
            new HistoricalResult { Pos = "2", Driver = "Somebody Else", Team = "Elsewhere" },
        ],
    };
}

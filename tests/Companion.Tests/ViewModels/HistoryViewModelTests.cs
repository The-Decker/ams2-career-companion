using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The History / Scrapbook lens (career-hub-design.md §4, decision 18): a read-only projection
/// of <see cref="ICareerSession.CareerTimeline"/> into per-season scrapbook cards, a records
/// book, and the archived <see cref="ICareerSession.ReadFeed"/> dispatches. These tests pin the
/// view-model's formatting + empty-state behaviour against a controlled timeline, independent of
/// the real projection (covered end-to-end in <see cref="SessionServiceTests"/>).
/// </summary>
public sealed class HistoryViewModelTests
{
    [Fact]
    public void Empty_timeline_shows_the_empty_state()
    {
        var history = new HistoryViewModel(new HistoryFakeSession());

        // IsEmpty gates the whole non-empty scrapbook in the view, so the empty state is the
        // only thing shown regardless of the (defaulted) records book.
        Assert.True(history.IsEmpty);
        Assert.Empty(history.Seasons);
        Assert.False(history.HasArticles);
        Assert.Null(history.Records.Rows.FirstOrDefault(r => r.Label == "Best finish")); // no bests before any race
    }

    [Fact]
    public void Season_cards_render_newest_first_with_the_champion_crown()
    {
        var session = new HistoryFakeSession
        {
            Timeline = new CareerTimeline
            {
                Seasons =
                [
                    new CareerSeasonCard
                    {
                        SeasonYear = 1967, PlayerPosition = 4, RoundsApplied = 11, RoundCount = 11,
                        IsComplete = true, FinalReputation = 42.5, FinalOpi = 0.4,
                        ChampionName = "Denny Hulme", PlayerIsChampion = false,
                        Headlines = ["Hulme takes the crown"],
                    },
                    new CareerSeasonCard
                    {
                        SeasonYear = 1968, PlayerPosition = 1, RoundsApplied = 6, RoundCount = 12,
                        IsComplete = false, ChampionName = "You", PlayerIsChampion = false,
                    },
                ],
            },
        };

        var history = new HistoryViewModel(session);

        Assert.False(history.IsEmpty);
        // Oldest-first timeline is reversed for the scrapbook: the latest season shows at the top.
        Assert.Equal(1968, history.Seasons[0].SeasonYear);
        Assert.Equal(1967, history.Seasons[1].SeasonYear);

        var inProgress = history.Seasons[0];
        Assert.False(inProgress.IsComplete);
        Assert.Equal("In progress — 6 of 12 rounds", inProgress.ResultText);
        Assert.False(inProgress.HasForm); // no folded rep/OPI until the season completes

        var completed = history.Seasons[1];
        Assert.True(completed.IsComplete);
        Assert.Equal("Finished P4", completed.ResultText);
        Assert.Equal("Champion: Denny Hulme", completed.ChampionText);
        Assert.True(completed.HasForm);
        Assert.Contains("Reputation 42.5", completed.FormText);
        Assert.True(completed.HasHeadlines);
    }

    [Fact]
    public void Records_book_shows_bests_and_only_streaks_worth_naming()
    {
        var session = new HistoryFakeSession
        {
            Timeline = new CareerTimeline
            {
                Seasons = [new CareerSeasonCard { SeasonYear = 1967, RoundsApplied = 5, RoundCount = 11 }],
                Records = new CareerRecordsBook
                {
                    BestFinish = 1, Wins = 3, Podiums = 4, Championships = 1,
                    TotalPoints = 27.5, SeasonsRaced = 1,
                    LongestWinStreak = 2, LongestPodiumStreak = 1,
                },
            },
        };

        var rows = new HistoryViewModel(session).Records.Rows;

        Assert.Contains(rows, r => r is { Label: "Best finish", Value: "P1" });
        Assert.Contains(rows, r => r is { Label: "Race wins", Value: "3" });
        Assert.Contains(rows, r => r is { Label: "Total points", Value: "27.5" });
        // A 2-race win streak IS worth naming; a 1-podium "streak" is not shown.
        Assert.Contains(rows, r => r.Label == "Longest win streak");
        Assert.DoesNotContain(rows, r => r.Label == "Longest podium streak");
    }

    [Fact]
    public void Archived_articles_reuse_the_read_feed_dispatches()
    {
        var session = new HistoryFakeSession
        {
            Timeline = new CareerTimeline
            {
                Seasons = [new CareerSeasonCard { SeasonYear = 1967, RoundsApplied = 1, RoundCount = 11 }],
            },
            Feed =
            [
                new NewsDispatch
                {
                    Headline = "Hulme wins at Kyalami", SeasonYear = 1967, Round = 1,
                    Body = "A commanding drive.", WhyText = "You finished P1.",
                },
            ],
        };

        var history = new HistoryViewModel(session);

        Assert.True(history.HasArticles);
        var article = Assert.Single(history.ArchivedArticles);
        Assert.Equal("Hulme wins at Kyalami", article.Headline);
        Assert.Equal("1967 · Round 1", article.Meta);
        Assert.True(article.HasWhy);
        Assert.False(article.IsExpanded);
        article.ToggleExpandedCommand.Execute(null);
        Assert.True(article.IsExpanded); // click-to-expand parity with the News ticker
    }

    /// <summary>A session that only implements what the History lens reads — the additive
    /// default seam members keep this minimal and prove the lens couples to nothing else.</summary>
    [Fact]
    public void Season_card_carries_the_real_historical_results_for_its_year()
    {
        var session = new HistoryFakeSession
        {
            Timeline = new CareerTimeline
            {
                Seasons =
                [
                    new CareerSeasonCard { SeasonYear = 1967, RoundsApplied = 1, RoundCount = 11 },
                    new CareerSeasonCard { SeasonYear = 1968, RoundsApplied = 1, RoundCount = 12 },
                ],
            },
            HistoricalSeasons = new Dictionary<int, HistoricalSeason>
            {
                [1967] = new HistoricalSeason
                {
                    Year = 1967,
                    Source = "Derived from f1db (CC BY 4.0)",
                    DriversChampion = new HistoricalChampion { Driver = "Denny Hulme", Team = "Brabham", Points = "51" },
                    ConstructorsChampion = new HistoricalTeamChampion { Team = "Brabham", Points = "63" },
                    Rounds =
                    [
                        new HistoricalRound
                        {
                            Round = 1, Name = "South African Grand Prix",
                            Winner = "Pedro Rodríguez", WinnerTeam = "Cooper", FastestLap = "Denny Hulme",
                            Results =
                            [
                                new HistoricalResult { Pos = "1", Driver = "Pedro Rodríguez", Team = "Cooper" },
                                new HistoricalResult { Pos = "DNF", Driver = "Jim Clark", Team = "Lotus", Status = "Engine" },
                            ],
                        },
                    ],
                },
                // 1968 deliberately has NO shipped history → that card carries none.
            },
        };

        var history = new HistoryViewModel(session);

        var card1968 = history.Seasons[0]; // newest first
        var card1967 = history.Seasons[1];

        Assert.False(card1968.HasRealSeason);
        Assert.True(card1967.HasRealSeason);

        var real = card1967.RealSeason!;
        Assert.Equal(1967, real.Year);
        Assert.False(real.IsExpanded); // collapsed by default
        Assert.Contains("Denny Hulme", real.DriversChampionText);
        Assert.Contains("Brabham", real.DriversChampionText);
        Assert.True(real.HasConstructorsChampion);

        var round = Assert.Single(real.Rounds);
        Assert.Equal("R1", round.RoundLabel);
        Assert.Contains("Pedro Rodríguez", round.WinnerText);
        Assert.Contains("Cooper", round.WinnerText);
        Assert.True(round.HasFastestLap);
        Assert.Equal(2, round.Results.Count);

        var dnf = round.Results[1];
        Assert.Equal("DNF", dnf.Pos);
        Assert.True(dnf.HasStatus);
        Assert.Equal("Engine", dnf.Status);
    }

    private sealed class HistoryFakeSession : ICareerSession
    {
        public Companion.ViewModels.Services.CareerTimeline Timeline { get; init; } =
            Companion.ViewModels.Services.CareerTimeline.Empty;

        public IReadOnlyList<NewsDispatch> Feed { get; init; } = [];

        public Companion.ViewModels.Services.CareerTimeline CareerTimeline() => Timeline;

        public IReadOnlyDictionary<int, HistoricalSeason> HistoricalSeasons { get; init; } =
            new Dictionary<int, HistoricalSeason>();

        public HistoricalSeason? HistoricalSeason(int year) =>
            HistoricalSeasons.GetValueOrDefault(year);

        public IReadOnlyList<NewsDispatch> ReadFeed() => Feed;

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerSummary Summary => new()
        {
            CareerName = "Fake",
            SeasonYear = 1967,
            SeriesName = "Test",
            CurrentRound = 1,
            RoundCount = 2,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        };

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) =>
            new() { RoundPoints = [], Movements = [], Headline = "" };

        public void Apply(ResultDraft draft) { }

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId) { }
    }
}

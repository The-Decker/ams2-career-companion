using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Pins the display-only History archive contract to the existing session reads. These tests use
/// plain read models only: no career folds, replay inputs, or generated-news behavior is exercised.
/// </summary>
public sealed class HistoryArchiveContractTests
{
    [Fact]
    public void Hero_uses_live_player_totals_and_joins_the_named_rival_by_id()
    {
        var session = new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: 3),
            Timeline = TimelineWithRaces(),
            Paddock = new SmgpPaddockModel
            {
                Drivers =
                [
                    Driver(
                        id: "driver.player",
                        name: "Mike Racer",
                        teamId: "team.bullets",
                        teamName: "Bullets",
                        isPlayer: true,
                        career: new SmgpCareerStats
                        {
                            Starts = 21,
                            Wins = 4,
                            Podiums = 9,
                            Poles = 3,
                            Top5s = 12,
                            Points = 87,
                            Titles = 1,
                        },
                        narrative: "Climbing toward the front."),
                    Driver(
                        id: "driver.senna",
                        name: "A. Senna",
                        teamId: "team.madonna",
                        teamName: "Madonna",
                        isPlayer: false,
                        career: null),
                ],
                Teams = [],
            },
            RivalId = "driver.senna",
        };

        var hero = new HistoryViewModel(session).Hero;

        Assert.Equal("Mike Racer", hero.PlayerName);
        Assert.Equal("Bullets", hero.TeamName);
        Assert.Equal("P3", hero.StandingText);
        Assert.Equal("Climbing toward the front.", hero.Trajectory);
        Assert.Equal(21, hero.Starts);
        Assert.Equal(4, hero.Wins);
        Assert.Equal(9, hero.Podiums);
        Assert.Equal(3, hero.Poles);
        Assert.Equal(1, hero.Championships);
        Assert.Equal(87, hero.Points);
        Assert.Equal("driver.senna", hero.CurrentRivalId);
        Assert.Equal("A. Senna", hero.CurrentRivalName);
        Assert.Equal("Madonna", hero.CurrentRivalTeamName);
        Assert.Equal("portraits/driver.senna.jpg", hero.CurrentRivalPortraitKey);
    }

    [Fact]
    public void Hero_falls_back_only_to_timeline_facts_when_smgp_identity_is_unavailable()
    {
        var session = new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: null),
            Timeline = new CareerTimeline
            {
                Seasons =
                [
                    new CareerSeasonCard
                    {
                        SeasonYear = 1990,
                        RoundsApplied = 1,
                        RoundCount = 16,
                        RoundLines = [Round(1, "Brazil", 2, 6.0)],
                    },
                ],
                Records = new CareerRecordsBook
                {
                    Wins = 2,
                    Podiums = 5,
                    Championships = 1,
                    TotalPoints = 44.5,
                },
            },
        };

        var history = new HistoryViewModel(session);
        var hero = history.Hero;

        Assert.Equal("driver.player", hero.PlayerName);
        Assert.False(hero.HasTeam);
        Assert.False(hero.HasPortrait);
        Assert.False(hero.HasCar);
        Assert.False(hero.HasCurrentRival);
        Assert.False(hero.HasTrajectory);
        Assert.Null(hero.Starts);
        Assert.Null(hero.Poles);
        Assert.Equal(2, hero.Wins);
        Assert.Equal(5, hero.Podiums);
        Assert.Equal(1, hero.Titles);
        Assert.Equal(44.5, hero.Points);
        Assert.Equal("Not yet classified", hero.StandingText);
        Assert.True(history.IsLegacyLimited);
        Assert.False(history.IsFresh);
    }

    [Fact]
    public void Race_archive_is_newest_first_uses_cumulative_deltas_and_does_not_invent_an_nc_reason()
    {
        var history = new HistoryViewModel(new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: 2),
            Timeline = TimelineWithRaces(),
        });

        Assert.Collection(
            history.RaceArchive,
            race =>
            {
                Assert.Equal("race:2:2", race.Key);
                Assert.Equal("Monaco", race.VenueName);
                Assert.Equal(6.5, race.PointsEarned);
                Assert.Equal(16.5, race.PlayerPointsAfter);
            },
            race =>
            {
                Assert.Equal("race:2:1", race.Key);
                Assert.Equal(10.0, race.PointsEarned);
            },
            race =>
            {
                Assert.Equal("race:1:2", race.Key);
                Assert.Equal("NC", race.FinishText);
                Assert.Equal(HistoryRaceStatus.NotClassified, race.Status);
                Assert.Equal("NOT CLASSIFIED", race.StatusLabel);
                Assert.Equal(0.0, race.PointsEarned);
                Assert.Contains("Not classified", race.StoryContext);
                Assert.False(race.StoryContext.Contains("DNF", StringComparison.OrdinalIgnoreCase));
                Assert.False(race.StoryContext.Contains("DNS", StringComparison.OrdinalIgnoreCase));
                Assert.False(race.StoryContext.Contains("DSQ", StringComparison.OrdinalIgnoreCase));
            },
            race =>
            {
                Assert.Equal("race:1:1", race.Key);
                Assert.Equal(5.0, race.PointsEarned);
            });
    }

    [Fact]
    public void Race_filters_and_search_compose_and_clear_back_to_the_full_archive()
    {
        var history = new HistoryViewModel(new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: 2),
            Timeline = TimelineWithRaces(),
        });

        var wins = Assert.Single(history.RaceFilters, filter => filter.Kind == HistoryRaceFilterKind.Wins);
        Assert.Equal(1, wins.Count);

        history.SelectedRaceFilter = wins;
        var win = Assert.Single(history.FilteredRaces);
        Assert.Equal("Brazil", win.VenueName);
        Assert.True(history.HasActiveRaceFilter);

        history.SearchText = "Monaco";
        Assert.Empty(history.FilteredRaces);
        Assert.True(history.IsRaceFilterEmpty);
        Assert.True(history.ClearRaceFiltersCommand.CanExecute(null));

        history.ClearRaceFiltersCommand.Execute(null);

        Assert.Equal("", history.SearchText);
        Assert.True(history.SelectedRaceFilter?.IsAll);
        Assert.Equal(4, history.FilteredRaces.Count);
        Assert.False(history.HasActiveRaceFilter);
        Assert.False(history.IsRaceFilterEmpty);
        Assert.False(history.ClearRaceFiltersCommand.CanExecute(null));

        history.SearchText = "NOT CLASSIFIED";
        var notClassified = Assert.Single(history.FilteredRaces);
        Assert.Equal("race:1:2", notClassified.Key);
    }

    [Fact]
    public void Events_are_chronological_and_join_a_rivalry_subject_portrait_without_inventing_events()
    {
        var player = Driver(
            id: "driver.player",
            name: "Mike Racer",
            teamId: "team.bullets",
            teamName: "Bullets",
            isPlayer: true,
            career: null,
            timeline:
            [
                new SmgpCareerBeat
                {
                    Season = 1,
                    Round = SmgpDispatch.SeasonStartRound,
                    WhenLabel = "Season 1",
                    Kind = SmgpBeatKind.Arrived,
                    Headline = "ARRIVAL",
                    Detail = "Joined the grid.",
                },
                new SmgpCareerBeat
                {
                    Season = 1,
                    Round = 1,
                    WhenLabel = "Season 1 - Brazil",
                    Kind = SmgpBeatKind.RivalryEarned,
                    Headline = "RIVALRY WON",
                    Detail = "A seat battle was earned.",
                    SubjectId = "driver.senna",
                },
            ]);
        var rival = Driver(
            id: "driver.senna",
            name: "A. Senna",
            teamId: "team.madonna",
            teamName: "Madonna",
            isPlayer: false,
            career: null);
        var session = new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: 2),
            Timeline = new CareerTimeline
            {
                Seasons =
                [
                    new CareerSeasonCard
                    {
                        SeasonYear = 1990,
                        RoundsApplied = 2,
                        RoundCount = 16,
                        RoundLines =
                        [
                            Round(1, "Brazil", 1, 9.0),
                            Round(2, "San Marino", 3, 13.0),
                        ],
                    },
                ],
            },
            Paddock = new SmgpPaddockModel { Drivers = [player, rival], Teams = [] },
        };

        var events = new HistoryViewModel(session).Events;

        Assert.Collection(
            events,
            item => Assert.Equal(HistoryEventKind.CareerStart, item.Kind),
            item => Assert.Equal(HistoryEventKind.Race, item.Kind),
            item =>
            {
                Assert.Equal(HistoryEventKind.Rivalry, item.Kind);
                Assert.Equal("driver.senna", item.SubjectDriverId);
                Assert.Equal("portraits/driver.senna.jpg", item.SubjectPortraitKey);
                Assert.Equal("race:1:1", item.RaceKey);
                Assert.True(item.HasRaceLink);
            },
            item => Assert.Equal(HistoryEventKind.Race, item.Kind));
    }

    [Fact]
    public void Latest_dispatches_are_newest_first_capped_at_six_and_preserve_story_identity()
    {
        var session = new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: 1),
            Timeline = TimelineWithRaces(),
            Dispatches = Enumerable.Range(1, 6)
                .Select(round => new SmgpDispatch
                {
                    SortSeason = 2,
                    SortRound = round,
                    SortSeq = 0,
                    WhenLabel = $"Season 2 - Round {round}",
                    Kind = round == 6 ? SmgpDispatchKind.TitleRace : SmgpDispatchKind.RaceResult,
                    Headline = $"SMGP ROUND {round}",
                    Body = $"Body {round}",
                    DriverArtKey = round == 6 ? "driver.senna" : "",
                    TeamArtKey = round == 6 ? "team.madonna" : "",
                })
                .ToList(),
            Feed =
            [
                new NewsDispatch
                {
                    SeasonYear = 1991,
                    Round = null,
                    Kind = "season",
                    Headline = "SEASON REVIEW",
                    Body = "The season closes.",
                    WhyText = "The final table is folded.",
                },
            ],
        };

        var dispatches = new HistoryViewModel(session).LatestDispatches;

        Assert.Equal(6, dispatches.Count);
        Assert.Equal("SEASON REVIEW", dispatches[0].Headline);
        Assert.Equal(HistoryDispatchImportance.Major, dispatches[0].Importance);
        Assert.Equal("Season", dispatches[0].Category);
        Assert.Equal("The final table is folded.", dispatches[0].WhyText);

        var titleStory = dispatches[1];
        Assert.Equal("SMGP ROUND 6", titleStory.Headline);
        Assert.Equal("Championship", titleStory.Category);
        Assert.Equal(HistoryDispatchImportance.Major, titleStory.Importance);
        Assert.Equal("driver.senna", titleStory.DriverPortraitKey);
        Assert.Equal("team.madonna", titleStory.TeamArtKey);
        Assert.Equal("race:2:6", titleStory.HistoryEventKey);
        Assert.True(titleStory.HasHistoryLink);

        Assert.Equal(
            ["SEASON REVIEW", "SMGP ROUND 6", "SMGP ROUND 5", "SMGP ROUND 4", "SMGP ROUND 3", "SMGP ROUND 2"],
            dispatches.Select(dispatch => dispatch.Headline).ToArray());
        Assert.DoesNotContain(dispatches, dispatch => dispatch.Headline == "SMGP ROUND 1");
    }

    [Fact]
    public void A_career_without_recorded_races_exposes_the_explicit_fresh_state()
    {
        var history = new HistoryViewModel(new ArchiveSession
        {
            SummaryValue = Summary(playerPosition: null),
            Timeline = CareerTimeline.Empty,
        });

        Assert.True(history.IsFresh);
        Assert.False(history.HasAnyRace);
        Assert.False(history.IsLegacyLimited);
        Assert.False(history.IsLoading);
        Assert.False(history.IsRaceFilterEmpty);
        Assert.Empty(history.RaceArchive);
        Assert.Empty(history.Events);
        Assert.Empty(history.LatestDispatches);
    }

    private static CareerSummary Summary(int? playerPosition) => new()
    {
        CareerName = "SMGP Archive",
        SeasonYear = 1991,
        SeriesName = "F1 Super Monaco GP",
        CurrentRound = 2,
        RoundCount = 16,
        PlayerDriverId = "driver.player",
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        PlayerPosition = playerPosition,
    };

    private static CareerTimeline TimelineWithRaces() => new()
    {
        Seasons =
        [
            new CareerSeasonCard
            {
                SeasonYear = 1990,
                RoundsApplied = 2,
                RoundCount = 16,
                RoundLines =
                [
                    Round(1, "Interlagos", 4, 5.0, rivalName: "A. Senna", rivalFinish: 1),
                    Round(2, "San Marino", null, 5.0, rivalName: "A. Senna", rivalFinish: 2),
                ],
            },
            new CareerSeasonCard
            {
                SeasonYear = 1991,
                RoundsApplied = 2,
                RoundCount = 16,
                RoundLines =
                [
                    Round(1, "Brazil", 1, 10.0, championAfter: "Mike Racer"),
                    Round(2, "Monaco", 2, 16.5, championAfter: "A. Senna"),
                ],
            },
        ],
        Records = new CareerRecordsBook
        {
            Wins = 1,
            Podiums = 2,
            TotalPoints = 21.5,
        },
    };

    private static CareerSeasonRoundLine Round(
        int round,
        string venue,
        int? playerFinish,
        double pointsAfter,
        string? rivalName = null,
        int? rivalFinish = null,
        string? championAfter = null) => new()
    {
        Round = round,
        Venue = venue,
        PlayerFinish = playerFinish,
        RivalName = rivalName,
        RivalFinish = rivalFinish,
        ChampionAfter = championAfter,
        PlayerPointsAfter = pointsAfter,
    };

    private static SmgpDriverCard Driver(
        string id,
        string name,
        string teamId,
        string teamName,
        bool isPlayer,
        SmgpCareerStats? career,
        string narrative = "",
        IReadOnlyList<SmgpCareerBeat>? timeline = null) => new()
    {
        DriverId = id,
        Name = name,
        TeamId = teamId,
        TeamName = teamName,
        Number = null,
        PortraitKey = $"portraits/{id}.jpg",
        CarKey = $"cars/{id}.png",
        Epithet = "",
        Bio = [],
        Quotes = [],
        IsPlayer = isPlayer,
        Career = career,
        Season = null,
        Prestige = isPlayer ? 3 : 5,
        Timeline = timeline ?? [],
        NarrativeIntro = narrative,
    };

    private sealed class ArchiveSession : ICareerSession
    {
        public CareerSummary SummaryValue { get; init; } = Summary(playerPosition: null);
        public CareerTimeline Timeline { get; init; } = Companion.ViewModels.Services.CareerTimeline.Empty;
        public SmgpPaddockModel? Paddock { get; init; }
        public string? RivalId { get; init; }
        public IReadOnlyList<SmgpDispatch> Dispatches { get; init; } = [];
        public IReadOnlyList<NewsDispatch> Feed { get; init; } = [];

        public CareerSummary Summary => SummaryValue;
        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerTimeline CareerTimeline() => Timeline;
        public SmgpPaddockModel? SmgpPaddock() => Paddock;
        public string? CurrentSmgpRivalDriverId() => RivalId;
        public IReadOnlyList<SmgpDispatch> SmgpDispatches() => Dispatches;
        public IReadOnlyList<NewsDispatch> ReadFeed() => Feed;

        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }
}

using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

namespace Companion.Tests.ViewModels;

public sealed class NewsContractTests
{
    [Fact]
    public void UnifiedProjection_MergesExactSources_AndLinksOnlyRecordedRaces()
    {
        var session = new NewsSession
        {
            Timeline = Timeline(Round(1, "Monaco")),
            Paddock = Paddock(),
            Dispatches =
            [
                new SmgpDispatch
                {
                    WhenLabel = "Season 1 · Monaco",
                    Kind = SmgpDispatchKind.RaceResult,
                    Headline = "A clean result",
                    Body = "The paddock saw the result.",
                    DriverArtKey = "driver.rival",
                    TeamArtKey = "team.rival",
                    SortSeason = 1,
                    SortRound = 1,
                    SortSeq = 7,
                },
            ],
            Feed =
            [
                new NewsDispatch
                {
                    Headline = "Season in review",
                    SeasonYear = 1990,
                    Kind = "season",
                    Body = "The year closed.",
                    WhyText = "The final table was recorded.",
                },
            ],
        };

        var vm = new NewsViewModel(session);

        Assert.Equal(2, vm.Stories.Count);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLegacyLimited);

        var smgp = Assert.Single(vm.Stories, story => story.Key == "smgp:1:1:7");
        Assert.Equal(1, smgp.SeasonOrdinal);
        Assert.Equal(1990, smgp.SeasonYear);
        Assert.Equal(1, smgp.Round);
        Assert.Equal("Round 1", smgp.RoundLabel);
        Assert.Equal(NewsStoryCategory.RaceReport, smgp.Category);
        Assert.Equal(NewsStoryImportance.Standard, smgp.Importance);
        Assert.Equal("A clean result", smgp.Headline);
        Assert.Equal("The paddock saw the result.", smgp.Body);
        Assert.Equal("Rival Driver", smgp.DriverName);
        Assert.Equal("Rival Team", smgp.TeamName);
        Assert.Equal("driver.rival", smgp.DriverPortraitKey);
        Assert.Equal("team.rival", smgp.TeamArtKey);
        Assert.Equal("cars/driver.rival.png", smgp.CarArtKey);
        Assert.Equal("race:1:1", smgp.HistoryEventKey);
        Assert.True(smgp.HasHistoryLink);
        Assert.True(smgp.CanRead);

        // Year and venue come from exact structured timeline facts; no calendar date or track id is invented.
        Assert.Equal("1990", smgp.DateLabel);
        Assert.Equal("Monaco", smgp.VenueName);
        Assert.Equal("", smgp.TrackArtKey);
        Assert.Equal("", smgp.Standfirst);

        var journal = Assert.Single(vm.Stories, story => story.Key == "journal:1990:season:0");
        Assert.Equal(NewsStoryCategory.Paddock, journal.Category);
        Assert.Equal(NewsStoryImportance.Major, journal.Importance);
        Assert.Equal("", journal.HistoryEventKey);
        Assert.Same(journal, vm.LeadStory);
        Assert.Single(vm.SecondaryStories, smgp);

        // Existing consumers keep their established journal-only Items projection.
        Assert.Single(vm.Items);
        Assert.Equal("Season in review", vm.Items[0].Headline);
    }

    [Fact]
    public void SmgpOnlyFeed_IsNotEmpty_AndMissingRaceCoordinateIsLegacyLimited()
    {
        var session = new NewsSession
        {
            Timeline = Timeline(Round(1, "Monaco")),
            Dispatches =
            [
                new SmgpDispatch
                {
                    WhenLabel = "Season 1 · Round 2",
                    Kind = SmgpDispatchKind.RivalWatch,
                    Headline = "The rival answers",
                    Body = "A response arrived.",
                    SortSeason = 1,
                    SortRound = 2,
                    SortSeq = 4,
                },
            ],
        };

        var vm = new NewsViewModel(session);

        Assert.Empty(vm.Items);
        Assert.Single(vm.Stories);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.IsLegacyLimited);
        Assert.False(vm.Stories[0].HasHistoryLink);
    }

    [Fact]
    public void CategoryAndSearchFilters_DriveLeadSecondaryAndFilteredEmptyState()
    {
        var session = new NewsSession
        {
            Timeline = Timeline(Round(1, "Monaco"), Round(2, "Phoenix")),
            Dispatches =
            [
                Dispatch(SmgpDispatchKind.RaceResult, 2, 2, "Race result", "Player scores"),
                Dispatch(SmgpDispatchKind.TitleRace, 1, 1, "Title picture", "Leader changes"),
                Dispatch(SmgpDispatchKind.RaceResult, 2, 3, "Race result 2", "Player scores"),
                Dispatch(SmgpDispatchKind.RaceResult, 2, 4, "Race result 3", "Player scores"),
                Dispatch(SmgpDispatchKind.RaceResult, 2, 5, "Race result 4", "Player scores"),
                Dispatch(SmgpDispatchKind.RaceResult, 2, 6, "Race result 5", "Player scores"),
                Dispatch(SmgpDispatchKind.RaceResult, 2, 7, "Race result 6", "Player scores"),
            ],
        };
        var vm = new NewsViewModel(session);
        var championship = Assert.Single(vm.AvailableCategories,
            category => category.Category == NewsStoryCategory.Championship);

        vm.SelectedCategory = championship;

        var filtered = Assert.Single(vm.FilteredStories);
        Assert.Equal("Title picture", filtered.Headline);
        Assert.Same(filtered, vm.LeadStory);
        Assert.Empty(vm.SecondaryStories);
        Assert.True(vm.HasActiveFilter);
        Assert.True(vm.ClearFiltersCommand.CanExecute(null));

        vm.SearchText = "not present";

        Assert.Empty(vm.FilteredStories);
        Assert.True(vm.IsFilteredEmpty);
        Assert.Null(vm.LeadStory);

        vm.ClearFiltersCommand.Execute(null);

        Assert.False(vm.HasActiveFilter);
        Assert.False(vm.IsFilteredEmpty);
        Assert.Equal(7, vm.FilteredStories.Count);
        Assert.Equal(5, vm.SecondaryStories.Count);
        Assert.False(vm.ClearFiltersCommand.CanExecute(null));
    }

    [Fact]
    public void ReaderCommands_AreDisabledForNullUnreadableAndAlreadyOpenTargets()
    {
        var session = new NewsSession
        {
            Timeline = Timeline(Round(1, "Monaco")),
            Dispatches =
            [
                Dispatch(SmgpDispatchKind.RaceResult, 1, 1, "Readable", "Body"),
                Dispatch(SmgpDispatchKind.RaceResult, 1, 2, "Headline only", ""),
            ],
        };
        var vm = new NewsViewModel(session);
        var readable = Assert.Single(vm.Stories, story => story.Headline == "Readable");
        var unreadable = Assert.Single(vm.Stories, story => story.Headline == "Headline only");

        Assert.False(vm.OpenArticleCommand.CanExecute(null));
        Assert.False(vm.OpenArticleCommand.CanExecute(unreadable));
        Assert.True(vm.OpenArticleCommand.CanExecute(readable));
        Assert.False(vm.CloseArticleCommand.CanExecute(null));
        Assert.False(vm.OpenStoryCommand.CanExecute(null));
        Assert.False(vm.OpenStoryCommand.CanExecute("missing"));
        Assert.True(vm.OpenStoryCommand.CanExecute(readable.Key));

        vm.OpenStoryCommand.Execute(readable.Key);

        Assert.True(vm.IsReaderOpen);
        Assert.Same(readable, vm.SelectedArticle);
        Assert.False(vm.OpenArticleCommand.CanExecute(readable));
        Assert.False(vm.OpenStoryCommand.CanExecute(readable.Key));
        Assert.True(vm.CloseArticleCommand.CanExecute(null));

        vm.CloseArticleCommand.Execute(null);

        Assert.False(vm.IsReaderOpen);
        Assert.Null(vm.SelectedArticle);
        Assert.False(vm.CloseArticleCommand.CanExecute(null));
        Assert.True(vm.OpenArticleCommand.CanExecute(readable));
        Assert.True(vm.OpenStoryCommand.CanExecute(readable.Key));
    }

    [Theory]
    [InlineData(NewsDetailLevel.HeadlinesOnly)]
    [InlineData(NewsDetailLevel.Minimal)]
    public void HeadlineModes_PreserveFactsButDisableArticleBodies(NewsDetailLevel detail)
    {
        var session = new NewsSession
        {
            Timeline = Timeline(Round(1, "Monaco")),
            Dispatches = [Dispatch(SmgpDispatchKind.RaceResult, 1, 1, "Headline", "Hidden body")],
        };

        var story = Assert.Single(new NewsViewModel(session, detail).Stories);

        Assert.Equal("Headline", story.Headline);
        Assert.Equal("", story.Body);
        Assert.False(story.HasBody);
        Assert.False(story.CanRead);
    }

    private static SmgpDispatch Dispatch(
        SmgpDispatchKind kind,
        int round,
        int sequence,
        string headline,
        string body) => new()
    {
        WhenLabel = $"Season 1 · Round {round}",
        Kind = kind,
        Headline = headline,
        Body = body,
        SortSeason = 1,
        SortRound = round,
        SortSeq = sequence,
    };

    private static CareerTimeline Timeline(params CareerSeasonRoundLine[] rounds) => new()
    {
        Seasons =
        [
            new CareerSeasonCard
            {
                SeasonYear = 1990,
                RoundsApplied = rounds.Length,
                RoundCount = 16,
                RoundLines = rounds,
            },
        ],
    };

    private static CareerSeasonRoundLine Round(int round, string venue) => new()
    {
        Round = round,
        Venue = venue,
    };

    private static SmgpPaddockModel Paddock() => new()
    {
        Drivers =
        [
            new SmgpDriverCard
            {
                DriverId = "driver.rival",
                Name = "Rival Driver",
                TeamId = "team.rival",
                TeamName = "Rival Team",
                Number = "7",
                PortraitKey = "portraits/driver.rival.jpg",
                CarKey = "cars/driver.rival.png",
                Epithet = "",
                Bio = [],
                Quotes = [],
                IsPlayer = false,
                Career = null,
                Season = null,
                Prestige = 4,
            },
        ],
        Teams =
        [
            new SmgpTeamCard
            {
                TeamId = "team.rival",
                Name = "Rival Team",
                Motto = "",
                LogoKey = "",
                History = [],
                Quotes = [],
                DriverNames = ["Rival Driver"],
                Prestige = 4,
            },
        ],
    };

    private sealed class NewsSession : ICareerSession
    {
        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerTimeline Timeline { get; init; } = Companion.ViewModels.Services.CareerTimeline.Empty;
        public SmgpPaddockModel? Paddock { get; init; }
        public IReadOnlyList<SmgpDispatch> Dispatches { get; init; } = [];
        public IReadOnlyList<NewsDispatch> Feed { get; init; } = [];

        public CareerSummary Summary { get; } = new()
        {
            CareerName = "News Contract",
            SeasonYear = 1990,
            SeriesName = "SMGP",
            CurrentRound = 1,
            RoundCount = 16,
            PlayerDriverId = "player",
            PlayerLiveryName = "Player",
        };

        public CareerTimeline CareerTimeline() => Timeline;
        public SmgpPaddockModel? SmgpPaddock() => Paddock;
        public IReadOnlyList<SmgpDispatch> SmgpDispatches() => Dispatches;
        public IReadOnlyList<NewsDispatch> ReadFeed() => Feed;
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

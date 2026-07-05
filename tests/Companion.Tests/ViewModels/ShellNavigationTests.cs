using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.Hub;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;
using Companion.ViewModels.Standings;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// ShellViewModel + HomeViewModel conductor logic (app-shell contract): Start → wizard /
/// continue → Home, the Home two-state Briefing ⇄ Enter-result flow, the Confirm → Apply
/// round-trip, season-complete pinning to the review, and session disposal on navigation.
/// Uses local fakes so this file has no coupling to the shared session-test fixtures.
/// </summary>
public sealed class ShellNavigationTests
{
    // ---------- shell ----------

    [Fact]
    public void Shell_starts_on_the_start_screen()
    {
        var shell = CreateShell(out _, out _);
        Assert.Same(shell.Start, shell.Current);
    }

    [Fact]
    public void New_career_request_navigates_to_the_wizard()
    {
        var shell = CreateShell(out _, out _);
        shell.Start.NewCareerCommand.Execute(null);

        Assert.IsType<NewCareerWizardViewModel>(shell.Current);
        Assert.Same(shell.Wizard, shell.Current);
    }

    [Fact]
    public void Continue_opens_the_career_lands_home_and_touches_the_mru()
    {
        var shell = CreateShell(out var factory, out var store);
        var entry = store.Seed("Z:\\careers\\one.ams2career", "Career One");
        shell.Start.Refresh();

        shell.Start.ContinueCommand.Execute(entry);

        Assert.Equal("Z:\\careers\\one.ams2career", factory.LastOpenedPath);
        var hub = Assert.IsType<HubViewModel>(shell.Current);
        Assert.IsType<HomeViewModel>(hub.Home); // the loop is re-homed verbatim as the Race tab
        Assert.Contains(store.Touched, t => t.Path == "Z:\\careers\\one.ams2career");
        Assert.Null(shell.StatusError);
    }

    [Fact]
    public void Open_failure_reports_and_stays_on_start()
    {
        var shell = CreateShell(out var factory, out var store);
        factory.OpenThrows = new IOException("locked");
        var entry = store.Seed("Z:\\careers\\bad.ams2career", "Bad");
        shell.Start.Refresh();

        shell.Start.ContinueCommand.Execute(entry);

        Assert.Same(shell.Start, shell.Current);
        Assert.Contains("locked", shell.StatusError);
    }

    [Fact]
    public void Go_to_start_disposes_the_open_session()
    {
        var shell = CreateShell(out var factory, out var store);
        var entry = store.Seed("Z:\\careers\\one.ams2career", "Career One");
        shell.Start.Refresh();
        shell.Start.ContinueCommand.Execute(entry);

        shell.GoToStartCommand.Execute(null);

        Assert.Same(shell.Start, shell.Current);
        Assert.True(factory.Session.Disposed);
    }

    // ---------- home two-state ----------

    [Fact]
    public void Home_starts_on_the_briefing_state()
    {
        using var home = new HomeViewModel(new FakeSession());
        Assert.True(home.IsBriefingState);
        Assert.Same(home.Briefing, home.CurrentContent);
        Assert.IsType<BriefingViewModel>(home.CurrentContent);
        Assert.False(home.IsSeasonReview);
        Assert.Equal("Round 1 of 2", home.RoundText);
    }

    [Fact]
    public void Enter_result_switches_state_and_keeps_partial_entry_across_the_toggle()
    {
        using var home = new HomeViewModel(new FakeSession());

        home.EnterResultCommand.Execute(null);
        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        entry.Input = "1";
        entry.SubmitCommand.Execute(null);

        home.ShowBriefingCommand.Execute(null);
        Assert.True(home.IsBriefingState);

        home.EnterResultCommand.Execute(null);
        Assert.Same(entry, home.CurrentContent); // the half-typed result survives the toggle
        Assert.Equal(1, entry.ResolvedCount);
    }

    [Fact]
    public void Confirm_is_gated_on_a_complete_draft()
    {
        using var home = new HomeViewModel(new FakeSession());
        home.EnterResultCommand.Execute(null);
        var entry = (ResultEntryViewModel)home.CurrentContent!;

        Assert.False(home.ConfirmResultCommand.CanExecute(null));

        CompleteRound(entry);

        Assert.True(home.ConfirmResultCommand.CanExecute(null));
    }

    [Fact]
    public void Confirm_then_apply_persists_advances_and_returns_to_the_briefing()
    {
        var session = new FakeSession();
        using var home = new HomeViewModel(session);
        home.EnterResultCommand.Execute(null);
        CompleteRound((ResultEntryViewModel)home.CurrentContent!);

        home.ConfirmResultCommand.Execute(null);
        var confirm = Assert.IsType<ConfirmViewModel>(home.CurrentContent);

        confirm.ApplyCommand.Execute(null);

        Assert.Equal(1, session.AppliedRounds);
        Assert.True(home.IsBriefingState);
        Assert.Equal("Round 2 of 2", home.RoundText);
    }

    [Fact]
    public void Confirm_back_returns_to_the_same_result_entry()
    {
        using var home = new HomeViewModel(new FakeSession());
        home.EnterResultCommand.Execute(null);
        var entry = (ResultEntryViewModel)home.CurrentContent!;
        CompleteRound(entry);

        home.ConfirmResultCommand.Execute(null);
        var confirm = (ConfirmViewModel)home.CurrentContent!;
        confirm.BackCommand.Execute(null);

        Assert.Same(entry, home.CurrentContent);
    }

    [Fact]
    public void Final_round_apply_pins_home_to_the_season_review()
    {
        var session = new FakeSession();
        using var home = new HomeViewModel(session);

        for (int round = 0; round < 2; round++)
        {
            home.EnterResultCommand.Execute(null);
            CompleteRound((ResultEntryViewModel)home.CurrentContent!);
            home.ConfirmResultCommand.Execute(null);
            ((ConfirmViewModel)home.CurrentContent!).ApplyCommand.Execute(null);
        }

        Assert.True(home.IsSeasonReview);
        var review = Assert.IsType<SeasonReviewViewModel>(home.CurrentContent);
        Assert.True(home.IsSeasonReviewState);
        Assert.Equal("Season complete", home.RoundText);
        Assert.False(home.EnterResultCommand.CanExecute(null));
        Assert.False(home.ShowBriefingCommand.CanExecute(null));

        // The review carries the session's offers; accepting one goes through the seam.
        var offer = Assert.Single(review.Offers);
        review.AcceptOfferCommand.Execute(offer);
        Assert.Equal(new[] { "team.brabham" }, session.AcceptedOffers);
        Assert.True(offer.IsAccepted);
        Assert.True(review.OfferAccepted);
    }

    [Fact]
    public void Entering_a_result_prefills_the_slider_with_the_recommendation()
    {
        var session = new FakeSession();
        using var home = new HomeViewModel(session);

        // Round 1: no recommendation yet — the neutral default.
        home.EnterResultCommand.Execute(null);
        var entry = (ResultEntryViewModel)home.CurrentContent!;
        Assert.Equal(ResultEntryViewModel.NeutralSlider, entry.SliderUsed);

        CompleteRound(entry);
        home.ConfirmResultCommand.Execute(null);
        ((ConfirmViewModel)home.CurrentContent!).ApplyCommand.Execute(null);

        // Round 2: the fake session now recommends 97 — the prompt is prefilled with it.
        home.EnterResultCommand.Execute(null);
        var second = (ResultEntryViewModel)home.CurrentContent!;
        Assert.Equal(97.0, second.SliderUsed);
    }

    [Fact]
    public void Standings_round_trip_returns_to_the_round_in_progress()
    {
        using var home = new HomeViewModel(new FakeSession());
        home.EnterResultCommand.Execute(null);
        var entry = home.CurrentContent;

        home.ShowStandingsCommand.Execute(null);
        Assert.True(home.IsStandingsState);

        home.BackToRoundCommand.Execute(null);
        Assert.Same(entry, home.CurrentContent); // back to the in-progress entry, not the briefing
    }

    [Fact]
    public void Sign_and_continue_reopens_the_career_into_a_fresh_home()
    {
        var shell = CreateShell(out var factory, out var store);
        factory.Session.AppliedRounds = 2; // season complete → home pins to the review
        factory.Session.Next = new NextSeasonInfo
        {
            PackDirectory = @"Z:\packs\f1-1969",
            PackId = "f1-1969",
            PackName = "Formula One 1969",
            SeasonYear = 1969,
            BridgedYears = [1968],
        };
        var entry = store.Seed("Z:\\careers\\one.ams2career", "Career One");
        shell.Start.Refresh();
        shell.Start.ContinueCommand.Execute(entry);

        var hub = Assert.IsType<HubViewModel>(shell.Current);
        var review = Assert.IsType<SeasonReviewViewModel>(hub.Home.CurrentContent);
        Assert.True(review.HasNextSeason);
        Assert.Equal("Sign & start 1969", review.SignButtonText);

        review.AcceptOfferCommand.Execute(review.Offers[0]);
        review.SignAndContinueCommand.Execute(null);

        // The transition went through the seam, the stale session was disposed, and the
        // career was REOPENED — a fresh Hub (and Home) over the reopened (latest-season) session.
        Assert.Equal(new[] { "team.brabham" }, factory.Session.SignedTeams);
        Assert.True(factory.Session.Disposed);
        Assert.Equal(2, factory.OpenCount);
        var reopened = Assert.IsType<HubViewModel>(shell.Current);
        Assert.NotSame(hub, reopened);
        Assert.Null(shell.StatusError);
    }

    [Fact]
    public void Disposing_home_disposes_session_and_watcher()
    {
        var session = new FakeSession();
        var watcher = new DisposableWatcher();
        var home = new HomeViewModel(session, watcher);

        home.Dispose();

        Assert.True(session.Disposed);
        Assert.True(watcher.Disposed);
    }

    // ---------- hub shell (Increment 1: the rail around the re-homed loop) ----------

    [Fact]
    public void Hub_opens_on_the_race_tab_with_the_loop_rehomed()
    {
        using var hub = new HubViewModel(new FakeSession());

        Assert.Equal(HubViewModel.RaceTabKey, hub.SelectedTab?.Key);
        Assert.Same(hub.Home, hub.SelectedTab?.Content);
        Assert.True(hub.Home.IsBriefingState); // the loop is live and untouched
        Assert.Collection(
            hub.Tabs,
            t => Assert.Equal(HubViewModel.RaceTabKey, t.Key),
            t => Assert.Equal(HubViewModel.StandingsTabKey, t.Key),
            t => Assert.Equal(HubViewModel.NewsTabKey, t.Key));
    }

    [Fact]
    public void Selecting_a_tab_marks_it_selected_and_clears_the_others()
    {
        using var hub = new HubViewModel(new FakeSession());
        var standings = hub.Tabs.Single(t => t.Key == HubViewModel.StandingsTabKey);

        hub.SelectTabCommand.Execute(standings);

        Assert.Same(standings, hub.SelectedTab);
        Assert.True(standings.IsSelected);
        Assert.All(
            hub.Tabs.Where(t => !ReferenceEquals(t, standings)),
            t => Assert.False(t.IsSelected));
    }

    [Fact]
    public void Number_key_selects_the_nth_tab_and_ignores_out_of_range()
    {
        using var hub = new HubViewModel(new FakeSession());

        Assert.True(hub.SelectTabByNumber(3));
        Assert.Equal(HubViewModel.NewsTabKey, hub.SelectedTab?.Key);

        Assert.False(hub.SelectTabByNumber(9)); // out of range → falls through, selection unchanged
        Assert.Equal(HubViewModel.NewsTabKey, hub.SelectedTab?.Key);
    }

    [Fact]
    public void Applying_a_round_snaps_back_to_race_and_reprojects_the_standings_lens()
    {
        using var hub = new HubViewModel(new FakeSession());
        var standingsTab = hub.Tabs.Single(t => t.Key == HubViewModel.StandingsTabKey);
        var before = standingsTab.Content;

        hub.SelectTabByNumber(2); // wander off to the Standings lens
        Assert.Equal(HubViewModel.StandingsTabKey, hub.SelectedTab?.Key);

        hub.Home.EnterResultCommand.Execute(null);
        CompleteRound((ResultEntryViewModel)hub.Home.CurrentContent!);
        hub.Home.ConfirmResultCommand.Execute(null);
        ((ConfirmViewModel)hub.Home.CurrentContent!).ApplyCommand.Execute(null);

        // anti-burial: after Apply we snap back to Race, and the lens is re-projected off new state
        Assert.Equal(HubViewModel.RaceTabKey, hub.SelectedTab?.Key);
        Assert.NotSame(before, standingsTab.Content);
    }

    [Fact]
    public void Escape_from_a_lens_tab_returns_to_the_race_tab()
    {
        using var hub = new HubViewModel(new FakeSession());
        hub.SelectTabByNumber(2); // Standings

        Assert.True(hub.TryEscapeBack());
        Assert.Equal(HubViewModel.RaceTabKey, hub.SelectedTab?.Key);
    }

    [Fact]
    public void Disposing_the_hub_disposes_the_session()
    {
        var session = new FakeSession();
        var hub = new HubViewModel(session);

        hub.Dispose();

        Assert.True(session.Disposed);
    }

    // ---------- helpers / fakes ----------

    private static ShellViewModel CreateShell(out FakeFactory factory, out FakeStore store)
    {
        factory = new FakeFactory();
        store = new FakeStore();
        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = static () => null,
            DocumentsDirectory = Path.GetTempPath(),
        };
        return new ShellViewModel(environment, factory, store);
    }

    /// <summary>Classifies both grid cars (#1 then #2) so the draft is complete.</summary>
    private static void CompleteRound(ResultEntryViewModel entry)
    {
        foreach (string number in new[] { "1", "2" })
        {
            entry.Input = number;
            entry.SubmitCommand.Execute(null);
        }
        Assert.True(entry.IsComplete);
    }

    private sealed class FakeSession : ICareerSession, IDisposable
    {
        public int AppliedRounds;
        public bool Disposed;

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerSummary Summary => new()
        {
            CareerName = "Fake Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = Math.Min(AppliedRounds + 1, Pack.Season.Rounds.Count),
            RoundCount = Pack.Season.Rounds.Count,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            SeasonComplete = AppliedRounds >= Pack.Season.Rounds.Count,
        };

        public BriefingModel? CurrentBriefing() => Summary.SeasonComplete
            ? null
            : new BriefingModel
            {
                Round = Pack.Season.Rounds[Math.Min(AppliedRounds, Pack.Season.Rounds.Count - 1)],
                VenueDisplayName = "Kyalami",
                IsPlaceholder = false,
                Settings = [new CopyableSetting("Track", "Kyalami Historic")],
            };

        public StageOutcome StageCurrentGrid() =>
            new() { Success = true, WrittenPath = "X.xml", Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() =>
        [
            Seat("driver.brabham", "Jack Brabham", "1"),
            Seat("driver.hulme", "Denny Hulme", "2"),
        ];

        public ConfirmModel Preview(ResultDraft draft) => new()
        {
            RoundPoints = [],
            Movements = [],
            Headline = "fake headline",
        };

        public void Apply(ResultDraft draft) => AppliedRounds++;

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => AppliedRounds > 0 ? 97 : null;

        public SeasonReviewModel? SeasonReview() => !Summary.SeasonComplete
            ? null
            : new SeasonReviewModel
            {
                SeasonYear = 1967,
                PlayerPosition = 2,
                FinalReputation = 41.5,
                FinalOpi = 0.4,
                Headlines = ["A fake season to remember"],
                Offers =
                [
                    new SeasonOfferModel
                    {
                        TeamId = "team.brabham",
                        TeamName = "Brabham-Repco",
                        Tier = 5,
                        SalaryBu = 6.5,
                        Score = 1.23,
                        Accepted = false,
                    },
                ],
            };

        public List<string> AcceptedOffers { get; } = [];

        public void AcceptOffer(string teamId) => AcceptedOffers.Add(teamId);

        public NextSeasonInfo? Next { get; set; }

        public NextSeasonInfo? NextSeason() => Next;

        public List<string> SignedTeams { get; } = [];

        public void StartNextSeason(string teamId) => SignedTeams.Add(teamId);

        public void Dispose() => Disposed = true;

        private static GridSeat Seat(string id, string name, string number) => new()
        {
            DriverId = id,
            DriverName = name,
            Number = number,
            TeamId = "team.brabham",
            TeamName = "Brabham-Repco",
            Ams2LiveryName = $"Livery #{number}",
            Ratings = TestPackBuilder.Driver(id).Ratings,
            Reliability = 0.9,
            WeightScalar = 1.0,
            PowerScalar = 1.0,
            DragScalar = 1.0,
            IsPlayer = id == "driver.hulme",
        };
    }

    private sealed class FakeFactory : ICareerFactory
    {
        public FakeSession Session { get; } = new();

        public string? LastOpenedPath { get; private set; }

        public int OpenCount { get; private set; }

        public Exception? OpenThrows { get; set; }

        public ICareerSession Create(CareerCreationRequest request) => Session;

        public ICareerSession Open(string careerFilePath)
        {
            if (OpenThrows is not null)
                throw OpenThrows;
            LastOpenedPath = careerFilePath;
            OpenCount++;
            return Session;
        }
    }

    private sealed class FakeStore : IRecentCareersStore
    {
        private readonly List<RecentCareer> _entries = [];

        public List<(string Path, string Name)> Touched { get; } = [];

        public RecentCareer Seed(string path, string name)
        {
            var entry = new RecentCareer
            {
                Path = path,
                CareerName = name,
                LastOpenedUtc = DateTimeOffset.UnixEpoch,
            };
            _entries.Add(entry);
            return entry;
        }

        public IReadOnlyList<RecentCareer> Load() => _entries.ToList();

        public void Touch(string path, string careerName) => Touched.Add((path, careerName));

        public void Remove(string path) => _entries.RemoveAll(e => e.Path == path);
    }

    private sealed class DisposableWatcher : IFileWatcher, IDisposable
    {
        public bool Disposed;

        public event EventHandler<string>? Changed;

        public void Watch(string filePath) => _ = Changed; // silence unused-event warning

        public void Stop()
        {
        }

        public void Dispose() => Disposed = true;
    }
}

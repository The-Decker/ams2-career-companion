using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Increment 2b.3 — the race-weekend result-entry flow in <see cref="HomeViewModel"/>. When a
/// round's weekend declares a qualifying session, the loop gains a qualifying-order step BEFORE the
/// race (reusing the result-entry grammar); the captured order rides on the race draft's
/// <see cref="ResultDraft.QualifyingOrder"/> and seeds the race grid pole-first. A round with no
/// weekend (every bundled pack) skips the step entirely, so the shipped single-race loop is
/// byte-identical — asserted here too.
/// </summary>
public sealed class WeekendLoopTests
{
    // ---------- the qualifying step ----------

    [Fact]
    public void Weekend_with_qualifying_opens_the_cinematic_gate_before_the_qualifying_editor()
    {
        using var home = new HomeViewModel(new WeekendSession());

        home.EnterResultCommand.Execute(null);

        var intro = AssertIntro(home, SessionIntroKind.Qualifying);
        Assert.Equal("RACE WEEKEND", intro.Eyebrow);
        Assert.Equal("QUALIFYING", intro.Title);
        Assert.Equal("qualifying", intro.ArtworkKey);
        Assert.Equal("Begin qualifying", intro.ActionLabel);
        Assert.Contains("Interlagos", intro.Subtitle);

        intro.ContinueCommand.Execute(null);
        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        Assert.True(home.IsResultEntryState);
        Assert.True(home.IsQualifyingStep);
        Assert.Equal("Qualifying", entry.SessionLabel);
        Assert.Equal("Set the grid  ⏎", home.ConfirmButtonText);
    }

    [Fact]
    public void Single_race_round_skips_qualifying_but_opens_the_race_cinematic_gate()
    {
        using var home = new HomeViewModel(new WeekendSession { Weekend = null });

        home.EnterResultCommand.Execute(null);

        var intro = AssertIntro(home, SessionIntroKind.Race);
        Assert.Equal("RACE DAY", intro.Eyebrow);
        Assert.Equal("RACE", intro.Title);
        Assert.Equal("race", intro.ArtworkKey);
        Assert.Equal("Start the race", intro.ActionLabel);
        intro.ContinueCommand.Execute(null);

        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        Assert.False(home.IsQualifyingStep);
        Assert.Null(entry.SessionLabel); // the byte-identical single-race screen
        Assert.Equal("Confirm result  ⏎", home.ConfirmButtonText);
    }

    [Fact]
    public void Setting_the_grid_advances_to_the_race_ordered_pole_first()
    {
        using var home = new HomeViewModel(new WeekendSession());

        var qualifying = OpenQualifying(home);
        // Qualify car #2 (Hulme) on pole, then car #1 (Brabham).
        Order(qualifying, "2", "1");
        Assert.True(home.ConfirmResultCommand.CanExecute(null)); // "Set the grid" is enabled

        home.ConfirmResultCommand.Execute(null); // set the grid → the starting-grid look

        // The starting grid shows the qualifying result pole-first BEFORE the race.
        var startingGrid = Assert.IsType<StartingGridViewModel>(home.CurrentContent);
        Assert.True(home.IsStartingGridState);
        Assert.Equal("Start the race  ⏎", home.ConfirmButtonText);
        Assert.Equal("driver.hulme", startingGrid.Slots[0].DriverId);   // pole-sitter leads the grid
        Assert.Equal("driver.brabham", startingGrid.Slots[1].DriverId);

        home.ConfirmResultCommand.Execute(null); // start the race → race entry

        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        var race = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        Assert.NotSame(qualifying, race);
        Assert.False(home.IsQualifyingStep);
        Assert.False(home.IsStartingGridState);
        Assert.Null(race.SessionLabel);
        Assert.Equal("Confirm result  ⏎", home.ConfirmButtonText);
        // The race grid is seeded from the qualifying order: pole-sitter Hulme leads Remaining.
        Assert.Equal("driver.hulme", race.Remaining[0].DriverId);
        Assert.Equal("driver.brabham", race.Remaining[1].DriverId);
    }

    [Fact]
    public void Starting_grid_wires_fixed_livery_car_art_from_the_session()
    {
        var session = new WeekendSession
        {
            GridCarArtKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Livery #1"] = "driver.authored_car_one",
                ["Livery #2"] = "driver.authored_car_two",
            },
        };
        using var home = new HomeViewModel(session);

        Order(OpenQualifying(home), "2", "1");
        home.ConfirmResultCommand.Execute(null);

        var grid = Assert.IsType<StartingGridViewModel>(home.CurrentContent);
        Assert.Equal("driver.authored_car_two", grid.Slots[0].CarKey);
        Assert.Equal("driver.authored_car_one", grid.Slots[1].CarKey);
    }

    [Fact]
    public void Applying_the_race_writes_the_captured_qualifying_order_into_the_draft()
    {
        var session = new WeekendSession();
        using var home = new HomeViewModel(session);

        Order(OpenQualifying(home), "2", "1"); // pole Hulme, then Brabham
        home.ConfirmResultCommand.Execute(null);                     // set the grid → starting grid
        home.ConfirmResultCommand.Execute(null);                     // start the race → race entry

        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        Order((ResultEntryViewModel)home.CurrentContent!, "2", "1"); // race result
        home.ConfirmResultCommand.Execute(null);                     // → confirm interstitial
        ((ConfirmViewModel)home.CurrentContent!).ApplyCommand.Execute(null);

        var draft = Assert.Single(session.Applied);
        Assert.Equal(new[] { "driver.hulme", "driver.brabham" }, draft.QualifyingOrder);
    }

    [Fact]
    public void Single_race_apply_carries_no_qualifying_order()
    {
        var session = new WeekendSession { Weekend = null };
        using var home = new HomeViewModel(session);

        home.EnterResultCommand.Execute(null);
        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        Order((ResultEntryViewModel)home.CurrentContent!, "1", "2");
        home.ConfirmResultCommand.Execute(null);
        ((ConfirmViewModel)home.CurrentContent!).ApplyCommand.Execute(null);

        var draft = Assert.Single(session.Applied);
        Assert.Null(draft.QualifyingOrder); // byte-identical to the shipped single-race draft
    }

    [Fact]
    public void The_qualifying_order_is_cleared_after_apply_so_the_next_round_re_qualifies()
    {
        var session = new WeekendSession();
        using var home = new HomeViewModel(session);

        // Round 1: qualify + race + apply.
        Order(OpenQualifying(home), "1", "2");
        home.ConfirmResultCommand.Execute(null); // set the grid → starting grid
        home.ConfirmResultCommand.Execute(null); // start the race → race entry
        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        Order((ResultEntryViewModel)home.CurrentContent!, "1", "2");
        home.ConfirmResultCommand.Execute(null);
        ((ConfirmViewModel)home.CurrentContent!).ApplyCommand.Execute(null);

        // Round 2 must open the qualifying step again (the captured order was consumed).
        home.EnterResultCommand.Execute(null);
        AssertIntro(home, SessionIntroKind.Qualifying).ContinueCommand.Execute(null);
        Assert.True(home.IsQualifyingStep);
        Assert.Equal("Qualifying", ((ResultEntryViewModel)home.CurrentContent!).SessionLabel);
    }

    // ---------- SMGP rival step (Upcoming Race loop) ----------

    [Fact]
    public void Smgp_career_shows_the_rival_step_before_qualifying_then_continues()
    {
        var session = new WeekendSession
        {
            SmgpBriefing = new Companion.ViewModels.Services.SmgpBriefingModel
            {
                RoundHeader = "SAN MARINO · ROUND 1",
                SeasonLine = "SEASON  P1 · 9 PTS",
                CareerLine = "",
                AdviceLine = "PASS AT THE HAIRPIN!",
                Titles = 0,
                SeasonOrdinal = 1,
                SeasonsTotal = Companion.Core.Smgp.SmgpRules.CampaignSeasons,
                CareerOver = false,
                Rivals =
                [
                    new Companion.ViewModels.Services.SmgpRivalOption
                    {
                        DriverId = "driver.brabham", DriverName = "Jack Brabham",
                        TeamId = "team.bullets", TeamName = "Bullets",
                        MachineLine = "g3m1", Quote = "IT'S INTERESTING.",
                        OfferOnWin = false, ForfeitOnLoss = false,
                    },
                ],
            },
        };
        using var home = new HomeViewModel(session);

        // The rival screen is its OWN step, shown before qualifying.
        home.EnterResultCommand.Execute(null);
        Assert.True(home.IsRivalStep);
        Assert.False(home.IsQualifyingStep);
        Assert.Equal("Continue  ⏎", home.ConfirmButtonText);

        // Continue advances to the qualifying cinematic (the rival step is done for this round).
        home.ConfirmResultCommand.Execute(null);
        Assert.False(home.IsRivalStep);
        AssertIntro(home, SessionIntroKind.Qualifying).ContinueCommand.Execute(null);
        Assert.True(home.IsQualifyingStep);
    }

    [Fact]
    public void Non_smgp_career_has_no_rival_step()
    {
        using var home = new HomeViewModel(new WeekendSession()); // no SmgpBriefing
        home.EnterResultCommand.Execute(null);
        Assert.False(home.IsRivalStep);
        AssertIntro(home, SessionIntroKind.Qualifying).ContinueCommand.Execute(null);
        Assert.True(home.IsQualifyingStep);
    }

    // ---------- two-race weekend (Increment 2e.3) ----------

    [Fact]
    public void Two_race_weekend_captures_each_race_then_applies_a_draft_with_additional_races()
    {
        var session = new WeekendSession
        {
            Weekend = new PackWeekend
            {
                Qualifying = new PackWeekendSession { Label = "Qualifying" },
                Races =
                [
                    new PackWeekendRace { Id = "race", Label = "Feature" },
                    new PackWeekendRace { Id = "race2", Label = "Sprint" },
                ],
            },
        };
        using var home = new HomeViewModel(session);

        // Qualifying → set the grid (pole: car #2 Hulme, then #1 Brabham).
        home.EnterResultCommand.Execute(null);
        AssertIntro(home, SessionIntroKind.Qualifying).ContinueCommand.Execute(null);
        Assert.True(home.IsQualifyingStep);
        Order((ResultEntryViewModel)home.CurrentContent!, "2", "1");
        home.ConfirmResultCommand.Execute(null); // set the grid → starting grid

        // The starting grid names the first race (Feature) it precedes.
        var grid = Assert.IsType<StartingGridViewModel>(home.CurrentContent);
        Assert.Contains("Feature", grid.Title);
        home.ConfirmResultCommand.Execute(null); // start the race → race entry

        // Race 1 (Feature) — NOT the round's last, so the primary action advances to the next race.
        var featureIntro = AssertIntro(home, SessionIntroKind.Race);
        Assert.Contains("Feature", featureIntro.Subtitle);
        featureIntro.ContinueCommand.Execute(null);
        var feature = (ResultEntryViewModel)home.CurrentContent!;
        Assert.Equal("Feature", feature.SessionLabel);
        Assert.False(home.IsQualifyingStep);
        Assert.Equal("Next race  ⏎", home.ConfirmButtonText);
        Order(feature, "2", "1"); // Hulme P1, Brabham P2
        home.ConfirmResultCommand.Execute(null);

        // Race 2 (Sprint) — the last race, so the primary action scores the round.
        var sprintIntro = AssertIntro(home, SessionIntroKind.Race);
        Assert.Contains("Sprint", sprintIntro.Subtitle);
        sprintIntro.ContinueCommand.Execute(null);
        var sprint = (ResultEntryViewModel)home.CurrentContent!;
        Assert.NotSame(feature, sprint);
        Assert.Equal("Sprint", sprint.SessionLabel);
        Assert.Equal("Confirm result  ⏎", home.ConfirmButtonText);
        Order(sprint, "1", "2"); // Brabham P1, Hulme P2
        home.ConfirmResultCommand.Execute(null);

        // Confirm → apply the whole two-race round.
        var confirm = Assert.IsType<ConfirmViewModel>(home.CurrentContent);
        confirm.ApplyCommand.Execute(null);

        var draft = Assert.Single(session.Applied);
        Assert.Equal(new[] { "driver.hulme", "driver.brabham" }, draft.Classified);       // race 1 (primary)
        Assert.Equal(new[] { "driver.hulme", "driver.brabham" }, draft.QualifyingOrder);
        var extra = Assert.Single(draft.AdditionalRaces!);
        Assert.Equal(new[] { "driver.brabham", "driver.hulme" }, extra.Classified);        // race 2
    }

    [Fact]
    public void Two_race_weekend_confirm_back_returns_to_the_last_race_entry()
    {
        var session = new WeekendSession
        {
            Weekend = new PackWeekend
            {
                Races =
                [
                    new PackWeekendRace { Id = "race", Label = "Feature" },
                    new PackWeekendRace { Id = "race2", Label = "Sprint" },
                ],
            },
        };
        using var home = new HomeViewModel(session);

        home.EnterResultCommand.Execute(null); // no qualifying → straight to race 1
        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        Order((ResultEntryViewModel)home.CurrentContent!, "1", "2");
        home.ConfirmResultCommand.Execute(null); // → race 2

        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        var sprint = (ResultEntryViewModel)home.CurrentContent!;
        Order(sprint, "1", "2");
        home.ConfirmResultCommand.Execute(null); // → confirm

        var confirm = Assert.IsType<ConfirmViewModel>(home.CurrentContent);
        confirm.BackCommand.Execute(null);

        Assert.Same(sprint, home.CurrentContent); // back re-opens the last race, its result intact
        Assert.Empty(session.Applied);
    }

    // ---------- navigation around the step ----------

    [Fact]
    public void A_partial_qualifying_order_survives_a_toggle_to_the_briefing()
    {
        using var home = new HomeViewModel(new WeekendSession());

        var qualifying = OpenQualifying(home);
        qualifying.Input = "2";
        qualifying.SubmitCommand.Execute(null);

        home.ShowBriefingCommand.Execute(null);
        Assert.True(home.IsBriefingState);

        home.EnterResultCommand.Execute(null);
        Assert.Same(qualifying, home.CurrentContent); // same half-entered qualifying grid
        Assert.Equal(1, qualifying.ResolvedCount);
    }

    [Fact]
    public void Standings_round_trip_during_qualifying_returns_to_the_qualifying_step()
    {
        using var home = new HomeViewModel(new WeekendSession());

        var qualifying = OpenQualifying(home);

        home.ShowStandingsCommand.Execute(null);
        Assert.True(home.IsStandingsState);

        home.BackToRoundCommand.Execute(null);
        Assert.Same(qualifying, home.CurrentContent);
    }

    [Fact]
    public void Session_intro_survives_briefing_and_standings_navigation_until_continue()
    {
        using var home = new HomeViewModel(new WeekendSession());

        home.EnterResultCommand.Execute(null);
        var intro = AssertIntro(home, SessionIntroKind.Qualifying);

        home.ShowBriefingCommand.Execute(null);
        home.EnterResultCommand.Execute(null);
        Assert.Same(intro, home.CurrentContent);

        home.ShowStandingsCommand.Execute(null);
        home.BackToRoundCommand.Execute(null);
        Assert.Same(intro, home.CurrentContent);

        intro.ContinueCommand.Execute(null);
        Assert.True(home.IsQualifyingStep);
    }

    [Fact]
    public void Starting_grid_survives_a_standings_round_trip()
    {
        using var home = new HomeViewModel(new WeekendSession());
        Order(OpenQualifying(home), "1", "2");
        home.ConfirmResultCommand.Execute(null);
        var grid = Assert.IsType<StartingGridViewModel>(home.CurrentContent);

        home.ShowStandingsCommand.Execute(null);
        home.BackToRoundCommand.Execute(null);

        Assert.Same(grid, home.CurrentContent);
        Assert.True(home.IsStartingGridState);
    }

    [Fact]
    public void Re_entering_an_already_created_race_editor_does_not_replay_its_intro()
    {
        using var home = new HomeViewModel(new WeekendSession { Weekend = null });

        home.EnterResultCommand.Execute(null);
        AssertIntro(home, SessionIntroKind.Race).ContinueCommand.Execute(null);
        var race = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        race.Input = "2";
        race.SubmitCommand.Execute(null);

        home.ShowBriefingCommand.Execute(null);
        home.EnterResultCommand.Execute(null);

        Assert.Same(race, home.CurrentContent);
        Assert.False(home.IsSessionIntroState);
        Assert.Equal(1, race.ResolvedCount);
    }

    [Fact]
    public void Session_intro_continue_is_exactly_once()
    {
        int advances = 0;
        var intro = new SessionIntroViewModel(
            SessionIntroKind.Race,
            "Monaco  ·  Round 1 of 16",
            () => advances++);

        intro.ContinueCommand.Execute(null);
        intro.ContinueCommand.Execute(null);

        Assert.Equal(1, advances);
        Assert.False(intro.ContinueCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Empty_grid_intro_failure_returns_to_the_briefing(bool qualifying)
    {
        var session = new WeekendSession
        {
            Weekend = qualifying
                ? new PackWeekend
                {
                    Qualifying = new PackWeekendSession { Label = "Qualifying" },
                    Races = [new PackWeekendRace { Id = "race", Label = "Grand Prix" }],
                }
                : null,
            Grid = [],
        };
        using var home = new HomeViewModel(session);

        home.EnterResultCommand.Execute(null);
        var intro = AssertIntro(home, qualifying ? SessionIntroKind.Qualifying : SessionIntroKind.Race);
        intro.ContinueCommand.Execute(null);

        Assert.True(home.IsBriefingState);
        Assert.False(home.IsSessionIntroState);
        Assert.Contains("no grid", home.ContentError, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- helpers ----------

    private static SessionIntroViewModel AssertIntro(HomeViewModel home, SessionIntroKind kind)
    {
        var intro = Assert.IsType<SessionIntroViewModel>(home.CurrentContent);
        Assert.True(home.IsSessionIntroState);
        Assert.False(home.IsResultEntryState);
        Assert.False(home.ConfirmResultCommand.CanExecute(null));
        Assert.Equal(kind, intro.Kind);
        return intro;
    }

    private static ResultEntryViewModel OpenQualifying(HomeViewModel home)
    {
        home.EnterResultCommand.Execute(null);
        AssertIntro(home, SessionIntroKind.Qualifying).ContinueCommand.Execute(null);
        var qualifying = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        Assert.True(home.IsQualifyingStep);
        return qualifying;
    }

    /// <summary>Classifies the given car numbers in order through the result-entry grammar (pole
    /// first for qualifying, P1 first for a race) and asserts the draft is complete.</summary>
    private static void Order(ResultEntryViewModel entry, params string[] carNumbers)
    {
        foreach (string number in carNumbers)
        {
            entry.Input = number;
            entry.SubmitCommand.Execute(null);
        }
        Assert.True(entry.IsComplete);
    }

    /// <summary>A minimal in-memory session whose current round runs a weekend with a qualifying
    /// session and a single race (set <see cref="Weekend"/> to null for the single-race path). The
    /// two-car grid mirrors the shared shell-navigation fake so the grammar drives identically.</summary>
    private sealed class WeekendSession : ICareerSession, IDisposable
    {
        public int AppliedRounds;
        public List<ResultDraft> Applied { get; } = [];

        public PackWeekend? Weekend { get; init; } = new()
        {
            Practice = new PackWeekendSession { Label = "Practice" },
            Qualifying = new PackWeekendSession { Label = "Qualifying" },
            Races = [new PackWeekendRace { Id = "race", Label = "Grand Prix" }],
        };

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerSummary Summary => new()
        {
            CareerName = "Weekend Career",
            SeasonYear = 1988,
            SeriesName = "Test Championship",
            CurrentRound = Math.Min(AppliedRounds + 1, Pack.Season.Rounds.Count),
            RoundCount = Pack.Season.Rounds.Count,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            SeasonComplete = AppliedRounds >= Pack.Season.Rounds.Count,
        };

        public PackWeekend? CurrentWeekend() => Summary.SeasonComplete ? null : Weekend;

        /// <summary>An SMGP rival briefing (null = a non-SMGP career, so no rival step fires).</summary>
        public Companion.ViewModels.Services.SmgpBriefingModel? SmgpBriefing { get; init; }

        public Companion.ViewModels.Services.SmgpBriefingModel? CurrentSmgpBriefing() =>
            Summary.SeasonComplete ? null : SmgpBriefing;

        public BriefingModel? CurrentBriefing() => Summary.SeasonComplete
            ? null
            : new BriefingModel
            {
                Round = Pack.Season.Rounds[Math.Min(AppliedRounds, Pack.Season.Rounds.Count - 1)],
                VenueDisplayName = "Interlagos",
                IsPlaceholder = false,
                Settings = [new CopyableSetting("Track", "Interlagos")],
            };

        public StageOutcome StageCurrentGrid() =>
            new() { Success = true, WrittenPath = "X.xml", Messages = [] };

        public IReadOnlyList<GridSeat> Grid { get; init; } =
        [
            Seat("driver.brabham", "Jack Brabham", "1"),
            Seat("driver.hulme", "Denny Hulme", "2"),
        ];

        public IReadOnlyList<GridSeat> CurrentGrid() => Grid;

        public IReadOnlyDictionary<string, string> GridCarArtKeys { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string? GridCarArtKeyForLivery(string ams2LiveryName) =>
            GridCarArtKeys.GetValueOrDefault(ams2LiveryName);

        public ConfirmModel Preview(ResultDraft draft) => new()
        {
            RoundPoints = [],
            Movements = [],
            Headline = "fake headline",
        };

        public void Apply(ResultDraft draft)
        {
            Applied.Add(draft);
            AppliedRounds++;
        }

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId) { }

        public void Dispose() { }

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
}

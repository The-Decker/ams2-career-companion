using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The reusable "Why?" inspector view-model (career-hub-design.md §5, decisions 4 + 5): shaping a
/// <see cref="JournalChain"/> for binding, and the clickable-number wiring in Standings + History.
/// The view-model tests are pure (built from a plain chain, no session); the wiring tests use a fake
/// session that records the JournalFor request and returns a controlled chain.
/// </summary>
public sealed class InspectorViewModelTests
{
    private static JournalChain SampleChain() => new()
    {
        Entity = "player",
        Round = 3,
        Title = "Why P2, You, Round 3",
        Summary = "You finished P2, beating your expected P5.",
        Contributions =
        [
            new JournalContribution
            {
                Label = "Expected finish", Detail = "Finished P2 against an expected P5.",
                Value = "P2", SourceSeq = 10,
            },
            new JournalContribution
            {
                Label = "Reputation", Detail = "Reputation moved this round, you beat your teammate.",
                Value = "43.2", SourceSeq = 12,
            },
            // A narrative row with no number (a headline) is valid alongside numeric rows.
            new JournalContribution { Label = "Headline", Detail = "Hulme storms to second.", SourceSeq = 14 },
        ],
    };

    // ---------- the pure view-model ----------

    [Fact]
    public void InspectorViewModel_projects_the_chain_for_binding()
    {
        var vm = new InspectorViewModel(SampleChain());

        Assert.Equal("Why P2, You, Round 3", vm.Title);
        Assert.Equal("You finished P2, beating your expected P5.", vm.Summary);
        Assert.True(vm.HasSummary);
        Assert.True(vm.HasRows);
        Assert.Equal(3, vm.Rows.Count);

        // Ordered rows, oldest journal seq first, the walk-back top to bottom.
        Assert.Equal(new[] { "Expected finish", "Reputation", "Headline" }, vm.Rows.Select(r => r.Label));
        Assert.Equal(new long[] { 10, 12, 14 }, vm.Rows.Select(r => r.SourceSeq));

        var numeric = vm.Rows[0];
        Assert.True(numeric.HasValue);
        Assert.Equal("P2", numeric.Value);
        Assert.True(numeric.HasDetail);

        // A narrative row carries no number, the value column stays empty.
        var narrative = vm.Rows[2];
        Assert.False(narrative.HasValue);
        Assert.Equal("", narrative.Value);
    }

    [Fact]
    public void Empty_chain_projects_to_no_rows_and_no_summary()
    {
        var vm = new InspectorViewModel(JournalChain.Empty);

        Assert.False(vm.HasRows);
        Assert.False(vm.HasSummary);
        Assert.Empty(vm.Rows);
    }

    // ---------- Standings wiring: click a number → open the inspector ----------

    [Fact]
    public void Standings_without_a_session_cannot_inspect_and_the_command_no_ops()
    {
        var vm = new StandingsViewModel([], TestPackBuilder.TwoRoundPack());

        Assert.False(vm.CanInspect);
        vm.OpenInspectorCommand.Execute("driver.hulme");
        Assert.Null(vm.SelectedInspector); // no session → nothing opens
        Assert.False(vm.IsInspectorOpen);
    }

    [Fact]
    public void Standings_open_inspector_walks_the_clicked_driver_and_closes_again()
    {
        var session = new InspectorFakeSession
        {
            Chain = new JournalChain
            {
                Entity = "driver.hulme", Title = "Why, Denny Hulme, 1967",
                Contributions = [new JournalContribution { Label = "Reputation", Value = "42" }],
            },
        };
        var vm = new StandingsViewModel([], session.Pack, settings: null, session: session);

        Assert.True(vm.CanInspect);
        vm.OpenInspectorCommand.Execute("driver.hulme");

        Assert.Equal(("driver.hulme", (int?)null), session.LastRequest);
        Assert.True(vm.IsInspectorOpen);
        Assert.Equal("Why, Denny Hulme, 1967", vm.SelectedInspector!.Title);

        // Keyboard + mouse parity: the same close command any input binds dismisses the panel.
        vm.CloseInspectorCommand.Execute(null);
        Assert.Null(vm.SelectedInspector);
        Assert.False(vm.IsInspectorOpen);
    }

    [Fact]
    public void Standings_cell_inspector_narrows_to_the_cell_round()
    {
        var session = new InspectorFakeSession
        {
            Chain = new JournalChain
            {
                Entity = "driver.hulme", Round = 2, Title = "Why P1, Denny Hulme, Round 2",
                Contributions = [new JournalContribution { Label = "Expected finish", Value = "P1" }],
            },
        };
        var vm = new StandingsViewModel([], session.Pack, settings: null, session: session);

        vm.OpenCellInspectorCommand.Execute(new RoundMatrixCellRef("driver.hulme", 2));

        Assert.Equal(("driver.hulme", (int?)2), session.LastRequest);
        Assert.True(vm.IsInspectorOpen);
    }

    [Fact]
    public void Standings_empty_chain_does_not_open_a_blank_panel()
    {
        var session = new InspectorFakeSession { Chain = JournalChain.Empty };
        var vm = new StandingsViewModel([], session.Pack, settings: null, session: session);

        vm.OpenInspectorCommand.Execute("driver.hulme");
        Assert.Null(vm.SelectedInspector); // an empty chain never opens a panel
    }

    [Fact]
    public void RoundMatrixCell_exposes_its_inspector_ref_only_when_it_has_coordinates()
    {
        var real = new RoundMatrixCell("9", IsDropped: false, "driver.hulme", 2);
        Assert.NotNull(real.InspectorRef);
        Assert.Equal("driver.hulme", real.InspectorRef!.DriverId);
        Assert.Equal(2, real.InspectorRef.Round);

        // A blank cell (no driver/round) has no click target, the number renders plain.
        var blank = new RoundMatrixCell("", IsDropped: false);
        Assert.Null(blank.InspectorRef);
    }

    // ---------- History wiring: click a season number → open the inspector ----------

    [Fact]
    public void History_opens_the_inspector_for_a_season_via_the_season_scoped_seam()
    {
        var session = new InspectorFakeSession
        {
            Chain = new JournalChain
            {
                Entity = "player", Title = "Why, You, 1967",
                Contributions = [new JournalContribution { Label = "Reputation", Value = "42" }],
            },
        };
        var history = new HistoryViewModel(session);

        // A season card's number walks the SEASON-SCOPED seam for that card's year, the current
        // season is just the current-year case of the same call.
        history.OpenSeasonInspectorCommand.Execute(1967);
        Assert.Equal(("player", 1967, (int?)null), session.LastSeasonRequest);
        Assert.True(history.IsInspectorOpen);

        history.CloseInspectorCommand.Execute(null);
        Assert.False(history.IsInspectorOpen);
    }

    [Fact]
    public void History_prior_season_number_now_walks_the_season_scoped_seam()
    {
        var session = new InspectorFakeSession
        {
            Chain = new JournalChain
            {
                Entity = "player", Title = "Why, You, 1965",
                Contributions = [new JournalContribution { Label = "Reputation", Value = "42" }],
            },
        };
        var history = new HistoryViewModel(session);

        // A finished earlier season is now reachable: the seam is asked for THAT year's journal,
        // not silently ignored (the Increment-3 follow-up this feature delivers).
        history.OpenSeasonInspectorCommand.Execute(1965);
        Assert.Equal(("player", 1965, (int?)null), session.LastSeasonRequest);
        Assert.True(history.IsInspectorOpen);
    }

    [Fact]
    public void History_season_with_no_journal_does_not_open_a_blank_panel()
    {
        var session = new InspectorFakeSession { Chain = JournalChain.Empty };
        var history = new HistoryViewModel(session);

        // A year the season-scoped seam has no rows for returns the empty chain, the panel stays
        // closed rather than opening blank.
        history.OpenSeasonInspectorCommand.Execute(1965);
        Assert.Equal(("player", 1965, (int?)null), session.LastSeasonRequest);
        Assert.False(history.IsInspectorOpen);
    }

    /// <summary>A session that records the JournalFor request and returns a controlled chain, the
    /// additive default seam members keep it minimal, proving the inspector couples only to
    /// <see cref="ICareerSession.JournalFor"/> (and, for History, <see cref="ICareerSession.Summary"/>).</summary>
    private sealed class InspectorFakeSession : ICareerSession
    {
        public JournalChain Chain { get; init; } = JournalChain.Empty;

        public (string Entity, int? Round)? LastRequest { get; private set; }

        public (string Entity, int SeasonYear, int? Round)? LastSeasonRequest { get; private set; }

        public JournalChain JournalFor(string entity, int? round = null)
        {
            LastRequest = (entity, round);
            return Chain;
        }

        public JournalChain JournalForSeason(string entity, int seasonYear, int? round = null)
        {
            LastSeasonRequest = (entity, seasonYear, round);
            return Chain;
        }

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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Companion.App.Views;
using Companion.Core.Dynasty;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render coverage for the Dynasty owner-economy surfaces: the "Team Ledger"
/// hub tab (header strip, cash-flow statement, sponsor board, development, staff, second seat,
/// pending plan, the inline refused-decision error, and the read-only fold) plus the bankruptcy
/// game-over takeover beside the death screen. Driven from a fake session serving the dashboard
/// fixture shape from tests/Companion.Tests/ViewModels/EconomyViewModelTests.cs. Self-skips off
/// Windows.</summary>
public sealed class EconomyRenderTests
{
    private sealed class EconomySession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render", SeasonYear = 1991, SeriesName = "SMGP",
            CurrentRound = 3, RoundCount = 16, PlayerDriverId = "driver.player", PlayerLiveryName = "L",
        };

        // ---------- economy seams (the surface under test) ----------

        public DynastyEconomyDashboard? Economy { get; set; }

        public DynastyEconomyDashboard? EconomyDashboard() => Economy;

        public BankruptcyScreenModel? Bankruptcy { get; set; }

        public BankruptcyScreenModel? BankruptcyScreen() => Bankruptcy;

        public List<DynastyEconomyDecision> EconomyDecisions { get; } = [];

        public Exception? DeclareEconomyDecisionThrows { get; set; }

        public void DeclareEconomyDecision(DynastyEconomyDecision decision)
        {
            if (DeclareEconomyDecisionThrows is not null)
                throw DeclareEconomyDecisionThrows;
            EconomyDecisions.Add(decision);
        }

        // ---------- the required minimal session surface (the PaddockSession shape) ----------

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest { PackId = "r", Name = "r", Version = "1", FormatVersion = 1 },
            Season = new SeasonDefinition
            {
                Year = 1991, SeriesName = "t", Ams2Class = "F-Classic_Gen3",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds = [new PackRound { Round = 1, Name = "R1", Date = "1991-01-01", Track = new PackTrackRef { Id = "monaco" }, Laps = 78 }],
            },
            Teams = [], Drivers = [], Entries = [],
        };

        public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() => [];
        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public Companion.Core.Scoring.StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<Companion.Core.Scoring.StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }

    // ---------- fixtures ----------

    private static DynastyEconomyDashboard Ledger(
        bool inDeficit = true,
        bool bankrupt = false,
        bool atCap = false,
        bool withPending = true) => new()
    {
        Balance = inDeficit ? "-2,500" : "88,000",
        InDeficit = inDeficit,
        DeficitRounds = inDeficit ? 2 : 0,
        GraceRounds = 4,
        HardFloor = "-25,000",
        Bankrupt = bankrupt,
        DevelopmentLevel = 2,
        DevelopmentMaxLevel = 8,
        NextDevelopmentCost = atCap ? "" : "8,000",
        DevelopmentAtCap = atCap,
        StaffTier = 1,
        StaffOptions =
        [
            new DynastyStaffOptionModel { Tier = 0, UpkeepPerSeason = "", IsCurrent = false },
            new DynastyStaffOptionModel { Tier = 1, UpkeepPerSeason = "14,000", IsCurrent = true },
            new DynastyStaffOptionModel { Tier = 2, UpkeepPerSeason = "30,000", IsCurrent = false },
        ],
        SecondSeat = SecondSeatDeal.Retained,
        SecondSeatSalaryPerSeason = "32,000",
        PayDriverBackingPerSeason = "12,000",
        ActiveSponsors =
        [
            new DynastySponsorContractModel
            {
                Id = "sponsor.turbo-lube", Name = "Turbo Lube", TierSlot = "major",
                SeasonsRemaining = 2, PerRace = "3,100", PerSeason = "18,000",
            },
        ],
        SponsorBoard =
        [
            new DynastySponsorOfferModel
            {
                Id = "sponsor.apex-lubricants", Name = "Apex Lubricants", TierSlot = "title",
                SigningBonus = "20,000", PerRace = "4,500", PerSeason = "40,000",
                ContractSeasons = 3, Eligible = true, IneligibleReason = "",
            },
            new DynastySponsorOfferModel
            {
                Id = "sponsor.comet-oil", Name = "Comet Oil", TierSlot = "minor",
                SigningBonus = "5,000", PerRace = "900", PerSeason = "8,000",
                ContractSeasons = 1, Eligible = false, IneligibleReason = "The title slot is taken.",
            },
        ],
        Statement =
        [
            new DynastyLedgerLineModel { Label = "Round 3 settlement", Round = 3, Net = "+9,300", BalanceAfter = "88,000", IsDeficit = false },
            new DynastyLedgerLineModel { Label = "Season settlement", Round = null, Net = "-10,700", BalanceAfter = "78,700", IsDeficit = false },
            new DynastyLedgerLineModel { Label = "Round 4 settlement", Round = 4, Net = "-2,500", BalanceAfter = "-2,500", IsDeficit = true },
        ],
        PendingDecisions = withPending
            ? [new DynastyPendingDecisionModel { Description = "Buy development (stage 3)", Amount = "-8,000", Seq = 41 }]
            : [],
        NextRound = 4,
    };

    private static BankruptcyScreenModel Folded(bool withRestore) => new()
    {
        DriverName = "Nova Reyes",
        TeamName = "Iris Autosport",
        Year = 1991,
        Round = 9,
        FinalBalance = "-26,400",
        DeficitRounds = 5,
        GraceRounds = 4,
        Record = new CareerRecordsBook
        {
            BestFinish = 1,
            Wins = 3,
            Podiums = 8,
            TotalPoints = 77,
            Championships = 1,
            SeasonsRaced = 2,
            LongestWinStreak = 2,
            LongestPodiumStreak = 4,
        },
        Seasons =
        [
            new CareerSeasonCard
            {
                SeasonYear = 1990,
                PlayerPosition = 1,
                RoundsApplied = 16,
                RoundCount = 16,
                IsComplete = true,
                ChampionName = "Nova Reyes",
                PlayerIsChampion = true,
            },
        ],
        RestoreSlots = withRestore
            ?
            [
                new SaveSlotInfo
                {
                    SlotId = "manual-001",
                    Label = "Before Monaco",
                    SeasonYear = 1991,
                    Round = 5,
                    CreatedUtc = "2026-07-12T20:00:00Z",
                    IsAutosave = false,
                },
            ]
            : [],
    };

    // ---------- Team Ledger tab ----------

    [Fact]
    public void EconomyView_RendersTheFullLedger()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new EconomyViewModel(new EconomySession { Economy = Ledger() });
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            // Header strip: the deficit balance, the "DEFICIT ROUND 2 OF 4" chip, no fold yet.
            Assert.Equal("-2,500", ((TextBlock)view.FindName("BalanceText")).Text);
            var deficitChip = (FrameworkElement)view.FindName("DeficitChip");
            Assert.Equal(Visibility.Visible, deficitChip.Visibility);
            Assert.Equal("DEFICIT ROUND 2 OF 4", ((TextBlock)((Border)deficitChip).Child).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("BankruptSeal")).Visibility);

            // Cash-flow statement: three rows, the deficit one among them.
            Assert.Equal(3, ((ItemsControl)view.FindName("StatementList")).Items.Count);

            // Sponsor board: one active contract, two board offers (the ineligible one stays listed).
            Assert.Single(((ItemsControl)view.FindName("ActiveSponsorList")).Items);
            Assert.Equal(2, ((ItemsControl)view.FindName("SponsorBoardList")).Items.Count);
            Assert.Equal(1, CountText(view, "The title slot is taken."));

            // Development stage bar + Buy.
            var stageBar = (ProgressBar)view.FindName("DevelopmentStageBar");
            Assert.Equal(8, stageBar.Maximum);
            Assert.Equal(2, stageBar.Value);
            Assert.Equal(Visibility.Visible, ((Button)view.FindName("BuyDevelopmentButton")).Visibility);

            // Staff single-select + the two-way second-seat toggle.
            Assert.Equal(3, ((ItemsControl)view.FindName("StaffOptionsList")).Items.Count);
            Assert.NotNull(view.FindName("SecondSeatRetainedButton"));
            Assert.NotNull(view.FindName("SecondSeatPayDriverButton"));

            // Pending plan, with no remove affordance by design.
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("PendingPanel")).Visibility);
            Assert.Single(((ItemsControl)view.FindName("PendingList")).Items);

            // Solvent controls: no fold notice, decisions enabled, no error.
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("FoldedNotice")).Visibility);
            Assert.True(((Grid)view.FindName("DecisionsColumn")).IsEnabled);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("EconomyActionErrorPanel")).Visibility);
        });
    }

    [Fact]
    public void EconomyView_SolventLedger_HidesTheDeficitAndPendingFurniture()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new EconomyViewModel(new EconomySession { Economy = Ledger(inDeficit: false, withPending: false) });
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            Assert.Equal("88,000", ((TextBlock)view.FindName("BalanceText")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("DeficitChip")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("PendingPanel")).Visibility);
        });
    }

    [Fact]
    public void EconomyView_BankruptLedger_StaysOpenAsAReadOnlyFinalLedger()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new EconomyViewModel(new EconomySession { Economy = Ledger(bankrupt: true, withPending: false) });
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("BankruptSeal")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("FoldedNotice")).Visibility);
            Assert.False(((Grid)view.FindName("DecisionsColumn")).IsEnabled);
        });
    }

    [Fact]
    public void EconomyView_AtDevelopmentCap_RetiresTheBuyButton()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new EconomyViewModel(new EconomySession { Economy = Ledger(atCap: true, withPending: false) });
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            Assert.Equal(Visibility.Collapsed, ((Button)view.FindName("BuyDevelopmentButton")).Visibility);
            Assert.Equal(1, CountText(view, "PROGRAMME AT CAP"));
        });
    }

    [Fact]
    public void EconomyView_DecisionCommands_FlowFromTheButtonsToTheSession()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = new EconomySession { Economy = Ledger(inDeficit: false, withPending: false) };
            var vm = new EconomyViewModel(session);
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            // Sign the eligible board offer (row 0).
            ClickItemButton((ItemsControl)view.FindName("SponsorBoardList"), 0, "Sign this sponsor");
            // Drop the active contract.
            ClickItemButton((ItemsControl)view.FindName("ActiveSponsorList"), 0, "Drop this sponsor");
            // Buy the next development stage.
            var buy = (Button)view.FindName("BuyDevelopmentButton");
            buy.Command.Execute(buy.CommandParameter);
            // Set staff tier 2 (row index 2).
            ClickItemButton((ItemsControl)view.FindName("StaffOptionsList"), 2, "Set staff tier");
            // Flip the second seat to the pay-driver deal.
            var payDriver = (Button)view.FindName("SecondSeatPayDriverButton");
            payDriver.Command.Execute(payDriver.CommandParameter);

            Assert.Equal(5, session.EconomyDecisions.Count);
            Assert.Equal(DynastyEconomyDecisionKind.SignSponsor, session.EconomyDecisions[0].Kind);
            Assert.Equal("sponsor.apex-lubricants", session.EconomyDecisions[0].SponsorId);
            Assert.Equal(DynastyEconomyDecisionKind.DropSponsor, session.EconomyDecisions[1].Kind);
            Assert.Equal("sponsor.turbo-lube", session.EconomyDecisions[1].SponsorId);
            Assert.Equal(DynastyEconomyDecisionKind.BuyDevelopment, session.EconomyDecisions[2].Kind);
            Assert.Equal(DynastyEconomyDecisionKind.SetStaff, session.EconomyDecisions[3].Kind);
            Assert.Equal(2, session.EconomyDecisions[3].StaffTier);
            Assert.Equal(DynastyEconomyDecisionKind.SetSecondSeat, session.EconomyDecisions[4].Kind);
            Assert.Equal(SecondSeatDeal.PayDriver, session.EconomyDecisions[4].SecondSeat);
            Assert.Equal("", vm.EconomyActionError);
        });
    }

    [Fact]
    public void EconomyView_RefusedDecision_ShowsTheReasonInlineNearTheActions()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = new EconomySession
            {
                Economy = Ledger(inDeficit: false, withPending: false),
                DeclareEconomyDecisionThrows = new InvalidOperationException("The team cannot afford this increment."),
            };
            var vm = new EconomyViewModel(session);
            var view = new EconomyView { DataContext = vm };
            Arrange(view, 1180, 900);

            var buy = (Button)view.FindName("BuyDevelopmentButton");
            buy.Command.Execute(buy.CommandParameter);
            view.UpdateLayout();

            // Inline near the failing action, never modal; the dashboard and decisions stay put.
            var panel = (FrameworkElement)view.FindName("EconomyActionErrorPanel");
            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(1, CountText(view, "The team cannot afford this increment."));
            Assert.Empty(session.EconomyDecisions);
            Assert.True(((Grid)view.FindName("DecisionsColumn")).IsEnabled);
        });
    }

    // ---------- bankruptcy takeover ----------

    [Fact]
    public void BankruptcyView_RendersTheAdministratorsNotice()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new BankruptcyHost { BankruptcyScreen = Folded(withRestore: true) };
            var view = new BankruptcyView { DataContext = host };
            Arrange(view, 1100, 900);

            Assert.Equal("Iris Autosport", ((TextBlock)view.FindName("BankruptcyTeamName")).Text);
            Assert.Equal(1, CountText(view, "B A N K R U P T"));
            Assert.Equal(1, CountText(view, "ADMINISTRATOR'S NOTICE"));
            Assert.True(ContainsText(view, "Nova Reyes") >= 2);   // owner-driver line + champion card
            Assert.True(ContainsText(view, "-26,400") >= 2);      // final balance chip + fold line

            // The career record with the death screen's dignity, plus the season almanac.
            Assert.Single(((ItemsControl)view.FindName("BankruptcySeasonRecap")).Items);

            // Restore slots on offer (a mortality-Normal career's saves survive the fold).
            var slots = (ItemsControl)view.FindName("BankruptcyRestoreSlots");
            Assert.Equal(Visibility.Visible, slots.Visibility);
            Assert.Single(slots.Items);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("BankruptcyNoRestoreCallout")).Visibility);
        });
    }

    [Fact]
    public void BankruptcyView_WithoutSaves_OffersNoRestore()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new BankruptcyHost { BankruptcyScreen = Folded(withRestore: false) };
            var view = new BankruptcyView { DataContext = host };
            Arrange(view, 1100, 900);

            var slots = (ItemsControl)view.FindName("BankruptcyRestoreSlots");
            Assert.Equal(Visibility.Collapsed, slots.Visibility);
            Assert.Empty(slots.Items);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("BankruptcyNoRestoreCallout")).Visibility);
        });
    }

    [Fact]
    public void HubView_Bankruptcy_ReplacesTheLiveHubWithTheTakeover()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new HubView
            {
                DataContext = new HubHost
                {
                    Home = new HomeHost
                    {
                        Briefing = new BriefingHost { SmgpCareerOver = false },
                        BankruptcyScreen = Folded(withRestore: true),
                    },
                },
            };
            Arrange(view, 1100, 820);

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LiveHubSurfaces")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("BankruptcySurface")).Visibility);
        });
    }

    [Fact]
    public void HubView_SolventCareer_KeepsTheLiveHubAndHidesTheTakeover()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new HubView
            {
                DataContext = new HubHost
                {
                    Home = new HomeHost
                    {
                        Briefing = new BriefingHost { SmgpCareerOver = false },
                        BankruptcyScreen = null,
                    },
                },
            };
            Arrange(view, 1100, 820);

            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("LiveHubSurfaces")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("BankruptcySurface")).Visibility);
        });
    }

    // ---------- stand-in hosts + helpers ----------

    private sealed class BankruptcyHost
    {
        public required BankruptcyScreenModel BankruptcyScreen { get; init; }
    }

    private sealed class HubHost
    {
        public required HomeHost Home { get; init; }
    }

    private sealed class HomeHost
    {
        public object? CareerOver => null;
        public required BriefingHost Briefing { get; init; }
        public required BankruptcyScreenModel? BankruptcyScreen { get; init; }
    }

    private sealed class BriefingHost
    {
        public bool SmgpCareerOver { get; init; }
    }

    private static void ClickItemButton(ItemsControl items, int index, string automationName)
    {
        var container = (DependencyObject)items.ItemContainerGenerator.ContainerFromIndex(index);
        Assert.NotNull(container);
        var button = Descendants<Button>(container).FirstOrDefault(
            b => AutomationProperties.GetName(b) == automationName);
        Assert.NotNull(button);
        Assert.True(button.Command.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
    }

    private static int CountText(DependencyObject root, string text) =>
        Descendants<TextBlock>(root).Count(tb => tb.Text == text);

    private static int ContainsText(DependencyObject root, string text) =>
        Descendants<TextBlock>(root).Count(tb => tb.Text.Contains(text, StringComparison.Ordinal));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static void Arrange(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Assert.True(view.ActualWidth > 0);
        Assert.True(view.ActualHeight > 0);
    }
}

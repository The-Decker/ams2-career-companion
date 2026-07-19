using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Companion.App.Views;
using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render coverage for the season review, driving the REAL <see cref="SeasonReviewViewModel"/>
/// over a fake career session (trimmed to the seams the review reads, the same shape as
/// Companion.Tests' FakeCareerSession in ViewModels/SessionTestSupport.cs):
/// the offer letters render as period documents of the season's era, a final-season hold (no next
/// season) collapses every forward-looking action to the archive message, and the terminal state
/// (a career whose fold left no review behind) renders as a clean archive page instead of crashing.
/// Self-skips off Windows.
/// </summary>
public sealed class SeasonReviewRenderTests
{
    /// <summary>The session seams <see cref="SeasonReviewViewModel"/> reads, settable per scenario;
    /// everything else follows the interface defaults exactly as the Companion.Tests fake does.</summary>
    private sealed class ReviewSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = 2,
            RoundCount = 2,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = "Player Livery",
            SeasonComplete = true,
        };

        public SeasonPack Pack { get; } = ReviewPack();

        public SeasonReviewModel? Review { get; set; }

        public NextSeasonInfo? Next { get; set; }

        public CharacterDossier? Dossier { get; set; }

        public int Cp { get; set; }

        public (string DriverId, string DisplayName)? Identity { get; set; }

        public SeasonReviewModel? SeasonReview() => Review;

        public NextSeasonInfo? NextSeason() => Next;

        public CharacterDossier? CharacterDossier() => Dossier;

        public int AvailableCharacterCp() => Cp;

        public (string DriverId, string DisplayName)? PlayerIdentity() => Identity;

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() => new() { Success = false, Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) => new()
        {
            RoundPoints = [],
            Movements = [],
            Headline = "",
        };

        public void Apply(ResultDraft draft) { }

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public void AcceptOffer(string teamId) { }
    }

    private static SeasonReviewModel Review() => new()
    {
        SeasonYear = 1967,
        PlayerPosition = 3,
        FinalReputation = 47.5,
        FinalOpi = 0.8,
        Headlines = ["Headline one", "Headline two"],
        Offers =
        [
            new SeasonOfferModel
            {
                TeamId = "team.lotus", TeamName = "Lotus", Tier = 5,
                SalaryBu = 7.5, Score = 2.1, Accepted = false,
            },
            new SeasonOfferModel
            {
                TeamId = "team.cooper", TeamName = "Cooper", Tier = 3,
                SalaryBu = 4.0, Score = 1.4, Accepted = false,
            },
        ],
    };

    private static NextSeasonInfo Next1968() => new()
    {
        PackDirectory = @"Z:\packs\f1-1968",
        PackId = "f1-1968",
        PackName = "Formula One 1968",
        SeasonYear = 1968,
        BridgedYears = [],
    };

    private static CharacterDossier Dossier() => new()
    {
        Name = "Denny",
        Level = 3,
        Xp = 500,
        XpIntoLevel = 100,
        XpForNextLevel = 300,
        LevelCap = 30,
        CpUnspent = 0,
        Stats =
        [
            new DossierStat("pace", "Pace", 0.60, Talent: true),
            new DossierStat("racecraft", "Racecraft", 0.55, Talent: true),
        ],
        Perks = [],
    };

    [Fact]
    public void SeasonReview_RendersPeriodOfferLetters_WhenANextSeasonWaits()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SeasonReviewViewModel(new ReviewSession
            {
                Review = Review(),
                Next = Next1968(),
                Identity = ("driver.hulme", "Denny Hulme"),
                Dossier = Dossier(),
                Cp = 2,
            });
            var view = new SeasonReviewView { DataContext = vm };
            Arrange(view);

            var panel = Assert.IsType<Border>(view.FindName("OfferLettersPanel"));
            Assert.Equal(Visibility.Visible, panel.Visibility);

            var letters = Assert.IsType<ItemsControl>(view.FindName("OfferLetters"));
            Assert.Equal(2, letters.Items.Count);
            letters.UpdateLayout();

            // The 1967 season skins each offer as a telegram: letterhead, medium stamp,
            // dateline and wire-copy body all bind straight off the OfferLetterViewModel.
            TextBlock[] rendered = Descendants<TextBlock>(letters).ToArray();
            Assert.Contains(rendered, block => block.Text == "LOTUS, RACE DEPT");
            Assert.Contains(rendered, block => block.Text == "COOPER, RACE DEPT");
            Assert.Contains(rendered, block => block.Text == "TELEGRAM");
            Assert.Contains(rendered,
                block => block.Text.Contains("FILED 1967 STOP", StringComparison.Ordinal));
            Assert.Contains(rendered,
                block => block.Text.Contains("TO DENNY HULME STOP", StringComparison.Ordinal));
            Assert.Contains(rendered,
                block => block.Text.Contains("OUR NUMBER ONE SEAT", StringComparison.Ordinal));
            Assert.Contains(rendered,
                block => InlineText(block).Contains("Tier 5", StringComparison.Ordinal) &&
                         InlineText(block).Contains("7.5 BU / season", StringComparison.Ordinal));

            // Every letter carries its own Accept command wired to the real view-model command.
            Assert.Equal(2, Descendants<Button>(letters).Count());

            var sign = Assert.IsType<Button>(view.FindName("SignAndContinueButton"));
            Assert.Equal(Visibility.Visible, sign.Visibility);
            Assert.Equal("Sign & start 1968", sign.Content?.ToString());

            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(view.FindName("DevelopmentPanel")).Visibility);
        });
    }

    [Fact]
    public void SeasonReview_FinalSeasonHold_CollapsesEveryForwardAction()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            // The season folded WITH offers and a fat development bank, but no next season exists:
            // every dead-end action must disappear, leaving only the archive message.
            var vm = new SeasonReviewViewModel(new ReviewSession
            {
                Review = Review(),
                Next = null,
                Dossier = Dossier(),
                Cp = 499,
            });
            var view = new SeasonReviewView { DataContext = vm };
            Arrange(view);

            Assert.False(vm.HasNextSeason);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("OfferLettersPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("DevelopmentPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Button>(view.FindName("SignAndContinueButton")).Visibility);
            Assert.Equal("This season is complete.",
                Assert.IsType<TextBlock>(view.FindName("TransitionText")).Text);
        });
    }

    [Fact]
    public void SeasonReview_TerminalReview_RendersACleanArchivePage()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            // The terminal variant: the career's fold left no review behind (the defensive null),
            // so the screen degrades to its title, an empty summary and the complete message,
            // every actionable surface collapsed, nothing thrown.
            var vm = new SeasonReviewViewModel(new ReviewSession
            {
                Review = null,
                Next = null,
            });
            var view = new SeasonReviewView { DataContext = vm };
            Arrange(view);

            Assert.Null(vm.Review);
            Assert.Contains(Descendants<TextBlock>(view),
                block => block.Text == "Season review");
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("OfferLettersPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("DevelopmentPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Button>(view.FindName("SignAndContinueButton")).Visibility);
            Assert.Equal("This season is complete.",
                Assert.IsType<TextBlock>(view.FindName("TransitionText")).Text);
        });
    }

    private static SeasonPack ReviewPack() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "render-pack",
            Name = "Render Pack",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test Championship",
            Ams2Class = "F-Vintage_Gen1",
            PointsSystem = new CatalogSeason
            {
                RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
                DriversBestN = new CatalogBestN { WholeSeason = 2 },
            },
            Rounds =
            [
                new PackRound
                {
                    Round = 1, Name = "Round 1", Date = "1967-01-02",
                    Track = new PackTrackRef { Id = "kyalami_historic" }, Laps = 40,
                },
                new PackRound
                {
                    Round = 2, Name = "Round 2", Date = "1967-05-07",
                    Track = new PackTrackRef { Id = "kyalami_historic" }, Laps = 40,
                },
            ],
        },
        Teams =
        [
            new PackTeam
            {
                Id = "team.brabham",
                Name = "Brabham-Repco",
                CarVehicleIds = ["formula_vintage_g1m2"],
                Reliability = 0.93,
                Prestige = 4,
                BudgetTier = 5,
            },
        ],
        Drivers =
        [
            new PackDriver { Id = "driver.brabham", Name = "driver.brabham", Ratings = Ratings() },
            new PackDriver { Id = "driver.hulme", Name = "driver.hulme", Ratings = Ratings() },
        ],
        Entries =
        [
            new PackEntry
            {
                TeamId = "team.brabham", DriverId = "driver.brabham", Number = "1",
                Rounds = "1-2", Ams2LiveryName = "Stock Livery #1",
            },
            new PackEntry
            {
                TeamId = "team.brabham", DriverId = "driver.hulme", Number = "2",
                Rounds = "1-2", Ams2LiveryName = "Stock Livery #2",
            },
        ],
    };

    private static PackDriverRatings Ratings() => new()
    {
        RaceSkill = 0.8,
        QualifyingSkill = 0.85,
        Aggression = 0.5,
        Defending = 0.5,
        Stamina = 0.8,
        Consistency = 0.8,
        StartReactions = 0.8,
        WetSkill = 0.8,
        TyreManagement = 0.8,
        AvoidanceOfMistakes = 0.8,
    };

    private static void Arrange(FrameworkElement view)
    {
        view.Measure(new Size(1200, 1400));
        view.Arrange(new Rect(0, 0, 1200, 1400));
        view.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string InlineText(TextBlock block) =>
        block.Text + string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
}

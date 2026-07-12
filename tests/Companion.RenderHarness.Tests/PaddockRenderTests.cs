using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Paddock tab: a real PaddockView over a real PaddockViewModel whose
/// fake session returns a two-driver / two-team paddock (with a full stat line + bio + quotes). Proves
/// the master-detail, the DRIVERS/TEAMS toggle DataTriggers, the stat tiles and every StaticResource
/// resolve without a render-time crash — which a compile can't. Self-skips off Windows.</summary>
public sealed class PaddockRenderTests
{
    private sealed class PaddockSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render", SeasonYear = 1989, SeriesName = "SMGP",
            CurrentRound = 1, RoundCount = 16, PlayerDriverId = "driver.player", PlayerLiveryName = "L",
        };

        public SmgpPaddockModel? SmgpPaddock() => new()
        {
            Drivers =
            [
                new SmgpDriverCard
                {
                    DriverId = "driver.ayrton_senna", Name = "Ayrton Senna", TeamId = "team.madonna",
                    TeamName = "Madonna", Number = "1", PortraitKey = "driver.ayrton_senna",
                    CarKey = "driver.ayrton_senna", Epithet = "THE UNTOUCHABLE KING",
                    Bio = ["The king.", "Serene and inevitable.", "The crown never slips."],
                    Quotes = ["I set the time they fail to reach."],
                    Stats = new SmgpDriverStatLine
                    {
                        DriverId = "driver.ayrton_senna", CareerStarts = 142, CareerWins = 69,
                        CareerPodiums = 118, CareerPoles = 72, CareerTop5s = 134, CareerPoints = 1012,
                        Championships = 6,
                    },
                    Prestige = 5,
                },
                new SmgpDriverCard
                {
                    DriverId = "driver.gilberto_ceara", Name = "Gilberto Ceara", TeamId = "team.bullets",
                    TeamName = "Bullets", Number = "17", PortraitKey = "driver.gilberto_ceara",
                    CarKey = "driver.gilberto_ceara", Epithet = "THE INSURGENT",
                    Bio = ["The outsider.", "Clawing up.", "The number one seat has his name on it."],
                    Quotes = ["A crown you steal is a crown."], Stats = null, Prestige = 3,
                },
            ],
            Teams =
            [
                new SmgpTeamCard
                {
                    TeamId = "team.madonna", Name = "Madonna", Motto = "THE CROWN NEVER SLIPS",
                    LogoKey = "team.madonna", History = ["The king's house.", "Yellow and red."],
                    Quotes = ["We are chased."], DriverNames = ["Ayrton Senna"], Prestige = 5,
                },
                new SmgpTeamCard
                {
                    TeamId = "team.bullets", Name = "Bullets", Motto = "STEAL THE CROWN",
                    LogoKey = "team.bullets", History = ["The insurgent house."],
                    Quotes = ["We bring the file."], DriverNames = ["Gilberto Ceara"], Prestige = 3,
                },
            ],
        };

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest { PackId = "r", Name = "r", Version = "1", FormatVersion = 1 },
            Season = new SeasonDefinition
            {
                Year = 1989, SeriesName = "t", Ams2Class = "F-Classic_Gen3",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds = [new PackRound { Round = 1, Name = "R1", Date = "1989-01-01", Track = new PackTrackRef { Id = "monaco" }, Laps = 78 }],
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

    [Fact]
    public void PaddockView_RendersDriversThenTeams()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new PaddockViewModel(new PaddockSession());
            Assert.True(vm.HasPaddock);
            Assert.Equal(2, vm.Drivers.Count);

            var view = new PaddockView { DataContext = vm };
            view.Measure(new Size(1100, 900));
            view.Arrange(new Rect(0, 0, 1100, 900));
            view.UpdateLayout();
            Assert.True(view.ActualWidth > 0 && view.ActualHeight > 0);

            // The selected driver's dossier renders: name, epithet, and a career stat number.
            Assert.True(CountText(view, "Ayrton Senna") >= 1);
            Assert.Equal(1, CountText(view, "THE UNTOUCHABLE KING"));
            Assert.True(CountText(view, "69") >= 1);   // career wins stat tile

            // Toggle to the TEAMS view — the team dossier renders its motto.
            vm.ShowTeamsListCommand.Execute(null);
            view.UpdateLayout();
            Assert.True(CountText(view, "THE CROWN NEVER SLIPS") >= 1);
        });
    }

    private static int CountText(DependencyObject root, string text) =>
        Descendants<System.Windows.Controls.TextBlock>(root).Count(tb => tb.Text == text);

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
}

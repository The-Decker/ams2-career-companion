using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.Ams2.Skins;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen contract for the read-only, selectable pre-qualifying Grid Preview.</summary>
public sealed class SkinsViewRenderTests
{
    private sealed class GridPreviewSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1988,
            SeriesName = "Super Monaco GP",
            CurrentRound = 1,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Lotus-Ford Cosworth #1 G. Hill",
        };

        public SeasonPack Pack { get; } = PreviewPack();

        public SkinAssignmentPlan CurrentSkinAssignments() => new()
        {
            Ams2Class = "F-Classic_Gen3",
            Assignments =
            [
                Assignment(
                    "driver.christopher_tegner", "team.orchis", "J. Brabham", "Brabham-Ford",
                    "3", "Brabham-Ford Cosworth #3 J. Brabham", "31", SkinStatus.CustomSkin),
                Assignment(
                    "driver.paul_white", "team.blanche", "C. Amon", "Ferrari",
                    "11", "Ferrari #11 C. Amon", "10", SkinStatus.NameOnly),
                Assignment(
                    "driver.nigel_jones", "team.tyrant", "Nobody", "Privateer",
                    "99", "typo #99 nobody", "", SkinStatus.Unbound),
                Assignment(
                    "driver.eddie_bellini", "team.serga", "K. Acheson", "Skoal Bandit",
                    "10", "Skoal Bandit Formula 1 Team #10", "", SkinStatus.InstalledInactive),
                Assignment(
                    RoundGridResolver.SyntheticPlayerDriverId, "team.zeroforce", "Nova Reyes", "Lotus-Ford",
                    "1", "Lotus-Ford Cosworth #1 G. Hill", "32", SkinStatus.CustomSkin, isPlayer: true),
            ],
        };

        public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() =>
        [
            new SeasonScheduleEntry
            {
                Round = 1,
                Name = "Monaco",
                Date = "1988-05-15",
                RealVenue = "Monaco",
                Ams2TrackName = "Azure",
                Laps = 78,
                Kind = SeasonTrackKind.RealVenue,
                Dnq =
                [
                    new ScheduleDnqEntry("Paul Klinger", "Zeroforce", "32"),
                    new ScheduleDnqEntry("Paul White", "Blanche", "10"),
                ],
            },
        ];

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

    [Fact]
    public void GridPreview_RendersEveryRequiredVisualAndSwitchesTheSelectedCar()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SkinsViewModel(new GridPreviewSession());
            Assert.Equal(5, vm.Cars.Count);
            Assert.True(vm.HasDnq);
            Assert.Equal(2, vm.DidNotQualify.Count);
            Assert.True(vm.SelectedCar!.IsPlayer);

            var view = new SkinsView { DataContext = vm };
            view.LayoutTransform = new ScaleTransform(1.3, 1.3);
            var host = new Border { Width = 1180, Height = 900, Child = view };
            host.Measure(new Size(1180, 900));
            host.Arrange(new Rect(0, 0, 1180, 900));
            host.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);

            var scroll = Assert.IsType<ScrollViewer>(view.FindName("SkinsScrollViewer"));
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
            Assert.True(Assert.IsType<Border>(view.FindName("SelectedCarPreview")).ActualHeight > 0);
            Assert.True(Assert.IsType<Border>(view.FindName("DidNotQualifyStrip")).ActualHeight > 0);

            var selector = Assert.IsType<ListBox>(view.FindName("GridEntrySelector"));
            Assert.Equal(5, selector.Items.Count);
            Assert.Same(vm.SelectedCar, selector.SelectedItem);

            var portrait = Assert.IsType<Image>(view.FindName("SelectedDriverPortraitPreview"));
            var logo = Assert.IsType<Image>(view.FindName("SelectedTeamLogoPreview"));
            var top = Assert.IsType<Image>(view.FindName("SelectedTopCarPreview"));
            var side = Assert.IsType<Image>(view.FindName("SelectedSideCarPreview"));
            Assert.NotNull(portrait.Source);
            Assert.NotNull(logo.Source);
            Assert.NotNull(top.Source);
            Assert.NotNull(side.Source);
            Assert.Contains("player.zeroforce", portrait.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("team.zeroforce", logo.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.paul_klinger", top.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.paul_klinger", side.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            AssertPreviewGeometry(view, portrait, logo, top, side);

            selector.SelectedIndex = 0;
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            host.UpdateLayout();

            Assert.Equal("J. Brabham", vm.SelectedCar!.DriverName);
            Assert.Contains("driver.christopher_tegner", portrait.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("team.orchis", logo.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.christopher_tegner", top.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.christopher_tegner", side.Source.ToString(), StringComparison.OrdinalIgnoreCase);

            Assert.Empty(FindVisualChildren<TextBox>(view));
            Assert.Empty(FindVisualChildren<ComboBox>(view));
            Assert.Equal(2, FindVisualChildren<Button>(view).Count());
        });
    }

    [Fact]
    public void GridPreview_WideViewport_IsContainedKeyboardAccessibleAndWraps()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SkinsViewModel(new GridPreviewSession());
            var view = new SkinsView { DataContext = vm };
            var window = new Window
            {
                Content = view,
                Width = 1600,
                Height = 1000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                window.Show();
                window.Activate();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);
                window.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Render);

                var scroll = Assert.IsType<ScrollViewer>(view.FindName("SkinsScrollViewer"));
                Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
                AssertHorizontallyContained(view, Assert.IsType<Border>(view.FindName("SelectedCarPreview")));
                AssertHorizontallyContained(view, Assert.IsType<Border>(view.FindName("DidNotQualifyStrip")));

                var previous = Assert.IsType<Button>(view.FindName("PreviewPreviousCarButton"));
                var next = Assert.IsType<Button>(view.FindName("PreviewNextCarButton"));
                Assert.Equal("Previous grid car", AutomationProperties.GetName(previous));
                Assert.Equal("Next grid car", AutomationProperties.GetName(next));
                Assert.True(previous.IsTabStop);
                Assert.True(next.IsTabStop);

                var selector = Assert.IsType<ListBox>(view.FindName("GridEntrySelector"));
                selector.SelectedIndex = 0;
                selector.UpdateLayout();
                var item = Assert.IsType<ListBoxItem>(selector.ItemContainerGenerator.ContainerFromIndex(0));
                item.ApplyTemplate();
                Assert.True(item.Focus());
                Keyboard.Focus(item);
                WpfRenderHarness.Pump(DispatcherPriority.Input);
                Assert.True(item.IsKeyboardFocusWithin);

                vm.SelectedCar = vm.Cars[0];
                vm.PreviousCarCommand.Execute(null);
                Assert.Same(vm.Cars[^1], vm.SelectedCar);
                vm.NextCarCommand.Execute(null);
                Assert.Same(vm.Cars[0], vm.SelectedCar);
            }
            finally
            {
                window.Close();
                WpfRenderHarness.Pump(DispatcherPriority.Background);
            }
        });
    }

    [Fact]
    public void GridPreview_MissingCarArtworkLeavesTransparentSpace()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Companion.App", "Views", "SkinsView.xaml"));

        Assert.DoesNotContain("&#xE7C0;", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("&#xE804;", xaml, StringComparison.Ordinal);
        Assert.Contains("&#xE77B;", xaml, StringComparison.Ordinal);
        Assert.Contains("&#xE7C1;", xaml, StringComparison.Ordinal);
    }

    private static SkinAssignment Assignment(
        string driverId,
        string teamId,
        string driverName,
        string teamName,
        string number,
        string livery,
        string slot,
        SkinStatus status,
        bool isPlayer = false) => new()
    {
        DriverId = driverId,
        TeamId = teamId,
        DriverName = driverName,
        TeamName = teamName,
        Number = number,
        LiveryName = livery,
        SkinSlot = slot,
        Status = status,
        IsPlayer = isPlayer,
        VehicleFolder = status == SkinStatus.CustomSkin ? "formula_classic_g3m1" : null,
        NearMiss = status == SkinStatus.Unbound ? "Typo #99 Nobody" : null,
    };

    private static SeasonPack PreviewPack() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "render",
            Name = "Render",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1988,
            SeriesName = "Super Monaco GP",
            Ams2Class = "F-Classic_Gen3",
            PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
            Rounds =
            [
                new PackRound
                {
                    Round = 1,
                    Name = "Test Grand Prix",
                    Date = "1988-03-01",
                    Track = new PackTrackRef { Id = "test_track" },
                    Laps = 60,
                },
            ],
        },
        Teams =
        [
            new PackTeam { Id = "team.zeroforce", Name = "Zeroforce", CarVehicleIds = ["formula_classic_g3m1"] },
            new PackTeam { Id = "team.orchis", Name = "Orchis", CarVehicleIds = ["formula_classic_g3m1"] },
            new PackTeam { Id = "team.blanche", Name = "Blanche", CarVehicleIds = ["formula_classic_g3m1"] },
        ],
        Drivers =
        [
            Driver("driver.paul_klinger", "Paul Klinger"),
            Driver("driver.christopher_tegner", "Christopher Tegner"),
            Driver("driver.paul_white", "Paul White"),
        ],
        Entries =
        [
            Entry("team.zeroforce", "driver.paul_klinger", "1", "Lotus-Ford Cosworth #1 G. Hill"),
            Entry("team.orchis", "driver.christopher_tegner", "3", "Brabham-Ford Cosworth #3 J. Brabham"),
            Entry("team.blanche", "driver.paul_white", "11", "Ferrari #11 C. Amon"),
        ],
    };

    private static PackDriver Driver(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Ratings = new PackDriverRatings { RaceSkill = 0.8, QualifyingSkill = 0.8 },
    };

    private static PackEntry Entry(string teamId, string driverId, string number, string liveryName) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = "1-16",
        Ams2LiveryName = liveryName,
    };

    private static void AssertPreviewGeometry(
        SkinsView view,
        FrameworkElement portrait,
        FrameworkElement logo,
        FrameworkElement top,
        FrameworkElement side)
    {
        var card = Assert.IsType<Border>(view.FindName("SelectedCarPreview"));
        Rect portraitBounds = BoundsIn(card, portrait);
        Rect logoBounds = BoundsIn(card, logo);
        Rect topBounds = BoundsIn(card, top);
        Rect sideBounds = BoundsIn(card, side);

        Assert.True(portraitBounds.Width >= 120, "Driver portrait must remain a full profile preview.");
        Assert.True(logoBounds.X > portraitBounds.X, "Team logo must remain visible beside the profile.");
        Assert.True(topBounds.X > portraitBounds.X, "Top view must remain on the upper right.");
        Assert.True(topBounds.Height >= 120, "Top-view car must remain a large preview.");
        Assert.True(sideBounds.Y > portraitBounds.Bottom, "Side view must remain below the upper previews.");
        Assert.True(sideBounds.Width > topBounds.Width * 2, "Side view must span the lower preview.");
    }

    private static Rect BoundsIn(Visual ancestor, FrameworkElement element) =>
        element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(), element.RenderSize));

    private static void AssertHorizontallyContained(FrameworkElement ancestor, FrameworkElement element)
    {
        Rect bounds = element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(), element.RenderSize));
        Assert.True(bounds.Left >= -1, $"{element.Name} starts outside the viewport at {bounds.Left}.");
        Assert.True(bounds.Right <= ancestor.ActualWidth + 1,
            $"{element.Name} ends at {bounds.Right}, beyond viewport width {ancestor.ActualWidth}.");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (T descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }
}

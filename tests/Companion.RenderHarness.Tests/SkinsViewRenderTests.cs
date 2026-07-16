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

/// <summary>Off-screen render of the Skins lens: a real SkinsView over a real SkinsViewModel whose
/// fake session returns a mixed skin picture (a custom-skin car, a default car, an unbound car with a
/// near-miss, and the player's own car). Exercises the player preview, before/after workbench, real
/// skin-slot labels, compact roster, and required-skin-packs block — the binding/crash surface a compiled-XAML build
/// cannot. Self-skips off Windows.</summary>
public sealed class SkinsViewRenderTests
{
    private sealed class SkinsSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1969,
            SeriesName = "Formula One",
            CurrentRound = 1,
            RoundCount = 11,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Player Livery",
        };

        public SeasonPack Pack { get; } = SkinPack();

        public SkinAssignmentPlan CurrentSkinAssignments() => new()
        {
            Ams2Class = "F-Vintage_Gen2",
            Assignments =
            [
                new SkinAssignment
                {
                    DriverId = "driver.christopher_tegner", TeamId = "team.orchis", SkinSlot = "31",
                    DriverName = "J. Brabham", TeamName = "Brabham-Ford", Number = "3",
                    LiveryName = "Brabham-Ford Cosworth #3 J. Brabham", IsPlayer = false,
                    Status = SkinStatus.CustomSkin, VehicleFolder = "formula_classic_g3m1",
                },
                new SkinAssignment
                {
                    DriverId = "driver.paul_white", TeamId = "team.blanche", SkinSlot = "10",
                    DriverName = "C. Amon", TeamName = "Ferrari", Number = "11",
                    LiveryName = "Ferrari #11 C. Amon", IsPlayer = false,
                    Status = SkinStatus.NameOnly,
                },
                new SkinAssignment
                {
                    DriverId = "driver.nigel_jones", TeamId = "team.tyrant",
                    DriverName = "Nobody", TeamName = "Privateer", Number = "99",
                    LiveryName = "typo #99 nobody", IsPlayer = false,
                    Status = SkinStatus.Unbound, NearMiss = "Typo #99 Nobody",
                },
                new SkinAssignment
                {
                    DriverId = "driver.eddie_bellini", TeamId = "team.serga",
                    DriverName = "K. Acheson", TeamName = "Skoal Bandit", Number = "10",
                    LiveryName = "Skoal Bandit Formula 1 Team #10", IsPlayer = false,
                    Status = SkinStatus.InstalledInactive,
                },
                new SkinAssignment
                {
                    DriverId = "driver.paul_klinger", TeamId = "team.zeroforce", SkinSlot = "32",
                    DriverName = "Nova Reyes", TeamName = "Lotus-Ford", Number = "1",
                    LiveryName = "Lotus-Ford Cosworth #1 G. Hill", IsPlayer = true,
                    Status = SkinStatus.CustomSkin, VehicleFolder = "formula_classic_g3m1",
                },
            ],
            InactiveLiveries = ["Skoal Bandit Formula 1 Team #10", "RAM #11 Winkelhock"],
            ActiveLiveries =
            [
                "Ferrari #11 C. Amon",
                "Brabham-Ford Cosworth #3 J. Brabham",
                "Lotus-Ford Cosworth #1 G. Hill",
                "Wrong Model Custom #5",
            ],
            ActiveLiverySlots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Ferrari #11 C. Amon"] = "10",
                ["Brabham-Ford Cosworth #3 J. Brabham"] = "31",
                ["Lotus-Ford Cosworth #1 G. Hill"] = "32",
                ["Wrong Model Custom #5"] = "55",
            },
            ActiveCustomLiveryModels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Brabham-Ford Cosworth #3 J. Brabham"] = "formula_classic_g3m1",
                ["Lotus-Ford Cosworth #1 G. Hill"] = "formula_classic_g3m1",
                ["Wrong Model Custom #5"] = "formula_classic_g3m2",
            },
            // A tiny cap so the over-cap warning panel + budget line both render (XAML validation).
            LiveryCap = 4,
        };

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

    private static SeasonPack SkinPack() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "render", Name = "Render", Version = "1.0.0", FormatVersion = 1,
            Requires = new PackRequirements
            {
                SkinPacks = [new PackSkinPackRequirement
                {
                    Name = "F1 1969 Season (Alain Fry)",
                    Url = "https://overtake.gg/example",
                    OverridesFolder = "F1_Season_1969",
                }],
            },
        },
        Season = new SeasonDefinition
        {
            Year = 1969, SeriesName = "Formula One", Ams2Class = "F-Vintage_Gen2",
            PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
            Rounds =
            [
                new PackRound
                {
                    Round = 1, Name = "Test Grand Prix", Date = "1969-03-01",
                    Track = new PackTrackRef { Id = "test_track" }, Laps = 60,
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

    [Fact]
    public void SkinsView_RendersThePlayerCribAndCarStatuses()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SkinsViewModel(new SkinsSession());
            Assert.True(vm.HasPlayerCar);
            Assert.Equal(5, vm.Cars.Count);
            Assert.True(vm.HasUnbound);
            Assert.True(vm.HasActivatable);
            Assert.Equal(2, vm.ActivatableLiveries.Count);
            Assert.Equal(5, vm.Editors.Count); // an editable row per seat
            Assert.Single(vm.RequiredSkinPacks);
            Assert.Equal("player.zeroforce", vm.PlayerPortraitKey);
            Assert.Equal("driver.paul_klinger", vm.PlayerCarKey);
            Assert.Equal("driver.paul_klinger", vm.PlayerTopCarKey);
            Assert.Equal("32", vm.PlayerSkinSlot);
            Assert.NotNull(vm.SelectedEditor);
            Assert.Equal("Lotus-Ford Cosworth #1 G. Hill", vm.SelectedEditor!.SelectedLivery);
            Assert.Null(vm.SelectedEditor.ReplacementSelection);
            Assert.Equal(
                ["Brabham-Ford Cosworth #3 J. Brabham", "Lotus-Ford Cosworth #1 G. Hill"],
                vm.SelectedEditor.LiveryOptions);
            Assert.DoesNotContain("Ferrari #11 C. Amon", vm.SelectedEditor.LiveryOptions);
            Assert.DoesNotContain("Wrong Model Custom #5", vm.SelectedEditor.LiveryOptions);

            var view = new SkinsView { DataContext = vm };
            view.LayoutTransform = new ScaleTransform(1.3, 1.3);
            var host = new Border { Width = 1180, Height = 900, Child = view };
            host.Measure(new Size(1180, 900));
            host.Arrange(new Rect(0, 0, 1180, 900));
            host.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            var scroll = Assert.IsType<ScrollViewer>(view.FindName("SkinsScrollViewer"));
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
            Assert.True(Assert.IsType<Border>(view.FindName("PlayerSkinHero")).ActualHeight > 0);
            Assert.True(Assert.IsType<Border>(view.FindName("SkinReplacementWorkbench")).ActualWidth > 0);
            Assert.Equal(5, Assert.IsType<ListBox>(view.FindName("SkinEditorRoster")).Items.Count);
            Assert.Equal(5, Assert.IsType<ItemsControl>(view.FindName("CurrentRoundCompactRoster")).Items.Count);
            Assert.NotNull(Assert.IsType<Image>(view.FindName("PlayerPortraitPreview")).Source);
            Assert.NotNull(Assert.IsType<Image>(view.FindName("PlayerSideCarPreview")).Source);
            Assert.NotNull(Assert.IsType<Image>(view.FindName("PlayerTopCarPreview")).Source);
            var selector = Assert.IsType<ComboBox>(view.FindName("SkinReplacementSelector"));
            Assert.Equal(2, selector.Items.Count);
            Assert.True(selector.IsEnabled);
            Assert.DoesNotContain(
                FindVisualChildren<Button>(view),
                button => string.Equals(button.Content as string, "Copy", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                FindVisualChildren<Button>(view),
                button => string.Equals(button.Content as string, "Switch skin season", StringComparison.Ordinal));

            var currentPortrait = Assert.IsType<Image>(view.FindName("CurrentSkinPortraitPreview"));
            var currentSide = Assert.IsType<Image>(view.FindName("CurrentSkinSidePreview"));
            var currentTop = Assert.IsType<Image>(view.FindName("CurrentSkinTopPreview"));
            var replacementPortrait = Assert.IsType<Image>(view.FindName("ReplacementSkinPortraitPreview"));
            var replacementSide = Assert.IsType<Image>(view.FindName("ReplacementSkinSidePreview"));
            var replacementTop = Assert.IsType<Image>(view.FindName("ReplacementSkinTopPreview"));
            Assert.Contains("player.zeroforce", currentPortrait.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.paul_klinger", currentSide.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.paul_klinger", currentTop.Source.ToString(), StringComparison.OrdinalIgnoreCase);

            vm.SelectedEditor.ReplacementSelection = "Brabham-Ford Cosworth #3 J. Brabham";
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            host.UpdateLayout();
            Assert.True(vm.SelectedEditor.IsReplacement);
            Assert.Equal("Brabham-Ford Cosworth #3 J. Brabham", vm.SelectedEditor.SelectedLivery);
            Assert.Equal("Christopher Tegner", vm.SelectedEditor.SelectedPreview.DriverName);
            Assert.Equal("31", vm.SelectedEditor.SelectedPreview.SkinSlot);
            Assert.Equal("driver.christopher_tegner", vm.SelectedEditor.SelectedPreview.CarKey);
            Assert.Contains("driver.christopher_tegner", replacementPortrait.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.christopher_tegner", replacementSide.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("driver.christopher_tegner", replacementTop.Source.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(currentPortrait.Source.ToString(), replacementPortrait.Source.ToString());
            Assert.NotEqual(currentSide.Source.ToString(), replacementSide.Source.ToString());
            Assert.NotEqual(currentTop.Source.ToString(), replacementTop.Source.ToString());
        });
    }

    [Fact]
    public void SkinsView_WideViewport_IsContainedAccessibleAndKeyboardFocused()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SkinsView { DataContext = new SkinsViewModel(new SkinsSession()) };
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
                Assert.True(scroll.ViewportWidth > 0);
                Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
                AssertHorizontallyContained(view, Assert.IsType<Border>(view.FindName("PlayerSkinHero")));
                AssertHorizontallyContained(view, Assert.IsType<Border>(view.FindName("SkinReplacementWorkbench")));
                AssertPreviewGeometry(
                    view,
                    "CurrentSkinComparisonCard",
                    "CurrentSkinPortraitPreview",
                    "CurrentSkinTopPreview",
                    "CurrentSkinSidePreview");
                AssertPreviewGeometry(
                    view,
                    "ReplacementSkinComparisonCard",
                    "ReplacementSkinPortraitPreview",
                    "ReplacementSkinTopPreview",
                    "ReplacementSkinSidePreview");

                var driverName = Assert.IsType<TextBox>(view.FindName("SkinDriverNameEditor"));
                var replacement = Assert.IsType<ComboBox>(view.FindName("SkinReplacementSelector"));
                Assert.Equal("Driver name", AutomationProperties.GetName(driverName));
                Assert.Equal("Replacement skin", AutomationProperties.GetName(replacement));

                var roster = Assert.IsType<ListBox>(view.FindName("SkinEditorRoster"));
                roster.SelectedIndex = 0;
                roster.UpdateLayout();
                var item = Assert.IsType<ListBoxItem>(roster.ItemContainerGenerator.ContainerFromIndex(0));
                item.ApplyTemplate();
                Assert.True(item.Focusable);
                Assert.True(item.IsTabStop);
                Assert.True(item.Focus());
                Keyboard.Focus(item);
                WpfRenderHarness.Pump(DispatcherPriority.Input);
                Assert.True(item.IsKeyboardFocusWithin);
                var focusFrame = Assert.IsType<Border>(item.Template.FindName("EditorRailFrame", item));
                Assert.Equal(new Thickness(2), focusFrame.BorderThickness);
            }
            finally
            {
                window.Close();
                WpfRenderHarness.Pump(DispatcherPriority.Background);
            }
        });
    }

    private static void AssertPreviewGeometry(
        SkinsView view,
        string cardName,
        string portraitName,
        string topName,
        string sideName)
    {
        var card = Assert.IsType<Border>(view.FindName(cardName));
        var portrait = Assert.IsType<Image>(view.FindName(portraitName));
        var top = Assert.IsType<Image>(view.FindName(topName));
        var side = Assert.IsType<Image>(view.FindName(sideName));

        Assert.IsType<Grid>(VisualTreeHelper.GetParent(top));
        Assert.IsType<Grid>(VisualTreeHelper.GetParent(side));

        Rect portraitBounds = BoundsIn(card, portrait);
        Rect topBounds = BoundsIn(card, top);
        Rect sideBounds = BoundsIn(card, side);

        Assert.True(portraitBounds.Width >= 110, $"{portraitName} was not enlarged.");
        Assert.True(topBounds.X > portraitBounds.X,
            $"{topName} must sit to the right of {portraitName}.");
        Assert.True(topBounds.Height >= 120, $"{topName} was not scaled larger.");
        Assert.True(sideBounds.Y > portraitBounds.Bottom,
            $"{sideName} must sit below the driver profile.");
        Assert.True(sideBounds.Width > topBounds.Width * 2,
            $"{sideName} must span the bottom of the comparison card.");
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
}

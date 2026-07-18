using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the cinematic starting-grid screen (shown after qualifying) over a
/// real <see cref="StartingGridViewModel"/>. Laying out the REAL <see cref="StartingGridView"/>
/// realises both the historical staggered card grid and the SMGP-only pixel starting straight plus
/// their conditions bars, resolving every StaticResource, canonical mode trigger, team-colour
/// binding, keyed car fallback, and player-highlight DataTrigger that a pure VM test can't exercise.
/// Self-skips on a non-Windows / non-STA host.</summary>
public sealed class StartingGridRenderTests
{
    private const string PlayerId = "driver.hulme";
    private static readonly PackDriverRatings Ratings = new() { RaceSkill = 0.8, QualifyingSkill = 0.8 };

    private static void AssertTeamAccent(Brush actual, string primaryHex, string secondaryHex)
    {
        Color primary = (Color)ColorConverter.ConvertFromString(primaryHex);
        Color secondary = (Color)ColorConverter.ConvertFromString(secondaryHex);
        if (primary == secondary)
        {
            Assert.Equal(primary, Assert.IsType<SolidColorBrush>(actual).Color);
            return;
        }

        var split = Assert.IsType<LinearGradientBrush>(actual);
        Assert.True(split.IsFrozen);
        Assert.Equal(new Point(0, 0), split.StartPoint);
        Assert.Equal(new Point(1, 1), split.EndPoint);
        Assert.Equal(4, split.GradientStops.Count);
        Assert.Equal(primary, split.GradientStops[0].Color);
        Assert.Equal(primary, split.GradientStops[1].Color);
        Assert.Equal(secondary, split.GradientStops[2].Color);
        Assert.Equal(secondary, split.GradientStops[3].Color);
        Assert.Equal(0.5, split.GradientStops[1].Offset);
        Assert.Equal(0.5, split.GradientStops[2].Offset);
    }

    private static GridSeat Seat(string id, string name, string number) => new()
    {
        DriverId = id,
        DriverName = name,
        TeamId = "team." + name.ToLowerInvariant(),
        TeamName = "Team " + name,
        Number = number,
        Ams2LiveryName = name,
        Ratings = Ratings,
        Reliability = 1.0,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = id == PlayerId,
    };

    // A tiny binding-only host is enough to exercise the view's canonical mode gate without adding
    // replica state to StartingGridViewModel: HomeView.DataContext.Session.Pack.Manifest.CareerStyle.
    private sealed class ModeHost
    {
        public ModeSession Session { get; } = new();
        public ModeBriefingView Briefing { get; } = new();
    }
    private sealed class ModeSession { public ModePack Pack { get; } = new(); }
    private sealed class ModePack
    {
        public ModeManifest Manifest { get; } = new();
        public ModeSeason Season { get; } = new();
    }
    private sealed class ModeManifest { public string CareerStyle => SmgpRules.CareerStyle; }
    private sealed class ModeSeason { public bool? RefuellingAllowed => false; }
    private sealed class ModeBriefingView
    {
        public ModeBriefing Briefing { get; } = new();
        public string FuelNote => "One tank covers the race; conserve fuel or shorten the distance.";
        public string SmgpAdviceLine => "Pass at the hairpin and protect the exit.";
    }
    private sealed class ModeBriefing { public ModeRound Round { get; } = new(); }
    private sealed class ModeRound
    {
        public int Laps => 61;
        public ModeSetupGuide SetupGuide { get; } = new();
    }
    private sealed class ModeSetupGuide { public ModeRaceSession Session { get; } = new(); }
    private sealed class ModeRaceSession { public bool MandatoryPitStop => false; }

    private static GridSeat SmgpSeat(
        string id, string name, string teamId, string teamName, string number, bool isPlayer = false) => new()
    {
        DriverId = id,
        DriverName = name,
        TeamId = teamId,
        TeamName = teamName,
        Number = number,
        Ams2LiveryName = name,
        Ratings = Ratings,
        Reliability = 1.0,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = isPlayer,
    };

    [Fact]
    public void StartingGridView_RendersTheTwoWideCards()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var grid = new[]
            {
                Seat("driver.brabham", "Brabham", "1"),
                Seat(PlayerId, "Hulme", "2"),
                Seat("driver.clark", "Clark", "5"),
            };
            var vm = new StartingGridViewModel(grid, PlayerId, "Feature",
                new GridConditions { LapDistanceKm = 5.278, Weather = "Clear", TrackTempC = 28, AirTempC = 22 });

            // The pole slot leads; the player's own card carries the team player image key.
            Assert.Equal("driver.brabham", vm.Slots[0].DriverId);
            Assert.Equal("player.hulme", vm.Slots[1].PortraitKey); // team-coloured player image
            // Staggered rows: odds on top (P1, P3), evens on the back row (P2).
            Assert.Equal([1, 3], vm.TopRow.Select(s => s.Position));
            Assert.Equal([2], vm.BottomRow.Select(s => s.Position));
            // Every card resolves a team accent colour.
            Assert.All(vm.Slots, s => Assert.StartsWith("#", s.TeamColor));
            Assert.Equal("5.278 km", vm.Conditions.LapDistanceLabel);

            var view = new StartingGridView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("HistoricalCardGrid")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SmgpPixelTrack")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("DesktopConditionsPanel")).Visibility);
        });
    }

    [Fact]
    public void StartingGridView_RendersSmgpPixelTrack_WithoutChangingHistoricalGrid()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            const string smgpPlayerId = "driver.player-entrant";
            GridSeat[] grid = Enumerable.Range(1, 26)
                .Select(position => SmgpSeat(
                    position == 20 ? smgpPlayerId : position == 1 ? "driver.ayrton_senna" : $"driver.smoke_{position}",
                    position == 20 ? "You" : $"Arcade Driver {position}",
                    position == 1 ? "team.madonna" : $"team.smoke_{(position + 1) / 2}",
                    position == 1 ? "Madonna" : $"Arcade Team {(position + 1) / 2}",
                    position.ToString(),
                    isPlayer: position == 20))
                .ToArray();
            StartingGridDnq[] dnq = Enumerable.Range(27, 8)
                .Select(position => new StartingGridDnq(
                    $"DNQ Driver {position}", $"DNQ Team {(position + 1) / 2}", position.ToString()))
                .ToArray();
            var vm = new StartingGridViewModel(
                grid, smgpPlayerId, "Preliminary Race",
                new GridConditions { LapDistanceKm = 5.040, Weather = "Clear", TrackTempC = 29, AirTempC = 23 },
                playerCarArtDriverId: "driver.giorgio_alberti", dnq: dnq,
                playerCountryFlagKey: "driver.george_turner");

            Assert.Equal("driver.ayrton_senna", vm.Slots[0].CountryFlagKey);
            Assert.Equal("driver.george_turner", vm.Slots[19].CountryFlagKey);

            var view = new StartingGridView { DataContext = vm };
            var home = new HomeView { DataContext = new ModeHost(), Content = view };
            // Approximate the grid's remaining content area after the real shell chrome and left rail
            // at the app's 920x620 minimum and 130% UI scale. Include the live 26+8 DNQ field.
            home.Measure(new Size(528, 350));
            home.Arrange(new Rect(0, 0, 528, 350));
            home.UpdateLayout();

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("HistoricalCardGrid")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SmgpPixelTrack")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("DesktopConditionsPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("DnqStrip")).Visibility);
            Assert.Equal("61", ((TextBlock)view.FindName("SmgpLapCount")).Text);
            Assert.Contains("One tank", ((TextBlock)view.FindName("SmgpStrategyText")).Text);
            Assert.Contains("hairpin", ((TextBlock)view.FindName("SmgpTacticalAdvice")).Text);
            Assert.Equal("PIT STOP  ·  OPTIONAL", ((TextBlock)view.FindName("SmgpPitRule")).Text);
            Assert.Equal("REFUELLING  ·  NOT ALLOWED", ((TextBlock)view.FindName("SmgpRefuelRule")).Text);
            Assert.True(((FrameworkElement)view.FindName("SmgpRacePlanPanel")).ActualHeight > 0);

            var slots = (ItemsControl)view.FindName("SmgpSlotList");
            var dnqList = (ItemsControl)view.FindName("DnqList");
            var scroll = (ScrollViewer)view.FindName("SmgpGridScroll");
            Assert.Equal(grid.Length, slots.Items.Count);
            Assert.Equal(dnq.Length, dnqList.Items.Count);
            Assert.NotNull(dnqList.ItemContainerGenerator.ContainerFromIndex(dnq.Length - 1));
            Assert.True(scroll.Focusable);
            Assert.True(scroll.ScrollableHeight > 0);

            var firstSlot = Assert.IsType<ContentPresenter>(slots.ItemContainerGenerator.ContainerFromIndex(0));
            var card = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpGridSlotCard", firstSlot));
            var visuals = Assert.IsType<Grid>(
                firstSlot.ContentTemplate.FindName("SmgpDriverVisuals", firstSlot));
            var portrait = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpPortraitFrame", firstSlot));
            var portraitOutline = Assert.IsType<Border>(
                firstSlot.ContentTemplate.FindName("SmgpPortraitOutline", firstSlot));
            var timingCard = Assert.IsType<Border>(
                firstSlot.ContentTemplate.FindName("SmgpTimingCard", firstSlot));
            var carBay = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpCarBay", firstSlot));
            var carBayOutline = Assert.IsType<Border>(
                firstSlot.ContentTemplate.FindName("SmgpGridBayOutline", firstSlot));
            var car = Assert.IsType<Image>(
                firstSlot.ContentTemplate.FindName("SmgpCarImage", firstSlot));
            var flag = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpFlagFrame", firstSlot));
            var flagImage = Assert.IsType<Image>(
                firstSlot.ContentTemplate.FindName("SmgpFlagImage", firstSlot));
            var logo = Assert.IsType<Image>(
                firstSlot.ContentTemplate.FindName("SmgpTeamLogoImage", firstSlot));
            var driverName = Assert.IsType<TextBlock>(
                firstSlot.ContentTemplate.FindName("SmgpDriverName", firstSlot));
            var teamName = Assert.IsType<TextBlock>(
                firstSlot.ContentTemplate.FindName("SmgpTeamName", firstSlot));
            var positionMarker = Assert.IsType<Border>(
                firstSlot.ContentTemplate.FindName("SmgpPositionMarker", firstSlot));
            Assert.True(firstSlot.ActualWidth >= 175);
            Assert.Equal(148, card.ActualHeight);
            Assert.Equal(new Thickness(3, 3, 3, 67), card.Margin);
            Assert.Equal(62, portrait.ActualWidth);
            Assert.Equal(62, portrait.ActualHeight);
            Assert.Equal(new Thickness(3), portraitOutline.BorderThickness);
            Assert.Equal(90, visuals.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(0, Grid.GetColumn(portrait));
            Assert.Equal(2, Grid.GetColumn(carBay));
            Assert.Equal(170, carBayOutline.MaxWidth);
            Assert.Equal(HorizontalAlignment.Stretch, carBayOutline.HorizontalAlignment);
            Assert.True(carBay.ActualWidth >= 85,
                $"Expected a usable compact car bay, measured {carBay.ActualWidth:0.##}px in a {firstSlot.ActualWidth:0.##}px slot.");
            Assert.True(carBay.ActualWidth <= firstSlot.ActualWidth);
            var rotation = Assert.IsType<RotateTransform>(car.LayoutTransform);
            Assert.Equal(0, rotation.Angle);
            Assert.Equal(108, car.Width);
            Assert.Equal(158, car.Height);
            var carCenterX = car.TranslatePoint(new Point(car.ActualWidth / 2, 0), carBay).X;
            var outlineCenterX = carBayOutline.TranslatePoint(
                new Point(carBayOutline.ActualWidth / 2, 0), carBay).X;
            Assert.InRange(Math.Abs(carCenterX - carBay.ActualWidth / 2), 0, 0.5);
            Assert.InRange(Math.Abs(outlineCenterX - carBay.ActualWidth / 2), 0, 0.5);
            AssertTeamAccent(portraitOutline.BorderBrush, vm.Slots[0].TeamColor, vm.Slots[0].TeamSecondaryColor);
            AssertTeamAccent(timingCard.BorderBrush, vm.Slots[0].TeamColor, vm.Slots[0].TeamSecondaryColor);
            AssertTeamAccent(positionMarker.Background, vm.Slots[0].TeamColor, vm.Slots[0].TeamSecondaryColor);
            Assert.NotNull(logo.Source);
            Assert.Equal(12.5, driverName.FontSize);
            Assert.Equal(10.5, teamName.FontSize);
            Assert.True(positionMarker.ActualWidth >= 56);
            Assert.Equal(44, flag.ActualWidth);
            Assert.Equal(30, flag.ActualHeight);
            Assert.Single(((Grid)flag).Children);
            Assert.IsType<Image>(((Grid)flag).Children[0]);
            Assert.Equal(Visibility.Visible, flag.Visibility);
            Assert.Equal(Visibility.Visible, flagImage.Visibility);
            Assert.NotNull(flagImage.Source);

            var positionTop = positionMarker.TranslatePoint(new Point(0, 0), visuals).Y;
            var portraitTop = portrait.TranslatePoint(new Point(0, 0), visuals).Y;
            var flagTop = flag.TranslatePoint(new Point(0, 0), visuals).Y;
            Assert.InRange(positionTop, -0.5, 0.5);
            Assert.InRange(portraitTop, 31.5, 32.5);
            Assert.InRange(flagTop, 99.5, 100.5);
            Assert.True(positionTop + positionMarker.ActualHeight < portraitTop,
                "The position card must sit above the portrait without overlap.");
            Assert.True(portraitTop + portrait.ActualHeight < flagTop,
                "The country flag must sit below the portrait without overlap.");

            var secondSlot = Assert.IsType<ContentPresenter>(slots.ItemContainerGenerator.ContainerFromIndex(1));
            var thirdSlot = Assert.IsType<ContentPresenter>(slots.ItemContainerGenerator.ContainerFromIndex(2));
            var firstTop = firstSlot.TranslatePoint(new Point(0, 0), slots).Y;
            var secondTop = secondSlot.TranslatePoint(new Point(0, 0), slots).Y;
            var thirdTop = thirdSlot.TranslatePoint(new Point(0, 0), slots).Y;
            var secondStagger = Assert.IsType<TranslateTransform>(secondSlot.RenderTransform);
            Assert.Equal(130, secondStagger.Y);
            Assert.InRange(secondTop - firstTop, 129.5, 130.5);
            Assert.InRange(thirdTop - firstTop, 217.5, 218.5);

            var playerSlot = Assert.IsType<ContentPresenter>(slots.ItemContainerGenerator.ContainerFromIndex(19));
            var playerFlag = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpFlagFrame", playerSlot));
            var playerFlagImage = Assert.IsType<Image>(
                playerSlot.ContentTemplate.FindName("SmgpFlagImage", playerSlot));
            var youBadge = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpYouBadge", playerSlot));
            var playerPortrait = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpPortraitFrame", playerSlot));
            var playerVisuals = Assert.IsType<Grid>(
                playerSlot.ContentTemplate.FindName("SmgpDriverVisuals", playerSlot));
            var playerPortraitImage = Assert.IsType<Image>(
                playerSlot.ContentTemplate.FindName("SmgpPortraitImage", playerSlot));
            Assert.Equal(Visibility.Visible, playerFlag.Visibility);
            Assert.Equal(Visibility.Visible, playerFlagImage.Visibility);
            Assert.NotNull(playerFlagImage.Source);
            Assert.Equal(Visibility.Visible, youBadge.Visibility);
            var playerScale = Assert.IsType<ScaleTransform>(playerPortrait.RenderTransform);
            Assert.Equal(1.05, playerScale.ScaleX);
            Assert.Equal(1.05, playerScale.ScaleY);
            Assert.Equal(Stretch.UniformToFill, playerPortraitImage.Stretch);
            Assert.True(playerPortrait.ActualWidth * playerScale.ScaleX <=
                        playerVisuals.ColumnDefinitions[0].ActualWidth,
                $"The {playerPortrait.ActualWidth * playerScale.ScaleX:0.##}px player portrait must fit " +
                $"inside its {playerVisuals.ColumnDefinitions[0].ActualWidth:0.##}px rail without right-edge clipping.");
        });
    }

    [Fact]
    public void StartingGridView_LegacyPlayerWithoutCountry_CollapsesResolvedFlagFrame()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            const string legacyPlayerId = "driver.player-entrant";
            GridSeat[] grid =
            [
                SmgpSeat(
                    legacyPlayerId, "Legacy Player", "team.lares", "Lares", "23",
                    isPlayer: true),
            ];
            var vm = new StartingGridViewModel(
                grid, legacyPlayerId, "Preliminary Race",
                new GridConditions
                {
                    LapDistanceKm = 5.040,
                    Weather = "Clear",
                    TrackTempC = 29,
                    AirTempC = 23,
                },
                playerCarArtDriverId: "driver.park_arai");
            Assert.Null(vm.Slots.Single().CountryFlagKey);

            var view = new StartingGridView { DataContext = vm };
            var home = new HomeView { DataContext = new ModeHost(), Content = view };
            home.Measure(new Size(720, 500));
            home.Arrange(new Rect(0, 0, 720, 500));
            home.UpdateLayout();

            var slots = Assert.IsType<ItemsControl>(view.FindName("SmgpSlotList"));
            var playerSlot = Assert.IsType<ContentPresenter>(
                slots.ItemContainerGenerator.ContainerFromIndex(0));
            var flagFrame = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpFlagFrame", playerSlot));
            var flagImage = Assert.IsType<Image>(
                playerSlot.ContentTemplate.FindName("SmgpFlagImage", playerSlot));

            Assert.Null(flagImage.Source);
            Assert.Equal(Visibility.Collapsed, flagImage.Visibility);
            Assert.Equal(Visibility.Collapsed, flagFrame.Visibility);
        });
    }

    [Fact]
    public void StartingGridView_SmgpTrackExpandsWithTheAvailableViewport()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            GridSeat[] grid = Enumerable.Range(1, 26)
                .Select(position => SmgpSeat(
                    $"driver.responsive_{position}",
                    $"Responsive Driver {position}",
                    $"team.responsive_{(position + 1) / 2}",
                    $"Responsive Team {(position + 1) / 2}",
                    position.ToString()))
                .ToArray();
            var vm = new StartingGridViewModel(
                grid, grid[0].DriverId, "Preliminary Race",
                new GridConditions
                {
                    LapDistanceKm = 5.040,
                    Weather = "Clear",
                    TrackTempC = 29,
                    AirTempC = 23,
                });
            var view = new StartingGridView { DataContext = vm };
            var home = new HomeView { DataContext = new ModeHost(), Content = view };

            (double TrackWidth, double ViewportWidth, double ScrollableWidth) MeasureAt(double width)
            {
                home.Measure(new Size(width, 800));
                home.Arrange(new Rect(0, 0, width, 800));
                home.UpdateLayout();

                var track = (FrameworkElement)view.FindName("SmgpTrackSurface");
                var scroll = (ScrollViewer)view.FindName("SmgpGridScroll");
                Assert.Same(track, scroll.Content);
                return (track.ActualWidth, scroll.ViewportWidth, scroll.ScrollableWidth);
            }

            var compact = MeasureAt(720);
            var wide = MeasureAt(1600);

            Assert.InRange(Math.Abs(compact.TrackWidth - compact.ViewportWidth), 0, 1);
            Assert.InRange(Math.Abs(wide.TrackWidth - wide.ViewportWidth), 0, 1);
            Assert.Equal(0, compact.ScrollableWidth);
            Assert.Equal(0, wide.ScrollableWidth);
            Assert.True(wide.TrackWidth > 1400,
                $"Expected the SMGP track to fill a wide monitor, but it measured {wide.TrackWidth:0.#}px.");
            Assert.True(wide.TrackWidth > compact.TrackWidth * 2,
                $"Expected responsive growth from {compact.TrackWidth:0.#}px to {wide.TrackWidth:0.#}px.");

            var slots = Assert.IsType<ItemsControl>(view.FindName("SmgpSlotList"));
            var firstSlot = Assert.IsType<ContentPresenter>(
                slots.ItemContainerGenerator.ContainerFromIndex(0));
            var carBay = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpCarBay", firstSlot));
            var carBayOutline = Assert.IsType<Border>(
                firstSlot.ContentTemplate.FindName("SmgpGridBayOutline", firstSlot));
            Assert.InRange(carBayOutline.ActualWidth, 169.5, 170.5);
            var originalOutlineCap = carBay.MaxWidth
                                     - carBayOutline.Margin.Left
                                     - carBayOutline.Margin.Right;
            var configuredReduction = 1 - carBayOutline.MaxWidth / originalOutlineCap;
            Assert.InRange(configuredReduction, 0.31, 0.34);
        });
    }

    [Fact]
    public void StartingGridView_RendersTheDnqStrip()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var grid = new[] { Seat("driver.brabham", "Brabham", "1"), Seat(PlayerId, "Hulme", "2") };
            var dnq = new[]
            {
                new StartingGridDnq("Paul White", "Blanche", "10"),
                new StartingGridDnq("Paul Klinger", "Zeroforce", "32"),
            };
            var vm = new StartingGridViewModel(grid, PlayerId, "Feature",
                conditions: null, playerCarArtDriverId: null, dnq: dnq);
            Assert.True(vm.HasDnq);

            var view = new StartingGridView { DataContext = vm };
            view.Measure(new Size(1000, 800));
            view.Arrange(new Rect(0, 0, 1000, 800));
            view.UpdateLayout();

            var strip = (FrameworkElement)view.FindName("DnqStrip");
            var list = (ItemsControl)view.FindName("DnqList");
            Assert.Equal(Visibility.Visible, strip.Visibility);
            Assert.Equal(dnq.Length, list.Items.Count);
            Assert.All(Enumerable.Range(0, dnq.Length), index =>
                Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(index)));
        });
    }
}

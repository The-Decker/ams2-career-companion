using System.Windows;
using System.Windows.Controls;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the cinematic starting-grid screen (shown after qualifying) over a
/// real <see cref="StartingGridViewModel"/>. Laying out the REAL <see cref="StartingGridView"/>
/// realises both the historical staggered card grid and the SMGP-only pixel starting straight plus
/// their conditions bars — resolving every StaticResource, canonical mode trigger, team-colour
/// binding, keyed car fallback, and player-highlight DataTrigger that a pure VM test can't exercise.
/// Self-skips on a non-Windows / non-STA host.</summary>
public sealed class StartingGridRenderTests
{
    private const string PlayerId = "driver.hulme";
    private static readonly PackDriverRatings Ratings = new() { RaceSkill = 0.8, QualifyingSkill = 0.8 };

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
                    $"team.smoke_{(position + 1) / 2}",
                    $"Arcade Team {(position + 1) / 2}",
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
                playerCarArtDriverId: "driver.giorgio_alberti", dnq: dnq);

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
            var carBay = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpCarBay", firstSlot));
            var flag = Assert.IsAssignableFrom<FrameworkElement>(
                firstSlot.ContentTemplate.FindName("SmgpFlagFrame", firstSlot));
            Assert.True(firstSlot.ActualWidth >= 175);
            Assert.Equal(136, card.ActualHeight);
            Assert.Equal(54, portrait.ActualWidth);
            Assert.Equal(0, Grid.GetColumn(portrait));
            Assert.Equal(2, Grid.GetColumn(carBay));
            Assert.True(carBay.ActualWidth >= 100);
            Assert.True(carBay.ActualWidth <= firstSlot.ActualWidth);
            Assert.Equal(Visibility.Visible, flag.Visibility);

            var playerSlot = Assert.IsType<ContentPresenter>(slots.ItemContainerGenerator.ContainerFromIndex(19));
            var playerFlag = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpFlagFrame", playerSlot));
            var youBadge = Assert.IsAssignableFrom<FrameworkElement>(
                playerSlot.ContentTemplate.FindName("SmgpYouBadge", playerSlot));
            Assert.Equal(Visibility.Collapsed, playerFlag.Visibility);
            Assert.Equal(Visibility.Visible, youBadge.Visibility);
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

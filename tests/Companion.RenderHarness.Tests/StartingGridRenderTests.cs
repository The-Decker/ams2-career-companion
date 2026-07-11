using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the cinematic starting-grid screen (shown after qualifying) over a
/// real <see cref="StartingGridViewModel"/>. Laying out the REAL <see cref="StartingGridView"/>
/// realises the staggered two-row team-coloured card grid + the conditions bars — resolving every
/// StaticResource (SurfaceBrush, AccentBrush, HexBrush, the AssetImage converter, …), the
/// team-colour binding and the player-highlight DataTrigger — which a pure VM test can't exercise.
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
        });
    }
}

using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the starting-grid screen (shown after qualifying) over a real
/// <see cref="StartingGridViewModel"/>. Laying out the REAL <see cref="StartingGridView"/> realises
/// the two-wide UniformGrid of driver/car cards — resolving every StaticResource (SurfaceBrush,
/// AccentBrush, the AssetImage converter, …) and the player-highlight DataTrigger — which a pure VM
/// test can't exercise. Self-skips on a non-Windows / non-STA host.</summary>
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
                Seat(PlayerId, "Hulme", "2"),
                Seat("driver.brabham", "Brabham", "1"),
                Seat("driver.clark", "Clark", "5"),
            };
            var vm = new StartingGridViewModel(grid, PlayerId, "Feature");

            // The player's own card carries the team player image key; the pole slot leads.
            Assert.Equal("driver.hulme", vm.Slots[0].DriverId);
            Assert.True(vm.Slots[0].IsPlayer);
            Assert.Equal("player.hulme", vm.Slots[0].PortraitKey); // team-coloured player image
            Assert.Equal("driver.brabham", vm.Slots[1].PortraitKey); // a rival keeps their own portrait

            var view = new StartingGridView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}

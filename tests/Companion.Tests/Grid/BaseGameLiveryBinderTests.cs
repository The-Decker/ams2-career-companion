using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;

namespace Companion.Tests.Grid;

/// <summary>The base-game livery rebind: every AI driver is moved onto a real base-game livery for
/// the class (from official-liveries.json) so AMS2 accepts the file and shows the drivers, instead
/// of referencing community-skin names the install may not have.</summary>
public sealed class BaseGameLiveryBinderTests
{
    private static CustomAiFile FileOf(params string[] liveries) => new()
    {
        VehicleClass = "F-Classic_Gen2",
        Drivers = liveries.Select((l, i) => new CustomAiDriver { LiveryName = l, Name = $"Driver {i}" }).ToList(),
    };

    private static IReadOnlyList<OfficialLivery> Base(params string[] names) =>
        names.Select((n, i) => new OfficialLivery { Name = n, Slot = 50 + i }).ToList();

    [Fact]
    public void RebindsEachDriverToADistinctBaseGameLivery_InOrder_KeepingNames()
    {
        var file = FileOf("1988 Lotus #1 - N. Piquet", "1988 McLaren #12 - A. Senna", "1988 Ferrari #27 - M. Alboreto");

        var result = BaseGameLiveryBinder.RebindToBaseGame(
            file, Base("United Racing #3", "United Racing #4", "McLaren MP4/4 #11"));

        Assert.Equal(
            new[] { "United Racing #3", "United Racing #4", "McLaren MP4/4 #11" },
            result.Drivers.Select(d => d.LiveryName));
        // The driver identities are untouched — only the livery each is painted as changes.
        Assert.Equal(new[] { "Driver 0", "Driver 1", "Driver 2" }, result.Drivers.Select(d => d.Name));
    }

    [Fact]
    public void MoreDriversThanLiveries_LeavesTheOverflowOnItsOriginalLivery()
    {
        var file = FileOf("A", "B", "C");
        var result = BaseGameLiveryBinder.RebindToBaseGame(file, Base("United Racing #3", "United Racing #4"));
        Assert.Equal(new[] { "United Racing #3", "United Racing #4", "C" }, result.Drivers.Select(d => d.LiveryName));
    }

    [Fact]
    public void PerTrackOverrideEntries_AreLeftAlone()
    {
        var file = new CustomAiFile
        {
            VehicleClass = "F-Classic_Gen2",
            Drivers =
            [
                new CustomAiDriver { LiveryName = "A" },
                new CustomAiDriver { LiveryName = "B", Tracks = ["Monza_1971"] }, // per-track override
                new CustomAiDriver { LiveryName = "C" },
            ],
        };

        var result = BaseGameLiveryBinder.RebindToBaseGame(file, Base("United Racing #3", "United Racing #4"));

        // Base entries take the first two base-game liveries; the per-track entry is untouched.
        Assert.Equal(new[] { "United Racing #3", "B", "United Racing #4" }, result.Drivers.Select(d => d.LiveryName));
    }

    [Fact]
    public void NoOfficialLiveriesForTheClass_ReturnsTheFileUnchanged()
    {
        var file = FileOf("A", "B");
        Assert.Same(file, BaseGameLiveryBinder.RebindToBaseGame(file, (IReadOnlyList<OfficialLivery>?)null));
        Assert.Same(file, BaseGameLiveryBinder.RebindToBaseGame(file, Base()));
    }

    private static IReadOnlySet<string> Installed(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);

    [Fact]
    public void InstalledActiveSkin_IsKept_TheRestAreFlooredToBaseGame()
    {
        var file = FileOf("1988 Lotus #1 - N. Piquet", "1988 McLaren #12 - A. Senna", "1988 Ferrari #27 - M. Alboreto");

        // Only the McLaren skin is installed & active on disk — it keeps its real paint.
        var result = BaseGameLiveryBinder.RebindToBaseGame(
            file,
            Base("United Racing #3", "United Racing #4", "McLaren MP4/4 #11"),
            Installed("1988 McLaren #12 - A. Senna"));

        Assert.Equal(
            new[] { "United Racing #3", "1988 McLaren #12 - A. Senna", "United Racing #4" },
            result.Drivers.Select(d => d.LiveryName));
    }

    [Fact]
    public void EveryDriverOnAnInstalledSkin_KeepsAllRealPaint_NoBaseGameUsed()
    {
        var file = FileOf("1988 Lotus #1 - N. Piquet", "1988 McLaren #12 - A. Senna");

        var result = BaseGameLiveryBinder.RebindToBaseGame(
            file,
            Base("United Racing #3", "United Racing #4"),
            Installed("1988 Lotus #1 - N. Piquet", "1988 McLaren #12 - A. Senna"));

        Assert.Equal(
            new[] { "1988 Lotus #1 - N. Piquet", "1988 McLaren #12 - A. Senna" },
            result.Drivers.Select(d => d.LiveryName));
    }

    [Fact]
    public void FlooredDriversStayDistinct_EvenWhenSomeSkinsAreKept()
    {
        var file = FileOf("keep-me", "floor-a", "floor-b");

        var result = BaseGameLiveryBinder.RebindToBaseGame(
            file,
            Base("United Racing #3", "United Racing #4", "United Racing #5"),
            Installed("keep-me"));

        Assert.Equal(
            new[] { "keep-me", "United Racing #3", "United Racing #4" },
            result.Drivers.Select(d => d.LiveryName));
    }
}

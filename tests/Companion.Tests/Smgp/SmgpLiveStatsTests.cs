using Companion.Core.Scoring;
using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>The pure live-stats counter: tallies wins/podiums/poles/top-5s/starts per driver from race
/// classifications + the qualifying pole. Display-only — never a fold input.</summary>
public sealed class SmgpLiveStatsTests
{
    private static ClassifiedEntry Fin(string id, int pos) =>
        new() { DriverId = id, Position = pos, Status = FinishStatus.Classified };

    [Fact]
    public void Tallies_win_podium_top5_and_start_by_finishing_position()
    {
        var race = new[] { Fin("a", 1), Fin("b", 2), Fin("c", 3), Fin("d", 4), Fin("e", 5), Fin("f", 6) };
        var acc = SmgpLiveStats.Accrue([(race, "a")]);

        Assert.Equal(new SmgpAccruedStats { Starts = 1, Wins = 1, Podiums = 1, Poles = 1, Top5s = 1 }, acc["a"]);
        Assert.Equal(new SmgpAccruedStats { Starts = 1, Wins = 0, Podiums = 1, Poles = 0, Top5s = 1 }, acc["c"]); // P3
        Assert.Equal(new SmgpAccruedStats { Starts = 1, Wins = 0, Podiums = 0, Poles = 0, Top5s = 1 }, acc["e"]); // P5
        Assert.Equal(new SmgpAccruedStats { Starts = 1, Wins = 0, Podiums = 0, Poles = 0, Top5s = 0 }, acc["f"]); // P6, start only
    }

    [Fact]
    public void A_retired_car_is_a_start_but_not_a_finish()
    {
        var race = new ClassifiedEntry[]
        {
            Fin("a", 1),
            new() { DriverId = "x", Position = null, Status = FinishStatus.Retired },
        };
        var acc = SmgpLiveStats.Accrue([(race, null)]);

        Assert.Equal(1, acc["x"].Starts);
        Assert.Equal(0, acc["x"].Wins);
        Assert.Equal(0, acc["x"].Podiums);
        Assert.Equal(0, acc["x"].Top5s);
    }

    [Fact]
    public void Accrues_across_rounds_and_credits_the_pole_sitter()
    {
        var r1 = new[] { Fin("a", 1), Fin("b", 2) };
        var r2 = new[] { Fin("b", 1), Fin("a", 2) };
        var acc = SmgpLiveStats.Accrue([(r1, "a"), (r2, "b")]);

        Assert.Equal(1, acc["a"].Wins);
        Assert.Equal(1, acc["b"].Wins);
        Assert.Equal(1, acc["a"].Poles);
        Assert.Equal(1, acc["b"].Poles);
        Assert.Equal(2, acc["a"].Starts);
        Assert.Equal(2, acc["a"].Podiums); // P1 then P2 are both podiums
    }

    [Fact]
    public void No_qualifying_means_no_poles_and_no_rounds_means_empty()
    {
        var acc = SmgpLiveStats.Accrue([(new[] { Fin("a", 1) }, null)]);
        Assert.Equal(0, acc["a"].Poles);
        Assert.Equal(1, acc["a"].Wins);

        Assert.Empty(SmgpLiveStats.Accrue([]));
    }
}

using Companion.Core.Grid;

namespace Companion.Tests.Grid;

/// <summary>"Choose the entire grid" (v0.6.0): a <see cref="GridSelection"/> filters the round's
/// field to the liveries the player chose, always keeping the player's own seat, and is byte-identical
/// to the full pack field when it includes everything (the identity that keeps existing careers +
/// the oracle unchanged).</summary>
public class GridSelectionTests
{
    private const string Brabham1 = "Brabham-Repco #1 J. Brabham";
    private const string Clark = "Lotus-Ford Cosworth #5 J. Clark";
    private const string Hill = "Lotus-Ford Cosworth #6 G. Hill";

    [Fact]
    public void Selection_KeepsOnlyTheChosenLiveries()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        var chosen = new GridSelection { IncludedLiveries = [Brabham1, Clark] };

        var plan = RoundGridResolver.Resolve(pack, 1, playerSeat: null, chosen);

        Assert.Equal(2, plan.Seats.Count);
        Assert.Contains(plan.Seats, s => s.Ams2LiveryName == Brabham1);
        Assert.Contains(plan.Seats, s => s.Ams2LiveryName == Clark);
        Assert.DoesNotContain(plan.Seats, s => s.Ams2LiveryName == Hill);
    }

    [Fact]
    public void Selection_AlwaysKeepsThePlayersOwnSeat_EvenWhenExcluded()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        // The chosen field does NOT list the player's livery — but the player must never be benched.
        var chosen = new GridSelection { IncludedLiveries = [Clark] };
        var player = new PlayerSeat { Ams2LiveryName = Brabham1 };

        var plan = RoundGridResolver.Resolve(pack, 1, player, chosen);

        Assert.Contains(plan.Seats, s => s is { IsPlayer: true, Ams2LiveryName: Brabham1 });
        Assert.Contains(plan.Seats, s => s.Ams2LiveryName == Clark);
        Assert.Equal(2, plan.Seats.Count);
    }

    [Fact]
    public void EverythingSelection_IsIdenticalToNoSelection()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        var baseline = RoundGridResolver.Resolve(pack, 1);
        var everything = RoundGridResolver.Resolve(pack, 1, playerSeat: null, GridSelection.Everything);
        var nullSelection = RoundGridResolver.Resolve(pack, 1, playerSeat: null, selection: null);

        Assert.Equal(
            baseline.Seats.Select(s => s.Ams2LiveryName),
            everything.Seats.Select(s => s.Ams2LiveryName));
        Assert.Equal(
            baseline.Seats.Select(s => s.Ams2LiveryName),
            nullSelection.Seats.Select(s => s.Ams2LiveryName));
    }

    [Fact]
    public void Includes_IsCaseSensitive_AndEverythingIncludesAll()
    {
        Assert.True(GridSelection.Everything.Includes("anything"));
        var sel = new GridSelection { IncludedLiveries = [Brabham1] };
        Assert.True(sel.Includes(Brabham1));
        Assert.False(sel.Includes(Brabham1.ToLowerInvariant()));
        Assert.False(sel.Includes(Clark));
    }

    [Fact]
    public void HasStructuralEquality_OverTheLiveryList()
    {
        // Two DIFFERENT list instances with the same contents must be equal — else a re-derived
        // selection would diverge from the deserialized one and break byte-identical replay.
        var a = new GridSelection { IncludedLiveries = new List<string> { Brabham1, Clark } };
        var b = new GridSelection { IncludedLiveries = new List<string> { Brabham1, Clark } };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, new GridSelection { IncludedLiveries = [Brabham1] });
        Assert.NotEqual(a, new GridSelection { IncludedLiveries = [Clark, Brabham1] }); // order matters
        Assert.Equal(GridSelection.Everything, new GridSelection { IncludedLiveries = [] });
    }
}

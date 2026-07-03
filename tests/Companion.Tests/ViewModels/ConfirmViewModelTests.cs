using Companion.Core.Numerics;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

public class ConfirmViewModelTests
{
    private static ConfirmModel Model(
        IReadOnlyList<(string DriverId, Rational Points)>? points = null,
        IReadOnlyList<(string DriverId, int? From, int? To)>? movements = null,
        string headline = "CLARK TAKES THE FLAG") => new()
    {
        RoundPoints = points ?? [],
        Movements = movements ?? [],
        Headline = headline,
    };

    private static ConfirmViewModel Vm(
        ConfirmModel model,
        Action? apply = null,
        Action? back = null,
        Func<string, string>? names = null) =>
        new(model, apply ?? (() => { }), back ?? (() => { }), names);

    // ---------- round points rows ----------

    [Fact]
    public void RoundPoints_ExposePointsText_IncludingFractions()
    {
        var vm = Vm(Model(points:
        [
            ("d.clark", new Rational(9)),
            ("d.hulme", new Rational(6)),
            ("d.fangio", new Rational(1, 7)), // shared fastest lap: exact fraction survives
        ]));

        Assert.Equal(
            new[] { ("d.clark", "9"), ("d.hulme", "6"), ("d.fangio", "1/7") },
            vm.RoundPoints.Select(r => (r.DriverId, r.PointsText)));
    }

    [Fact]
    public void DisplayNameResolver_AppliesToBothRowKinds_DefaultIsTheId()
    {
        var names = new Dictionary<string, string> { ["d.clark"] = "Jim Clark" };
        string Resolve(string id) => names.GetValueOrDefault(id, id);

        var model = Model(
            points: [("d.clark", new Rational(9)), ("d.unknown", Rational.Zero)],
            movements: [("d.clark", 2, 1)]);

        var resolved = Vm(model, names: Resolve);
        Assert.Equal("Jim Clark", resolved.RoundPoints[0].DisplayName);
        Assert.Equal("d.unknown", resolved.RoundPoints[1].DisplayName);
        Assert.Equal("Jim Clark", resolved.Movements[0].DisplayName);

        var unresolved = Vm(model);
        Assert.Equal("d.clark", unresolved.RoundPoints[0].DisplayName);
    }

    // ---------- movement rows & glyphs ----------

    [Fact]
    public void Movements_CarryDirectionGlyphs()
    {
        var vm = Vm(Model(movements:
        [
            ("d.up", 3, 1),      // gained two
            ("d.down", 2, 3),    // lost one
            ("d.same", 4, 4),    // unchanged
            ("d.debut", null, 5) // round 1: no previous position
        ]));

        Assert.Equal(
            new[] { "▲2", "▼1", "–", "–" },
            vm.Movements.Select(m => m.Glyph));
        Assert.Equal(("3", "1"), (vm.Movements[0].FromText, vm.Movements[0].ToText));
        Assert.Equal(("–", "5"), (vm.Movements[3].FromText, vm.Movements[3].ToText));
    }

    [Theory]
    [InlineData(5, 3, "▲2")]
    [InlineData(1, 2, "▼1")]
    [InlineData(7, 7, "–")]
    [InlineData(null, 4, "–")]
    [InlineData(4, null, "–")]
    [InlineData(null, null, "–")]
    [InlineData(10, 1, "▲9")]
    public void Glyph_CoversEveryDirection(int? from, int? to, string expected) =>
        Assert.Equal(expected, ConfirmViewModel.Glyph(from, to));

    // ---------- headline & commands ----------

    [Fact]
    public void Headline_IsPassedThrough()
    {
        var vm = Vm(Model(headline: "HULME HOLDS OFF THE PACK AT MONZA"));
        Assert.Equal("HULME HOLDS OFF THE PACK AT MONZA", vm.Headline);
    }

    [Fact]
    public void ApplyCommand_InvokesTheInjectedDelegate_Only()
    {
        int applied = 0, backed = 0;
        var vm = Vm(Model(), apply: () => applied++, back: () => backed++);

        vm.ApplyCommand.Execute(null);

        Assert.Equal(1, applied);
        Assert.Equal(0, backed);
    }

    [Fact]
    public void BackCommand_InvokesTheInjectedDelegate_Only()
    {
        int applied = 0, backed = 0;
        var vm = Vm(Model(), apply: () => applied++, back: () => backed++);

        vm.BackCommand.Execute(null);

        Assert.Equal(0, applied);
        Assert.Equal(1, backed);
    }

    [Fact]
    public void Constructor_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfirmViewModel(null!, () => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() => new ConfirmViewModel(Model(), null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => new ConfirmViewModel(Model(), () => { }, null!));
    }
}
